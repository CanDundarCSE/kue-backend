namespace Kue.Api.Entities;

public class Review
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int MediaId { get; set; }
    public string Content { get; set; } = null!;
    public int? Rating { get; set; }
    public bool ContainsSpoilers { get; set; } = false;

    /// <summary>
    /// Spoiler-masked spans as UTF-16 char offsets (exclusive end) into
    /// <see cref="Content"/>. Stored as a JSON column; empty when the review
    /// has no masked parts.
    /// </summary>
    public List<SpoilerRange> Spoilers { get; set; } = [];

    /// <summary>
    /// Private reviews are only visible to their author. Public reviews are
    /// returned by the per-media review feed.
    /// </summary>
    public bool IsPublic { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public Media Media { get; set; } = null!;
}

/// <summary>
/// One spoiler-masked span. <see cref="Start"/> is inclusive,
/// <see cref="End"/> exclusive, both UTF-16 char offsets into the review
/// content (matching C# string indexing and JS selection offsets).
/// </summary>
public class SpoilerRange
{
    public int Start { get; set; }
    public int End { get; set; }
}