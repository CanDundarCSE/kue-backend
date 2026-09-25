using System.ComponentModel.DataAnnotations;

namespace Kue.Api.Dtos.Lists;

public class AddCustomListItemRequest
{
    // Provide either MediaId OR (MediaType + ExternalSource + ExternalId)
    public int? MediaId { get; set; }

    public string? MediaType { get; set; } // "anime", "series", "manga", "movie", "game"
    public string? ExternalSource { get; set; } // "anilist", "tmdb", "igdb"
    public string? ExternalId { get; set; }

    // Optional metadata to ingest immediately if media is not in database yet
    public string? Title { get; set; }
    public string? CoverImage { get; set; }
    public int? Year { get; set; }
    public double? Score { get; set; }
    public int? TotalUnits { get; set; }
    public string? UnitName { get; set; }
    public int? RuntimeMinutes { get; set; }

    [StringLength(500, ErrorMessage = "Notes cannot exceed 500 characters.")]
    public string? Notes { get; set; }

    public int? Order { get; set; }
}
