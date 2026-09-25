namespace Kue.Api.Entities;

public class Media
{
    public int Id { get; set; }
    public string MediaType { get; set; } = null!; // "movie", "series", "anime", "manga", "game"
    public string ExternalSource { get; set; } = null!; // "tmdb", "anilist", "igdb"
    public string ExternalId { get; set; } = null!; // External API ID

    // Core metadata
    public string Title { get; set; } = null!;
    public string? OriginalTitle { get; set; }
    public string? Description { get; set; }
    public string? CoverImage { get; set; }
    public string? BannerImage { get; set; }
    public int? Year { get; set; }
    public double? Score { get; set; }
    public string? Status { get; set; } // "Finished Airing", "Releasing", "Released", etc.
    public List<string> Genres { get; set; } = [];

    // Episodic / Chapter tracking (populated for anime, series, manga)
    public int? TotalUnits { get; set; } // Total episodes or chapters
    public string? UnitName { get; set; } // "Episodes" or "Chapters"
    public int? TotalSeasons { get; set; } // TV Series / multi-season Anime
    public int? TotalVolumes { get; set; } // Manga / Light Novels

    // Movie specific
    public int? RuntimeMinutes { get; set; }

    // Game specific
    public List<string>? Platforms { get; set; } // e.g. ["PC", "PlayStation 5", "Nintendo Switch"]
    public string? Developer { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; } = false;

    public List<LibraryEntry> LibraryEntries { get; set; } = [];
    public List<Review> Reviews { get; set; } = [];
}
