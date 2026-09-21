using Microsoft.AspNetCore.Mvc;

namespace Kue.Api.Controllers;

[ApiController]
[Route("api/v1/me/library")]
public class LibraryController : ControllerBase
{
    [HttpGet]
    public IActionResult GetLibrary(
        [FromQuery] string? type = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        return Ok(new
        {
            type,
            status,
            page,
            pageSize,
            items = new[]
            {
                new
                {
                    mediaId = 25,
                    mediaType = type ?? "anime",
                    status = status ?? "in_progress",
                    progress = 12,
                    isFavorite = false
                }
            }
        });
    }

    [HttpPost]
    public IActionResult AddToLibrary()
    {
        return Ok(new
        {
            message = "Media added to library successfully!"
        });
    }

    [HttpGet("{mediaId:int}")]
    public IActionResult GetLibraryEntry(int mediaId)
    {
        return Ok(new
        {
            mediaId,
            status = "in_progress",
            progress = 12,
            isFavorite = false
        });
    }

    [HttpPut("{mediaId:int}")]
    public IActionResult UpdateLibraryEntry(int mediaId)
    {
        return Ok(new
        {
            mediaId,
            message = "Library entry updated successfully!"
        });
    }

    [HttpPatch("{mediaId:int}/status")]
    public IActionResult UpdateStatus(int mediaId)
    {
        return Ok(new
        {
            mediaId,
            status = "completed",
            message = "Library status updated successfully!"
        });
    }

    [HttpPost("{mediaId:int}/progress")]
    public IActionResult UpdateProgress(int mediaId)
    {
        return Ok(new
        {
            mediaId,
            progress = 13,
            message = "Library progress updated successfully!"
        });
    }

    [HttpDelete("{mediaId:int}")]
    public IActionResult RemoveFromLibrary(int mediaId)
    {
        return Ok(new
        {
            mediaId,
            message = "Media removed from library successfully!"
        });
    }

    [HttpGet("continue")]
    public IActionResult GetContinue()
    {
        return Ok(new
        {
            items = new[]
            {
                new
                {
                    mediaId = 25,
                    mediaType = "anime",
                    status = "in_progress",
                    progress = 12
                }
            }
        });
    }
}