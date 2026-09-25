namespace Kue.Api.Dtos.Lists;

public class CustomListItemDto
{
    public int Id { get; set; }
    public int ListId { get; set; }
    public int MediaId { get; set; }
    public int Order { get; set; }
    public string? Notes { get; set; }
    public DateTime AddedAt { get; set; }

    public CustomListMediaSummaryDto Media { get; set; } = null!;
}

public class CustomListMediaSummaryDto
{
    public int Id { get; set; }
    public string Title { get; set; } = null!;
    public string MediaType { get; set; } = null!;
    public string? CoverImage { get; set; }
    public int? Year { get; set; }
    public double? Score { get; set; }
    public int? TotalUnits { get; set; }
    public string? UnitName { get; set; }
    public int? RuntimeMinutes { get; set; }
    public List<string>? Platforms { get; set; }
}
