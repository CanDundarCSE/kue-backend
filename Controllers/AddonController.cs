using Kue.Api.Services.Playback;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Kue.Api.Controllers;

/// <summary>
/// Public Stremio-compatible addon surface. Access is granted by the opaque
/// token in the path (the addon URL itself is the credential). No Kue user
/// API is exposed here: a subtitle request applies playback-start evidence to
/// the token owner's library, and the only payload is an empty subtitle list.
/// </summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting("addon")]
[Route("addon/{token}")]
[Produces("application/json")]
public class AddonController : ControllerBase
{
    private readonly IAddonCredentialService _credentials;
    private readonly IPlaybackTrackingService _tracking;
    private readonly TimeProvider _time;
    private readonly ILogger<AddonController> _logger;

    public AddonController(
        IAddonCredentialService credentials,
        IPlaybackTrackingService tracking,
        TimeProvider time,
        ILogger<AddonController> logger)
    {
        _credentials = credentials;
        _tracking = tracking;
        _time = time;
        _logger = logger;
    }

    [HttpGet("manifest.json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Manifest(string token, CancellationToken ct)
    {
        var credential = await _credentials.ValidateAsync(token, ct);
        if (credential is null) return AddonNotFound();

        Response.Headers.CacheControl = "no-store";

        return Ok(new
        {
            id = "community.kue.playback",
            version = "1.0.0",
            name = "Kue Playback Bridge",
            description = "Adds titles you open in Nuvio to your Kue library. Completion stays manual in Kue.",
            resources = new[] { "subtitles" },
            types = new[] { "movie", "series" },
            idPrefixes = new[] { "tt", "tmdb" },
            catalogs = Array.Empty<object>(),
            behaviorHints = new { configurable = false }
        });
    }

    /// <summary>
    /// Clients call this after selecting a stream. The response is always an
    /// empty subtitle list; the request itself is the (weak) playback signal.
    /// </summary>
    [HttpGet("subtitles/{type}/{**rest}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Subtitles(string token, string type, string rest, CancellationToken ct)
    {
        var credential = await _credentials.ValidateAsync(token, ct);
        if (credential is null) return AddonNotFound();

        Response.Headers.CacheControl = "no-store";

        // `rest` can be "tt123.json" or "tt123/videoHash=...&videoSize=....json".
        var firstSegment = rest.Split('/', 2)[0];

        try
        {
            await _tracking.ApplyAsync(credential.UserId, type, firstSegment, ct);
            await _credentials.TouchAsync(
                credential.Id,
                _time.GetUtcNow().UtcDateTime,
                ct);
        }
        catch (Exception ex)
        {
            // A client must always get subtitles even if tracking fails. Never
            // log the token-bearing URL or the id payload at warning level.
            _logger.LogWarning(ex, "Could not apply addon playback tracking");
        }

        return Ok(new { subtitles = Array.Empty<object>() });
    }

    private IActionResult AddonNotFound()
    {
        Response.Headers.CacheControl = "no-store";
        // Same response for unknown and revoked tokens: no oracle.
        return NotFound(new { error = "not found" });
    }
}
