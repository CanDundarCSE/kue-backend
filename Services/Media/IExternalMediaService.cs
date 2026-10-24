using Kue.Api.Dtos.Media;

namespace Kue.Api.Services.Media;

public interface IExternalMediaService
{
    Task<PagedResponseDto<MediaDto>> SearchAsync(string query, string? mediaType = null, int page = 1, int pageSize = 20, string? sort = null, int? year = null, double? minScore = null, CancellationToken ct = default);
    Task<MediaDto?> GetDetailsAsync(string mediaType, string externalSource, string externalId, CancellationToken ct = default);

    /// <summary>
    /// Maps an IMDb id to a TMDB id using TMDB's documented
    /// <c>/find</c> endpoint. Results are cached in memory (including negative
    /// results, briefly) so repeated playback starts do not hit TMDB.
    /// </summary>
    Task<string?> FindTmdbIdByImdbIdAsync(string imdbId, string mediaType, CancellationToken ct = default);
    Task<PagedResponseDto<MediaDto>> GetTrendingAsync(string? mediaType = null, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<PagedResponseDto<MediaDto>> GetUpcomingAsync(string? mediaType = null, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<PagedResponseDto<MediaDto>> GetTopAsync(string? mediaType = null, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<PagedResponseDto<MediaDto>> GetSimilarAsync(
        string mediaType,
        string? externalSource = null,
        string? externalId = null,
        IEnumerable<string>? genres = null,
        int pageSize = 10,
        CancellationToken ct = default);
}
