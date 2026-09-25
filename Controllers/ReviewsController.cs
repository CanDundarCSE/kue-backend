using System.Security.Claims;
using Kue.Api.Data;
using Kue.Api.Dtos.Common;
using Kue.Api.Dtos.Media;
using Kue.Api.Dtos.Reviews;
using Kue.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kue.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public class ReviewsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<ReviewsController> _logger;

    public ReviewsController(AppDbContext context, ILogger<ReviewsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpPost("media/{mediaId:int}/reviews")]
    [Authorize]
    [ProducesResponseType(typeof(ReviewDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateReview(int mediaId, [FromBody] CreateReviewRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var media = await _context.Media.FirstOrDefaultAsync(m => m.Id == mediaId, ct);
        if (media is null)
        {
            return NotFound(new MessageResponseDto($"Media with ID {mediaId} not found."));
        }

        var alreadyReviewed = await _context.Reviews
            .AnyAsync(r => r.UserId == userId.Value && r.MediaId == mediaId, ct);

        if (alreadyReviewed)
        {
            return BadRequest(new MessageResponseDto("You have already reviewed this media. Use PUT /api/v1/reviews/{reviewId} to update your existing review."));
        }

        var review = new Review
        {
            UserId = userId.Value,
            MediaId = mediaId,
            Content = request.Content.Trim(),
            Rating = request.Rating,
            ContainsSpoilers = request.ContainsSpoilers,
            LikesCount = 0,
            CreatedAt = DateTime.UtcNow
        };

        _context.Reviews.Add(review);

        // If a rating is specified, also sync with the user's LibraryEntry rating
        if (request.Rating.HasValue)
        {
            var libraryEntry = await _context.LibraryEntries
                .FirstOrDefaultAsync(e => e.UserId == userId.Value && e.MediaId == mediaId, ct);

            if (libraryEntry != null)
            {
                libraryEntry.Rating = request.Rating.Value;
                libraryEntry.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync(ct);

        var author = await _context.Users.AsNoTracking().FirstAsync(u => u.Id == userId.Value, ct);

        var dto = MapToDto(review, author, media, isLikedByCurrentUser: false);
        return StatusCode(StatusCodes.Status201Created, dto);
    }

    [HttpGet("media/{mediaId:int}/reviews")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PagedResponseDto<ReviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMediaReviews(
        int mediaId,
        [FromQuery] string sort = "newest",
        [FromQuery] bool? spoilers = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        var media = await _context.Media.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mediaId, ct);
        if (media is null)
        {
            return NotFound(new MessageResponseDto($"Media with ID {mediaId} not found."));
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var currentUserId = GetCurrentUserId();

        var query = _context.Reviews
            .AsNoTracking()
            .Include(r => r.User)
            .Include(r => r.Media)
            .Where(r => r.MediaId == mediaId);

        if (spoilers.HasValue)
        {
            query = query.Where(r => r.ContainsSpoilers == spoilers.Value);
        }

        query = sort.Trim().ToLowerInvariant() switch
        {
            "most_liked" or "likes" or "popular" => query.OrderByDescending(r => r.LikesCount).ThenByDescending(r => r.CreatedAt),
            "highest_rated" or "top" => query.OrderByDescending(r => r.Rating).ThenByDescending(r => r.CreatedAt),
            "lowest_rated" => query.OrderBy(r => r.Rating).ThenByDescending(r => r.CreatedAt),
            _ => query.OrderByDescending(r => r.CreatedAt)
        };

        var totalItems = await query.CountAsync(ct);
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var reviews = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // Fetch which reviews the current user has liked
        var likedReviewIds = new HashSet<int>();
        if (currentUserId.HasValue && reviews.Count > 0)
        {
            var reviewIds = reviews.Select(r => r.Id).ToList();
            var liked = await _context.ReviewLikes
                .AsNoTracking()
                .Where(l => l.UserId == currentUserId.Value && reviewIds.Contains(l.ReviewId))
                .Select(l => l.ReviewId)
                .ToListAsync(ct);

            likedReviewIds = [.. liked];
        }

        var dtos = reviews
            .Select(r => MapToDto(r, r.User, r.Media, likedReviewIds.Contains(r.Id)))
            .ToList();

        return Ok(new PagedResponseDto<ReviewDto>
        {
            Items = dtos,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalPages
        });
    }

    [HttpGet("reviews/{reviewId:int}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ReviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReview(int reviewId, CancellationToken ct)
    {
        var review = await _context.Reviews
            .AsNoTracking()
            .Include(r => r.User)
            .Include(r => r.Media)
            .FirstOrDefaultAsync(r => r.Id == reviewId, ct);

        if (review is null)
        {
            return NotFound(new MessageResponseDto($"Review with ID {reviewId} not found."));
        }

        var currentUserId = GetCurrentUserId();
        var isLiked = false;
        if (currentUserId.HasValue)
        {
            isLiked = await _context.ReviewLikes
                .AsNoTracking()
                .AnyAsync(l => l.ReviewId == reviewId && l.UserId == currentUserId.Value, ct);
        }

        return Ok(MapToDto(review, review.User, review.Media, isLiked));
    }

    [HttpPut("reviews/{reviewId:int}")]
    [Authorize]
    [ProducesResponseType(typeof(ReviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateReview(int reviewId, [FromBody] UpdateReviewRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var review = await _context.Reviews
            .Include(r => r.User)
            .Include(r => r.Media)
            .FirstOrDefaultAsync(r => r.Id == reviewId, ct);

        if (review is null)
        {
            return NotFound(new MessageResponseDto($"Review with ID {reviewId} not found."));
        }

        if (review.UserId != userId.Value)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageResponseDto("You can only edit your own reviews."));
        }

        review.Content = request.Content.Trim();
        review.Rating = request.Rating;
        review.ContainsSpoilers = request.ContainsSpoilers;
        review.UpdatedAt = DateTime.UtcNow;

        // Sync rating with LibraryEntry if present
        if (request.Rating.HasValue)
        {
            var libraryEntry = await _context.LibraryEntries
                .FirstOrDefaultAsync(e => e.UserId == userId.Value && e.MediaId == review.MediaId, ct);

            if (libraryEntry != null)
            {
                libraryEntry.Rating = request.Rating.Value;
                libraryEntry.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync(ct);

        var isLiked = await _context.ReviewLikes
            .AsNoTracking()
            .AnyAsync(l => l.ReviewId == reviewId && l.UserId == userId.Value, ct);

        return Ok(MapToDto(review, review.User, review.Media, isLiked));
    }

    [HttpDelete("reviews/{reviewId:int}")]
    [Authorize]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteReview(int reviewId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var review = await _context.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId, ct);
        if (review is null)
        {
            return NotFound(new MessageResponseDto($"Review with ID {reviewId} not found."));
        }

        var isOwner = review.UserId == userId.Value;
        var isAdmin = User.IsInRole("Admin");

        if (!isOwner && !isAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageResponseDto("You can only delete your own reviews."));
        }

        _context.Reviews.Remove(review);
        await _context.SaveChangesAsync(ct);

        return Ok(new MessageResponseDto("Review deleted successfully!"));
    }

    [HttpPost("reviews/{reviewId:int}/like")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LikeReview(int reviewId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var review = await _context.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId, ct);
        if (review is null)
        {
            return NotFound(new MessageResponseDto($"Review with ID {reviewId} not found."));
        }

        var existingLike = await _context.ReviewLikes
            .FirstOrDefaultAsync(l => l.ReviewId == reviewId && l.UserId == userId.Value, ct);

        if (existingLike is null)
        {
            _context.ReviewLikes.Add(new ReviewLike
            {
                ReviewId = reviewId,
                UserId = userId.Value,
                CreatedAt = DateTime.UtcNow
            });

            review.LikesCount++;
            await _context.SaveChangesAsync(ct);
        }

        return Ok(new
        {
            reviewId,
            likesCount = review.LikesCount,
            isLiked = true,
            message = "Review liked successfully!"
        });
    }

    [HttpDelete("reviews/{reviewId:int}/like")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UnlikeReview(int reviewId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var review = await _context.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId, ct);
        if (review is null)
        {
            return NotFound(new MessageResponseDto($"Review with ID {reviewId} not found."));
        }

        var existingLike = await _context.ReviewLikes
            .FirstOrDefaultAsync(l => l.ReviewId == reviewId && l.UserId == userId.Value, ct);

        if (existingLike != null)
        {
            _context.ReviewLikes.Remove(existingLike);
            review.LikesCount = Math.Max(0, review.LikesCount - 1);
            await _context.SaveChangesAsync(ct);
        }

        return Ok(new
        {
            reviewId,
            likesCount = review.LikesCount,
            isLiked = false,
            message = "Review unliked successfully!"
        });
    }

    // --- Private Helpers ---

    private int? GetCurrentUserId()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idClaim, out var id) ? id : null;
    }

    private static ReviewDto MapToDto(Review review, User author, Entities.Media media, bool isLikedByCurrentUser)
    {
        return new ReviewDto
        {
            Id = review.Id,
            UserId = author.Id,
            Username = author.Username,
            MediaId = media.Id,
            MediaTitle = media.Title,
            MediaType = media.MediaType,
            MediaCoverImage = media.CoverImage,
            MediaYear = media.Year,
            Content = review.Content,
            Rating = review.Rating,
            ContainsSpoilers = review.ContainsSpoilers,
            LikesCount = review.LikesCount,
            IsLikedByCurrentUser = isLikedByCurrentUser,
            CreatedAt = review.CreatedAt,
            UpdatedAt = review.UpdatedAt
        };
    }
}