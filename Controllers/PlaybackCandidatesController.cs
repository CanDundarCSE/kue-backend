using System.Security.Claims;
using Kue.Api.Dtos.Common;
using Kue.Api.Dtos.Library;
using Kue.Api.Dtos.Playback;
using Kue.Api.Services.Playback;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kue.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/me/playback-candidates")]
[Produces("application/json")]
public class PlaybackCandidatesController : ControllerBase
{
    private readonly IPlaybackCandidateService _candidates;

    public PlaybackCandidatesController(IPlaybackCandidateService candidates)
    {
        _candidates = candidates;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<PlaybackCandidateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        return Ok(await _candidates.ListPendingAsync(userId.Value, ct));
    }

    /// <summary>
    /// Explicit confirmation. This is the only way a candidate becomes a
    /// library entry; a subtitle request alone never does.
    /// </summary>
    [HttpPost("{id:int}/confirm")]
    [ProducesResponseType(typeof(LibraryEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Confirm(int id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var result = await _candidates.ConfirmAsync(userId.Value, id, ct);

        return result.Outcome switch
        {
            PlaybackConfirmOutcome.Confirmed => Ok(result.Entry),
            PlaybackConfirmOutcome.Unresolved => Conflict(new MessageResponseDto(
                "This title could not be matched to a Kue media record yet, so it cannot be confirmed.")),
            _ => NotFound(new MessageResponseDto("Playback candidate not found."))
        };
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Dismiss(int id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var dismissed = await _candidates.DismissAsync(userId.Value, id, ct);
        return dismissed ? NoContent() : NotFound(new MessageResponseDto("Playback candidate not found."));
    }

    private int? GetCurrentUserId()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idClaim, out var id) ? id : null;
    }
}
