namespace Kue.Api.Dtos.Ratings;

public class UserRatingDto
{
    public int MediaId { get; set; }
    public string MediaTitle { get; set; } = null!;
    public string MediaType { get; set; } = null!;
    public string? MediaCoverImage { get; set; }
    public int? MediaYear { get; set; }
    public int Rating { get; set; }
    public DateTime RatedAt { get; set; }
}
