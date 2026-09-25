using System.Security.Claims;
using Kue.Api.Data;
using Kue.Api.Dtos.Stats;
using Kue.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kue.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/me/stats")]
[Produces("application/json")]
public class StatsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<StatsController> _logger;

    public StatsController(AppDbContext context, ILogger<StatsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet("overview")]
    [ProducesResponseType(typeof(StatsOverviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetOverview(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var entries = await _context.LibraryEntries
            .AsNoTracking()
            .Include(e => e.Media)
            .Where(e => e.UserId == userId.Value)
            .ToListAsync(ct);

        var overview = new StatsOverviewDto
        {
            TotalItems = entries.Count,
            TotalCompleted = entries.Count(e => e.Status == "completed"),
            TotalInProgress = entries.Count(e => e.Status is "watching" or "reading" or "playing" or "in_progress"),
            TotalPlanned = entries.Count(e => e.Status is "planned" or "plan_to_watch" or "plan_to_read" or "plan_to_play"),
            TotalDropped = entries.Count(e => e.Status == "dropped"),
            TotalOnHold = entries.Count(e => e.Status == "on_hold"),
            TotalFavorites = entries.Count(e => e.IsFavorite),
            MeanScore = CalculateMeanScore(entries.Where(e => e.Rating.HasValue).Select(e => (double)e.Rating!.Value)),

            Anime = BuildTypeBreakdown(entries, "anime"),
            Manga = BuildTypeBreakdown(entries, "manga"),
            Movie = BuildTypeBreakdown(entries, "movie"),
            Series = BuildTypeBreakdown(entries, "series"),
            Game = BuildTypeBreakdown(entries, "game")
        };

        // Quick access summary fields for frontend convenience
        overview.TotalAnimeWatching = overview.Anime.InProgress;
        overview.TotalMangaReading = overview.Manga.InProgress;
        overview.TotalMoviesWatched = overview.Movie.Completed;
        overview.TotalGamesCompleted = overview.Game.Completed;
        overview.TotalSeriesWatching = overview.Series.InProgress;
        overview.AverageAnimeScore = overview.Anime.MeanScore;
        overview.TotalEpisodesWatched = overview.Anime.TotalUnitsConsumed + overview.Series.TotalUnitsConsumed;
        overview.TotalChaptersRead = overview.Manga.TotalUnitsConsumed;

        return Ok(overview);
    }

    [HttpGet("genres")]
    [ProducesResponseType(typeof(GenreStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetGenres(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var entries = await _context.LibraryEntries
            .AsNoTracking()
            .Include(e => e.Media)
            .Where(e => e.UserId == userId.Value)
            .ToListAsync(ct);

        if (entries.Count == 0)
        {
            return Ok(new GenreStatsDto { Items = [] });
        }

        var genreCounts = entries
            .SelectMany(e => e.Media.Genres)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g.Trim())
            .GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Genre = group.Key,
                Count = group.Count()
            })
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Genre)
            .ToList();

        var totalMediaCount = entries.Count;
        var items = genreCounts.Select(g => new GenreStatItemDto
        {
            Genre = g.Genre,
            Count = g.Count,
            Percentage = totalMediaCount > 0 ? Math.Round((g.Count / (double)totalMediaCount) * 100, 1) : 0
        }).ToList();

        return Ok(new GenreStatsDto { Items = items });
    }

    [HttpGet("time-spent")]
    [ProducesResponseType(typeof(TimeSpentStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetTimeSpent(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var entries = await _context.LibraryEntries
            .AsNoTracking()
            .Include(e => e.Media)
            .Where(e => e.UserId == userId.Value)
            .ToListAsync(ct);

        int animeMinutes = 0;
        int seriesMinutes = 0;
        int movieMinutes = 0;
        int gamesCompleted = 0;
        int mangaChaptersRead = 0;

        foreach (var e in entries)
        {
            var type = e.Media.MediaType.ToLowerInvariant();
            if (type == "anime")
            {
                var units = e.Status == "completed" ? (e.Progress ?? e.Media.TotalUnits ?? 0) : (e.Progress ?? 0);
                animeMinutes += units * 24; // Average 24 min per anime episode
            }
            else if (type == "series")
            {
                var units = e.Status == "completed" ? (e.Progress ?? e.Media.TotalUnits ?? 0) : (e.Progress ?? 0);
                seriesMinutes += units * 45; // Average 45 min per TV episode
            }
            else if (type == "movie" && e.Status == "completed")
            {
                movieMinutes += e.Media.RuntimeMinutes ?? 105; // Use actual movie runtime, fallback 105 min
            }
            else if (type == "game" && e.Status == "completed")
            {
                gamesCompleted++;
            }
            else if (type == "manga")
            {
                var units = e.Status == "completed" ? (e.Progress ?? e.Media.TotalUnits ?? 0) : (e.Progress ?? 0);
                mangaChaptersRead += units;
            }
        }

        var totalMinutes = animeMinutes + seriesMinutes + movieMinutes;
        var totalHours = Math.Round(totalMinutes / 60.0, 1);
        var totalDays = Math.Round(totalHours / 24.0, 1);

        return Ok(new TimeSpentStatsDto
        {
            TotalMinutes = totalMinutes,
            TotalHours = totalHours,
            TotalDays = totalDays,
            AnimeMinutes = animeMinutes,
            SeriesMinutes = seriesMinutes,
            MovieMinutes = movieMinutes,
            GamesCompleted = gamesCompleted,
            MangaChaptersRead = mangaChaptersRead
        });
    }

    [HttpGet("activity")]
    [ProducesResponseType(typeof(ActivityStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetActivity([FromQuery] int days = 30, CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        days = Math.Clamp(days, 1, 365);
        var cutoff = DateTime.UtcNow.Date.AddDays(-days);

        var entries = await _context.LibraryEntries
            .AsNoTracking()
            .Where(e => e.UserId == userId.Value && (e.AddedAt >= cutoff || (e.CompletedAt.HasValue && e.CompletedAt.Value >= cutoff)))
            .ToListAsync(ct);

        var dateMap = new Dictionary<string, (int Added, int Completed)>();

        foreach (var e in entries)
        {
            if (e.AddedAt >= cutoff)
            {
                var dateStr = e.AddedAt.ToString("yyyy-MM-dd");
                dateMap.TryGetValue(dateStr, out var current);
                dateMap[dateStr] = (current.Added + 1, current.Completed);
            }

            if (e.CompletedAt.HasValue && e.CompletedAt.Value >= cutoff)
            {
                var dateStr = e.CompletedAt.Value.ToString("yyyy-MM-dd");
                dateMap.TryGetValue(dateStr, out var current);
                dateMap[dateStr] = (current.Added, current.Completed + 1);
            }
        }

        var items = dateMap
            .OrderByDescending(kv => kv.Key)
            .Select(kv => new ActivityItemDto
            {
                Date = kv.Key,
                Added = kv.Value.Added,
                Completed = kv.Value.Completed
            })
            .ToList();

        return Ok(new ActivityStatsDto { Items = items });
    }

    // --- Private Calculation Helpers ---

    private int? GetCurrentUserId()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idClaim, out var id) ? id : null;
    }

    private static MediaTypeBreakdownDto BuildTypeBreakdown(List<LibraryEntry> allEntries, string mediaType)
    {
        var entries = allEntries
            .Where(e => e.Media.MediaType.Equals(mediaType, StringComparison.OrdinalIgnoreCase))
            .ToList();

        int unitsConsumed = 0;
        if (mediaType is "anime" or "series" or "manga")
        {
            foreach (var e in entries)
            {
                if (e.Status == "completed")
                {
                    unitsConsumed += e.Progress ?? e.Media.TotalUnits ?? 0;
                }
                else
                {
                    unitsConsumed += e.Progress ?? 0;
                }
            }
        }

        var rated = entries.Where(e => e.Rating.HasValue).Select(e => (double)e.Rating!.Value).ToList();

        return new MediaTypeBreakdownDto
        {
            Total = entries.Count,
            Completed = entries.Count(e => e.Status == "completed"),
            InProgress = entries.Count(e => e.Status is "watching" or "reading" or "playing" or "in_progress"),
            Planned = entries.Count(e => e.Status is "planned" or "plan_to_watch" or "plan_to_read" or "plan_to_play"),
            Dropped = entries.Count(e => e.Status == "dropped"),
            OnHold = entries.Count(e => e.Status == "on_hold"),
            TotalUnitsConsumed = unitsConsumed,
            MeanScore = CalculateMeanScore(rated)
        };
    }

    private static double? CalculateMeanScore(IEnumerable<double> scores)
    {
        var list = scores.ToList();
        return list.Count > 0 ? Math.Round(list.Average(), 1) : null;
    }
}