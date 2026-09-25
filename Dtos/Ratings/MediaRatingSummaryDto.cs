namespace Kue.Api.Dtos.Ratings;

public class MediaRatingSummaryDto
{
    public int MediaId { get; set; }
    public double AverageRating { get; set; }
    public int TotalRatings { get; set; }
    public Dictionary<int, int> Distribution { get; set; } = new();
}
