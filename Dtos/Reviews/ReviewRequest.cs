namespace Kue.Api.Dtos.Reviews;

public class CreateReviewRequest
{
    public int MediaId { get; set; }
    public string Content { get; set; } = null!;
    public int? Rating { get; set; }
    public bool ContainsSpoilers { get; set; } = false;
    public List<SpoilerRangeDto>? Spoilers { get; set; }
    public bool IsPublic { get; set; } = true;
}

public class UpdateReviewRequest
{
    public string Content { get; set; } = null!;
    public int? Rating { get; set; }
    public bool ContainsSpoilers { get; set; }
    public List<SpoilerRangeDto>? Spoilers { get; set; }
    public bool IsPublic { get; set; } = true;
}