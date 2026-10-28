using System.Security.Claims;
using Kue.Api.Dtos.Common;
using Kue.Api.Dtos.Playback;
using Kue.Api.Services.Playback;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Kue.Api.Controllers;

/// <summary>
/// Management of the user's addon credentials. Each device gets its own URL so
/// connecting a second device never disconnects the first. The raw token is
/// shown once at creation; afterwards only existence and usage time are
/// returned.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/me/addon")]
[Produces("application/json")]
public class MeAddonController : ControllerBase
{
    private readonly IAddonCredentialService _credentials;
    private readonly IConfiguration _configuration;

    public MeAddonController(IAddonCredentialService credentials, IConfiguration configuration)
    {
        _credentials = credentials;
        _configuration = configuration;
    }

    [HttpGet("credentials")]
    [ProducesResponseType(typeof(AddonCredentialListDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCredentials(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var credentials = await _credentials.ListActiveAsync(userId.Value, ct);

        return Ok(new AddonCredentialListDto
        {
            Configured = credentials.Count > 0,
            Credentials = credentials
                .Select(c => new AddonCredentialSummaryDto
                {
                    Id = c.Id,
                    Label = c.Label,
                    CreatedAt = c.CreatedAt,
                    LastUsedAt = c.LastUsedAt
                })
                .ToList()
        });
    }

    [HttpPost("credentials")]
    [ProducesResponseType(typeof(AddonCredentialCreatedDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateCredential(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AddonCredentialCreateRequest? request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var result = await _credentials.CreateAsync(userId.Value, request?.Label, ct);
        if (result is null)
        {
            return Conflict(new MessageResponseDto(
                $"You can have at most {IAddonCredentialService.MaxActiveCredentials} active addon URLs. Revoke one first."));
        }

        var (rawToken, credential) = result.Value;

        return StatusCode(StatusCodes.Status201Created, new AddonCredentialCreatedDto
        {
            Id = credential.Id,
            Label = credential.Label,
            Token = rawToken,
            ManifestUrl = $"{ResolvePublicBaseUrl()}/addon/{rawToken}/manifest.json",
            CreatedAt = credential.CreatedAt
        });
    }

    /// <summary>Revokes a single device's URL; other devices stay connected.</summary>
    [HttpDelete("credentials/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RevokeCredential(int id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var revoked = await _credentials.RevokeAsync(userId.Value, id, ct);
        return revoked ? NoContent() : NotFound(new MessageResponseDto("No active addon credential."));
    }

    /// <summary>Disconnects every device at once.</summary>
    [HttpDelete("credentials")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RevokeAllCredentials(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var revoked = await _credentials.RevokeAllAsync(userId.Value, ct);
        return revoked ? NoContent() : NotFound(new MessageResponseDto("No active addon credential."));
    }

    private string ResolvePublicBaseUrl()
    {
        var baseUrl = _configuration["Addon:PublicBaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = $"{Request.Scheme}://{Request.Host}";
        }

        return baseUrl.TrimEnd('/');
    }

    private int? GetCurrentUserId()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idClaim, out var id) ? id : null;
    }
}
