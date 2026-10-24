using Kue.Api.Services.Playback;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Kue.Api.Controllers;

/// <summary>
/// Public Stremio-compatible addon surface. Access is granted by the opaque
/// token in the path (the addon URL itself is the credential). No Kue user
/// API is exposed here: the only side effect is recording a playback-start
/// candidate, and the only payload is an empty subtitle list.
/// </summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting("addon")]
[Route("addon/{token}")]
[Produces("application/json")]
public class AddonController : ControllerBase
{
    private readonly IAddonCredentialService _credentials;
    private readonly IPlaybackCandidateService _candidates;
    private readonly TimeProvider _time;
    private readonly ILogger<AddonController> _logger;

    public AddonController(
        IAddonCredentialService credentials,
        IPlaybackCandidateService candidates,
        TimeProvider time,
        ILogger<AddonController> logger)
    {
        _credentials = credentials;
        _candidates = candidates;
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
            description = "Records a playback-start candidate in Kue. Does not detect completion.",
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
            await _candidates.RecordAsync(credential.UserId, type, firstSegment, ct);
            await _credentials.TouchAsync(
                credential.Id,
                _time.GetUtcNow().UtcDateTime,
                ct);
        }
        catch (Exception ex)
        {
            // A client must always get subtitles even if bookkeeping fails.
            _logger.LogWarning(ex, "Could not record addon playback candidate");
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
