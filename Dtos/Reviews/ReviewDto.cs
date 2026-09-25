namespace Kue.Api.Dtos.Reviews;

public class ReviewDto
{
    public int Id { get; set; }
    
    // User info
    public int UserId { get; set; }
    public string Username { get; set; } = null!;

    // Media info
    public int MediaId { get; set; }
    public string MediaTitle { get; set; } = null!;
    public string MediaType { get; set; } = null!;
    public string? MediaCoverImage { get; set; }
    public int? MediaYear { get; set; }

    // Review details
    public string Content { get; set; } = null!;
    public int? Rating { get; set; }
    public bool ContainsSpoilers { get; set; }
    public int LikesCount { get; set; }
    public bool IsLikedByCurrentUser { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
