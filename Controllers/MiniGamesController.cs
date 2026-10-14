using System.Security.Claims;
using Kue.Api.Data;
using Kue.Api.Dtos.Common;
using Kue.Api.Dtos.MiniGames;
using Kue.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kue.Api.Controllers;

[ApiController]
[Route("api/v1/me/mini-games")]
[Authorize]
[Produces("application/json")]
public class MiniGamesController : ControllerBase
{
    // Poster-guessing scoring: 100 per round, -20 per wrong guess, 10 floor.
    // Mirrored in the web client; the server re-validates every bound.
    private const string PosterGuessGame = "poster-guess";
    private const int MaxPointsPerRound = 100;
    private const int MaxRoundsPerGame = 10;

    // Server-side hint prices. The client sends the hint type, never an
    // amount, so underpaying is impossible.
    private static readonly Dictionary<string, int> HintPrices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["year"] = 10,
        ["type"] = 15,
        ["firstLetter"] = 25,
        ["fiftyFifty"] = 15,
    };

    private readonly AppDbContext _context;

    public MiniGamesController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet("wallet")]
    [ProducesResponseType(typeof(MiniGameWalletDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetWallet(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId.Value, ct);
        if (user is null) return Unauthorized();

        return Ok(new MiniGameWalletDto
        {
            Balance = user.GameBalance,
            TotalEarned = user.GameTotalEarned,
        });
    }

    [HttpPost("wallet/spend")]
    [ProducesResponseType(typeof(SpendHintResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SpendHint([FromBody] SpendHintRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var hintType = request?.HintType?.Trim() ?? "";
        if (!HintPrices.TryGetValue(hintType, out var price))
        {
            return BadRequest(new MessageResponseDto("Unknown hint type."));
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId.Value, ct);
        if (user is null) return Unauthorized();

        if (user.GameBalance < price)
        {
            return BadRequest(new MessageResponseDto("Not enough points for this hint."));
        }

        user.GameBalance -= price;
        await _context.SaveChangesAsync(ct);

        return Ok(new SpendHintResponseDto
        {
            HintType = hintType,
            Price = price,
            Balance = user.GameBalance,
        });
    }

    [HttpPost("games")]
    [ProducesResponseType(typeof(SubmitMiniGameResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SubmitGame([FromBody] SubmitMiniGameRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        if (request is null || string.IsNullOrWhiteSpace(request.Game))
        {
            return BadRequest(new MessageResponseDto("Game is required."));
        }

        var rounds = request.Rounds ?? [];
        if (rounds.Count == 0 || rounds.Count > MaxRoundsPerGame)
        {
            return BadRequest(new MessageResponseDto("Invalid round count."));
        }

        // Sanity caps: a round can never pay more than a perfect solve, and
        // attempt counts stay within the game's rules. This keeps casual
        // score-spoofing out without a full server-judged protocol.
        foreach (var round in rounds)
        {
            if (round.Points < 0 || round.Points > MaxPointsPerRound)
            {
                return BadRequest(new MessageResponseDto("Invalid round score."));
            }
            if (round.AttemptsUsed < 1 || round.AttemptsUsed > 10)
            {
                return BadRequest(new MessageResponseDto("Invalid attempt count."));
            }
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId.Value, ct);
        if (user is null) return Unauthorized();

        var score = rounds.Sum(r => r.Points);
        var roundsWon = rounds.Count(r => r.Points > 0);

        user.GameBalance += score;
        user.GameTotalEarned += score;

        _context.MiniGameScores.Add(new MiniGameScore
        {
            UserId = userId.Value,
            Game = request.Game.Trim(),
            Score = score,
            RoundsWon = roundsWon,
            RoundsTotal = rounds.Count,
        });

        await _context.SaveChangesAsync(ct);

        return Ok(new SubmitMiniGameResponseDto
        {
            Score = score,
            RoundsWon = roundsWon,
            RoundsTotal = rounds.Count,
            Balance = user.GameBalance,
            TotalEarned = user.GameTotalEarned,
        });
    }

    [HttpGet("leaderboard")]
    [ProducesResponseType(typeof(LeaderboardResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetLeaderboard(
        [FromQuery] string? game,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        game = string.IsNullOrWhiteSpace(game) ? PosterGuessGame : game.Trim();
        limit = Math.Clamp(limit, 1, 50);

        // Cumulative board: whoever earned the most total points leads, so a
        // perfect game never permanently locks the top spot — anyone can be
        // overtaken by earning more. Ties break by best single game, then
        // fewest games (same total in fewer games = more skilled).
        // Grouping runs in memory: EF Core can't translate per-group
        // aggregates with an include, and score tables stay small enough.
        var rows = await _context.MiniGameScores
            .AsNoTracking()
            .Include(s => s.User)
            .Where(s => s.Game == game && (!s.User.IsPrivate || s.UserId == userId.Value))
            .OrderByDescending(s => s.Score)
            .Take(2000)
            .ToListAsync(ct);

        LeaderboardEntryDto MapEntry(int id, string username, string? avatarId, int total, int best, int games) =>
            new()
            {
                UserId = id,
                Username = username,
                AvatarId = avatarId,
                TotalScore = total,
                BestScore = best,
                GamesPlayed = games,
            };

        var bestPerUser = rows
            .GroupBy(s => s.UserId)
            .Select(g => MapEntry(
                g.Key,
                g.First().User.Username,
                g.First().User.AvatarId,
                g.Sum(s => s.Score),
                g.Max(s => s.Score),
                g.Count()))
            .OrderByDescending(e => e.TotalScore)
            .ThenByDescending(e => e.BestScore)
            .ThenBy(e => e.GamesPlayed)
            .Take(limit)
            .ToList();

        var own = rows.Where(s => s.UserId == userId.Value).ToList();
        LeaderboardEntryDto? mine = own.Count == 0
            ? null
            : MapEntry(
                userId.Value,
                own[0].User.Username,
                own[0].User.AvatarId,
                own.Sum(s => s.Score),
                own.Max(s => s.Score),
                own.Count);

        return Ok(new LeaderboardResponseDto { Entries = bestPerUser, Mine = mine });
    }

    private int? GetCurrentUserId()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idClaim, out var id) ? id : null;
    }
}
