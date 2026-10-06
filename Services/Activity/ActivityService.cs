using Kue.Api.Data;
using Kue.Api.Dtos.Friends;
using Kue.Api.Dtos.Stats;
using Kue.Api.Entities;
using Microsoft.EntityFrameworkCore;
using MediaEntity = Kue.Api.Entities.Media;

namespace Kue.Api.Services.Activity;

public class ActivityService : IActivityService
{
    private readonly AppDbContext _context;
    private readonly ILogger<ActivityService> _logger;

    public ActivityService(AppDbContext context, ILogger<ActivityService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<UserActivity> LogActivityAsync(
        int userId,
        string activityType,
        int? mediaId = null,
        int? customListId = null,
        string? action = null,
        string? detailText = null,
        string? formattedText = null,
        string? status = null,
        int? progress = null,
        int? progressDelta = null,
        int? totalUnits = null,
        string? unitName = null,
        int? rating = null,
        int? itemCount = null,
        string? platform = null,
        DateTime? createdAt = null,
        CancellationToken ct = default)
    {
        // Resolve media title / custom list name if needed for auto-formatting
        MediaEntity? media = null;
        if (mediaId.HasValue)
        {
            media = await _context.Media.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mediaId.Value, ct);
        }

        CustomList? customList = null;
        if (customListId.HasValue)
        {
            customList = await _context.CustomLists.AsNoTracking().FirstOrDefaultAsync(l => l.Id == customListId.Value, ct);
        }

        // Auto-compute totalUnits and unitName if not provided
        if (media != null)
        {
            totalUnits ??= media.TotalUnits;
            unitName ??= media.UnitName ?? (media.MediaType == "manga" ? "chapters" : "episodes");
        }

        // Build formatted strings if not explicitly supplied
        BuildActivityText(
            activityType,
            media,
            customList,
            status,
            progress,
            progressDelta,
            totalUnits,
            unitName,
            rating,
            itemCount,
            ref action,
            ref detailText,
            ref formattedText);

        var activity = new UserActivity
        {
            UserId = userId,
            ActivityType = activityType,
            MediaId = mediaId,
            CustomListId = customListId,
            Action = action,
            DetailText = detailText,
            FormattedText = formattedText,
            Status = status,
            Progress = progress,
            ProgressDelta = progressDelta,
            TotalUnits = totalUnits,
            UnitName = unitName,
            Rating = rating,
            ItemCount = itemCount,
            Platform = platform,
            CreatedAt = createdAt ?? DateTime.UtcNow
        };

        _context.UserActivities.Add(activity);
        await _context.SaveChangesAsync(ct);

        return activity;
    }

    public async Task<ActivityStatsDto> GetUserActivitiesAsync(
        int userId,
        int days = 30,
        int limit = 20,
        CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 365);
        limit = Math.Clamp(limit, 1, 100);
        var cutoff = DateTime.UtcNow.Date.AddDays(-days);

        // Fetch logged user activities
        var activities = await _context.UserActivities
            .AsNoTracking()
            .Include(a => a.Media)
            .Include(a => a.CustomList)
            .Where(a => a.UserId == userId && a.CreatedAt >= cutoff)
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        // If no or few logged activities, synthesize from LibraryEntries so existing users have instant data
        if (activities.Count < 5)
        {
            var synthesized = await SynthesizeUserActivitiesAsync(userId, cutoff, limit - activities.Count, ct);
            var existingMediaIds = activities.Where(a => a.MediaId.HasValue).Select(a => a.MediaId!.Value).ToHashSet();
            foreach (var syn in synthesized)
            {
                if (!existingMediaIds.Contains(syn.MediaId ?? -1))
                {
                    activities.Add(syn);
                }
            }
            activities = activities.OrderByDescending(a => a.CreatedAt).Take(limit).ToList();
        }

        // Map to ActivityItemDto
        var items = activities.Select(a => MapToActivityItemDto(a)).ToList();

        // Calculate daily aggregation summaries (for heatmap/calendar)
        var dailySummaries = CalculateDailySummaries(activities);

        return new ActivityStatsDto
        {
            Items = items,
            Activities = items,
            DailySummaries = dailySummaries
        };
    }

    public async Task<List<FriendActivityDto>> GetFriendsActivitiesAsync(
        int userId,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        // Find accepted mutual friends
        var friendUserIds = await _context.Friendships
            .AsNoTracking()
            .Where(f => f.Status == "accepted" && (f.RequesterId == userId || f.AddresseeId == userId))
            .Select(f => f.RequesterId == userId ? f.AddresseeId : f.RequesterId)
            .Distinct()
            .ToListAsync(ct);

        if (friendUserIds.Count == 0)
        {
            return [];
        }

        // Fetch friend activities
        var activities = await _context.UserActivities
            .AsNoTracking()
            .Include(a => a.User)
            .Include(a => a.Media)
            .Include(a => a.CustomList)
            .Where(a => friendUserIds.Contains(a.UserId))
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // If sparse, synthesize from friends' library entries
        if (activities.Count < pageSize)
        {
            var needed = pageSize - activities.Count;
            var synthFriends = await SynthesizeFriendsActivitiesAsync(friendUserIds, needed, ct);
            var existingActivityKeys = activities
                .Select(a => $"{a.UserId}_{a.MediaId}_{a.ActivityType}")
                .ToHashSet();

            foreach (var syn in synthFriends)
            {
                var key = $"{syn.UserId}_{syn.MediaId}_{syn.ActivityType}";
                if (!existingActivityKeys.Contains(key))
                {
                    activities.Add(syn);
                }
            }

            activities = activities.OrderByDescending(a => a.CreatedAt).Take(pageSize).ToList();
        }

        return activities.Select(MapToFriendActivityDto).ToList();
    }

    public string FormatTimeAgo(DateTime dt)
    {
        var diff = DateTime.UtcNow - dt;
        if (diff.TotalMinutes < 1) return "just now";
        if (diff.TotalHours < 1) return $"{Math.Max(1, (int)diff.TotalMinutes)}m";
        if (diff.TotalDays < 1) return $"{Math.Max(1, (int)diff.TotalHours)}h";
        if (diff.TotalDays < 30) return $"{Math.Max(1, (int)diff.TotalDays)}d";
        return $"{Math.Max(1, (int)(diff.TotalDays / 30))}mo";
    }

    public string FormatDate(DateTime dt)
    {
        // "FEB 14", "FEB 09"
        return dt.ToString("MMM dd").ToUpperInvariant();
    }

    public string? FormatRating(int? rating)
    {
        if (!rating.HasValue || rating.Value <= 0) return null;
        if (rating.Value <= 5) return $"{rating.Value}/5";

        var val = rating.Value / 2.0;
        return val % 1 == 0 ? $"{(int)val}/5" : $"{val:0.#}/5";
    }

    // --- Private text formatting engine ---

    private void BuildActivityText(
        string activityType,
        MediaEntity? media,
        CustomList? customList,
        string? status,
        int? progress,
        int? progressDelta,
        int? totalUnits,
        string? unitName,
        int? rating,
        int? itemCount,
        ref string? action,
        ref string? detailText,
        ref string? formattedText)
    {
        var mediaTitle = media?.Title ?? "item";
        var mediaType = media?.MediaType?.ToLowerInvariant() ?? "";
        var ratingStr = FormatRating(rating);

        switch (activityType)
        {
            case "progress":
                if (mediaType == "manga")
                {
                    action ??= progressDelta.HasValue && progressDelta.Value > 0
                        ? $"read {progressDelta.Value} {(progressDelta.Value == 1 ? "chapter" : "chapters")} of"
                        : "read chapters of";
                    detailText ??= totalUnits.HasValue ? $"now {progress ?? 0}/{totalUnits.Value}" : $"now ch. {progress ?? 0}";
                }
                else if (mediaType == "game")
                {
                    action ??= "played";
                    detailText ??= totalUnits.HasValue ? $"{progress ?? 0} of {totalUnits.Value} h" : $"{progress ?? 0} h";
                }
                else // anime, series, etc.
                {
                    action ??= progressDelta.HasValue && progressDelta.Value > 0
                        ? $"logged {progressDelta.Value} {(progressDelta.Value == 1 ? "episode" : "episodes")} of"
                        : "logged episodes of";
                    detailText ??= totalUnits.HasValue ? $"now {progress ?? 0}/{totalUnits.Value}" : $"now ep {progress ?? 0}";
                }
                formattedText ??= $"{action} {mediaTitle} · {detailText}";
                break;

            case "completed":
                action ??= mediaType is "game" or "movie" ? "finished" : "completed";
                var compDetails = new List<string>();
                if (mediaType == "game" && media?.RuntimeMinutes.HasValue == true)
                {
                    compDetails.Add($"{media.RuntimeMinutes.Value / 60} h");
                }
                if (!string.IsNullOrWhiteSpace(ratingStr))
                {
                    compDetails.Add($"rated {ratingStr}");
                }
                detailText ??= compDetails.Count > 0 ? string.Join(" · ", compDetails) : null;
                formattedText ??= detailText != null ? $"{action} {mediaTitle} · {detailText}" : $"{action} {mediaTitle}";
                break;

            case "started":
                action ??= "started";
                if (mediaType == "manga")
                {
                    detailText ??= totalUnits.HasValue ? $"ch. {progress ?? 1} of {totalUnits.Value}" : $"ch. {progress ?? 1}";
                }
                else
                {
                    detailText ??= totalUnits.HasValue ? $"ep. {progress ?? 1} of {totalUnits.Value}" : $"ep. {progress ?? 1}";
                }
                formattedText ??= $"{action} {mediaTitle} · {detailText}";
                break;

            case "playing":
                action ??= "is playing";
                detailText ??= progress.HasValue && totalUnits.HasValue
                    ? $"{progress.Value} of {totalUnits.Value} h"
                    : progress.HasValue ? $"{progress.Value} h" : null;
                formattedText ??= detailText != null ? $"{action} {mediaTitle} · {detailText}" : $"{action} {mediaTitle}";
                break;

            case "list_added":
                var listName = customList?.Name ?? "list";
                var count = itemCount ?? 1;
                action ??= count > 1 ? $"added {count} titles to" : "added to";
                detailText ??= null;
                formattedText ??= $"{action} {listName}";
                break;

            case "rated":
                action ??= "rated";
                detailText ??= ratingStr != null ? $"rated {ratingStr}" : null;
                formattedText ??= detailText != null ? $"{action} {mediaTitle} · {detailText}" : $"{action} {mediaTitle}";
                break;

            case "added":
            default:
                action ??= "added";
                var normStatus = status?.ToLowerInvariant() ?? "planning";
                if (normStatus is "watching" or "reading" or "playing" or "in_progress")
                {
                    var unitPrefix = mediaType == "manga" ? "ch" : "ep";
                    detailText ??= progress.HasValue && progress.Value > 0
                        ? $"{normStatus}, {unitPrefix} {progress.Value}"
                        : normStatus;
                }
                else
                {
                    detailText ??= normStatus;
                }
                formattedText ??= !string.IsNullOrWhiteSpace(detailText)
                    ? $"{action} {mediaTitle} · {detailText}"
                    : $"{action} {mediaTitle}";
                break;
        }
    }

    private ActivityItemDto MapToActivityItemDto(UserActivity a)
    {
        var ratingStr = FormatRating(a.Rating);
        var mediaTitle = a.Media?.Title;
        var listName = a.CustomList?.Name;

        var fullDesc = a.FormattedText
            ?? (!string.IsNullOrWhiteSpace(a.DetailText)
                ? $"{a.Action} {mediaTitle ?? listName} · {a.DetailText}"
                : $"{a.Action} {mediaTitle ?? listName}");

        return new ActivityItemDto
        {
            Id = a.Id,
            CreatedAt = a.CreatedAt,
            Date = FormatDate(a.CreatedAt),
            FormattedDate = FormatDate(a.CreatedAt),
            TimeAgo = FormatTimeAgo(a.CreatedAt),
            ActivityType = a.ActivityType,
            Action = a.Action ?? "logged",
            DetailText = a.DetailText,
            Description = fullDesc,
            MediaId = a.MediaId,
            MediaTitle = mediaTitle,
            MediaType = a.Media?.MediaType,
            MediaCoverImage = a.Media?.CoverImage,
            Status = a.Status,
            Progress = a.Progress,
            ProgressDelta = a.ProgressDelta,
            TotalUnits = a.TotalUnits ?? a.Media?.TotalUnits,
            UnitName = a.UnitName ?? a.Media?.UnitName,
            Rating = a.Rating,
            FormattedRating = ratingStr,
            RuntimeMinutes = a.Media?.RuntimeMinutes,
            Platform = a.Platform,
            CustomListId = a.CustomListId,
            CustomListName = listName,
            ItemCount = a.ItemCount,
            Added = a.ActivityType == "added" ? 1 : 0,
            Completed = a.ActivityType == "completed" ? 1 : 0
        };
    }

    private FriendActivityDto MapToFriendActivityDto(UserActivity a)
    {
        var ratingStr = FormatRating(a.Rating);
        var mediaTitle = a.Media?.Title;
        var listName = a.CustomList?.Name;
        var username = a.User?.Username ?? "User";

        var fullText = a.FormattedText
            ?? (!string.IsNullOrWhiteSpace(a.DetailText)
                ? $"{a.Action} {mediaTitle ?? listName} · {a.DetailText}"
                : $"{a.Action} {mediaTitle ?? listName}");

        var initial = string.IsNullOrWhiteSpace(username) ? "U" : username.Trim().Substring(0, 1).ToUpperInvariant();

        return new FriendActivityDto
        {
            Id = a.Id,
            UserId = a.UserId,
            Username = username,
            Bio = a.User?.Bio,
            UserInitial = initial,
            ActivityType = a.ActivityType,
            Action = a.Action ?? "updated",
            DetailText = a.DetailText,
            FormattedText = fullText,
            TimeAgo = FormatTimeAgo(a.CreatedAt),
            FormattedDate = FormatDate(a.CreatedAt),
            CreatedAt = a.CreatedAt,
            MediaId = a.MediaId,
            MediaTitle = mediaTitle,
            MediaType = a.Media?.MediaType,
            MediaCoverImage = a.Media?.CoverImage,
            CustomListId = a.CustomListId,
            CustomListName = listName,
            ItemCount = a.ItemCount,
            Status = a.Status,
            Progress = a.Progress,
            ProgressDelta = a.ProgressDelta,
            TotalUnits = a.TotalUnits ?? a.Media?.TotalUnits,
            UnitName = a.UnitName ?? a.Media?.UnitName,
            Rating = a.Rating,
            FormattedRating = ratingStr,
            RuntimeMinutes = a.Media?.RuntimeMinutes,
            Platform = a.Platform
        };
    }

    private List<ActivityDailySummaryDto> CalculateDailySummaries(List<UserActivity> activities)
    {
        return activities
            .GroupBy(a => a.CreatedAt.ToString("yyyy-MM-dd"))
            .OrderByDescending(g => g.Key)
            .Select(g => new ActivityDailySummaryDto
            {
                Date = g.Key,
                Added = g.Count(a => a.ActivityType == "added"),
                Completed = g.Count(a => a.ActivityType == "completed"),
                Total = g.Count()
            })
            .ToList();
    }

    private async Task<List<UserActivity>> SynthesizeUserActivitiesAsync(
        int userId, DateTime cutoff, int limit, CancellationToken ct)
    {
        var entries = await _context.LibraryEntries
            .AsNoTracking()
            .Include(e => e.Media)
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.UpdatedAt ?? e.AddedAt)
            .Take(limit)
            .ToListAsync(ct);

        var list = new List<UserActivity>();
        foreach (var e in entries)
        {
            var isComp = e.Status == "completed";
            var type = isComp ? "completed" : (e.Progress.HasValue && e.Progress.Value > 0 ? "progress" : "added");
            string? act = null, detail = null, formatted = null;

            BuildActivityText(
                type,
                e.Media,
                null,
                e.Status,
                e.Progress,
                null,
                e.Media.TotalUnits,
                e.Media.UnitName,
                e.Rating,
                null,
                ref act,
                ref detail,
                ref formatted);

            list.Add(new UserActivity
            {
                UserId = userId,
                ActivityType = type,
                MediaId = e.MediaId,
                Media = e.Media,
                Action = act,
                DetailText = detail,
                FormattedText = formatted,
                Status = e.Status,
                Progress = e.Progress,
                TotalUnits = e.Media.TotalUnits,
                UnitName = e.Media.UnitName,
                Rating = e.Rating,
                Platform = e.Platform,
                CreatedAt = e.UpdatedAt ?? e.AddedAt
            });
        }

        return list;
    }

    private async Task<List<UserActivity>> SynthesizeFriendsActivitiesAsync(
        List<int> friendIds, int limit, CancellationToken ct)
    {
        var entries = await _context.LibraryEntries
            .AsNoTracking()
            .Include(e => e.User)
            .Include(e => e.Media)
            .Where(e => friendIds.Contains(e.UserId))
            .OrderByDescending(e => e.UpdatedAt ?? e.AddedAt)
            .Take(limit)
            .ToListAsync(ct);

        var list = new List<UserActivity>();
        foreach (var e in entries)
        {
            var isComp = e.Status == "completed";
            var type = isComp ? "completed" : (e.Progress.HasValue && e.Progress.Value > 0 ? "progress" : "added");
            string? act = null, detail = null, formatted = null;

            BuildActivityText(
                type,
                e.Media,
                null,
                e.Status,
                e.Progress,
                null,
                e.Media.TotalUnits,
                e.Media.UnitName,
                e.Rating,
                null,
                ref act,
                ref detail,
                ref formatted);

            list.Add(new UserActivity
            {
                UserId = e.UserId,
                User = e.User,
                ActivityType = type,
                MediaId = e.MediaId,
                Media = e.Media,
                Action = act,
                DetailText = detail,
                FormattedText = formatted,
                Status = e.Status,
                Progress = e.Progress,
                TotalUnits = e.Media.TotalUnits,
                UnitName = e.Media.UnitName,
                Rating = e.Rating,
                Platform = e.Platform,
                CreatedAt = e.UpdatedAt ?? e.AddedAt
            });
        }

        return list;
    }
}
