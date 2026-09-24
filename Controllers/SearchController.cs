using Kue.Api.Dtos.Media;
using Kue.Api.Services.Media;
using Microsoft.AspNetCore.Mvc;

namespace Kue.Api.Controllers;

[ApiController]
[Route("api/v1/search")]
[Produces("application/json")]
public class SearchController : ControllerBase
{
    private readonly IMediaService _mediaService;

    public SearchController(IMediaService mediaService)
    {
        _mediaService = mediaService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResponseDto<MediaDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] string? query,
        [FromQuery] string? type,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Ok(new PagedResponseDto<MediaDto>
            {
                Items = [],
                Page = page,
                PageSize = pageSize,
                TotalItems = 0,
                TotalPages = 0
            });
        }

        var results = await _mediaService.SearchMediaAsync(query, type, page, pageSize, ct);
        return Ok(results);
    }
}