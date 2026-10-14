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

        // Best game per user. Private accounts stay hidden unless it's you,
        // matching the privacy rule on the public ratings endpoints.
        // Grouping runs in memory: EF Core can't translate per-group First()
        // with an include, and score tables stay small enough for it.
        var rows = await _context.MiniGameScores
            .AsNoTracking()
            .Include(s => s.User)
            .Where(s => s.Game == game && (!s.User.IsPrivate || s.UserId == userId.Value))
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.CreatedAt)
            .Take(1000)
            .ToListAsync(ct);

        var bestPerUser = rows
            .GroupBy(s => s.UserId)
            .Select(g => g.First())
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.CreatedAt)
            .Take(limit)
            .Select(s => new LeaderboardEntryDto
            {
                UserId = s.UserId,
                Username = s.User.Username,
                AvatarId = s.User.AvatarId,
                BestScore = s.Score,
                RoundsWon = s.RoundsWon,
                AchievedAt = s.CreatedAt,
            })
            .ToList();

        var mine = await _context.MiniGameScores
            .AsNoTracking()
            .Include(s => s.User)
            .Where(s => s.Game == game && s.UserId == userId.Value)
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.CreatedAt)
            .Select(s => new LeaderboardEntryDto
            {
                UserId = s.UserId,
                Username = s.User.Username,
                AvatarId = s.User.AvatarId,
                BestScore = s.Score,
                RoundsWon = s.RoundsWon,
                AchievedAt = s.CreatedAt,
            })
            .FirstOrDefaultAsync(ct);

        return Ok(new LeaderboardResponseDto { Entries = bestPerUser, Mine = mine });
    }

    private int? GetCurrentUserId()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idClaim, out var id) ? id : null;
    }
}
