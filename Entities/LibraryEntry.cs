namespace Kue.Api.Entities;

public class LibraryEntry
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int MediaId { get; set; }
    public Media Media { get; set; } = null!;

    public string Status { get; set; } = null!; // "planning", "in_progress", "completed", "on_hold", "dropped"
    public int? Progress { get; set; } // Current episode or chapter (anime, series, manga)
    public string? Platform { get; set; } // Specifically for video games (e.g. "PC", "PS5")
    public int? Rating { get; set; } // 1-10 rating
    public bool IsFavorite { get; set; } = false;

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
