using System.ComponentModel.DataAnnotations;

namespace Kue.Api.Dtos.Library;

public class AddToLibraryRequest
{
    // Provide either MediaId OR (ExternalSource + ExternalId)
    public int? MediaId { get; set; }

    [Required]
    public string MediaType { get; set; } = null!; // "anime", "series", "manga", "movie", "game"

    public string? ExternalSource { get; set; } // "anilist", "tmdb", "igdb"
    public string? ExternalId { get; set; }

    [Required]
    public string Status { get; set; } = null!; // "planning", "in_progress", "completed", "on_hold", "dropped"

    // Optional metadata to ingest immediately if media is not in database yet
    public string? Title { get; set; }
    public string? CoverImage { get; set; }
    public int? Year { get; set; }
    public double? Score { get; set; }
    public int? TotalUnits { get; set; }
    public string? UnitName { get; set; }
    public int? RuntimeMinutes { get; set; }

    // User library progress & tracking
    public int? Progress { get; set; }
    public string? Platform { get; set; }
    public bool IsFavorite { get; set; } = false;
    public int? Rating { get; set; }
}