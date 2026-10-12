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
        [FromQuery] string? sort = null,
        [FromQuery] int? year = null,
        [FromQuery] double? minScore = null,
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

        var normalizedSort = sort?.Trim().ToLowerInvariant() switch
        {
            "newest" or "oldest" or "highest" or "lowest" => sort!.Trim().ToLowerInvariant(),
            _ => null,
        };
        var normalizedYear = year is >= 1800 and <= 2100 ? year : null;
        var normalizedMinScore = minScore is >= 0 and <= 10 ? minScore : null;

        var results = await _mediaService.SearchMediaAsync(query, type, page, pageSize, normalizedSort, normalizedYear, normalizedMinScore, ct);
        return Ok(results);
    }
}