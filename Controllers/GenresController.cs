using Microsoft.AspNetCore.Mvc;

namespace Kue.Api.Controllers;

[ApiController]
[Route("api/v1/genres")]
public class GenresController : ControllerBase
{
    [HttpGet]
    public IActionResult GetGenres(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        return Ok(new
        {
            message = "Genres retrieved successfully!"
        });
    }

    [HttpGet("{id:int}")]
    public IActionResult GetGenreById(int id)
    {
        return Ok(new
        {
            id,
            message = "Genre retrieved successfully!"
        });
    }

     [HttpGet("{id:int}/media")]
    public IActionResult GetGenreMedia(
        int id,
        [FromQuery] string? type = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        return Ok(new
        {
            genreId = id,
            type,
            page,
            pageSize,
            items = new[]
            {
                new
                {
                    id = 1,
                    title = "Example Media",
                    mediaType = type ?? "movie"
                }
            }
        });
    }
}