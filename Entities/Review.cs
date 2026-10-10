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
    /// Private reviews are only visible to their author. Public reviews are
    /// returned by the per-media review feed.
    /// </summary>
    public bool IsPublic { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public Media Media { get; set; } = null!;
}