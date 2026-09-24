using System.Security.Claims;
using Kue.Api.Dtos.Common;
using Kue.Api.Dtos.Library;
using Kue.Api.Dtos.Media;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kue.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/me/library")]
[Produces("application/json")]
public class LibraryController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponseDto<LibraryEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetLibrary(
        [FromQuery] string? type = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var sampleMediaType = type ?? "anime";
        var isEpisodic = sampleMediaType is "anime" or "series" or "manga";

        var sampleEntries = new List<LibraryEntryDto>
        {
            new()
            {
                Id = 1,
                UserId = 1,
                MediaId = 25,
                MediaType = sampleMediaType,
                Status = status ?? (sampleMediaType == "game" ? "playing" : "in_progress"),
                Progress = isEpisodic ? 12 : null,
                TotalUnits = isEpisodic ? (sampleMediaType == "manga" ? 120 : 24) : null,
                UnitName = sampleMediaType == "manga" ? "Chapters" : (isEpisodic ? "Episodes" : null),
                Platform = sampleMediaType == "game" ? "PlayStation 5" : null,
                IsFavorite = true,
                Rating = 9,
                AddedAt = DateTime.UtcNow.AddDays(-10),
                UpdatedAt = DateTime.UtcNow,
                Media = new LibraryMediaSummaryDto
                {
                    Id = 25,
                    Title = sampleMediaType == "game" ? "Elden Ring" : (sampleMediaType == "movie" ? "Spirited Away" : "Frieren: Beyond Journey's End"),
                    MediaType = sampleMediaType,
                    Year = 2023,
                    Score = 9.4,
                    TotalUnits = isEpisodic ? (sampleMediaType == "manga" ? 120 : 24) : null,
                    UnitName = sampleMediaType == "manga" ? "Chapters" : (isEpisodic ? "Episodes" : null),
                    RuntimeMinutes = sampleMediaType == "movie" ? 125 : null,
                    Platforms = sampleMediaType == "game" ? ["PC", "PlayStation 5", "Xbox Series X"] : null
                }
            }
        };

        return Ok(new PagedResponseDto<LibraryEntryDto>
        {
            Items = sampleEntries,
            Page = page,
            PageSize = pageSize,
            TotalItems = 1,
            TotalPages = 1
        });
    }

    [HttpPost]
    [ProducesResponseType(typeof(LibraryEntryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult AddToLibrary([FromBody] AddToLibraryRequest request)
    {
        var isEpisodic = request.MediaType.ToLower() is "anime" or "series" or "manga";

        var entry = new LibraryEntryDto
        {
            Id = 101,
            UserId = 1,
            MediaId = request.MediaId,
            MediaType = request.MediaType.ToLower(),
            Status = request.Status.ToLower(),
            Progress = isEpisodic ? (request.Progress ?? 0) : null,
            TotalUnits = isEpisodic ? (request.MediaType.ToLower() == "manga" ? 100 : 24) : null,
            UnitName = request.MediaType.ToLower() == "manga" ? "Chapters" : (isEpisodic ? "Episodes" : null),
            Platform = request.MediaType.ToLower() == "game" ? request.Platform : null,
            IsFavorite = request.IsFavorite,
            Rating = request.Rating,
            AddedAt = DateTime.UtcNow,
            Media = new LibraryMediaSummaryDto
            {
                Id = request.MediaId,
                Title = "Added Media Title",
                MediaType = request.MediaType.ToLower(),
                Year = 2024,
                Score = 8.5,
                TotalUnits = isEpisodic ? (request.MediaType.ToLower() == "manga" ? 100 : 24) : null,
                UnitName = request.MediaType.ToLower() == "manga" ? "Chapters" : (isEpisodic ? "Episodes" : null),
                RuntimeMinutes = request.MediaType.ToLower() == "movie" ? 110 : null,
                Platforms = request.MediaType.ToLower() == "game" ? ["PC", "PS5"] : null
            }
        };

        return StatusCode(StatusCodes.Status201Created, entry);
    }

    [HttpGet("{mediaId:int}")]
    [ProducesResponseType(typeof(LibraryEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetLibraryEntry(int mediaId)
    {
        var entry = new LibraryEntryDto
        {
            Id = 1,
            UserId = 1,
            MediaId = mediaId,
            MediaType = "anime",
            Status = "in_progress",
            Progress = 12,
            TotalUnits = 24,
            UnitName = "Episodes",
            IsFavorite = true,
            Rating = 9,
            AddedAt = DateTime.UtcNow.AddDays(-5),
            Media = new LibraryMediaSummaryDto
            {
                Id = mediaId,
                Title = "Sample Media",
                MediaType = "anime",
                Year = 2024,
                Score = 9.0,
                TotalUnits = 24,
                UnitName = "Episodes"
            }
        };

        return Ok(entry);
    }

    [HttpPut("{mediaId:int}")]
    [ProducesResponseType(typeof(LibraryEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult UpdateLibraryEntry(int mediaId, [FromBody] UpdateLibraryRequest request)
    {
        var entry = new LibraryEntryDto
        {
            Id = 1,
            UserId = 1,
            MediaId = mediaId,
            MediaType = "anime",
            Status = request.Status ?? "in_progress",
            Progress = request.Progress ?? 12,
            TotalUnits = 24,
            UnitName = "Episodes",
            Platform = request.Platform,
            Rating = request.Rating,
            IsFavorite = request.IsFavorite ?? false,
            UpdatedAt = DateTime.UtcNow,
            Media = new LibraryMediaSummaryDto
            {
                Id = mediaId,
                Title = "Sample Media",
                MediaType = "anime",
                TotalUnits = 24,
                UnitName = "Episodes"
            }
        };

        return Ok(entry);
    }

    [HttpPatch("{mediaId:int}/status")]
    [ProducesResponseType(typeof(LibraryEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult UpdateStatus(int mediaId, [FromBody] UpdateLibraryStatusRequest request)
    {
        var entry = new LibraryEntryDto
        {
            Id = 1,
            UserId = 1,
            MediaId = mediaId,
            MediaType = "anime",
            Status = request.Status.ToLower(),
            Progress = request.Status.ToLower() == "completed" ? 24 : 12,
            TotalUnits = 24,
            UnitName = "Episodes",
            UpdatedAt = DateTime.UtcNow,
            CompletedAt = request.Status.ToLower() == "completed" ? DateTime.UtcNow : null,
            Media = new LibraryMediaSummaryDto
            {
                Id = mediaId,
                Title = "Sample Media",
                MediaType = "anime",
                TotalUnits = 24,
                UnitName = "Episodes"
            }
        };

        return Ok(entry);
    }

    [HttpPost("{mediaId:int}/progress")]
    [ProducesResponseType(typeof(LibraryEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult UpdateProgress(int mediaId, [FromBody] UpdateLibraryProgressRequest request)
    {
        // For demonstration / domain rule enforcement:
        // Progress (episodes/chapters) is only relevant to Anime, TV Series, and Manga
        var entry = new LibraryEntryDto
        {
            Id = 1,
            UserId = 1,
            MediaId = mediaId,
            MediaType = "anime",
            Status = request.Progress >= 24 ? "completed" : "in_progress",
            Progress = request.Progress,
            TotalUnits = 24,
            UnitName = "Episodes",
            UpdatedAt = DateTime.UtcNow,
            CompletedAt = request.Progress >= 24 ? DateTime.UtcNow : null,
            Media = new LibraryMediaSummaryDto
            {
                Id = mediaId,
                Title = "Sample Anime",
                MediaType = "anime",
                TotalUnits = 24,
                UnitName = "Episodes"
            }
        };

        return Ok(entry);
    }

    [HttpDelete("{mediaId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult RemoveFromLibrary(int mediaId)
    {
        return NoContent();
    }

    [HttpGet("continue")]
    [ProducesResponseType(typeof(List<LibraryEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetContinue()
    {
        var items = new List<LibraryEntryDto>
        {
            new()
            {
                Id = 1,
                UserId = 1,
                MediaId = 25,
                MediaType = "anime",
                Status = "in_progress",
                Progress = 12,
                TotalUnits = 24,
                UnitName = "Episodes",
                AddedAt = DateTime.UtcNow.AddDays(-3),
                Media = new LibraryMediaSummaryDto
                {
                    Id = 25,
                    Title = "Frieren: Beyond Journey's End",
                    MediaType = "anime",
                    Year = 2023,
                    Score = 9.4,
                    TotalUnits = 24,
                    UnitName = "Episodes"
                }
            },
            new()
            {
                Id = 2,
                UserId = 1,
                MediaId = 88,
                MediaType = "game",
                Status = "playing",
                Platform = "PlayStation 5",
                AddedAt = DateTime.UtcNow.AddDays(-7),
                Media = new LibraryMediaSummaryDto
                {
                    Id = 88,
                    Title = "Elden Ring",
                    MediaType = "game",
                    Year = 2022,
                    Score = 9.6,
                    Platforms = ["PC", "PlayStation 5", "Xbox Series X"]
                }
            }
        };

        return Ok(items);
    }
}