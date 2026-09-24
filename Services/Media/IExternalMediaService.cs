using Kue.Api.Dtos.Media;

namespace Kue.Api.Services.Media;

public interface IExternalMediaService
{
    Task<PagedResponseDto<MediaDto>> SearchAsync(string query, string? mediaType = null, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<MediaDto?> GetDetailsAsync(string mediaType, string externalSource, string externalId, CancellationToken ct = default);
    Task<PagedResponseDto<MediaDto>> GetTrendingAsync(string? mediaType = null, int page = 1, int pageSize = 20, CancellationToken ct = default);
}
