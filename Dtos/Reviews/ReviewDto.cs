namespace Kue.Api.Dtos.Reviews;

public class ReviewDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Username { get; set; } = null!;
    public int MediaId { get; set; }
    public string MediaTitle { get; set; } = null!;
    public string MediaType { get; set; } = null!;
    public string MediaCoverImage { get; set; } = null!;
    public string Content { get; set; } = null!;
    public int? Rating { get; set; }
    public bool ContainsSpoilers { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}