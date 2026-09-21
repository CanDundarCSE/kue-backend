using Microsoft.AspNetCore.Mvc;

namespace Kue.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
public class UsersController : ControllerBase
{
    [HttpGet("{username}")]
    public IActionResult GetUser(string username)
    {
        return Ok(new
        {
            username,
            displayName = "Example User",
            bio = "Hello from Kue!",
            joinedAt = "2026-01-01"
        });
    }

    [HttpGet("{username}/library")]
    public IActionResult GetUserLibrary(string username)
    {
        return Ok(new
        {
            username,
            items = new[]
            {
                new
                {
                    mediaId = 25,
                    mediaType = "anime",
                    status = "completed",
                    progress = 24
                }
            }
        });
    }

    [HttpGet("{username}/lists")]
    public IActionResult GetUserLists(string username)
    {
        return Ok(new
        {
            username,
            items = new[]
            {
                new
                {
                    id = 1,
                    name = "Favori Animeler"
                }
            }
        });
    }

    [HttpGet("{username}/ratings")]
    public IActionResult GetUserRatings(string username)
    {
        return Ok(new
        {
            username,
            items = new[]
            {
                new
                {
                    mediaId = 25,
                    rating = 9
                }
            }
        });
    }

    [HttpGet("{username}/reviews")]
    public IActionResult GetUserReviews(string username)
    {
        return Ok(new
        {
            username,
            items = new[]
            {
                new
                {
                    reviewId = 1,
                    mediaId = 25,
                    rating = 9,
                    content = "Great anime!"
                }
            }
        });
    }

    [HttpGet("{username}/activity")]
    public IActionResult GetUserActivity(string username)
    {
        return Ok(new
        {
            username,
            items = new[]
            {
                new
                {
                    type = "completed",
                    mediaId = 25,
                    date = "2026-09-21"
                }
            }
        });
    }
}