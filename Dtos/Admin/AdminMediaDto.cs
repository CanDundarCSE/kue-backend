namespace Kue.Api.Dtos.Admin;

public class AdminMediaDto
{
    public int Id { get; set; }
    public string Title { get; set; } = null!;
    public string MediaType { get; set; } = null!;
    public string ExternalSource { get; set; } = null!;
    public string ExternalId { get; set; } = null!;
    public string? CoverImage { get; set; }
    public int? Year { get; set; }
    public double? Score { get; set; }
    public int? TotalUnits { get; set; }
    public string? UnitName { get; set; }
    public int? RuntimeMinutes { get; set; }
    public List<string>? Platforms { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int TrackedCount { get; set; }
}
