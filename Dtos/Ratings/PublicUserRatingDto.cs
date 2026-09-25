namespace Kue.Api.Dtos.Ratings;

public class PublicUserRatingDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = null!;
    public int Rating { get; set; }
    public DateTime RatedAt { get; set; }
}
