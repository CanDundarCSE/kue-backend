namespace Kue.Api.Dtos.Stats;

public class StatsOverviewDto
{
    // Overall Counts
    public int TotalItems { get; set; }
    public int TotalCompleted { get; set; }
    public int TotalInProgress { get; set; }
    public int TotalPlanned { get; set; }
    public int TotalDropped { get; set; }
    public int TotalOnHold { get; set; }
    public int TotalFavorites { get; set; }
    public double? MeanScore { get; set; }

    // Quick access summary fields
    public int TotalAnimeWatching { get; set; }
    public int TotalMangaReading { get; set; }
    public int TotalMoviesWatched { get; set; }
    public int TotalGamesCompleted { get; set; }
    public int TotalSeriesWatching { get; set; }
    public double? AverageAnimeScore { get; set; }
    public int TotalEpisodesWatched { get; set; }
    public int TotalChaptersRead { get; set; }

    // Detailed Type Breakdowns
    public MediaTypeBreakdownDto Anime { get; set; } = new();
    public MediaTypeBreakdownDto Manga { get; set; } = new();
    public MediaTypeBreakdownDto Movie { get; set; } = new();
    public MediaTypeBreakdownDto Series { get; set; } = new();
    public MediaTypeBreakdownDto Game { get; set; } = new();
}

public class MediaTypeBreakdownDto
{
    public int Total { get; set; }
    public int Completed { get; set; }
    public int InProgress { get; set; }
    public int Planned { get; set; }
    public int Dropped { get; set; }
    public int OnHold { get; set; }
    public int TotalUnitsConsumed { get; set; } // episodes watched, chapters read, etc.
    public double? MeanScore { get; set; }
}
