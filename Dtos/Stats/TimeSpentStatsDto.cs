namespace Kue.Api.Dtos.Stats;

public class TimeSpentStatsDto
{
    public int TotalMinutes { get; set; }
    public double TotalHours { get; set; }
    public double TotalDays { get; set; }

    public int AnimeMinutes { get; set; }
    public int SeriesMinutes { get; set; }
    public int MovieMinutes { get; set; }

    // Supplementary status-driven counts (no artificial minutes)
    public int GamesCompleted { get; set; }
    public int MangaChaptersRead { get; set; }
}
