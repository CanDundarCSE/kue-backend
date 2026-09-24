using System.Security.Claims;
using Kue.Api.Data;
using Kue.Api.Dtos.Common;
using Kue.Api.Dtos.Library;
using Kue.Api.Dtos.Media;
using Kue.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kue.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/me/library")]
[Produces("application/json")]
public class LibraryController : ControllerBase
{
    private readonly AppDbContext _context;

    public LibraryController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResponseDto<LibraryEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetLibrary(
        [FromQuery] string? type = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var query = _context.LibraryEntries
            .AsNoTracking()
            .Include(e => e.Media)
            .Where(e => e.UserId == userId.Value);

        if (!string.IsNullOrWhiteSpace(type))
        {
            var normalizedType = type.Trim().ToLower();
            query = query.Where(e => e.Media.MediaType.ToLower() == normalizedType);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim().ToLower();
            query = query.Where(e => e.Status.ToLower() == normalizedStatus);
        }

        var totalItems = await query.CountAsync(ct);
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var entries = await query
            .OrderByDescending(e => e.UpdatedAt ?? e.AddedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var dtos = entries.Select(MapToDto).ToList();

        return Ok(new PagedResponseDto<LibraryEntryDto>
        {
            Items = dtos,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalPages
        });
    }

    [HttpPost]
    [ProducesResponseType(typeof(LibraryEntryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> AddToLibrary([FromBody] AddToLibraryRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var media = await _context.Media.FirstOrDefaultAsync(m => m.Id == request.MediaId, ct);
        if (media is null)
        {
            return NotFound(new MessageResponseDto("Media not found in catalog."));
        }

        var alreadyExists = await _context.LibraryEntries
            .AnyAsync(e => e.UserId == userId.Value && e.MediaId == request.MediaId, ct);
        if (alreadyExists)
        {
            return BadRequest(new MessageResponseDto("This media is already in your library."));
        }

        var normalizedStatus = request.Status.Trim().ToLower();
        var isEpisodic = media.MediaType is "anime" or "series" or "manga";

        int? initialProgress = null;
        if (isEpisodic)
        {
            initialProgress = request.Progress ?? 0;
            if (media.TotalUnits.HasValue && initialProgress > media.TotalUnits.Value)
            {
                initialProgress = media.TotalUnits.Value;
            }
        }

        var isCompleted = normalizedStatus == "completed";
        if (isCompleted && isEpisodic && media.TotalUnits.HasValue)
        {
            initialProgress = media.TotalUnits.Value;
        }

        var entry = new LibraryEntry
        {
            UserId = userId.Value,
            MediaId = request.MediaId,
            Status = normalizedStatus,
            Progress = initialProgress,
            Platform = media.MediaType == "game" ? request.Platform : null,
            Rating = request.Rating,
            IsFavorite = request.IsFavorite,
            AddedAt = DateTime.UtcNow,
            CompletedAt = isCompleted ? DateTime.UtcNow : null
        };

        _context.LibraryEntries.Add(entry);
        await _context.SaveChangesAsync(ct);

        entry.Media = media;

        return StatusCode(StatusCodes.Status201Created, MapToDto(entry));
    }

    [HttpGet("{mediaId:int}")]
    [ProducesResponseType(typeof(LibraryEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetLibraryEntry(int mediaId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var entry = await _context.LibraryEntries
            .AsNoTracking()
            .Include(e => e.Media)
            .FirstOrDefaultAsync(e => e.UserId == userId.Value && e.MediaId == mediaId, ct);

        if (entry is null)
        {
            return NotFound(new MessageResponseDto("Media not found in your library."));
        }

        return Ok(MapToDto(entry));
    }

    [HttpPut("{mediaId:int}")]
    [ProducesResponseType(typeof(LibraryEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateLibraryEntry(int mediaId, [FromBody] UpdateLibraryRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var entry = await _context.LibraryEntries
            .Include(e => e.Media)
            .FirstOrDefaultAsync(e => e.UserId == userId.Value && e.MediaId == mediaId, ct);

        if (entry is null)
        {
            return NotFound(new MessageResponseDto("Media not found in your library."));
        }

        var isEpisodic = entry.Media.MediaType is "anime" or "series" or "manga";

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var newStatus = request.Status.Trim().ToLower();
            if (newStatus == "completed" && entry.Status != "completed")
            {
                entry.CompletedAt = DateTime.UtcNow;
                if (isEpisodic && entry.Media.TotalUnits.HasValue)
                {
                    entry.Progress = entry.Media.TotalUnits.Value;
                }
            }
            else if (newStatus != "completed")
            {
                entry.CompletedAt = null;
            }
            entry.Status = newStatus;
        }

        if (isEpisodic && request.Progress.HasValue)
        {
            var newProgress = request.Progress.Value;
            if (entry.Media.TotalUnits.HasValue && newProgress >= entry.Media.TotalUnits.Value)
            {
                newProgress = entry.Media.TotalUnits.Value;
                entry.Status = "completed";
                entry.CompletedAt = DateTime.UtcNow;
            }
            else if (entry.Status == "completed" && entry.Media.TotalUnits.HasValue && newProgress < entry.Media.TotalUnits.Value && string.IsNullOrWhiteSpace(request.Status))
            {
                entry.Status = "in_progress";
                entry.CompletedAt = null;
            }
            entry.Progress = newProgress;
        }

        if (entry.Media.MediaType == "game" && request.Platform is not null)
        {
            entry.Platform = request.Platform;
        }

        if (request.Rating.HasValue)
        {
            entry.Rating = request.Rating.Value;
        }

        if (request.IsFavorite.HasValue)
        {
            entry.IsFavorite = request.IsFavorite.Value;
        }

        entry.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return Ok(MapToDto(entry));
    }

    [HttpPatch("{mediaId:int}/status")]
    [ProducesResponseType(typeof(LibraryEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateStatus(int mediaId, [FromBody] UpdateLibraryStatusRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var entry = await _context.LibraryEntries
            .Include(e => e.Media)
            .FirstOrDefaultAsync(e => e.UserId == userId.Value && e.MediaId == mediaId, ct);

        if (entry is null)
        {
            return NotFound(new MessageResponseDto("Media not found in your library."));
        }

        var newStatus = request.Status.Trim().ToLower();
        var isEpisodic = entry.Media.MediaType is "anime" or "series" or "manga";

        if (newStatus == "completed" && entry.Status != "completed")
        {
            entry.CompletedAt = DateTime.UtcNow;
            if (isEpisodic && entry.Media.TotalUnits.HasValue)
            {
                entry.Progress = entry.Media.TotalUnits.Value;
            }
        }
        else if (newStatus != "completed")
        {
            entry.CompletedAt = null;
        }

        entry.Status = newStatus;
        entry.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        return Ok(MapToDto(entry));
    }

    [HttpPost("{mediaId:int}/progress")]
    [ProducesResponseType(typeof(LibraryEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateProgress(int mediaId, [FromBody] UpdateLibraryProgressRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var entry = await _context.LibraryEntries
            .Include(e => e.Media)
            .FirstOrDefaultAsync(e => e.UserId == userId.Value && e.MediaId == mediaId, ct);

        if (entry is null)
        {
            return NotFound(new MessageResponseDto("Media not found in your library."));
        }

        var isEpisodic = entry.Media.MediaType is "anime" or "series" or "manga";
        if (!isEpisodic)
        {
            return BadRequest(new MessageResponseDto("Progress tracking is only applicable to anime, series, and manga. For movies and games, please update the status."));
        }

        var newProgress = request.Progress;
        if (entry.Media.TotalUnits.HasValue && newProgress >= entry.Media.TotalUnits.Value)
        {
            newProgress = entry.Media.TotalUnits.Value;
            entry.Status = "completed";
            entry.CompletedAt = DateTime.UtcNow;
        }
        else if (entry.Status == "completed" && entry.Media.TotalUnits.HasValue && newProgress < entry.Media.TotalUnits.Value)
        {
            entry.Status = "in_progress";
            entry.CompletedAt = null;
        }
        else if (entry.Status == "planning" && newProgress > 0)
        {
            entry.Status = "in_progress";
        }

        entry.Progress = newProgress;
        entry.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        return Ok(MapToDto(entry));
    }

    [HttpDelete("{mediaId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RemoveFromLibrary(int mediaId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var entry = await _context.LibraryEntries
            .FirstOrDefaultAsync(e => e.UserId == userId.Value && e.MediaId == mediaId, ct);

        if (entry is null)
        {
            return NotFound(new MessageResponseDto("Media not found in your library."));
        }

        _context.LibraryEntries.Remove(entry);
        await _context.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpGet("continue")]
    [ProducesResponseType(typeof(List<LibraryEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetContinue(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var entries = await _context.LibraryEntries
            .AsNoTracking()
            .Include(e => e.Media)
            .Where(e => e.UserId == userId.Value && (e.Status == "in_progress" || e.Status == "playing" || e.Status == "reading"))
            .OrderByDescending(e => e.UpdatedAt ?? e.AddedAt)
            .Take(10)
            .ToListAsync(ct);

        var dtos = entries.Select(MapToDto).ToList();

        return Ok(dtos);
    }

    // --- Private Helpers ---

    private int? GetCurrentUserId()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idClaim, out var id) ? id : null;
    }

    private static LibraryEntryDto MapToDto(LibraryEntry entry)
    {
        return new LibraryEntryDto
        {
            Id = entry.Id,
            UserId = entry.UserId,
            MediaId = entry.MediaId,
            MediaType = entry.Media.MediaType,
            Status = entry.Status,
            IsFavorite = entry.IsFavorite,
            Rating = entry.Rating,
            Platform = entry.Platform,
            Progress = entry.Progress,
            TotalUnits = entry.Media.TotalUnits,
            UnitName = entry.Media.UnitName,
            AddedAt = entry.AddedAt,
            UpdatedAt = entry.UpdatedAt,
            CompletedAt = entry.CompletedAt,
            Media = new LibraryMediaSummaryDto
            {
                Id = entry.Media.Id,
                Title = entry.Media.Title,
                MediaType = entry.Media.MediaType,
                CoverImage = entry.Media.CoverImage,
                Year = entry.Media.Year,
                Score = entry.Media.Score,
                TotalUnits = entry.Media.TotalUnits,
                UnitName = entry.Media.UnitName,
                RuntimeMinutes = entry.Media.RuntimeMinutes,
                Platforms = entry.Media.Platforms
            }
        };
    }
}