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

    public async Task<ReviewDto?> GetReviewByIdAsync(int reviewId, int? currentUserId = null, CancellationToken ct = default)
    {
        var review = await _context.Reviews
            .AsNoTracking()
            .Include(r => r.User)
            .Include(r => r.Media)
            .FirstOrDefaultAsync(r => r.Id == reviewId, ct);

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
            .Where(r => r.MediaId == mediaId);

        var totalCount = await query.CountAsync(ct);

        var reviews = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var dtos = reviews.Select(r => MapToDto(r)).ToList();

        return (dtos, totalCount);
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

        var review = new Review
        {
            UserId = userId,
            MediaId = request.MediaId,
            Content = request.Content.Trim(),
            Rating = request.Rating,
            ContainsSpoilers = request.ContainsSpoilers,
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

        review.Content = request.Content.Trim();
        review.Rating = request.Rating;
        review.ContainsSpoilers = request.ContainsSpoilers;
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
            Content = review.Content,
            Rating = review.Rating,
            ContainsSpoilers = review.ContainsSpoilers,
            CreatedAt = review.CreatedAt,
            UpdatedAt = review.UpdatedAt
        };
    }
}