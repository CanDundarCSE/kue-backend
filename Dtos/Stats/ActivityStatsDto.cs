namespace Kue.Api.Dtos.Stats;

public class ActivityStatsDto
{
    /// <summary>
    /// Feed of recent activities (Image 1 "RECENT ACTIVITY").
    /// </summary>
    public List<ActivityItemDto> Items { get; set; } = [];

    /// <summary>
    /// Alias for Items for explicit frontend clarity.
    /// </summary>
    public List<ActivityItemDto> Activities { get; set; } = [];

    /// <summary>
    /// Daily aggregation summaries (e.g. for calendar contribution heatmaps).
    /// </summary>
    public List<ActivityDailySummaryDto> DailySummaries { get; set; } = [];
}

public class ActivityItemDto
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Formatted month and day, e.g. "FEB 14" (as displayed in Image 1).
    /// </summary>
    public string Date { get; set; } = null!;
    public string FormattedDate { get; set; } = null!;

    /// <summary>
    /// Relative time string, e.g. "2h", "1d".
    /// </summary>
    public string TimeAgo { get; set; } = null!;

    /// <summary>
    /// Activity category: "progress", "completed", "started", "added", "rated", "list_added".
    /// </summary>
    public string ActivityType { get; set; } = null!;

    /// <summary>
    /// Action verb or prefix, e.g. "logged 2 episodes of", "finished", "read 12 chapters of", "added", "completed".
    /// </summary>
    public string Action { get; set; } = null!;

    /// <summary>
    /// Secondary detail string, e.g. "now 18/28", "45 h · rated 4/5", "now 246/374", "watching, ep 9", "rated 5/5".
    /// </summary>
    public string? DetailText { get; set; }

    /// <summary>
    /// Full sentence description, e.g. "logged 2 episodes of Frieren · now 18/28".
    /// </summary>
    public string Description { get; set; } = null!;

    // Media metadata
    public int? MediaId { get; set; }
    public string? MediaTitle { get; set; }
    public string? MediaType { get; set; } // "anime", "manga", "series", "movie", "game"
    public string? MediaCoverImage { get; set; }

    // Progress details
    public string? Status { get; set; }
    public int? Progress { get; set; }
    public int? ProgressDelta { get; set; }
    public int? TotalUnits { get; set; }
    public string? UnitName { get; set; }

    // Rating & details
    public int? Rating { get; set; }
    public string? FormattedRating { get; set; } // "4/5", "5/5"
    public int? RuntimeMinutes { get; set; }
    public string? Platform { get; set; }

    // Custom list metadata
    public int? CustomListId { get; set; }
    public string? CustomListName { get; set; }
    public int? ItemCount { get; set; }

    // Backward compatibility for daily calendar heatmap
    public int Added { get; set; }
    public int Completed { get; set; }
}

public class ActivityDailySummaryDto
{
    public string Date { get; set; } = null!; // "yyyy-MM-dd"
    public int Added { get; set; }
    public int Completed { get; set; }
    public int Total { get; set; }
}
