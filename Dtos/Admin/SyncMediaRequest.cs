using System.ComponentModel.DataAnnotations;

namespace Kue.Api.Dtos.Admin;

public class SyncMediaRequest
{
    [Required]
    public string MediaType { get; set; } = null!; // "movie", "series", "anime", "manga", "game"

    [Required]
    public string ExternalSource { get; set; } = null!; // "tmdb", "anilist", "igdb"

    [Required]
    public string ExternalId { get; set; } = null!;
}