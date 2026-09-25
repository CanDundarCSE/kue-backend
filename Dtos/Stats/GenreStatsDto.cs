namespace Kue.Api.Dtos.Stats;

public class GenreStatsDto
{
    public List<GenreStatItemDto> Items { get; set; } = [];
}

public class GenreStatItemDto
{
    public string Genre { get; set; } = null!;
    public int Count { get; set; }
    public double Percentage { get; set; }
}
