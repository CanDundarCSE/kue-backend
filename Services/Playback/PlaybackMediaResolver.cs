using Kue.Api.Data;
using Kue.Api.Dtos.Library;
using Kue.Api.Services.Media;
using Microsoft.EntityFrameworkCore;

namespace Kue.Api.Services.Playback;

public interface IPlaybackMediaResolver
{
    /// <summary>
    /// Resolves a parsed addon id to a Kue media row, hydrating it from TMDB
    /// when needed. Returns null when the id cannot be mapped (unconfigured
    /// TMDB, unknown id, upstream failure) — the candidate is still recorded.
    /// </summary>
    Task<Entities.Media?> ResolveAsync(AddonVideoId id, CancellationToken ct = default);
}

public class PlaybackMediaResolver : IPlaybackMediaResolver
{
    private readonly AppDbContext _context;
    private readonly IExternalMediaService _externalMediaService;
    private readonly IMediaService _mediaService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PlaybackMediaResolver> _logger;

    public PlaybackMediaResolver(
        AppDbContext context,
        IExternalMediaService externalMediaService,
        IMediaService mediaService,
        IConfiguration configuration,
        ILogger<PlaybackMediaResolver> logger)
    {
        _context = context;
        _externalMediaService = externalMediaService;
        _mediaService = mediaService;
        _configuration = configuration;
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
                _logger.LogInformation("No TMDB mapping for {MediaType} {ImdbId}; candidate will be unresolved",
                    id.MediaType, id.ExternalId);
                return null;
            }
        }

        var existing = await _context.Media
            .AsNoTracking()
            .FirstOrDefaultAsync(m =>
                m.MediaType == id.MediaType &&
                m.ExternalSource == "tmdb" &&
                m.ExternalId == tmdbId, ct);

        if (existing is not null) return existing;

        // Without TMDB credentials the JIT path would create a placeholder row.
        // Leave the candidate unresolved instead of inventing metadata.
        if (!IsTmdbConfigured()) return null;

        try
        {
            return await _mediaService.GetOrCreateMediaAsync(new AddToLibraryRequest
            {
                MediaType = id.MediaType,
                ExternalSource = "tmdb",
                ExternalId = tmdbId,
                Status = "in_progress"
            }, ct, requireExternalDetails: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not hydrate TMDB media {MediaType} {TmdbId}", id.MediaType, tmdbId);
            return null;
        }
    }

    private bool IsTmdbConfigured()
    {
        return !string.IsNullOrWhiteSpace(_configuration["Tmdb:BearerToken"])
            || !string.IsNullOrWhiteSpace(_configuration["Tmdb:ApiKey"]);
    }
}
