using System.Security.Claims;
using Kue.Api.Dtos.Common;
using Kue.Api.Dtos.Playback;
using Kue.Api.Services.Playback;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kue.Api.Controllers;

/// <summary>
/// Management of the user's addon credential. The raw token is shown once at
/// creation; afterwards only its existence and usage time are returned.
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
    [ProducesResponseType(typeof(AddonCredentialDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCredential(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var credential = await _credentials.GetActiveAsync(userId.Value, ct);

        return Ok(new AddonCredentialDto
        {
            Configured = credential is not null,
            CreatedAt = credential?.CreatedAt,
            LastUsedAt = credential?.LastUsedAt
        });
    }

    [HttpPost("credentials")]
    [ProducesResponseType(typeof(AddonCredentialCreatedDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateCredential(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var (rawToken, credential) = await _credentials.CreateAsync(userId.Value, ct);

        var baseUrl = _configuration["Addon:PublicBaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = $"{Request.Scheme}://{Request.Host}";
        }
        baseUrl = baseUrl.TrimEnd('/');

        return StatusCode(StatusCodes.Status201Created, new AddonCredentialCreatedDto
        {
            Token = rawToken,
            ManifestUrl = $"{baseUrl}/addon/{rawToken}/manifest.json",
            CreatedAt = credential.CreatedAt
        });
    }

    [HttpDelete("credentials")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RevokeCredential(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var revoked = await _credentials.RevokeAllAsync(userId.Value, ct);
        return revoked ? NoContent() : NotFound(new MessageResponseDto("No active addon credential."));
    }

    private int? GetCurrentUserId()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idClaim, out var id) ? id : null;
    }
}
