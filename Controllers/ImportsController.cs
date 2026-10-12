using System.Security.Claims;
using Kue.Api.Dtos.Common;
using Kue.Api.Dtos.Import;
using Kue.Api.Services.Import;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kue.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/me/imports")]
[Produces("application/json")]
public class ImportsController : ControllerBase
{
    private readonly IImportService _importService;

    public ImportsController(IImportService importService)
    {
        _importService = importService;
    }

    /// <summary>
    /// Upload an export file (MAL XML, AniList/Trakt JSON, CSV) and start a
    /// background import. Returns immediately with the job.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ImportJobDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> StartImport([FromBody] StartImportRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        try
        {
            var job = await _importService.StartAsync(userId.Value, request, ct);
            return StatusCode(StatusCodes.Status201Created, job);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new MessageResponseDto(ex.Message));
        }
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<ImportJobDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetImports(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        return Ok(await _importService.GetJobsAsync(userId.Value, ct));
    }

    [HttpGet("{jobId:int}")]
    [ProducesResponseType(typeof(ImportJobDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetImport(int jobId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var job = await _importService.GetJobAsync(userId.Value, jobId, ct);
        if (job is null) return NotFound(new MessageResponseDto("Import job not found."));

        return Ok(job);
    }

    /// <summary>
    /// Resolve conflicts with a bulk default (keep/import) plus per-item
    /// overrides, then apply the import in the background.
    /// </summary>
    [HttpPost("{jobId:int}/resolve")]
    [ProducesResponseType(typeof(ImportJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ResolveImport(
        int jobId, [FromBody] ResolveImportRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        try
        {
            var job = await _importService.ResolveAsync(userId.Value, jobId, request, ct);
            if (job is null) return NotFound(new MessageResponseDto("Import job not found."));
            return Ok(job);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new MessageResponseDto(ex.Message));
        }
    }

    [HttpDelete("{jobId:int}")]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CancelImport(int jobId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        try
        {
            var cancelled = await _importService.CancelAsync(userId.Value, jobId, ct);
            if (!cancelled) return NotFound(new MessageResponseDto("Import job not found."));
            return Ok(new MessageResponseDto("Import cancelled."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new MessageResponseDto(ex.Message));
        }
    }

    private int? GetCurrentUserId()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idClaim, out var id) ? id : null;
    }
}
