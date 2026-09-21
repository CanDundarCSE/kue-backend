using Microsoft.AspNetCore.Mvc;

namespace Kue.Api.Controllers;

[ApiController]
[Route("api/v1/admin")]
public class AdminController : ControllerBase
{
    // Users

    [HttpGet("users")]
    public IActionResult GetUsers()
    {
        return Ok(new
        {
            items = new[]
            {
                new
                {
                    id = 1,
                    username = "example_user",
                    role = "user",
                    isBanned = false
                }
            }
        });
    }

    [HttpPut("users/{id:int}/role")]
    public IActionResult UpdateUserRole(int id)
    {
        return Ok(new
        {
            userId = id,
            role = "admin",
            message = "User role updated successfully!"
        });
    }

    [HttpPut("users/{id:int}/ban")]
    public IActionResult BanUser(int id)
    {
        return Ok(new
        {
            userId = id,
            isBanned = true,
            message = "User banned successfully!"
        });
    }

    [HttpDelete("users/{id:int}")]
    public IActionResult DeleteUser(int id)
    {
        return Ok(new
        {
            userId = id,
            message = "User deleted successfully!"
        });
    }


    // External Media Metadata

    [HttpGet("media")]
    public IActionResult GetMedia()
    {
        return Ok(new
        {
            items = new[]
            {
                new
                {
                    mediaType = "movie",
                    externalSource = "tmdb",
                    externalId = "1396",
                    title = "Example Movie",
                    cached = true,
                    lastSyncedAt = "2026-09-21"
                }
            }
        });
    }

    [HttpPost("media/{mediaType}/{externalId}/refresh")]
    public IActionResult RefreshMedia(
        string mediaType,
        string externalId)
    {
        return Ok(new
        {
            mediaType,
            externalSource = GetSource(mediaType),
            externalId,
            message = "Media metadata refreshed successfully!"
        });
    }

    [HttpPost("media/sync")]
    public IActionResult SyncMedia()
    {
        return Ok(new
        {
            message = "Media synchronization started successfully!"
        });
    }

    private static string GetSource(string mediaType)
    {
        return mediaType.ToLower() switch
        {
            "movie" => "tmdb",
            "series" => "tmdb",
            "game" => "igdb",
            "anime" => "anilist",
            "manga" => "anilist",
            _ => "unknown"
        };
    }
}