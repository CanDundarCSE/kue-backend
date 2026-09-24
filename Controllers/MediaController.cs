using Kue.Api.Data;
using Kue.Api.Dtos.Common;
using Kue.Api.Dtos.Media;
using Kue.Api.Services.Media;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kue.Api.Controllers;

[ApiController]
[Route("api/v1/media")]
[Produces("application/json")]
public class MediaController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IMediaService _mediaService;

    public MediaController(AppDbContext context, IMediaService mediaService)
    {
        _context = context;
        _mediaService = mediaService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResponseDto<MediaDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMedia(
        [FromQuery] string? type,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? genre = null,
        [FromQuery] int? year = null,
        [FromQuery] string? platform = null,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.Media.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(type))
        {
            var normalizedType = type.Trim().ToLower();
            query = query.Where(m => m.MediaType.ToLower() == normalizedType);
        }

        if (!string.IsNullOrWhiteSpace(genre))
        {
            query = query.Where(m => m.Genres.Contains(genre));
        }

        if (year.HasValue)
        {
            query = query.Where(m => m.Year == year.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim().ToLower();
            query = query.Where(m => m.Status != null && m.Status.ToLower() == normalizedStatus);
        }

        if (!string.IsNullOrWhiteSpace(platform))
        {
            query = query.Where(m => m.Platforms != null && m.Platforms.Contains(platform));
        }

        var totalItems = await query.CountAsync(ct);

        // If local DB has items or query parameters were passed, return local items
        if (totalItems > 0 || !string.IsNullOrWhiteSpace(genre) || year.HasValue || !string.IsNullOrWhiteSpace(status) || !string.IsNullOrWhiteSpace(platform))
        {
            var entities = await query
                .OrderByDescending(m => m.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            var items = entities.Select(MediaDto.FromEntity).ToList();

            return Ok(new PagedResponseDto<MediaDto>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalItems = totalItems,
                TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize)
            });
        }

        // Otherwise (fresh DB without catalog populated yet), fall back to trending from external service
        var trending = await _mediaService.GetTrendingMediaAsync(type, page, pageSize, ct);
        return Ok(trending);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(MediaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMediaById(int id, CancellationToken ct = default)
    {
        var media = await _mediaService.GetMediaDetailsAsync(id, ct);
        if (media == null)
        {
            return NotFound(new MessageResponseDto($"Media with ID {id} not found."));
        }

        return Ok(media);
    }

    [HttpGet("trending")]
    [ProducesResponseType(typeof(PagedResponseDto<MediaDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTrendingMedia(
        [FromQuery] string? type,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var trending = await _mediaService.GetTrendingMediaAsync(type, page, pageSize, ct);
        return Ok(trending);
    }

    [HttpGet("popular")]
    [ProducesResponseType(typeof(PagedResponseDto<MediaDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPopularMedia(
        [FromQuery] string? type,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var popular = await _mediaService.GetTrendingMediaAsync(type, page, pageSize, ct);
        return Ok(popular);
    }

    [HttpGet("top")]
    [ProducesResponseType(typeof(PagedResponseDto<MediaDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTopMedia(
        [FromQuery] string? type,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.Media.AsNoTracking().Where(m => m.Score.HasValue);
        if (!string.IsNullOrWhiteSpace(type))
        {
            var normalizedType = type.Trim().ToLower();
            query = query.Where(m => m.MediaType.ToLower() == normalizedType);
        }

        var totalItems = await query.CountAsync(ct);
        if (totalItems > 0)
        {
            var entities = await query
                .OrderByDescending(m => m.Score)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            var items = entities.Select(MediaDto.FromEntity).ToList();

            return Ok(new PagedResponseDto<MediaDto>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalItems = totalItems,
                TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize)
            });
        }

        var trending = await _mediaService.GetTrendingMediaAsync(type, page, pageSize, ct);
        return Ok(trending);
    }

    [HttpGet("upcoming")]
    [ProducesResponseType(typeof(PagedResponseDto<MediaDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUpcomingMedia(
        [FromQuery] string? type,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var currentYear = DateTime.UtcNow.Year;
        var query = _context.Media.AsNoTracking()
            .Where(m => (m.Year.HasValue && m.Year.Value >= currentYear) || m.Status == "Upcoming" || m.Status == "Not Yet Aired");

        if (!string.IsNullOrWhiteSpace(type))
        {
            var normalizedType = type.Trim().ToLower();
            query = query.Where(m => m.MediaType.ToLower() == normalizedType);
        }

        var totalItems = await query.CountAsync(ct);
        if (totalItems > 0)
        {
            var entities = await query
                .OrderBy(m => m.Year)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            var items = entities.Select(MediaDto.FromEntity).ToList();

            return Ok(new PagedResponseDto<MediaDto>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalItems = totalItems,
                TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize)
            });
        }

        var trending = await _mediaService.GetTrendingMediaAsync(type, page, pageSize, ct);
        return Ok(trending);
    }

    [HttpGet("{id:int}/similar")]
    [ProducesResponseType(typeof(PagedResponseDto<MediaDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSimilar(int id, [FromQuery] int pageSize = 10, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 50);

        var media = await _context.Media.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (media == null)
        {
            return Ok(new PagedResponseDto<MediaDto> { Page = 1, PageSize = pageSize, TotalItems = 0, TotalPages = 0 });
        }

        var query = _context.Media.AsNoTracking()
            .Where(m => m.Id != id && m.MediaType == media.MediaType);

        if (media.Genres.Count > 0)
        {
            var firstGenre = media.Genres[0];
            query = query.Where(m => m.Genres.Contains(firstGenre));
        }

        var entities = await query
            .Take(pageSize)
            .ToListAsync(ct);

        var items = entities.Select(MediaDto.FromEntity).ToList();

        return Ok(new PagedResponseDto<MediaDto>
        {
            Items = items,
            Page = 1,
            PageSize = pageSize,
            TotalItems = items.Count,
            TotalPages = 1
        });
    }

    [HttpGet("{id:int}/details")]
    [ProducesResponseType(typeof(MediaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetails(int id, CancellationToken ct = default)
    {
        return await GetMediaById(id, ct);
    }
}