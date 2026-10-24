using Kue.Api.Data;
using Kue.Api.Dtos.Library;
using Kue.Api.Dtos.Playback;
using Kue.Api.Entities;
using Kue.Api.Services.Activity;
using Microsoft.EntityFrameworkCore;

namespace Kue.Api.Services.Playback;

public class PlaybackCandidateService : IPlaybackCandidateService
{
    private const int DefaultDebounceSeconds = 300;
    private const int MinDebounceSeconds = 30;
    private const int MaxDebounceSeconds = 3600;

    private readonly AppDbContext _context;
    private readonly IPlaybackMediaResolver _mediaResolver;
    private readonly IActivityService _activityService;
    private readonly TimeProvider _time;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PlaybackCandidateService> _logger;

    public PlaybackCandidateService(
        AppDbContext context,
        IPlaybackMediaResolver mediaResolver,
        IActivityService activityService,
        TimeProvider time,
        IConfiguration configuration,
        ILogger<PlaybackCandidateService> logger)
    {
        _context = context;
        _mediaResolver = mediaResolver;
        _activityService = activityService;
        _time = time;
        _configuration = configuration;
        _logger = logger;
    }

    private TimeSpan DebounceWindow
    {
        get
        {
            var configured = _configuration.GetValue<int?>("Addon:DebounceWindowSeconds");
            var seconds = Math.Clamp(configured ?? DefaultDebounceSeconds, MinDebounceSeconds, MaxDebounceSeconds);
            return TimeSpan.FromSeconds(seconds);
        }
    }

    public async Task<PlaybackCandidate?> RecordAsync(int userId, string? type, string? rawId, CancellationToken ct = default)
    {
        var parsed = AddonVideoIdParser.TryParse(type, rawId);
        if (parsed is null)
        {
            _logger.LogDebug("Ignored malformed addon subtitle id for user {UserId}", userId);
            return null;
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var since = now - DebounceWindow;

        // Debounce: repeated subtitle requests caused by player retries,
        // failovers or episode changes within a short window fold into one
        // candidate. A request outside the window starts a new candidate, so a
        // genuine rewatch is never permanently suppressed.
        var candidate = await _context.PlaybackCandidates
            .Where(c =>
                c.UserId == userId &&
                c.MediaType == parsed.MediaType &&
                c.ExternalSource == parsed.ExternalSource &&
                c.ExternalId == parsed.ExternalId &&
                c.Season == parsed.Season &&
                c.Episode == parsed.Episode &&
                c.Status == PlaybackCandidateStatus.Pending &&
                c.LastSeenAt >= since)
            .OrderByDescending(c => c.LastSeenAt)
            .FirstOrDefaultAsync(ct);

        if (candidate is not null)
        {
            candidate.LastSeenAt = now;
            candidate.RequestCount++;

            if (candidate.MediaId is null)
            {
                await TryAttachMediaAsync(candidate, parsed, ct);
            }

            await _context.SaveChangesAsync(ct);
            return candidate;
        }

        var media = await _mediaResolver.ResolveAsync(parsed, ct);

        candidate = new PlaybackCandidate
        {
            UserId = userId,
            MediaId = media?.Id,
            MediaType = parsed.MediaType,
            ExternalSource = parsed.ExternalSource,
            ExternalId = parsed.ExternalId,
            ResolvedExternalId = media?.ExternalId,
            Season = parsed.Season,
            Episode = parsed.Episode,
            FirstSeenAt = now,
            LastSeenAt = now,
            RequestCount = 1,
            Status = PlaybackCandidateStatus.Pending
        };

        _context.PlaybackCandidates.Add(candidate);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Recorded playback candidate {CandidateId} for user {UserId} ({MediaType} {Source}:{ExternalId} S{Season}E{Episode}, resolved={Resolved})",
            candidate.Id, userId, parsed.MediaType, parsed.ExternalSource, parsed.ExternalId,
            parsed.Season, parsed.Episode, media is not null);

        return candidate;
    }

    private async Task TryAttachMediaAsync(PlaybackCandidate candidate, AddonVideoId parsed, CancellationToken ct)
    {
        var media = await _mediaResolver.ResolveAsync(parsed, ct);
        if (media is null) return;

        candidate.MediaId = media.Id;
        candidate.ResolvedExternalId = media.ExternalId;
    }

    public async Task<List<PlaybackCandidateDto>> ListPendingAsync(int userId, CancellationToken ct = default)
    {
        var candidates = await _context.PlaybackCandidates
            .AsNoTracking()
            .Include(c => c.Media)
            .Where(c => c.UserId == userId && c.Status == PlaybackCandidateStatus.Pending)
            .OrderByDescending(c => c.LastSeenAt)
            .Take(50)
            .ToListAsync(ct);

        return candidates.Select(MapToDto).ToList();
    }

    public async Task<PlaybackConfirmResult> ConfirmAsync(int userId, int candidateId, CancellationToken ct = default)
    {
        var candidate = await _context.PlaybackCandidates
            .Include(c => c.Media)
            .FirstOrDefaultAsync(c => c.Id == candidateId && c.UserId == userId, ct);

        if (candidate is null || candidate.Status != PlaybackCandidateStatus.Pending)
        {
            return new PlaybackConfirmResult(PlaybackConfirmOutcome.NotFound, null);
        }

        if (candidate.Media is null)
        {
            return new PlaybackConfirmResult(PlaybackConfirmOutcome.Unresolved, null);
        }

        var media = candidate.Media;
        var now = _time.GetUtcNow().UtcDateTime;
        var isEpisodic = media.MediaType is "series" or "anime";

        var entry = await _context.LibraryEntries
            .Include(e => e.Media)
            .FirstOrDefaultAsync(e => e.UserId == userId && e.MediaId == media.Id, ct);

        var activityType = "started";

        if (entry is null)
        {
            entry = new LibraryEntry
            {
                UserId = userId,
                MediaId = media.Id,
                Media = media,
                Status = isEpisodic ? "in_progress" : "completed",
                Progress = isEpisodic ? ClampProgress(ProgressFromCandidate(candidate), media.TotalUnits) : null,
                AddedAt = now,
                UpdatedAt = now,
                CompletedAt = isEpisodic ? null : now
            };

            if (!isEpisodic) activityType = "completed";
            _context.LibraryEntries.Add(entry);
        }
        else if (isEpisodic)
        {
            var progressEpisode = ProgressFromCandidate(candidate);
            if (progressEpisode is { } episode)
            {
                var target = ClampProgress(episode, media.TotalUnits);
                if (target.HasValue && (entry.Progress ?? 0) < target.Value)
                {
                    entry.Progress = target.Value;
                    activityType = "progress";
                }
            }

            if (entry.Status is "planning" or "on_hold" or "dropped")
            {
                entry.Status = "in_progress";
                entry.CompletedAt = null;
            }

            if (entry.Status != "completed" && media.TotalUnits.HasValue
                && entry.Progress >= media.TotalUnits.Value)
            {
                entry.Status = "completed";
                entry.CompletedAt = now;
                activityType = "completed";
            }

            entry.UpdatedAt = now;
        }
        else
        {
            // Explicit confirmation is the only way a movie becomes completed.
            entry.Status = "completed";
            entry.CompletedAt ??= now;
            entry.UpdatedAt = now;
            activityType = "completed";
        }

        candidate.Status = PlaybackCandidateStatus.Confirmed;
        candidate.ConfirmedAt = now;

        await _context.SaveChangesAsync(ct);

        try
        {
            await _activityService.LogActivityAsync(
                userId: userId,
                activityType: activityType,
                mediaId: media.Id,
                status: entry.Status,
                progress: entry.Progress,
                ct: ct);
        }
        catch (Exception ex)
        {
            // Activity logging must not fail the confirmation.
            _logger.LogDebug(ex, "Could not log playback confirmation activity");
        }

        return new PlaybackConfirmResult(PlaybackConfirmOutcome.Confirmed, MapToDto(entry));
    }

    public async Task<bool> DismissAsync(int userId, int candidateId, CancellationToken ct = default)
    {
        var candidate = await _context.PlaybackCandidates
            .FirstOrDefaultAsync(c => c.Id == candidateId && c.UserId == userId, ct);

        if (candidate is null || candidate.Status != PlaybackCandidateStatus.Pending)
        {
            return false;
        }

        candidate.Status = PlaybackCandidateStatus.Dismissed;
        candidate.DismissedAt = _time.GetUtcNow().UtcDateTime;
        await _context.SaveChangesAsync(ct);
        return true;
    }

    private static int? ClampProgress(int? value, int? totalUnits)
    {
        if (value is null) return null;
        var progress = Math.Max(0, value.Value);
        return totalUnits.HasValue ? Math.Min(progress, totalUnits.Value) : progress;
    }

    /// <summary>
    /// Kue's library stores a single progress integer, so only season 1 (or an
    /// explicitly season-less request) can be mapped without inventing data.
    /// Later seasons leave progress untouched rather than writing an episode
    /// number that would be read back as a season-1 position.
    /// </summary>
    private static int? ProgressFromCandidate(PlaybackCandidate candidate)
    {
        return candidate.Season is null or 0 or 1 ? candidate.Episode : null;
    }

    private static PlaybackCandidateDto MapToDto(PlaybackCandidate candidate)
    {
        return new PlaybackCandidateDto
        {
            Id = candidate.Id,
            MediaId = candidate.MediaId,
            MediaType = candidate.MediaType,
            ExternalSource = candidate.ExternalSource,
            ExternalId = candidate.ExternalId,
            Title = candidate.Media?.Title,
            CoverImage = candidate.Media?.CoverImage,
            Year = candidate.Media?.Year,
            TotalUnits = candidate.Media?.TotalUnits,
            UnitName = candidate.Media?.UnitName,
            Season = candidate.Season,
            Episode = candidate.Episode,
            FirstSeenAt = candidate.FirstSeenAt,
            LastSeenAt = candidate.LastSeenAt,
            RequestCount = candidate.RequestCount
        };
    }

    private static LibraryEntryDto MapToDto(LibraryEntry entry)
    {
        return new LibraryEntryDto
        {
            Id = entry.Id,
            UserId = entry.UserId,
            MediaId = entry.MediaId,
            MediaType = entry.Media.MediaType,
            Status = entry.Status,
            IsFavorite = entry.IsFavorite,
            Rating = entry.Rating,
            Platform = entry.Platform,
            Progress = entry.Progress,
            TotalUnits = entry.Media.TotalUnits,
            UnitName = entry.Media.UnitName,
            AddedAt = entry.AddedAt,
            UpdatedAt = entry.UpdatedAt,
            CompletedAt = entry.CompletedAt,
            Media = new LibraryMediaSummaryDto
            {
                Id = entry.Media.Id,
                Title = entry.Media.Title,
                MediaType = entry.Media.MediaType,
                CoverImage = entry.Media.CoverImage,
                Year = entry.Media.Year,
                Score = entry.Media.Score,
                TotalUnits = entry.Media.TotalUnits,
                UnitName = entry.Media.UnitName,
                RuntimeMinutes = entry.Media.RuntimeMinutes,
                Platforms = entry.Media.Platforms,
                ExternalSource = entry.Media.ExternalSource,
                ExternalId = entry.Media.ExternalId
            }
        };
    }
}
