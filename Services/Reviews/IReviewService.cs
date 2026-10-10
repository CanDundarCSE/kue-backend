using Kue.Api.Dtos.Reviews;

namespace Kue.Api.Services.Reviews;

public interface IReviewService
{
    Task<ReviewDto?> GetReviewByIdAsync(int reviewId, int? currentUserId = null, CancellationToken ct = default);
    Task<(List<ReviewDto> Reviews, int TotalCount)> GetMediaReviewsAsync(
        int mediaId,
        int page,
        int pageSize,
        int? currentUserId = null,
        CancellationToken ct = default);
    Task<ReviewDto> CreateReviewAsync(int userId, CreateReviewRequest request, CancellationToken ct = default);
    Task<ReviewDto?> UpdateReviewAsync(int userId, int reviewId, UpdateReviewRequest request, CancellationToken ct = default);
    Task<bool> DeleteReviewAsync(int userId, int reviewId, CancellationToken ct = default);
    Task<bool> HasUserReviewedMediaAsync(int userId, int mediaId, CancellationToken ct = default);
}