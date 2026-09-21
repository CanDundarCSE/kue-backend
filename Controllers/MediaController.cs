using Microsoft.AspNetCore.Mvc;

namespace Kue.Api.Controllers;

[ApiController]
[Route("api/v1/media")]
public class MediaController : ControllerBase
{
    [HttpGet]
    public IActionResult GetMedia(
        [FromQuery] string? type,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? genre = null,
        [FromQuery] int? year = null,
        [FromQuery] string? platform = null,
        [FromQuery] string? status = null)
    {
        return Ok(new
        {
            message = "Media retrieved successfully!"
        });
    }

    [HttpGet("{id:int}")]
    public IActionResult GetMediaById(int id)
    {
        return Ok(new
        {
            id,
            message = "Media retrieved successfully!"
        });
    }

    [HttpGet("trending")]
    public IActionResult GetTrendingMedia()
    {
        return Ok(new
        {
            message = "Trending media retrieved successfully!"
        });
    }

     [HttpGet("popular")]
    public IActionResult GetPopularMedia()
    {
        return Ok(new
        {
            message = "Popular media retrieved successfully!"
        });
    }

    [HttpGet("top")]
    public IActionResult GetTopMedia()
    {
        return Ok(new
        {
            message = "Top media retrieved successfully!"
        });
    }

    [HttpGet("upcoming")]
    public IActionResult GetUpcomingMedia()
    {
        return Ok(new
        {
            message = "Upcoming media retrieved successfully!"
        });
    }

    [HttpGet("{id:int}/similar")]
    public IActionResult GetSimilar(int id)
    {
        return Ok(new
        {
            mediaId = id,
            message = "Similar media retrieved successfully!"
        });
    }

    [HttpGet("{id:int}/details")]
    public IActionResult GetDetails(int id)
    {
        return Ok(new
        {
            mediaId = id,
            message = "Media details retrieved successfully!"
        });
    }
}