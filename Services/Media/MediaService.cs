using Kue.Api.Data;
using Kue.Api.Dtos.Library;
using Kue.Api.Dtos.Media;
using Kue.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kue.Api.Services.Media;

public class MediaService : IMediaService
{
    private readonly AppDbContext _context;
    private readonly IExternalMediaService _externalMediaService;
    private readonly ILogger<MediaService> _logger;

    public MediaService(AppDbContext context, IExternalMediaService externalMediaService, ILogger<MediaService> logger)
    {
        _context = context;
        _externalMediaService = externalMediaService;
        _logger = logger;
    }

    public async Task<Entities.Media> GetOrCreateMediaAsync(AddToLibraryRequest request, CancellationToken ct = default, bool requireExternalDetails = false)
    {
        // 1. If MediaId was already provided, look up in database
        if (request.MediaId.HasValue && request.MediaId.Value > 0)
        {
            var existingById = await _context.Media.FirstOrDefaultAsync(m => m.Id == request.MediaId.Value, ct);
            if (existingById != null)
            {
                return existingById;
            }
        }

        // 2. If ExternalSource and ExternalId provided, check if already cached in DB
        if (!string.IsNullOrWhiteSpace(request.ExternalSource) && !string.IsNullOrWhiteSpace(request.ExternalId))
        {
            var normalizedSource = request.ExternalSource.Trim().ToLowerInvariant();
            var normalizedType = request.MediaType.Trim().ToLowerInvariant();

            var existingByExternal = await _context.Media.FirstOrDefaultAsync(
                m => m.MediaType.ToLower() == normalizedType &&
                     m.ExternalSource.ToLower() == normalizedSource &&
                     m.ExternalId == request.ExternalId, ct);

            if (existingByExternal != null)
            {
                return existingByExternal;
            }

            // 3. JIT Ingestion: Not in database yet! Fetch full details or use payload
            MediaDto? fetchedDto = null;
            try
            {
                fetchedDto = await _externalMediaService.GetDetailsAsync(normalizedType, normalizedSource, request.ExternalId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not fetch details from external service for {Type} {Id}", normalizedType, request.ExternalId);
            }

            if (requireExternalDetails && fetchedDto is null)
            {
                throw new InvalidOperationException(
                    $"External source '{normalizedSource}' did not return metadata for {normalizedType} '{request.ExternalId}'.");
            }

            var title = fetchedDto?.Title ?? request.Title ?? "Untitled Media";
            var description = fetchedDto?.Description;
            var coverImage = fetchedDto?.CoverImage ?? request.CoverImage;
            var bannerImage = fetchedDto?.BannerImage;
            var year = fetchedDto?.Year ?? request.Year;
            var score = fetchedDto?.Score ?? request.Score;
            var genres = fetchedDto?.Genres ?? [];
            var totalUnits = fetchedDto?.TotalUnits ?? request.TotalUnits;
            var unitName = fetchedDto?.UnitName ?? request.UnitName ?? (normalizedType == "manga" ? "Chapters" : (normalizedType is "anime" or "series" ? "Episodes" : null));
            var totalSeasons = fetchedDto?.TotalSeasons;
            var totalVolumes = fetchedDto?.TotalVolumes;
            var seasonEpisodeCounts = fetchedDto?.SeasonEpisodeCounts;
            var runtimeMinutes = fetchedDto?.RuntimeMinutes ?? request.RuntimeMinutes;
            var platforms = (fetchedDto?.Platforms != null && fetchedDto.Platforms.Count > 0) ? fetchedDto.Platforms : request.Platforms;
            var developer = fetchedDto?.Developer ?? request.Developer;

            var newMedia = new Entities.Media
            {
                MediaType = normalizedType,
                ExternalSource = normalizedSource,
                ExternalId = request.ExternalId,
                Title = title,
                Description = description,
                CoverImage = coverImage,
                BannerImage = bannerImage,
                Year = year,
                Score = score,
                Genres = genres,
                TotalUnits = totalUnits,
                UnitName = unitName,
                TotalSeasons = totalSeasons,
                TotalVolumes = totalVolumes,
                SeasonEpisodeCounts = seasonEpisodeCounts,
                RuntimeMinutes = runtimeMinutes,
                Platforms = platforms,
                Developer = developer,
                CreatedAt = DateTime.UtcNow
            };

            try
            {
                _context.Media.Add(newMedia);
                await _context.SaveChangesAsync(ct);

                _logger.LogInformation("JIT Ingestion: Saved {MediaType} '{Title}' ({Source}:{ExternalId}) as MediaId {Id}",
                    newMedia.MediaType, newMedia.Title, newMedia.ExternalSource, newMedia.ExternalId, newMedia.Id);

                return newMedia;
            }
            catch (DbUpdateException ex)
            {
                // In EF Core, if SaveChanges fails, the entity remains in the tracker.
                // Detach it to keep the context clean.
                _context.Entry(newMedia).State = EntityState.Detached;

                // Check if another concurrent request already created it (race condition)
                var existingConcurrently = await _context.Media.FirstOrDefaultAsync(
                    m => m.MediaType.ToLower() == normalizedType &&
                         m.ExternalSource.ToLower() == normalizedSource &&
                         m.ExternalId == request.ExternalId, ct);

                if (existingConcurrently != null)
                {
                    _logger.LogInformation("Race condition resolved: {MediaType} '{Title}' ({Source}:{ExternalId}) was concurrently inserted. Reusing MediaId {Id}",
                        normalizedType, title, normalizedSource, request.ExternalId, existingConcurrently.Id);
                    return existingConcurrently;
                }

                // If not caused by concurrent creation, rethrow the exception
                _logger.LogError(ex, "Failed to save new media during JIT ingestion");
                throw;
            }
        }

        throw new InvalidOperationException("You must provide either MediaId or both ExternalSource and ExternalId.");
    }

    public async Task<PagedResponseDto<MediaDto>> SearchMediaAsync(string query, string? mediaType = null, int page = 1, int pageSize = 20, string? sort = null, int? year = null, double? minScore = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new PagedResponseDto<MediaDto> { Page = page, PageSize = pageSize };
        }

        // Live search via external providers (AniList for anime/manga, etc.)
        // Provider methods swallow outages and return empty pages, so an
        // empty result also means "externals unavailable" -> serve the local
        // catalog instead of an empty page.
        PagedResponseDto<MediaDto>? externalResults = null;
        try
        {
            externalResults = await _externalMediaService.SearchAsync(query, mediaType, page, pageSize, sort, year, minScore, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "External search failed for '{Query}'. Falling back to the local catalog.", query);
        }

        if (externalResults is null || externalResults.Items.Count == 0)
        {
            if (externalResults is not null)
            {
                _logger.LogWarning("External search returned no items for '{Query}'. Falling back to the local catalog.", query);
            }
            return await SearchDatabaseAsync(query, mediaType, page, pageSize, sort, year, minScore, ct);
        }

        // Check if any results already exist in our database so we can include our local MediaId!
        if (externalResults.Items.Count > 0)
        {
            var externalIds = externalResults.Items.Select(i => i.ExternalId).ToList();
            var existingMedia = await _context.Media
                .AsNoTracking()
                .Where(m => externalIds.Contains(m.ExternalId))
                .ToListAsync(ct);

            foreach (var item in externalResults.Items)
            {
                var match = existingMedia.FirstOrDefault(m =>
                    m.MediaType.Equals(item.MediaType, StringComparison.OrdinalIgnoreCase) &&
                    m.ExternalSource.Equals(item.ExternalSource, StringComparison.OrdinalIgnoreCase) &&
                    m.ExternalId == item.ExternalId);

                if (match != null)
                {
                    item.Id = match.Id; // Attach our local database ID!
                }
            }
        }

        return externalResults;
    }

    public async Task<MediaDto?> GetMediaDetailsAsync(int id, CancellationToken ct = default)
    {
        var media = await _context.Media
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        if (media == null) return null;

        return MediaDto.FromEntity(media);
    }

    public async Task<PagedResponseDto<MediaDto>> GetTrendingMediaAsync(string? mediaType = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        try
        {
            var trending = await _externalMediaService.GetTrendingAsync(mediaType, page, pageSize, ct);
            if (trending.Items.Count > 0) return trending;
            _logger.LogWarning("External trending returned no items. Falling back to the local catalog.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "External trending failed. Falling back to the local catalog.");
        }

        return await GetDatabaseHighlightsAsync(mediaType, page, pageSize, DatabaseHighlightOrder.ByScore, ct);
    }

    public async Task<PagedResponseDto<MediaDto>> GetUpcomingMediaAsync(string? mediaType = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        try
        {
            var upcoming = await _externalMediaService.GetUpcomingAsync(mediaType, page, pageSize, ct);
            if (upcoming.Items.Count > 0) return upcoming;
            _logger.LogWarning("External upcoming returned no items. Falling back to the local catalog.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "External upcoming failed. Falling back to the local catalog.");
        }

        return await GetDatabaseHighlightsAsync(mediaType, page, pageSize, DatabaseHighlightOrder.ByRecent, ct);
    }

    public async Task<PagedResponseDto<MediaDto>> GetTopMediaAsync(string? mediaType = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        try
        {
            var top = await _externalMediaService.GetTopAsync(mediaType, page, pageSize, ct);
            if (top.Items.Count > 0) return top;
            _logger.LogWarning("External top chart returned no items. Falling back to the local catalog.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "External top chart failed. Falling back to the local catalog.");
        }

        return await GetDatabaseHighlightsAsync(mediaType, page, pageSize, DatabaseHighlightOrder.ByScore, ct);
    }

    public async Task<PagedResponseDto<MediaDto>> GetSimilarMediaAsync(
        string mediaType,
        string? externalSource = null,
        string? externalId = null,
        IEnumerable<string>? genres = null,
        int pageSize = 10,
        CancellationToken ct = default)
    {
        PagedResponseDto<MediaDto>? externalResults = null;
        try
        {
            externalResults = await _externalMediaService.GetSimilarAsync(
                mediaType, externalSource, externalId, genres, pageSize, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "External similar lookup failed. Falling back to the local catalog.");
        }

        if (externalResults is not null && externalResults.Items.Count >= Math.Min(4, pageSize))
        {
            return externalResults;
        }

        var databaseItems = await GetSimilarFromDatabaseAsync(mediaType, externalSource, externalId, genres, pageSize, ct);
        if (databaseItems.Count > 0)
        {
            _logger.LogWarning(
                "Serving {Count} similar titles from the local catalog for {MediaType} {Source}:{ExternalId}.",
                databaseItems.Count, mediaType, externalSource, externalId);
            return new PagedResponseDto<MediaDto>
            {
                Items = databaseItems,
                Page = 1,
                PageSize = pageSize,
                TotalItems = databaseItems.Count,
                TotalPages = 1
            };
        }

        return externalResults ?? new PagedResponseDto<MediaDto> { Page = 1, PageSize = pageSize };
    }

    // ---------------------------------------------------------------------
    // Local-catalog fallbacks (used when external providers are down).
    // The local catalog only contains titles users have added to a library,
    // so results are limited — but library, search, discover and similar
    // keep working instead of returning empty pages.
    // ---------------------------------------------------------------------

    private enum DatabaseHighlightOrder
    {
        ByScore,
        ByRecent
    }

    private async Task<PagedResponseDto<MediaDto>> SearchDatabaseAsync(
        string query, string? mediaType, int page, int pageSize, string? sort, int? year, double? minScore, CancellationToken ct)
    {
        var pattern = $"%{EscapeLikePattern(query.Trim())}%";
        var normalizedType = NormalizeDbMediaType(mediaType);

        var dbQuery = _context.Media
            .AsNoTracking()
            .Where(m => EF.Functions.ILike(m.Title, pattern)
                || (m.OriginalTitle != null && EF.Functions.ILike(m.OriginalTitle, pattern)));

        if (normalizedType is not null)
        {
            dbQuery = dbQuery.Where(m => m.MediaType.ToLower() == normalizedType);
        }

        if (year.HasValue)
        {
            dbQuery = dbQuery.Where(m => m.Year == year.Value);
        }

        if (minScore.HasValue)
        {
            dbQuery = dbQuery.Where(m => m.Score != null && m.Score >= minScore.Value);
        }

        var totalItems = await dbQuery.CountAsync(ct);
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var normalizedSort = sort?.Trim().ToLowerInvariant();
        dbQuery = normalizedSort switch
        {
            "newest" => dbQuery.OrderByDescending(m => m.Year ?? -1).ThenBy(m => m.Title),
            "oldest" => dbQuery.OrderBy(m => m.Year ?? 9999).ThenBy(m => m.Title),
            "highest" => dbQuery.OrderByDescending(m => m.Score ?? -1).ThenBy(m => m.Title),
            "lowest" => dbQuery.OrderBy(m => m.Score ?? 11).ThenBy(m => m.Title),
            _ => dbQuery.OrderByDescending(m => m.Score ?? -1).ThenBy(m => m.Title),
        };

        var entities = await dbQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResponseDto<MediaDto>
        {
            Items = entities.Select(MediaDto.FromEntity).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalPages
        };
    }

    private async Task<PagedResponseDto<MediaDto>> GetDatabaseHighlightsAsync(
        string? mediaType, int page, int pageSize, DatabaseHighlightOrder order, CancellationToken ct)
    {
        var normalizedType = NormalizeDbMediaType(mediaType);

        var dbQuery = _context.Media.AsNoTracking().AsQueryable();
        if (normalizedType is not null)
        {
            dbQuery = dbQuery.Where(m => m.MediaType.ToLower() == normalizedType);
        }

        dbQuery = order == DatabaseHighlightOrder.ByRecent
            ? dbQuery.OrderByDescending(m => m.CreatedAt).ThenBy(m => m.Title)
            : dbQuery.OrderByDescending(m => m.Score ?? -1).ThenBy(m => m.Title);

        var totalItems = await dbQuery.CountAsync(ct);
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var entities = await dbQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResponseDto<MediaDto>
        {
            Items = entities.Select(MediaDto.FromEntity).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalPages
        };
    }

    private async Task<List<MediaDto>> GetSimilarFromDatabaseAsync(
        string mediaType,
        string? externalSource,
        string? externalId,
        IEnumerable<string>? genres,
        int pageSize,
        CancellationToken ct)
    {
        var normalizedType = NormalizeDbMediaType(mediaType);
        var genreSet = genres?
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);

        var dbQuery = _context.Media.AsNoTracking().AsQueryable();
        if (normalizedType is not null)
        {
            dbQuery = dbQuery.Where(m => m.MediaType.ToLower() == normalizedType);
        }

        // Genre overlap is ranked in memory (Postgres array overlap over a
        // denormalized name list does not translate cleanly), so pull a
        // bounded candidate window ordered by score.
        var candidates = await dbQuery
            .OrderByDescending(m => m.Score ?? -1)
            .Take(100)
            .ToListAsync(ct);

        return candidates
            .Where(m => string.IsNullOrWhiteSpace(externalId)
                || m.ExternalId != externalId
                || (!string.IsNullOrWhiteSpace(externalSource)
                    && !m.ExternalSource.Equals(externalSource, StringComparison.OrdinalIgnoreCase)))
            .Select(m => new
            {
                Dto = MediaDto.FromEntity(m),
                Overlap = genreSet.Count == 0
                    ? 0
                    : m.Genres.Count(g => g != null && genreSet.Contains(g.Trim().ToLowerInvariant()))
            })
            .Where(x => genreSet.Count == 0 || x.Overlap > 0)
            .OrderByDescending(x => x.Overlap)
            .ThenByDescending(x => x.Dto.Score ?? -1)
            .Take(pageSize)
            .Select(x => x.Dto)
            .ToList();
    }

    private static string? NormalizeDbMediaType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType)) return null;
        return mediaType.Trim().ToLowerInvariant() switch
        {
            "tv" or "tvshow" or "tv-show" or "show" or "shows" or "series" => "series",
            "film" or "films" or "movies" or "movie" => "movie",
            "games" or "videogame" or "video-game" or "game" => "game",
            "manga" or "mangas" => "manga",
            "anime" or "animes" => "anime",
            var t => t
        };
    }

    private static string EscapeLikePattern(string value)
    {
        return value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
    }
}
