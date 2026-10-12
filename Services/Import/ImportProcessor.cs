using System.Text.Json;
using Kue.Api.Data;
using Kue.Api.Dtos.Import;
using Kue.Api.Dtos.Media;
using Kue.Api.Entities;
using Kue.Api.Services.Activity;
using Kue.Api.Services.Media;
using Kue.Api.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Kue.Api.Services.Import;

public class ImportProcessor
{
    private static readonly HashSet<string> ValidStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "planning", "in_progress", "completed", "on_hold", "dropped"
    };

    private readonly AppDbContext _context;
    private readonly IExternalMediaService _externalMediaService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IActivityService _activityService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<ImportProcessor> _logger;

    public ImportProcessor(
        AppDbContext context,
        IExternalMediaService externalMediaService,
        IHttpClientFactory httpClientFactory,
        IActivityService activityService,
        INotificationService notificationService,
        ILogger<ImportProcessor> logger)
    {
        _context = context;
        _externalMediaService = externalMediaService;
        _httpClientFactory = httpClientFactory;
        _activityService = activityService;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task ProcessAsync(int jobId, CancellationToken ct)
    {
        var job = await _context.ImportJobs
            .Include(j => j.Items)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null || job.Status != "pending") return;

        job.Status = "running";
        job.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        try
        {
            var processed = job.Items.Count(i => i.Status != "pending");
            foreach (var item in job.Items.Where(i => i.Status == "pending").OrderBy(i => i.Id))
            {
                if (await IsCancelledAsync(job, ct)) return;

                try
                {
                    await MatchAsync(item, ct);
                    await DiffAsync(job.UserId, item, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Import item {ItemId} failed for job {JobId}.", item.Id, jobId);
                    item.Status = "failed";
                    item.Error = "Could not match this title.";
                }

                job.ProcessedItems = ++processed;
                job.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(ct);
            }

            if (await IsCancelledAsync(job, ct)) return;

            var conflicts = job.Items.Count(i => i.Status == "conflict");
            job.Status = conflicts > 0 ? "awaiting_resolution" : "applying";
            job.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);

            if (job.Status == "applying")
            {
                await ApplyAsync(jobId, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Import job {JobId} failed.", jobId);
            job.Status = "failed";
            job.Error = "Import failed unexpectedly. You can delete it and try again.";
            job.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }
    }

    public async Task ApplyAsync(int jobId, CancellationToken ct)
    {
        var job = await _context.ImportJobs
            .Include(j => j.Items)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null || job.Status != "applying") return;

        try
        {
            // Entries may have appeared or changed since processing (user kept
            // using the app): re-validate before writing anything.
            if (await RevalidateAsync(job, ct))
            {
                job.Status = "awaiting_resolution";
                job.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "Import job {JobId} found new conflicts at apply time; back to review.", jobId);
                return;
            }
            await _context.SaveChangesAsync(ct);

            var needsReview = false;
            foreach (var item in job.Items.OrderBy(i => i.Id))
            {
                if (await IsCancelledAsync(job, ct)) return;

                try
                {
                    if (await ApplyItemAsync(job, item, ct)) needsReview = true;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Applying import item {ItemId} failed for job {JobId}.", item.Id, jobId);
                    item.Status = "failed";
                    item.Error = "Could not save this title.";
                }

                await _context.SaveChangesAsync(ct);
            }

            if (await IsCancelledAsync(job, ct)) return;

            if (needsReview)
            {
                job.Status = "awaiting_resolution";
                job.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "Import job {JobId} hit concurrent edits while applying; back to review.", jobId);
                return;
            }

            // Reconcile: items skipped during processing or resolve-time
            // re-validation never passed through the apply gate, so derive the
            // final skipped count instead of trusting increments alone.
            var failed = job.Items.Count(i => i.Status == "failed");
            job.SkippedCount = Math.Max(0, job.TotalItems - job.AddedCount - job.UpdatedCount - failed);
            job.Status = "completed";
            job.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);

            try
            {
                await _notificationService.CreateNotificationAsync(
                    userId: job.UserId,
                    actorId: job.UserId,
                    type: "import_completed",
                    title: "Import finished",
                    message: $"{job.AddedCount} added, {job.UpdatedCount} updated, {job.SkippedCount} skipped.",
                    referenceId: job.Id,
                    referenceType: "import",
                    ct: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Import completion notification failed for job {JobId}.", jobId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Applying import job {JobId} failed.", jobId);
            job.Status = "failed";
            job.Error = "Import failed unexpectedly. You can delete it and try again.";
            job.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }
    }

    private async Task<bool> IsCancelledAsync(ImportJob job, CancellationToken ct)
    {
        await _context.Entry(job).ReloadAsync(ct);
        return job.Status == "cancelled";
    }

    // --- Matching ---

    private async Task MatchAsync(ImportItem item, CancellationToken ct)
    {
        // 1. Direct external-ID match against the local catalog.
        if (!string.IsNullOrWhiteSpace(item.ExternalSource)
            && !string.IsNullOrWhiteSpace(item.ExternalId))
        {
            var source = item.ExternalSource.Trim().ToLowerInvariant();
            var externalId = item.ExternalId.Trim();

            Entities.Media? byId = null;
            if (source == "mal" && (item.MediaType is "anime" or "manga"))
            {
                byId = await ResolveMalToLocalAsync(item, source, externalId, ct);
            }
            else
            {
                byId = await _context.Media.AsNoTracking().FirstOrDefaultAsync(m =>
                    m.MediaType.ToLower() == (item.MediaType ?? m.MediaType).ToLower()
                    && m.ExternalSource.ToLower() == source
                    && m.ExternalId == externalId, ct);
            }

            if (byId is not null)
            {
                item.MatchedMediaId = byId.Id;
                return;
            }
        }

        // 2. Exact title match in the local catalog.
        if (!string.IsNullOrWhiteSpace(item.Title))
        {
            var query = _context.Media.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(item.MediaType))
            {
                var type = item.MediaType.Trim().ToLowerInvariant();
                query = query.Where(m => m.MediaType.ToLower() == type);
            }

            var candidates = await query
                .Where(m => EF.Functions.ILike(m.Title, $"%{EscapeLike(item.Title.Trim())}%"))
                .Take(20)
                .ToListAsync(ct);

            var exact = candidates.FirstOrDefault(m =>
                string.Equals(m.Title.Trim(), item.Title.Trim(), StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                item.MatchedMediaId = exact.Id;
                return;
            }

            // 3. External search fallback (throttled): accept a title-equal top hit.
            await Task.Delay(700, ct);
            PagedResponseDto<MediaDto>? external = null;
            try
            {
                external = await _externalMediaService.SearchAsync(
                    item.Title.Trim(), item.MediaType, 1, 5, null, null, null, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "External match search failed for '{Title}'.", item.Title);
            }

            var hit = external?.Items.FirstOrDefault(m =>
                string.Equals(m.Title.Trim(), item.Title.Trim(), StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                var local = await _context.Media.AsNoTracking().FirstOrDefaultAsync(m =>
                    m.MediaType.ToLower() == hit.MediaType.ToLower()
                    && m.ExternalSource.ToLower() == hit.ExternalSource.ToLower()
                    && m.ExternalId == hit.ExternalId, ct);
                if (local is not null)
                {
                    item.MatchedMediaId = local.Id;
                }
                else
                {
                    item.CandidateJson = JsonSerializer.Serialize(new CandidateSnapshot
                    {
                        MediaType = hit.MediaType,
                        ExternalSource = hit.ExternalSource,
                        ExternalId = hit.ExternalId,
                        Title = hit.Title,
                        CoverImage = hit.CoverImage,
                        Year = hit.Year,
                        Score = hit.Score,
                        Genres = hit.Genres,
                        TotalUnits = hit.TotalUnits,
                        UnitName = hit.UnitName,
                        RuntimeMinutes = hit.RuntimeMinutes,
                        Platforms = hit.Platforms,
                        Developer = hit.Developer,
                    });
                }
                return;
            }
        }
    }

    /// <summary>
    /// MAL ids aren't stored locally (anime rows are keyed by AniList id), so
    /// resolve idMal -> AniList entry, then match or snapshot it.
    /// </summary>
    private async Task<Entities.Media?> ResolveMalToLocalAsync(
        ImportItem item, string source, string externalId, CancellationToken ct)
    {
        await Task.Delay(800, ct);

        var anilistType = item.MediaType == "manga" ? "MANGA" : "ANIME";
        if (!int.TryParse(externalId, out var malId)) return null;

        try
        {
            var client = _httpClientFactory.CreateClient();
            using var content = new StringContent(JsonSerializer.Serialize(new
            {
                query = @"query ($idMal: Int, $type: MediaType) {
  Media(idMal: $idMal, type: $type) {
    id title { english romaji } coverImage { large } startDate { year }
    averageScore genres episodes chapters
  }
}",
                variables = new { idMal = malId, type = anilistType }
            }), System.Text.Encoding.UTF8, "application/json");

            using var response = await client.PostAsync("https://graphql.anilist.co", content, ct);
            if (!response.IsSuccessStatusCode) return null;

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("data", out var data)
                || !data.TryGetProperty("Media", out var media)
                || media.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var anilistId = media.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number
                ? idEl.GetInt32().ToString()
                : null;
            if (anilistId is null) return null;

            var local = await _context.Media.AsNoTracking().FirstOrDefaultAsync(m =>
                m.ExternalSource == "anilist" && m.ExternalId == anilistId, ct);
            if (local is not null) return local;

            // Snapshot for creation at apply time.
            string? title = null;
            if (media.TryGetProperty("title", out var titleEl))
            {
                title = GetJsonString(titleEl, "english") ?? GetJsonString(titleEl, "romaji");
            }

            int? year = null;
            if (media.TryGetProperty("startDate", out var sd) && sd.ValueKind == JsonValueKind.Object
                && sd.TryGetProperty("year", out var yr) && yr.ValueKind == JsonValueKind.Number)
            {
                year = yr.GetInt32();
            }

            double? score = null;
            if (media.TryGetProperty("averageScore", out var sc) && sc.ValueKind == JsonValueKind.Number)
            {
                score = Math.Round(sc.GetDouble() / 10.0, 1);
            }

            var genres = new List<string>();
            if (media.TryGetProperty("genres", out var gn) && gn.ValueKind == JsonValueKind.Array)
            {
                foreach (var g in gn.EnumerateArray())
                {
                    if (g.ValueKind == JsonValueKind.String) genres.Add(g.GetString()!);
                }
            }

            item.CandidateJson = JsonSerializer.Serialize(new CandidateSnapshot
            {
                MediaType = item.MediaType ?? "anime",
                ExternalSource = "anilist",
                ExternalId = anilistId,
                Title = string.IsNullOrWhiteSpace(title) ? item.Title : title,
                CoverImage = media.TryGetProperty("coverImage", out var cov) ? GetJsonString(cov, "large") : null,
                Year = year,
                Score = score,
                Genres = genres,
            });
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "AniList idMal lookup failed for {MalId}.", externalId);
            return null;
        }
    }

    // --- Diffing ---

    /// <summary>
    /// Pure field comparison shared by processing, resolve-time and apply-time
    /// re-validation. Only imported non-null values participate.
    /// </summary>
    public static List<FieldConflictDto> BuildConflicts(
        string? localStatus,
        int? localProgress,
        int? localRating,
        string? localPlatform,
        bool localFavorite,
        ImportItem item)
    {
        var conflicts = new List<FieldConflictDto>();
        void Compare(string field, string? local, string? imported)
        {
            if (imported is null) return;
            if (!string.Equals((local ?? "").Trim(), imported.Trim(), StringComparison.Ordinal))
            {
                conflicts.Add(new FieldConflictDto { Field = field, Local = local, Imported = imported });
            }
        }

        Compare("status", localStatus, item.ImportedStatus);
        Compare("progress", localProgress?.ToString(), item.ImportedProgress?.ToString());
        Compare("rating", localRating?.ToString(), item.ImportedRating?.ToString());
        Compare("platform", localPlatform, item.ImportedPlatform);
        Compare("favorite", localFavorite.ToString().ToLowerInvariant(), item.ImportedFavorite?.ToString().ToLowerInvariant());
        return conflicts;
    }

    private async Task DiffAsync(int userId, ImportItem item, CancellationToken ct)
    {
        if (item.MatchedMediaId is null && string.IsNullOrWhiteSpace(item.CandidateJson))
        {
            item.Status = "unmatched";
            return;
        }

        LibraryEntry? entry = null;
        if (item.MatchedMediaId.HasValue)
        {
            entry = await _context.LibraryEntries.AsNoTracking().FirstOrDefaultAsync(
                e => e.UserId == userId && e.MediaId == item.MatchedMediaId.Value, ct);
        }

        if (entry is null)
        {
            item.Status = "ready";
            item.Decision = "import";
            return;
        }

        var conflicts = BuildConflicts(
            entry.Status, entry.Progress, entry.Rating, entry.Platform, entry.IsFavorite, item);

        if (conflicts.Count == 0)
        {
            item.Status = "skipped";
            item.Decision = "skip";
            return;
        }

        item.Status = "conflict";
        item.Decision = null;
        item.ConflictJson = JsonSerializer.Serialize(conflicts);
    }

    /// <summary>
    /// Re-diffs items about to be applied against current database state.
    /// Returns true when auto-ready items turned into genuine conflicts and
    /// need fresh review. Explicit "import" decisions stand; items that
    /// converged meanwhile are auto-skipped.
    /// </summary>
    private async Task<bool> RevalidateAsync(ImportJob job, CancellationToken ct)
    {
        var demoted = false;

        foreach (var item in job.Items
            .Where(i => i.Status == "ready" || (i.Status == "conflict" && i.Decision == "import"))
            .OrderBy(i => i.Id))
        {
            LibraryEntry? entry = null;
            if (item.MatchedMediaId.HasValue)
            {
                entry = await _context.LibraryEntries.AsNoTracking().FirstOrDefaultAsync(
                    e => e.UserId == job.UserId && e.MediaId == item.MatchedMediaId.Value, ct);
            }

            if (entry is null)
            {
                // Deleted (or never added) since processing: proceed to (re-)add.
                if (item.Status == "conflict")
                {
                    item.Status = "ready";
                    item.Decision ??= "import";
                    item.ConflictJson = null;
                }
                continue;
            }

            var conflicts = BuildConflicts(
                entry.Status, entry.Progress, entry.Rating, entry.Platform, entry.IsFavorite, item);
            if (conflicts.Count == 0)
            {
                item.Status = "skipped";
                item.Decision = "skip";
                item.ConflictJson = null;
                continue;
            }

            item.ConflictJson = JsonSerializer.Serialize(conflicts);
            if (item.Decision != "import")
            {
                item.Status = "conflict";
                item.Decision = null;
                demoted = true;
            }
        }

        return demoted;
    }

    // --- Applying ---

    /// <summary>Applies one item. Returns true when it needs fresh review.</summary>
    private async Task<bool> ApplyItemAsync(ImportJob job, ImportItem item, CancellationToken ct)
    {
        if (item.Status == "applied") return false;

        if (item.Decision == "keep" || item.Decision == "skip"
            || item.Status is "unmatched" or "failed" or "skipped")
        {
            if (item.Status is "ready" or "conflict")
            {
                item.Status = "skipped";
                job.SkippedCount++;
            }
            else if (item.Status == "unmatched")
            {
                job.SkippedCount++;
            }
            return false;
        }

        if (item.Decision != "import" && item.Status != "ready")
        {
            item.Status = "skipped";
            job.SkippedCount++;
            return false;
        }

        var media = await EnsureMediaAsync(item, ct);
        if (media is null)
        {
            item.Status = "failed";
            item.Error = "Could not resolve this title.";
            return false;
        }

        var entry = await _context.LibraryEntries.FirstOrDefaultAsync(
            e => e.UserId == job.UserId && e.MediaId == media.Id, ct);

        if (entry is null)
        {
            var added = BuildNewEntry(job.UserId, media, item);
            _context.LibraryEntries.Add(added);
            try
            {
                await _context.SaveChangesAsync(ct);
                job.AddedCount++;
                entry = added;
            }
            catch (DbUpdateException)
            {
                // Raced with a manual add: detach only our loser row (the job
                // and item stay tracked) and update the winner instead.
                _context.Entry(added).State = EntityState.Detached;
                entry = await _context.LibraryEntries.FirstOrDefaultAsync(
                    e => e.UserId == job.UserId && e.MediaId == media.Id, ct);
                if (entry is null)
                {
                    item.Status = "failed";
                    item.Error = "Could not save this title.";
                    return false;
                }
                if (await ApplyUpdateAsync(job, item, media, entry, ct)) return true;
            }
        }
        else
        {
            if (await ApplyUpdateAsync(job, item, media, entry, ct)) return true;
        }

        item.Status = "applied";
        item.MatchedMediaId = media.Id;

        try
        {
            await _activityService.LogActivityAsync(
                userId: job.UserId,
                activityType: entry.Status == "completed" ? "completed" : "added",
                mediaId: media.Id,
                status: entry.Status,
                progress: entry.Progress,
                rating: entry.Rating,
                platform: entry.Platform,
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Import activity log failed for media {MediaId}.", media.Id);
        }

        return false;
    }

    private static LibraryEntry BuildNewEntry(int userId, Entities.Media media, ImportItem item)
    {
        return new LibraryEntry
        {
            UserId = userId,
            MediaId = media.Id,
            Status = ValidStatusOrDefault(item.ImportedStatus),
            Progress = item.ImportedProgress,
            Platform = media.MediaType == "game" ? item.ImportedPlatform : null,
            Rating = item.ImportedRating,
            IsFavorite = item.ImportedFavorite ?? false,
            AddedAt = DateTime.UtcNow,
            CompletedAt = item.ImportedStatus == "completed" ? DateTime.UtcNow : null,
        };
    }

    private static void MutateEntry(LibraryEntry entry, Entities.Media media, ImportItem item)
    {
        if (ValidStatuses.Contains(item.ImportedStatus ?? ""))
        {
            entry.Status = item.ImportedStatus!;
        }
        if (item.ImportedProgress.HasValue) entry.Progress = item.ImportedProgress;
        if (item.ImportedRating.HasValue) entry.Rating = item.ImportedRating;
        if (media.MediaType == "game" && item.ImportedPlatform is not null)
        {
            entry.Platform = item.ImportedPlatform;
        }
        if (item.ImportedFavorite.HasValue) entry.IsFavorite = item.ImportedFavorite.Value;
        entry.UpdatedAt = DateTime.UtcNow;
        if (entry.Status == "completed" && entry.CompletedAt is null)
        {
            entry.CompletedAt = DateTime.UtcNow;
        }
        else if (entry.Status != "completed")
        {
            entry.CompletedAt = null;
        }
    }

    /// <summary>
    /// Writes imported values onto an existing entry. On an optimistic-concurrency
    /// conflict the stale write is discarded and the item is re-diffed fresh:
    /// converged items skip silently, still-differing ones go back to review.
    /// Returns true when the item needs fresh review.
    /// </summary>
    private async Task<bool> ApplyUpdateAsync(
        ImportJob job, ImportItem item, Entities.Media media, LibraryEntry entry, CancellationToken ct)
    {
        MutateEntry(entry, media, item);

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogInformation(
                "Library entry {MediaId} changed during import job {JobId}; re-validating item {ItemId}.",
                media.Id, job.Id, item.Id);

            _context.Entry(entry).State = EntityState.Detached;
            var fresh = await _context.LibraryEntries.AsNoTracking().FirstOrDefaultAsync(
                e => e.UserId == job.UserId && e.MediaId == media.Id, ct);

            if (fresh is null)
            {
                // Deleted mid-apply: recreate from the import values.
                _context.LibraryEntries.Add(BuildNewEntry(job.UserId, media, item));
                await _context.SaveChangesAsync(ct);
                job.AddedCount++;
                return false;
            }

            var conflicts = BuildConflicts(
                fresh.Status, fresh.Progress, fresh.Rating, fresh.Platform, fresh.IsFavorite, item);
            if (conflicts.Count == 0)
            {
                item.Status = "skipped";
                item.Decision = "skip";
                item.ConflictJson = null;
                return false;
            }

            item.Status = "conflict";
            item.Decision = null;
            item.ConflictJson = JsonSerializer.Serialize(conflicts);
            return true;
        }

        job.UpdatedCount++;
        return false;
    }

    private async Task<Entities.Media?> EnsureMediaAsync(ImportItem item, CancellationToken ct)
    {
        if (item.MatchedMediaId.HasValue)
        {
            return await _context.Media.FirstOrDefaultAsync(m => m.Id == item.MatchedMediaId.Value, ct);
        }

        if (string.IsNullOrWhiteSpace(item.CandidateJson)) return null;

        CandidateSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<CandidateSnapshot>(item.CandidateJson);
        }
        catch (JsonException)
        {
            return null;
        }
        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.ExternalSource)
            || string.IsNullOrWhiteSpace(snapshot.ExternalId)
            || string.IsNullOrWhiteSpace(snapshot.MediaType))
        {
            return null;
        }

        var existing = await _context.Media.FirstOrDefaultAsync(m =>
            m.MediaType.ToLower() == snapshot.MediaType.ToLower()
            && m.ExternalSource.ToLower() == snapshot.ExternalSource.ToLower()
            && m.ExternalId == snapshot.ExternalId, ct);
        if (existing is not null) return existing;

        var media = new Entities.Media
        {
            MediaType = snapshot.MediaType.ToLowerInvariant(),
            ExternalSource = snapshot.ExternalSource.ToLowerInvariant(),
            ExternalId = snapshot.ExternalId,
            Title = string.IsNullOrWhiteSpace(snapshot.Title) ? item.Title : snapshot.Title,
            CoverImage = snapshot.CoverImage,
            Year = snapshot.Year,
            Score = snapshot.Score,
            Genres = snapshot.Genres ?? [],
            TotalUnits = snapshot.TotalUnits,
            UnitName = snapshot.UnitName,
            RuntimeMinutes = snapshot.RuntimeMinutes,
            Platforms = snapshot.Platforms,
            Developer = snapshot.Developer,
            CreatedAt = DateTime.UtcNow,
        };

        _context.Media.Add(media);
        try
        {
            await _context.SaveChangesAsync(ct);
            return media;
        }
        catch (DbUpdateException)
        {
            _context.Entry(media).State = EntityState.Detached;
            return await _context.Media.FirstOrDefaultAsync(m =>
                m.MediaType.ToLower() == snapshot.MediaType.ToLower()
                && m.ExternalSource.ToLower() == snapshot.ExternalSource.ToLower()
                && m.ExternalId == snapshot.ExternalId, ct);
        }
    }

    private static string ValidStatusOrDefault(string? status) =>
        !string.IsNullOrWhiteSpace(status) && ValidStatuses.Contains(status.Trim())
            ? status.Trim().ToLowerInvariant()
            : "planning";

    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    private static string? GetJsonString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var prop)
        && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private sealed class CandidateSnapshot
    {
        public string MediaType { get; set; } = string.Empty;
        public string ExternalSource { get; set; } = string.Empty;
        public string ExternalId { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? CoverImage { get; set; }
        public int? Year { get; set; }
        public double? Score { get; set; }
        public List<string>? Genres { get; set; }
        public int? TotalUnits { get; set; }
        public string? UnitName { get; set; }
        public int? RuntimeMinutes { get; set; }
        public List<string>? Platforms { get; set; }
        public string? Developer { get; set; }
    }
}
