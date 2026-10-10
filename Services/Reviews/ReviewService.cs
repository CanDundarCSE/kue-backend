using Kue.Api.Data;
using Kue.Api.Dtos.Reviews;
using Kue.Api.Entities;
using Kue.Api.Services.Reviews;
using Microsoft.EntityFrameworkCore;

namespace Kue.Api.Services.Reviews;

public class ReviewService : IReviewService
{
    private readonly AppDbContext _context;
    private readonly ILogger<ReviewService> _logger;

    public ReviewService(AppDbContext context, ILogger<ReviewService> logger)
    {
        _context = context;
        _logger = logger;
    }

    private const int MaxContentLength = 255;

    public async Task<ReviewDto?> GetReviewByIdAsync(int reviewId, int? currentUserId = null, CancellationToken ct = default)
    {
        var review = await _context.Reviews
            .AsNoTracking()
            .Include(r => r.User)
            .Include(r => r.Media)
            // A private review is only readable by its author.
            .Where(r => r.Id == reviewId && (r.IsPublic || r.UserId == currentUserId))
            .FirstOrDefaultAsync(ct);

        if (review is null)
            return null;

        return MapToDto(review);
    }

    public async Task<(List<ReviewDto> Reviews, int TotalCount)> GetMediaReviewsAsync(
        int mediaId,
        int page,
        int pageSize,
        int? currentUserId = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var mediaExists = await _context.Media.AnyAsync(m => m.Id == mediaId, ct);
        if (!mediaExists)
        {
            return ([], 0);
        }

        var query = _context.Reviews
            .AsNoTracking()
            .Include(r => r.User)
            .Include(r => r.Media)
            .Where(r => r.MediaId == mediaId)
            // Public feed: other people's private reviews stay hidden, but the
            // signed-in author still sees their own.
            .Where(r => r.IsPublic || r.UserId == currentUserId);

        var totalCount = await query.CountAsync(ct);

        var reviews = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var dtos = reviews.Select(r => MapToDto(r)).ToList();

        return (dtos, totalCount);
    }

    public async Task<(List<ReviewDto> Reviews, int TotalCount)> GetUserReviewsAsync(
        int userId,
        int page,
        int pageSize,
        bool? isPublic = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = _context.Reviews
            .AsNoTracking()
            .Include(r => r.User)
            .Include(r => r.Media)
            .Where(r => r.UserId == userId);

        if (isPublic.HasValue)
        {
            query = query.Where(r => r.IsPublic == isPublic.Value);
        }

        var totalCount = await query.CountAsync(ct);

        var reviews = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (reviews.Select(r => MapToDto(r)).ToList(), totalCount);
    }

    public async Task<ReviewDto?> GetUserReviewByMediaAsync(int userId, int mediaId, CancellationToken ct = default)
    {
        var review = await _context.Reviews
            .AsNoTracking()
            .Include(r => r.User)
            .Include(r => r.Media)
            .FirstOrDefaultAsync(r => r.UserId == userId && r.MediaId == mediaId, ct);

        return review is null ? null : MapToDto(review);
    }

    public async Task<ReviewDto> CreateReviewAsync(int userId, CreateReviewRequest request, CancellationToken ct = default)
    {
        var media = await _context.Media.FirstOrDefaultAsync(m => m.Id == request.MediaId, ct);
        if (media is null)
        {
            throw new ArgumentException($"Media with ID {request.MediaId} not found.");
        }

        var existingReview = await _context.Reviews
            .FirstOrDefaultAsync(r => r.UserId == userId && r.MediaId == request.MediaId, ct);

        if (existingReview != null)
        {
            throw new InvalidOperationException("You have already reviewed this media.");
        }

        if (request.Rating.HasValue && (request.Rating < 1 || request.Rating > 10))
        {
            throw new ArgumentException("Rating must be between 1 and 10.");
        }

        var content = request.Content?.Trim() ?? string.Empty;
        if (content.Length == 0)
        {
            throw new ArgumentException("Review content cannot be empty.");
        }

        // The column is varchar(255); without this guard an over-long review
        // surfaces as an opaque 500 from the provider.
        if (content.Length > MaxContentLength)
        {
            throw new ArgumentException($"Review content cannot exceed {MaxContentLength} characters.");
        }

        var review = new Review
        {
            UserId = userId,
            MediaId = request.MediaId,
            Content = content,
            Rating = request.Rating,
            ContainsSpoilers = request.ContainsSpoilers,
            IsPublic = request.IsPublic,
            CreatedAt = DateTime.UtcNow
        };

        _context.Reviews.Add(review);
        await _context.SaveChangesAsync(ct);

        await _context.Entry(review).Reference(r => r.User).LoadAsync(ct);
        await _context.Entry(review).Reference(r => r.Media).LoadAsync(ct);

        return MapToDto(review);
    }

    public async Task<ReviewDto?> UpdateReviewAsync(int userId, int reviewId, UpdateReviewRequest request, CancellationToken ct = default)
    {
        var review = await _context.Reviews
            .Include(r => r.User)
            .Include(r => r.Media)
            .FirstOrDefaultAsync(r => r.Id == reviewId && r.UserId == userId, ct);

        if (review is null)
            return null;

        if (request.Rating.HasValue && (request.Rating < 1 || request.Rating > 10))
        {
            throw new ArgumentException("Rating must be between 1 and 10.");
        }

        var content = request.Content?.Trim() ?? string.Empty;
        if (content.Length == 0)
        {
            throw new ArgumentException("Review content cannot be empty.");
        }

        if (content.Length > MaxContentLength)
        {
            throw new ArgumentException($"Review content cannot exceed {MaxContentLength} characters.");
        }

        review.Content = content;
        review.Rating = request.Rating;
        review.ContainsSpoilers = request.ContainsSpoilers;
        review.IsPublic = request.IsPublic;
        review.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        return MapToDto(review);
    }

    public async Task<bool> DeleteReviewAsync(int userId, int reviewId, CancellationToken ct = default)
    {
        var review = await _context.Reviews
            .FirstOrDefaultAsync(r => r.Id == reviewId && r.UserId == userId, ct);

        if (review is null)
            return false;

        _context.Reviews.Remove(review);
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> HasUserReviewedMediaAsync(int userId, int mediaId, CancellationToken ct = default)
    {
        return await _context.Reviews
            .AnyAsync(r => r.UserId == userId && r.MediaId == mediaId, ct);
    }

    private ReviewDto MapToDto(Review review)
    {
        return new ReviewDto
        {
            Id = review.Id,
            UserId = review.UserId,
            Username = review.User.Username,
            MediaId = review.MediaId,
            MediaTitle = review.Media.Title,
            MediaType = review.Media.MediaType,
            MediaCoverImage = review.Media.CoverImage ?? string.Empty,
            MediaYear = review.Media.Year,
            Content = review.Content,
            Rating = review.Rating,
            ContainsSpoilers = review.ContainsSpoilers,
            IsPublic = review.IsPublic,
            CreatedAt = review.CreatedAt,
            UpdatedAt = review.UpdatedAt
        };
    }
}