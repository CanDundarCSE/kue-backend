using Kue.Api.Data;
using Kue.Api.Entities;
using Kue.Api.Services.Activity;
using MediaEntity = Kue.Api.Entities.Media;
using Microsoft.EntityFrameworkCore;

namespace Kue.Api.Services.Playback;

/// <summary>
/// Turns addon playback-start evidence into library state.
///
/// The only signal Nuvio provides is "a subtitle request happened for this
/// id", so this service deliberately never marks anything completed:
/// <list type="bullet">
/// <item>Movies are added as in-progress on first sight; completion stays manual.</item>
/// <item>Series/anime advance to an absolute episode number, never backwards.</item>
/// <item>Repeated requests are idempotent: no duplicate rows, no duplicate
/// activity, no progress regression.</item>
/// </list>
/// </summary>
public class PlaybackTrackingService : IPlaybackTrackingService
{
    private readonly AppDbContext _context;
    private readonly IPlaybackMediaResolver _mediaResolver;
    private readonly IActivityService _activityService;
    private readonly TimeProvider _time;
    private readonly ILogger<PlaybackTrackingService> _logger;

    public PlaybackTrackingService(
        AppDbContext context,
        IPlaybackMediaResolver mediaResolver,
        IActivityService activityService,
        TimeProvider time,
        ILogger<PlaybackTrackingService> logger)
    {
        _context = context;
        _mediaResolver = mediaResolver;
        _activityService = activityService;
        _time = time;
        _logger = logger;
    }

    public async Task ApplyAsync(int userId, string? type, string? rawId, CancellationToken ct = default)
    {
        var parsed = AddonVideoIdParser.TryParse(type, rawId);
        if (parsed is null)
        {
            _logger.LogDebug("Ignored malformed addon subtitle id for user {UserId}", userId);
            return;
        }

        var media = await _mediaResolver.ResolveAsync(parsed, ct);
        if (media is null)
        {
            _logger.LogInformation(
                "No Kue media for addon playback ({MediaType} {Source}:{ExternalId}); nothing to track",
                parsed.MediaType, parsed.ExternalSource, parsed.ExternalId);
            return;
        }

        var isEpisodic = media.MediaType is "series" or "anime";
        var now = _time.GetUtcNow().UtcDateTime;

        var entry = await _context.LibraryEntries
            .Include(e => e.Media)
            .FirstOrDefaultAsync(e => e.UserId == userId && e.MediaId == media.Id, ct);

        if (entry is null)
        {
            await CreateEntryAsync(userId, media, parsed, isEpisodic, now, ct);
            return;
        }

        // A movie's status is user-owned. Opening it again must never complete
        // it or overwrite its completion timestamp.
        if (!isEpisodic) return;

        // Completion is only ever set by the user. Leave such rows untouched.
        if (entry.Status == "completed") return;

        await AdvanceEntryAsync(userId, entry, media, parsed, now, ct);
    }

    private async Task CreateEntryAsync(
        int userId,
        MediaEntity media,
        AddonVideoId parsed,
        bool isEpisodic,
        DateTime now,
        CancellationToken ct)
    {
        var progress = isEpisodic ? ComputeAbsoluteProgress(media, parsed) : null;
        var entry = new LibraryEntry
        {
            UserId = userId,
            MediaId = media.Id,
            Media = media,
            Status = "in_progress",
            Progress = progress,
            AddedAt = now,
            UpdatedAt = now,
            CompletedAt = null
        };

        _context.LibraryEntries.Add(entry);

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Concurrent subtitle requests can race on the unique
            // (UserId, MediaId) index. Fold into the row the winner created
            // instead of surfacing an error or inserting a duplicate.
            _context.Entry(entry).State = EntityState.Detached;

            var existing = await _context.LibraryEntries
                .Include(e => e.Media)
                .FirstOrDefaultAsync(e => e.UserId == userId && e.MediaId == media.Id, ct);

            if (existing is null) throw;
            if (!isEpisodic || existing.Status == "completed") return;

            await AdvanceEntryAsync(userId, existing, media, parsed, now, ct);
            return;
        }

        _logger.LogInformation(
            "Auto-added {MediaType} '{Title}' to user {UserId} library from addon playback (progress={Progress})",
            media.MediaType, media.Title, userId, progress);

        await LogActivityAsync(
            userId,
            activityType: "added",
            mediaId: media.Id,
            status: entry.Status,
            progress: entry.Progress,
            progressDelta: null,
            totalUnits: media.TotalUnits,
            unitName: media.UnitName,
            ct);
    }

    private async Task AdvanceEntryAsync(
        int userId,
        LibraryEntry entry,
        MediaEntity media,
        AddonVideoId parsed,
        DateTime now,
        CancellationToken ct)
    {
        var absolute = ComputeAbsoluteProgress(media, parsed);
        var target = ClampProgress(absolute, media.TotalUnits);
        var current = entry.Progress ?? 0;

        var statusChanged = entry.Status is "planning" or "on_hold" or "dropped";
        var progressChanged = target.HasValue && target.Value > current;

        if (!statusChanged && !progressChanged) return;

        var oldStatus = entry.Status;
        if (statusChanged) entry.Status = "in_progress";

        int? delta = null;
        if (progressChanged)
        {
            delta = target!.Value - current;
            entry.Progress = target.Value;
        }

        entry.UpdatedAt = now;

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request/import touched the row first. The next subtitle
            // retry will re-evaluate against fresh state; do not retry blindly.
            _logger.LogDebug("Concurrent library update for user {UserId} media {MediaId}", userId, media.Id);
            return;
        }

        _logger.LogDebug(
            "Addon playback advanced media {MediaId} for user {UserId} (progress={Progress}, status={Status})",
            media.Id, userId, entry.Progress, entry.Status);

        if (progressChanged)
        {
            await LogActivityAsync(
                userId,
                activityType: "progress",
                mediaId: media.Id,
                status: entry.Status,
                progress: entry.Progress,
                progressDelta: delta,
                totalUnits: media.TotalUnits,
                unitName: media.UnitName,
                ct);
        }
        else if (statusChanged && oldStatus is not "in_progress")
        {
            await LogActivityAsync(
                userId,
                activityType: "started",
                mediaId: media.Id,
                status: entry.Status,
                progress: entry.Progress,
                progressDelta: null,
                totalUnits: media.TotalUnits,
                unitName: media.UnitName,
                ct);
        }
    }

    /// <summary>
    /// Converts "season S, episode E" into Kue's single absolute episode
    /// number. Returns null when that cannot be computed honestly, in which
    /// case the caller leaves progress unchanged rather than inventing data.
    /// </summary>
    /// <remarks>
    /// Only the seasons *before* the requested one contribute to the offset.
    /// A missing count for any of them makes the absolute position unknown.
    /// Season 0 (specials) is not part of the main run and is never mapped.
    /// Season 1 needs no metadata: S1E5 is already episode 5.
    /// </remarks>
    internal static int? ComputeAbsoluteProgress(MediaEntity media, AddonVideoId parsed)
    {
        if (parsed.Episode is not { } episode || episode <= 0) return null;
        if (parsed.Season is not { } season || season < 1) return null;
        if (season == 1) return episode;

        var counts = media.SeasonEpisodeCounts;
        if (counts is null || counts.Count == 0) return null;

        // If TMDB knows how many episodes this season has and the client claims
        // a higher number, the metadata is stale or the id is wrong. Do not
        // invent a position from contradictory data.
        if (counts.TryGetValue(season, out var thisSeasonCount)
            && thisSeasonCount > 0
            && episode > thisSeasonCount)
        {
            return null;
        }

        var offset = 0;
        for (var before = 1; before < season; before++)
        {
            if (!counts.TryGetValue(before, out var episodeCount) || episodeCount <= 0)
            {
                return null;
            }
            offset += episodeCount;
        }

        return offset + episode;
    }

    private static int? ClampProgress(int? value, int? totalUnits)
    {
        if (value is null) return null;
        var progress = Math.Max(0, value.Value);
        return totalUnits.HasValue ? Math.Min(progress, totalUnits.Value) : progress;
    }

    private async Task LogActivityAsync(
        int userId,
        string activityType,
        int mediaId,
        string status,
        int? progress,
        int? progressDelta,
        int? totalUnits,
        string? unitName,
        CancellationToken ct)
    {
        try
        {
            await _activityService.LogActivityAsync(
                userId: userId,
                activityType: activityType,
                mediaId: mediaId,
                status: status,
                progress: progress,
                progressDelta: progressDelta,
                totalUnits: totalUnits,
                unitName: unitName,
                ct: ct);
        }
        catch (Exception ex)
        {
            // Activity is a side effect; playback tracking must not fail for it.
            _logger.LogDebug(ex, "Could not log addon playback activity");
        }
    }
}
