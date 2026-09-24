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

    public async Task<Entities.Media> GetOrCreateMediaAsync(AddToLibraryRequest request, CancellationToken ct = default)
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
            var runtimeMinutes = fetchedDto?.RuntimeMinutes ?? request.RuntimeMinutes;
            var platforms = fetchedDto?.Platforms;
            var developer = fetchedDto?.Developer;

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

    public async Task<PagedResponseDto<MediaDto>> SearchMediaAsync(string query, string? mediaType = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new PagedResponseDto<MediaDto> { Page = page, PageSize = pageSize };
        }

        // Live search via external providers (AniList for anime/manga, etc.)
        var externalResults = await _externalMediaService.SearchAsync(query, mediaType, page, pageSize, ct);

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
        return await _externalMediaService.GetTrendingAsync(mediaType, page, pageSize, ct);
    }
}
