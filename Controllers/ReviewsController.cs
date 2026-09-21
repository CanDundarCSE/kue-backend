using Microsoft.AspNetCore.Mvc;

namespace Kue.Api.Controllers;

[ApiController]
[Route("api/v1")]
public class ReviewsController : ControllerBase
{
    [HttpPost("media/{mediaId:int}/reviews")]
    public IActionResult CreateReview(int mediaId)
    {
        return Ok(new
        {
            mediaId,
            message = "Review created successfully!"
        });
    }

    [HttpGet("media/{mediaId:int}/reviews")]
    public IActionResult GetMediaReviews(int mediaId)
    {
        return Ok(new
        {
            mediaId,
            items = new[]
            {
                new
                {
                    reviewId = 1,
                    userId = 1,
                    rating = 9,
                    content = "Great anime!"
                }
            }
        });
    }

    [HttpGet("reviews/{reviewId:int}")]
    public IActionResult GetReview(int reviewId)
    {
        return Ok(new
        {
            reviewId,
            rating = 9,
            content = "Great anime!"
        });
    }

    [HttpPut("reviews/{reviewId:int}")]
    public IActionResult UpdateReview(int reviewId)
    {
        return Ok(new
        {
            reviewId,
            message = "Review updated successfully!"
        });
    }

    [HttpDelete("reviews/{reviewId:int}")]
    public IActionResult DeleteReview(int reviewId)
    {
        return Ok(new
        {
            reviewId,
            message = "Review deleted successfully!"
        });
    }

    [HttpPost("reviews/{reviewId:int}/like")]
    public IActionResult LikeReview(int reviewId)
    {
        return Ok(new
        {
            reviewId,
            message = "Review liked successfully!"
        });
    }

    [HttpDelete("reviews/{reviewId:int}/like")]
    public IActionResult UnlikeReview(int reviewId)
    {
        return Ok(new
        {
            reviewId,
            message = "Review unliked successfully!"
        });
    }
}