using System.ComponentModel.DataAnnotations;

namespace Kue.Api.Dtos.Admin;

public class SyncMediaRequest
{
    [Required]
    [StringLength(50)]
    public string MediaType { get; set; } = null!; // "movie", "series", "anime", "manga", "game"

    [Required]
    [StringLength(50)]
    public string ExternalSource { get; set; } = null!; // "tmdb", "anilist", "igdb"

    [Required]
    [StringLength(100)]
    public string ExternalId { get; set; } = null!;
}