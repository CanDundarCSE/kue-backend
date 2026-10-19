using System.Security.Claims;
using Kue.Api.Constants;
using Kue.Api.Data;
using Kue.Api.Dtos.Common;
using Kue.Api.Dtos.Users;
using Kue.Api.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kue.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/me")]
[Produces("application/json")]
public class MeController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly Kue.Api.Services.Activity.IActivityService _activityService;
    private readonly IConfiguration _configuration;

    public MeController(
        AppDbContext context,
        Kue.Api.Services.Activity.IActivityService activityService,
        IConfiguration configuration)
    {
        _context = context;
        _activityService = activityService;
        _configuration = configuration;
    }

    [HttpGet]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId.Value, ct);

        if (user is null)
        {
            return NotFound(new MessageResponseDto("User not found."));
        }

        return Ok(new UserProfileDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            KueId = user.KueId,
            AvatarId = user.AvatarId,
            Bio = user.Bio,
            IsPrivate = user.IsPrivate,
            Roles = user.Roles,
            CreatedAt = user.CreatedAt,
            TermsVersion = user.TermsVersion
        });
    }

    [HttpPut]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId.Value, ct);
        if (user is null)
        {
            return NotFound(new MessageResponseDto("User not found."));
        }

        if (!string.IsNullOrWhiteSpace(request.Username))
        {
            var username = request.Username.Trim();
            if (username.Length < 3)
            {
                return BadRequest(new MessageResponseDto("Username must be between 3 and 50 characters."));
            }

            if (username != user.Username)
            {
                // Case-insensitive so "Alice" and "alice" can't coexist; the
                // unique lower() index race-proofs this check (see the
                // DbUpdateException handler below).
                var normalizedUsername = username.ToLowerInvariant();
                var isTaken = await _context.Users.AnyAsync(
                    u => u.Username.ToLower() == normalizedUsername && u.Id != user.Id, ct);
                if (isTaken)
                {
                    return BadRequest(new MessageResponseDto("Username is already taken."));
                }
                user.Username = username;
            }
        }

        if (request.Bio is not null)
        {
            user.Bio = string.IsNullOrWhiteSpace(request.Bio) ? null : request.Bio.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var email = request.Email.Trim();
            if (email != user.Email)
            {
                var emailTaken = await _context.Users.AnyAsync(
                    u => u.Email == email && u.Id != user.Id, ct);
                if (emailTaken)
                {
                    return BadRequest(new MessageResponseDto("Email is already taken."));
                }
                user.Email = email;
            }
        }

        if (request.IsPrivate.HasValue)
        {
            user.IsPrivate = request.IsPrivate.Value;
        }

        if (request.AvatarId is not null)
        {
            // "" clears the avatar (fall back to the first-letter one).
            if (string.IsNullOrWhiteSpace(request.AvatarId))
            {
                user.AvatarId = null;
            }
            else
            {
                var avatarId = request.AvatarId.Trim();
                if (!Kue.Api.Constants.AvatarIds.All.Contains(avatarId))
                {
                    return BadRequest(new MessageResponseDto("Unknown avatar."));
                }
                user.AvatarId = avatarId;
            }
        }

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Concurrent registration/change won a unique index race.
            return BadRequest(new MessageResponseDto("Username or email is already taken."));
        }

        return Ok(new UserProfileDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            KueId = user.KueId,
            AvatarId = user.AvatarId,
            Bio = user.Bio,
            IsPrivate = user.IsPrivate,
            Roles = user.Roles,
            CreatedAt = user.CreatedAt,
            TermsVersion = user.TermsVersion
        });
    }

    [HttpGet("activity")]
    [ProducesResponseType(typeof(Kue.Api.Dtos.Stats.ActivityStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetActivity(
        [FromQuery] int days = 30,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var result = await _activityService.GetUserActivitiesAsync(userId.Value, days, limit, ct);
        return Ok(result);
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMe(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId.Value, ct);
        if (user is null)
        {
            return NotFound(new MessageResponseDto("User not found."));
        }

        _context.Users.Remove(user);
        await _context.SaveChangesAsync(ct);

        Response.Cookies.Delete("accessToken", new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/"
        });

        Response.Cookies.Delete("refreshToken", new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/api/v1/auth"
        });

        return NoContent();
    }

    [HttpPost("terms")]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AcceptTerms([FromBody] AcceptTermsRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (!request.IsTermsAccepted)
        {
            return BadRequest(new MessageResponseDto(
                "You must accept the Privacy Policy and Terms of Service to continue."));
        }

        if (!string.Equals(request.TermsVersion, LegalVersion.Current, StringComparison.Ordinal))
        {
            return BadRequest(new MessageResponseDto(
                "The Privacy Policy or Terms of Service has changed. Please reload and review the current version."));
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId.Value, ct);
        if (user is null)
        {
            return NotFound(new MessageResponseDto("User not found."));
        }

        user.IsTermsAccepted = true;
        user.TermsAcceptedAt = DateTime.UtcNow;
        user.TermsVersion = LegalVersion.Current;

        await _context.SaveChangesAsync(ct);

        return Ok(new UserProfileDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            KueId = user.KueId,
            AvatarId = user.AvatarId,
            Bio = user.Bio,
            IsPrivate = user.IsPrivate,
            Roles = user.Roles,
            CreatedAt = user.CreatedAt,
            TermsVersion = user.TermsVersion
        });
    }

    // Mints a short-lived hub token for the browser's SignalR connection. The
    // access token stays in an HttpOnly cookie, so cross-origin hub connections
    // (split web/API hosting) never need to expose it to JavaScript.
    [HttpPost("hub-token")]
    [ProducesResponseType(typeof(HubTokenResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateHubToken(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId.Value && !u.IsBanned, ct);
        if (user is null)
        {
            return NotFound(new MessageResponseDto("User not found."));
        }

        var (token, expiresAt) = HubTokenFactory.Create(user, _configuration);

        return Ok(new HubTokenResponseDto
        {
            Token = token,
            ExpiresAt = expiresAt
        });
    }

    private int? GetCurrentUserId()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idClaim, out var id) ? id : null;
    }
} 