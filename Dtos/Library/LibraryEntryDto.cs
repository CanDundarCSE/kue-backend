namespace Kue.Api.Dtos.Library;

public class LibraryEntryDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int MediaId { get; set; }
    public string MediaType { get; set; } = null!; // "anime", "series", "manga", "movie", "game"
    public string Status { get; set; } = null!; // "planning", "in_progress", "completed", "on_hold", "dropped"
    public bool IsFavorite { get; set; }
    public int? Rating { get; set; } // 1-10
    public string? Platform { get; set; } // Specifically for games (e.g. "PS5", "PC")

    // Incremental progress (only for episodic/chapter media: anime, series, manga)
    public int? Progress { get; set; } // Current episode or chapter
    public int? TotalUnits { get; set; } // Total episodes or chapters
    public string? UnitName { get; set; } // "Episodes" or "Chapters" (null for movies and games)

    public DateTime AddedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // Embedded media summary for cards/lists
    public LibraryMediaSummaryDto Media { get; set; } = null!;
}

public class LibraryMediaSummaryDto
{
    public int Id { get; set; }
    public string Title { get; set; } = null!;
    public string MediaType { get; set; } = null!;
    public string? CoverImage { get; set; }
    public int? Year { get; set; }
    public double? Score { get; set; }
    public int? TotalUnits { get; set; }
    public string? UnitName { get; set; }
    public int? RuntimeMinutes { get; set; }
    public List<string>? Platforms { get; set; }
}
