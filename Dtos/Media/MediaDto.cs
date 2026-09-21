namespace Kue.Api.DTOs.Media;

public class MediaDto
{
    public string MediaType { get; set; } = null!;
    public string ExternalSource { get; set; } = null!;
    public string ExternalId { get; set; } = null!;

    public string Title { get; set; } = null!;
    public string? CoverImage { get; set; }
    public int? Year { get; set; }
    public double? Score { get; set; }
    public string? Status { get; set; }
}