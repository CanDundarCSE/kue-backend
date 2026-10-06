using Kue.Api.Dtos.Friends;
using Kue.Api.Dtos.Stats;
using Kue.Api.Entities;

namespace Kue.Api.Services.Activity;

public interface IActivityService
{
    Task<UserActivity> LogActivityAsync(
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
        CancellationToken ct = default);

    Task<ActivityStatsDto> GetUserActivitiesAsync(
        int userId,
        int days = 30,
        int limit = 20,
        CancellationToken ct = default);

    Task<List<FriendActivityDto>> GetFriendsActivitiesAsync(
        int userId,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    string FormatTimeAgo(DateTime dt);
    string FormatDate(DateTime dt);
    string? FormatRating(int? rating);
}

