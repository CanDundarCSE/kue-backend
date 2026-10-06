namespace Kue.Api.Entities;

public class UserActivity
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public string ActivityType { get; set; } = null!; // "progress", "completed", "started", "added", "rated", "list_added"

    public int? MediaId { get; set; }
    public Media? Media { get; set; }

    public int? CustomListId { get; set; }
    public CustomList? CustomList { get; set; }

    public string? Action { get; set; } // "logged", "finished", "read", "added", "completed", "is playing", "started"
    public string? DetailText { get; set; } // "now 18/28", "45 h · rated 4/5", "now 246/374", "watching, ep 9", "rated 5/5"
    public string? FormattedText { get; set; } // Full text representation

    public string? Status { get; set; } // "watching", "reading", "playing", "completed", "planning", "on_hold", "dropped"
    public int? Progress { get; set; }
    public int? ProgressDelta { get; set; }
    public int? TotalUnits { get; set; }
    public string? UnitName { get; set; }
    public int? Rating { get; set; } // 1-10
    public int? ItemCount { get; set; }
    public string? Platform { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

