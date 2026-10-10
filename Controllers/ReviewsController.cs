using System.Security.Claims;
using Kue.Api.Data;
using Kue.Api.Dtos.Common;
using Kue.Api.Dtos.Media;
using Kue.Api.Dtos.Reviews;
using Kue.Api.Services.Activity;
using Kue.Api.Services.Reviews;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kue.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public class ReviewsController : ControllerBase
{
    private readonly IReviewService _reviewService;
    private readonly IActivityService _activityService;
    private readonly AppDbContext _context;
    private readonly ILogger<ReviewsController> _logger;

    public ReviewsController(
        IReviewService reviewService,
        IActivityService activityService,
        AppDbContext context,
        ILogger<ReviewsController> logger)
    {
        _reviewService = reviewService;
        _activityService = activityService;
        _context = context;
        _logger = logger;
    }

    [HttpGet("media/{mediaId:int}/reviews")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PagedResponseDto<ReviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMediaReviews(
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
        pageSize = Math.Clamp(pageSize, 1, 50);

        var currentUserId = GetCurrentUserId();

        var (reviews, totalCount) = await _reviewService.GetMediaReviewsAsync(
            mediaId, page, pageSize, currentUserId, ct);

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return Ok(new PagedResponseDto<ReviewDto>
        {
            Items = reviews,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalCount,
            TotalPages = totalPages
        });
    }

    [HttpGet("reviews/{reviewId:int}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ReviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReview(int reviewId, CancellationToken ct = default)
    {
        var currentUserId = GetCurrentUserId();
        var review = await _reviewService.GetReviewByIdAsync(reviewId, currentUserId, ct);

        if (review is null)
        {
            return NotFound(new MessageResponseDto("Review not found."));
        }

        return Ok(review);
    }

    [HttpGet("me/reviews")]
    [Authorize]
    [ProducesResponseType(typeof(PagedResponseDto<ReviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyReviews(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool? isPublic = null,
        CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var (reviews, totalCount) = await _reviewService.GetUserReviewsAsync(
            userId.Value, page, pageSize, isPublic, ct);

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return Ok(new PagedResponseDto<ReviewDto>
        {
            Items = reviews,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalCount,
            TotalPages = totalPages
        });
    }

    [HttpGet("me/reviews/by-media/{mediaId:int}")]
    [Authorize]
    [ProducesResponseType(typeof(ReviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyReviewForMedia(int mediaId, CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var review = await _reviewService.GetUserReviewByMediaAsync(userId.Value, mediaId, ct);
        if (review is null)
            return NotFound(new MessageResponseDto("Review not found."));

        return Ok(review);
    }

    [HttpPost("me/reviews")]
    [Authorize]
    [ProducesResponseType(typeof(ReviewDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateReview([FromBody] CreateReviewRequest request, CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        try
        {
            var review = await _reviewService.CreateReviewAsync(userId.Value, request, ct);

            try
            {
                await _activityService.LogActivityAsync(
                    userId: userId.Value,
                    activityType: "reviewed",
                    mediaId: request.MediaId,
                    rating: request.Rating,
                    detailText: request.ContainsSpoilers ? "with spoilers" : null,
                    ct: ct);
            }
            catch
            {
                // Ignore activity log failure
            }

            return CreatedAtAction(nameof(GetReview), new { reviewId = review.Id }, review);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new MessageResponseDto(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new MessageResponseDto(ex.Message));
        }
    }

    [HttpPut("me/reviews/{reviewId:int}")]
    [Authorize]
    [ProducesResponseType(typeof(ReviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateReview(int reviewId, [FromBody] UpdateReviewRequest request, CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        try
        {
            var review = await _reviewService.UpdateReviewAsync(userId.Value, reviewId, request, ct);

            if (review is null)
            {
                return NotFound(new MessageResponseDto("Review not found."));
            }

            return Ok(review);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new MessageResponseDto(ex.Message));
        }
    }

    [HttpDelete("me/reviews/{reviewId:int}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteReview(int reviewId, CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var deleted = await _reviewService.DeleteReviewAsync(userId.Value, reviewId, ct);

        if (!deleted)
        {
            return NotFound(new MessageResponseDto("Review not found."));
        }

        return Ok(new { message = "Review deleted successfully!" });
    }

    // --- Private Helpers ---

    private int? GetCurrentUserId()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idClaim, out var id) ? id : null;
    }
}