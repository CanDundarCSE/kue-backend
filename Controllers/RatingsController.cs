using Microsoft.AspNetCore.Mvc;

namespace Kue.Api.Controllers;

[ApiController]
[Route("api/v1")]
public class RatingsController : ControllerBase
{
    [HttpPost("me/ratings")]
    public IActionResult CreateRating()
    {
        return Ok(new
        {
            message = "Rating created successfully!"
        });
    }

    [HttpGet("me/ratings")]
    public IActionResult GetMyRatings()
    {
        return Ok(new
        {
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

    [HttpPut("me/ratings/{mediaId:int}")]
    public IActionResult UpdateRating(int mediaId)
    {
        return Ok(new
        {
            mediaId,
            rating = 9,
            message = "Rating updated successfully!"
        });
    }

    [HttpDelete("me/ratings/{mediaId:int}")]
    public IActionResult DeleteRating(int mediaId)
    {
        return Ok(new
        {
            mediaId,
            message = "Rating deleted successfully!"
        });
    }

    [HttpGet("media/{mediaId:int}/ratings")]
    public IActionResult GetMediaRatings(int mediaId)
    {
        return Ok(new
        {
            mediaId,
            items = new[]
            {
                new
                {
                    userId = 1,
                    rating = 9
                }
            }
        });
    }

    [HttpGet("media/{mediaId:int}/ratings/summary")]
    public IActionResult GetMediaRatingSummary(int mediaId)
    {
        return Ok(new
        {
            mediaId,
            averageRating = 8.7,
            totalRatings = 1248
        });
    }
}