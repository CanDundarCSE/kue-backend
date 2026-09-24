using System.ComponentModel.DataAnnotations;

namespace Kue.Api.Dtos.Library;

public class AddToLibraryRequest
{
    [Required]
    public int MediaId { get; set; }

    [Required]
    public string MediaType { get; set; } = null!; // "anime", "series", "manga", "movie", "game"

    [Required]
    public string Status { get; set; } = null!; // "planning", "in_progress", "completed", "on_hold", "dropped"

    // Optional initial progress for episodic/reading media (ignored for movies/games)
    public int? Progress { get; set; }

    // Optional platform for video games (e.g. "PC", "PS5")
    public string? Platform { get; set; }

    public bool IsFavorite { get; set; } = false;
    public int? Rating { get; set; }
}