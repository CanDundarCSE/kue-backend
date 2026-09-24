namespace Kue.Api.Dtos.Media;

public class MediaDto
{
    public int Id { get; set; }
    public string MediaType { get; set; } = null!; // "movie", "series", "anime", "manga", "game"
    public string ExternalSource { get; set; } = null!; // "tmdb", "anilist", "igdb"
    public string ExternalId { get; set; } = null!;

    public string Title { get; set; } = null!;
    public string? OriginalTitle { get; set; }
    public string? Description { get; set; }
    public string? CoverImage { get; set; }
    public string? BannerImage { get; set; }
    public int? Year { get; set; }
    public double? Score { get; set; }
    public string? Status { get; set; } // "Finished Airing", "Releasing", "Released"
    public List<string> Genres { get; set; } = [];

    // Episodic / Chapter tracking info (only for Anime, Series, Manga)
    public int? TotalUnits { get; set; } // Total episodes or total chapters
    public string? UnitName { get; set; } // "Episodes" for anime/series, "Chapters" for manga, null for movies & games
    public int? TotalSeasons { get; set; } // For TV Series / multi-season Anime
    public int? TotalVolumes { get; set; } // For Manga / Light Novels

    // Movie specific
    public int? RuntimeMinutes { get; set; }

    // Game specific
    public List<string>? Platforms { get; set; } // ["PC", "PlayStation 5", "Xbox Series X", "Nintendo Switch"]
    public string? Developer { get; set; }

    public static MediaDto FromEntity(Entities.Media media)
    {
        return new MediaDto
        {
            Id = media.Id,
            MediaType = media.MediaType,
            ExternalSource = media.ExternalSource,
            ExternalId = media.ExternalId,
            Title = media.Title,
            OriginalTitle = media.OriginalTitle,
            Description = media.Description,
            CoverImage = media.CoverImage,
            BannerImage = media.BannerImage,
            Year = media.Year,
            Score = media.Score,
            Status = media.Status,
            Genres = media.Genres,
            TotalUnits = media.TotalUnits,
            UnitName = media.UnitName,
            TotalSeasons = media.TotalSeasons,
            TotalVolumes = media.TotalVolumes,
            RuntimeMinutes = media.RuntimeMinutes,
            Platforms = media.Platforms,
            Developer = media.Developer
        };
    }
}