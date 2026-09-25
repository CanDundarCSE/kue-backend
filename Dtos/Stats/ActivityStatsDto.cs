namespace Kue.Api.Dtos.Stats;

public class ActivityStatsDto
{
    public List<ActivityItemDto> Items { get; set; } = [];
}

public class ActivityItemDto
{
    public string Date { get; set; } = null!; // "yyyy-MM-dd"
    public int Added { get; set; }
    public int Completed { get; set; }
}
