using Microsoft.AspNetCore.Mvc;

namespace Kue.Api.Controllers;

[ApiController]
[Route("api/v1/me/stats")]
public class StatsController : ControllerBase
{
    [HttpGet("overview")]
    public IActionResult GetOverview()
    {
        return Ok(new
        {
            totalAnimeWatching = 4,
            totalMangaReading = 2,
            totalMoviesWatched = 37,
            totalGamesCompleted = 11,
            averageAnimeScore = 7.8,
            totalEpisodesWatched = 512,
            totalChaptersRead = 980
        });
    }

    [HttpGet("genres")]
    public IActionResult GetGenres()
    {
        return Ok(new
        {
            items = new[]
            {
                new
                {
                    genre = "Action",
                    count = 25
                },
                new
                {
                    genre = "Comedy",
                    count = 18
                }
            }
        });
    }

    [HttpGet("activity")]
    public IActionResult GetActivity()
    {
        return Ok(new
        {
            items = new[]
            {
                new
                {
                    date = "2026-09-21",
                    completed = 2,
                    started = 1
                }
            }
        });
    }

    [HttpGet("time-spent")]
    public IActionResult GetTimeSpent()
    {
        return Ok(new
        {
            totalMinutes = 12540,
            animeMinutes = 7200,
            seriesMinutes = 2400,
            movieMinutes = 1800,
            gameMinutes = 1140
        });
    }
}