namespace Kue.Api.Dtos.Friends;

public class FriendActivityDto
{
    public int Id { get; set; }

    // Friend / Actor user information (for avatar initial and display name in Image 2)
    public int UserId { get; set; }
    public string Username { get; set; } = null!;
    public string? Bio { get; set; }
    public string UserInitial { get; set; } = null!; // e.g. "M", "R", "A", "J", "N"

    // Activity presentation
    public string ActivityType { get; set; } = null!; // "completed", "playing", "list_added", "started", "progress", "rated"
    public string Action { get; set; } = null!; // "completed", "is playing", "added 12 titles to", "started", "finished"
    public string? DetailText { get; set; } // "rated 5/5", "84 of 150 h", "ch. 1 of 327"
    public string FormattedText { get; set; } = null!; // e.g. "completed Breaking Bad · rated 5/5"

    // Relative timestamp and formatted date
    public string TimeAgo { get; set; } = null!; // "2h", "5h", "1d", "2d", "3d"
    public string FormattedDate { get; set; } = null!; // "FEB 14"
    public DateTime CreatedAt { get; set; }

    // Media metadata (if media-related)
    public int? MediaId { get; set; }
    public string? MediaTitle { get; set; }
    public string? MediaType { get; set; } // "anime", "manga", "series", "movie", "game"
    public string? MediaCoverImage { get; set; }

    // Custom list metadata (if list-related, e.g. "Aki added 12 titles to Cozy fantasy, ranked")
    public int? CustomListId { get; set; }
    public string? CustomListName { get; set; }
    public int? ItemCount { get; set; }

    // Status, progress and rating
    public string? Status { get; set; }
    public int? Progress { get; set; }
    public int? ProgressDelta { get; set; }
    public int? TotalUnits { get; set; }
    public string? UnitName { get; set; }
    public int? Rating { get; set; }
    public string? FormattedRating { get; set; } // "5/5"
    public int? RuntimeMinutes { get; set; }
    public string? Platform { get; set; }
}

