using Kue.Api.Data;
using Kue.Api.Dtos.Library;
using Kue.Api.Services.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Kue.Api.Services.Playback;

public interface IPlaybackMediaResolver
{
    /// <summary>
    /// Resolves a parsed addon id to a Kue media row, hydrating it from TMDB
    /// when needed. Returns null when the id cannot be mapped (unconfigured
    /// TMDB, unknown id, upstream failure); nothing is tracked in that case.
    /// </summary>
    Task<Entities.Media?> ResolveAsync(AddonVideoId id, CancellationToken ct = default);
}

public class PlaybackMediaResolver : IPlaybackMediaResolver
{
    private readonly AppDbContext _context;
    private readonly IExternalMediaService _externalMediaService;
    private readonly IMediaService _mediaService;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PlaybackMediaResolver> _logger;

    private static readonly TimeSpan UnresolvedCacheDuration = TimeSpan.FromMinutes(5);

    public PlaybackMediaResolver(
        AppDbContext context,
        IExternalMediaService externalMediaService,
        IMediaService mediaService,
        IConfiguration configuration,
        IMemoryCache cache,
        ILogger<PlaybackMediaResolver> logger)
    {
        _context = context;
        _externalMediaService = externalMediaService;
        _mediaService = mediaService;
        _configuration = configuration;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Entities.Media?> ResolveAsync(AddonVideoId id, CancellationToken ct = default)
    {
        var tmdbId = id.ExternalId;

        if (id.ExternalSource == "imdb")
        {
            // Cached (in-memory) lookup; the resolver is also only reached for
            // ids not already present in the local catalog.
            tmdbId = await _externalMediaService.FindTmdbIdByImdbIdAsync(id.ExternalId, id.MediaType, ct);
            if (string.IsNullOrWhiteSpace(tmdbId))
            {
                _logger.LogInformation("No TMDB mapping for {MediaType} {ImdbId}; playback will not be tracked",
                    id.MediaType, id.ExternalId);
                return null;
            }
        }

        var existing = await _context.Media
            .FirstOrDefaultAsync(m =>
                m.MediaType == id.MediaType &&
                m.ExternalSource == "tmdb" &&
                m.ExternalId == tmdbId, ct);

        if (existing is not null)
        {
            await EnsureSeasonEpisodeCountsAsync(existing, ct);
            return existing;
        }

        // Without TMDB credentials the JIT path would create a placeholder row.
        // Leave the request unresolved instead of inventing metadata.
        if (!IsTmdbConfigured()) return null;

        // A client retrying the same unusable id must not hammer TMDB. A short
        // negative cache is the debounce now that no candidate row exists to
        // fold repeated requests into.
        var unresolvedKey = $"playback:unresolved:{id.MediaType}:{id.ExternalSource}:{tmdbId}";
        if (_cache.TryGetValue(unresolvedKey, out _)) return null;

        try
        {
            var created = await _mediaService.GetOrCreateMediaAsync(new AddToLibraryRequest
            {
                MediaType = id.MediaType,
                ExternalSource = "tmdb",
                ExternalId = tmdbId,
                Status = "in_progress"
            }, ct, requireExternalDetails: true);

            _cache.Remove(unresolvedKey);
            return created;
        }
        catch (Exception ex)
        {
            _cache.Set(unresolvedKey, true, UnresolvedCacheDuration);
            _logger.LogWarning(ex, "Could not hydrate TMDB media {MediaType} {TmdbId}", id.MediaType, tmdbId);
            return null;
        }
    }

    private bool IsTmdbConfigured()
    {
        return !string.IsNullOrWhiteSpace(_configuration["Tmdb:BearerToken"])
            || !string.IsNullOrWhiteSpace(_configuration["Tmdb:ApiKey"]);
    }

    /// <summary>
    /// Older series rows were catalogued before season counts were stored.
    /// Backfill them once from TMDB (never on every request) so absolute
    /// episode progress becomes available without a bulk migration.
    /// </summary>
    private async Task EnsureSeasonEpisodeCountsAsync(Entities.Media media, CancellationToken ct)
    {
        if (media.MediaType is not ("series" or "anime")) return;
        if (media.SeasonEpisodeCounts is { Count: > 0 }) return;
        if (!IsTmdbConfigured()) return;

        try
        {
            var details = await _externalMediaService.GetDetailsAsync(
                media.MediaType, "tmdb", media.ExternalId, ct);

            if (details?.SeasonEpisodeCounts is not { Count: > 0 } counts) return;

            media.SeasonEpisodeCounts = counts;
            media.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Enrichment is best-effort; tracking proceeds without progress.
            _logger.LogDebug(ex, "Could not backfill season counts for media {MediaId}", media.Id);
        }
    }
}
