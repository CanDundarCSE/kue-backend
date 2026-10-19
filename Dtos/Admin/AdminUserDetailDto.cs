namespace Kue.Api.Dtos.Admin;

public class AdminUserDetailDto
{
    public AdminUserDto User { get; set; } = null!;
    public List<AdminUserActivityDto> RecentActivity { get; set; } = [];
}

public class AdminUserActivityDto
{
    public int Id { get; set; }
    public string ActivityType { get; set; } = null!;
    public string? Action { get; set; }
    public string? DetailText { get; set; }
    public string? FormattedText { get; set; }
    public string? Status { get; set; }
    public int? Progress { get; set; }
    public int? Rating { get; set; }

    public int? MediaId { get; set; }
    public string? MediaTitle { get; set; }
    public string? MediaType { get; set; }

    public DateTime CreatedAt { get; set; }
}
