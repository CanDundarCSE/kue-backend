using Kue.Api.Dtos.Common;
using Kue.Api.Dtos.Media;
using Microsoft.AspNetCore.Mvc;

namespace Kue.Api.Controllers;

[ApiController]
[Route("api/v1/media")]
[Produces("application/json")]
public class MediaController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponseDto<MediaDto>), StatusCodes.Status200OK)]
    public IActionResult GetMedia(
        [FromQuery] string? type,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? genre = null,
        [FromQuery] int? year = null,
        [FromQuery] string? platform = null,
        [FromQuery] string? status = null)
    {
        var sampleItems = CreateSampleMediaList(type);

        return Ok(new PagedResponseDto<MediaDto>
        {
            Items = sampleItems,
            Page = page,
            PageSize = pageSize,
            TotalItems = sampleItems.Count,
            TotalPages = 1
        });
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(MediaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public IActionResult GetMediaById(int id)
    {
        var media = CreateSampleMediaItem(id, "anime");
        return Ok(media);
    }

    [HttpGet("trending")]
    [ProducesResponseType(typeof(PagedResponseDto<MediaDto>), StatusCodes.Status200OK)]
    public IActionResult GetTrendingMedia()
    {
        var items = CreateSampleMediaList(null);
        return Ok(new PagedResponseDto<MediaDto>
        {
            Items = items,
            Page = 1,
            PageSize = 20,
            TotalItems = items.Count,
            TotalPages = 1
        });
    }

    [HttpGet("popular")]
    [ProducesResponseType(typeof(PagedResponseDto<MediaDto>), StatusCodes.Status200OK)]
    public IActionResult GetPopularMedia()
    {
        var items = CreateSampleMediaList(null);
        return Ok(new PagedResponseDto<MediaDto>
        {
            Items = items,
            Page = 1,
            PageSize = 20,
            TotalItems = items.Count,
            TotalPages = 1
        });
    }

    [HttpGet("top")]
    [ProducesResponseType(typeof(PagedResponseDto<MediaDto>), StatusCodes.Status200OK)]
    public IActionResult GetTopMedia()
    {
        var items = CreateSampleMediaList(null);
        return Ok(new PagedResponseDto<MediaDto>
        {
            Items = items,
            Page = 1,
            PageSize = 20,
            TotalItems = items.Count,
            TotalPages = 1
        });
    }

    [HttpGet("upcoming")]
    [ProducesResponseType(typeof(PagedResponseDto<MediaDto>), StatusCodes.Status200OK)]
    public IActionResult GetUpcomingMedia()
    {
        var items = CreateSampleMediaList(null);
        return Ok(new PagedResponseDto<MediaDto>
        {
            Items = items,
            Page = 1,
            PageSize = 20,
            TotalItems = items.Count,
            TotalPages = 1
        });
    }

    [HttpGet("{id:int}/similar")]
    [ProducesResponseType(typeof(PagedResponseDto<MediaDto>), StatusCodes.Status200OK)]
    public IActionResult GetSimilar(int id)
    {
        var items = CreateSampleMediaList(null);
        return Ok(new PagedResponseDto<MediaDto>
        {
            Items = items,
            Page = 1,
            PageSize = 10,
            TotalItems = items.Count,
            TotalPages = 1
        });
    }

    [HttpGet("{id:int}/details")]
    [ProducesResponseType(typeof(MediaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public IActionResult GetDetails(int id)
    {
        var media = CreateSampleMediaItem(id, "game");
        return Ok(media);
    }

    // --- Helpers for Scalar / OpenAPI Sample Generation ---

    private static List<MediaDto> CreateSampleMediaList(string? requestedType)
    {
        return
        [
            new MediaDto
            {
                Id = 1,
                MediaType = "anime",
                ExternalSource = "anilist",
                ExternalId = "154587",
                Title = "Frieren: Beyond Journey's End",
                OriginalTitle = "Sousou no Frieren",
                Description = "During their decade-long quest to defeat the Demon King, the members of the hero's party...",
                Year = 2023,
                Score = 9.4,
                Status = "Finished Airing",
                Genres = ["Adventure", "Drama", "Fantasy"],
                TotalUnits = 28,
                UnitName = "Episodes",
                TotalSeasons = 1
            },
            new MediaDto
            {
                Id = 2,
                MediaType = "game",
                ExternalSource = "igdb",
                ExternalId = "119133",
                Title = "Elden Ring",
                Description = "THE NEW FANTASY ACTION RPG. Rise, Tarnished, and be guided by grace...",
                Year = 2022,
                Score = 9.6,
                Status = "Released",
                Genres = ["Action RPG", "Open World", "Dark Fantasy"],
                Platforms = ["PC", "PlayStation 5", "PlayStation 4", "Xbox Series X/S", "Xbox One"],
                Developer = "FromSoftware"
            },
            new MediaDto
            {
                Id = 3,
                MediaType = "movie",
                ExternalSource = "tmdb",
                ExternalId = "157336",
                Title = "Interstellar",
                Description = "A team of explorers travel through a wormhole in space in an attempt to ensure humanity's survival.",
                Year = 2014,
                Score = 8.7,
                Status = "Released",
                Genres = ["Sci-Fi", "Drama", "Adventure"],
                RuntimeMinutes = 169
            },
            new MediaDto
            {
                Id = 4,
                MediaType = "manga",
                ExternalSource = "anilist",
                ExternalId = "30002",
                Title = "Berserk",
                Year = 1989,
                Score = 9.5,
                Status = "Releasing",
                Genres = ["Action", "Adventure", "Dark Fantasy"],
                TotalUnits = 376,
                UnitName = "Chapters",
                TotalVolumes = 42
            }
        ];
    }

    private static MediaDto CreateSampleMediaItem(int id, string type)
    {
        return new MediaDto
        {
            Id = id,
            MediaType = type,
            ExternalSource = type == "game" ? "igdb" : "anilist",
            ExternalId = "100" + id,
            Title = type == "game" ? "Elden Ring" : "Sample Media Title",
            Year = 2023,
            Score = 9.2,
            Status = "Released",
            Genres = ["Action", "Adventure"],
            TotalUnits = type is "anime" or "series" or "manga" ? 24 : null,
            UnitName = type == "manga" ? "Chapters" : (type is "anime" or "series" ? "Episodes" : null),
            RuntimeMinutes = type == "movie" ? 135 : null,
            Platforms = type == "game" ? ["PC", "PlayStation 5"] : null
        };
    }
}