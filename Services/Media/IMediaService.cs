using Kue.Api.Dtos.Library;
using Kue.Api.Dtos.Media;
using Kue.Api.Entities;

namespace Kue.Api.Services.Media;

public interface IMediaService
{
    Task<Entities.Media> GetOrCreateMediaAsync(AddToLibraryRequest request, CancellationToken ct = default);
    Task<PagedResponseDto<MediaDto>> SearchMediaAsync(string query, string? mediaType = null, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<MediaDto?> GetMediaDetailsAsync(int id, CancellationToken ct = default);
    Task<PagedResponseDto<MediaDto>> GetTrendingMediaAsync(string? mediaType = null, int page = 1, int pageSize = 20, CancellationToken ct = default);
}
