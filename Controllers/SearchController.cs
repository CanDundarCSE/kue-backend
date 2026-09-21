using Microsoft.AspNetCore.Mvc;

namespace Kue.Api.Controllers;

[ApiController]
[Route("api/v1/search")]
public class SearchController : ControllerBase
{
    [HttpGet]
    public IActionResult Search(
        [FromQuery] string? query,
        [FromQuery] string? type,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        return Ok(new
        {
            message = "Search results retrieved successfully!"
        });
    }
}