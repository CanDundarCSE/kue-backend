namespace Kue.Api.Entities;

public class Review
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int MediaId { get; set; }
    public Media Media { get; set; } = null!;

    public string Content { get; set; } = null!;
    public int? Rating { get; set; } // 1-10
    public bool ContainsSpoilers { get; set; } = false;
    public int LikesCount { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public List<ReviewLike> Likes { get; set; } = [];
}
