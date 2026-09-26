using System.Security.Claims;
using Kue.Api.Data;
using Kue.Api.Dtos.Common;
using Kue.Api.Dtos.Media;
using Kue.Api.Dtos.Ratings;
using Kue.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kue.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public class RatingsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<RatingsController> _logger;

    public RatingsController(AppDbContext context, ILogger<RatingsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet("me/ratings")]
    [Authorize]
    [ProducesResponseType(typeof(PagedResponseDto<UserRatingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyRatings(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.LibraryEntries
            .AsNoTracking()
            .Include(e => e.Media)
            .Where(e => e.UserId == userId.Value && e.Rating.HasValue);

        var totalItems = await query.CountAsync(ct);
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var entries = await query
            .OrderByDescending(e => e.UpdatedAt ?? e.AddedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var dtos = entries.Select(e => new UserRatingDto
        {
            MediaId = e.MediaId,
            MediaTitle = e.Media.Title,
            MediaType = e.Media.MediaType,
            MediaCoverImage = e.Media.CoverImage,
            MediaYear = e.Media.Year,
            Rating = e.Rating!.Value,
            RatedAt = e.UpdatedAt ?? e.AddedAt
        }).ToList();

        return Ok(new PagedResponseDto<UserRatingDto>
        {
            Items = dtos,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalPages
        });
    }

    [HttpPut("me/ratings/{mediaId:int}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateRating(int mediaId, [FromBody] SetRatingRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var media = await _context.Media.FirstOrDefaultAsync(m => m.Id == mediaId, ct);
        if (media is null)
        {
            return NotFound(new MessageResponseDto($"Media with ID {mediaId} not found."));
        }

        var entry = await _context.LibraryEntries
            .FirstOrDefaultAsync(e => e.UserId == userId.Value && e.MediaId == mediaId, ct);

        if (entry != null)
        {
            entry.Rating = request.Rating;
            entry.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            // If user hasn't added this media to library yet, add it automatically as completed with this rating
            entry = new LibraryEntry
            {
                UserId = userId.Value,
                MediaId = mediaId,
                Status = "completed",
                Rating = request.Rating,
                Progress = media.TotalUnits,
                AddedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            };
            _context.LibraryEntries.Add(entry);
        }

        await _context.SaveChangesAsync(ct);

        return Ok(new
        {
            mediaId,
            rating = request.Rating,
            message = "Rating updated successfully!"
        });
    }

    [HttpDelete("me/ratings/{mediaId:int}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteRating(int mediaId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var entry = await _context.LibraryEntries
            .FirstOrDefaultAsync(e => e.UserId == userId.Value && e.MediaId == mediaId, ct);

        if (entry is null || !entry.Rating.HasValue)
        {
            return NotFound(new MessageResponseDto("Rating not found for this media."));
        }

        entry.Rating = null;
        entry.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        return Ok(new
        {
            mediaId,
            message = "Rating deleted successfully!"
        });
    }

    [HttpGet("media/{mediaId:int}/ratings")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PagedResponseDto<PublicUserRatingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMediaRatings(
        int mediaId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var mediaExists = await _context.Media.AnyAsync(m => m.Id == mediaId, ct);
        if (!mediaExists)
        {
            return NotFound(new MessageResponseDto($"Media with ID {mediaId} not found."));
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var currentUserId = GetCurrentUserId();

        var query = _context.LibraryEntries
            .AsNoTracking()
            .Include(e => e.User)
            .Where(e => e.MediaId == mediaId && e.Rating.HasValue);

        // Privacy: Filter out ratings from private accounts, unless the requesting user is the owner
        query = query.Where(e => !e.User.IsPrivate || (currentUserId.HasValue && e.UserId == currentUserId.Value));

        var totalItems = await query.CountAsync(ct);
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var entries = await query
            .OrderByDescending(e => e.UpdatedAt ?? e.AddedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var dtos = entries.Select(e => new PublicUserRatingDto
        {
            UserId = e.UserId,
            Username = e.User.Username,
            Rating = e.Rating!.Value,
            RatedAt = e.UpdatedAt ?? e.AddedAt
        }).ToList();

        return Ok(new PagedResponseDto<PublicUserRatingDto>
        {
            Items = dtos,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalPages
        });
    }

    [HttpGet("media/{mediaId:int}/ratings/summary")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(MediaRatingSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMediaRatingSummary(int mediaId, CancellationToken ct)
    {
        var mediaExists = await _context.Media.AnyAsync(m => m.Id == mediaId, ct);
        if (!mediaExists)
        {
            return NotFound(new MessageResponseDto($"Media with ID {mediaId} not found."));
        }

        var ratings = await _context.LibraryEntries
            .AsNoTracking()
            .Where(e => e.MediaId == mediaId && e.Rating.HasValue)
            .Select(e => e.Rating!.Value)
            .ToListAsync(ct);

        var distribution = new Dictionary<int, int>();
        for (int i = 1; i <= 10; i++)
        {
            distribution[i] = 0;
        }

        foreach (var r in ratings)
        {
            if (distribution.ContainsKey(r))
            {
                distribution[r]++;
            }
        }

        var average = ratings.Count > 0 ? Math.Round(ratings.Average(), 1) : 0;

        return Ok(new MediaRatingSummaryDto
        {
            MediaId = mediaId,
            AverageRating = average,
            TotalRatings = ratings.Count,
            Distribution = distribution
        });
    }

    // --- Private Helpers ---

    private int? GetCurrentUserId()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idClaim, out var id) ? id : null;
    }
}