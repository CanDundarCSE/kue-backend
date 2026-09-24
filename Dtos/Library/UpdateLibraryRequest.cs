namespace Kue.Api.Dtos.Library;

public class UpdateLibraryRequest
{
    public string? Status { get; set; } // "planning", "in_progress", "completed", "on_hold", "dropped"
    public int? Progress { get; set; } // For anime/series/manga
    public string? Platform { get; set; } // For games (e.g. "PS5", "PC")
    public int? Rating { get; set; } // 1-10
    public bool? IsFavorite { get; set; }
}