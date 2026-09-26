using System.Security.Claims;
using Kue.Api.Dtos.Auth;
using Kue.Api.Dtos.Common;
using Kue.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Kue.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private const string RefreshCookiePath = "/api/v1/auth";
    private readonly IAuthService _authService;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public AuthController(IAuthService authService, IConfiguration configuration, IWebHostEnvironment environment)
    {
        _authService = authService;
        _configuration = configuration;
        _environment = environment;
    }

    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        try 
        {
            var response = await _authService.RegisterAsync(request, ct);
            SetAuthCookies(response.AccessToken, response.RefreshToken);
            return StatusCode(StatusCodes.Status201Created, response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new MessageResponseDto(ex.Message));
        }
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        try 
        {
            var response = await _authService.LoginAsync(request, ct);
            SetAuthCookies(response.AccessToken, response.RefreshToken);
            return Ok(response);
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new MessageResponseDto("Invalid email or password."));
        }
    }

    [HttpPost("refresh")]
    [EnableRateLimiting("refresh")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest? request, CancellationToken ct)
    {
        var refreshToken = request?.RefreshToken;
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            Request.Cookies.TryGetValue("refreshToken", out refreshToken);
        }

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Unauthorized(new MessageResponseDto("Refresh token missing."));
        }

        try 
        {
            var response = await _authService.RefreshTokenAsync(refreshToken, ct);
            SetAuthCookies(response.AccessToken, response.RefreshToken);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            ClearAuthCookies();
            return Unauthorized(new MessageResponseDto(ex.Message));
        }
    }

    [HttpPost("logout")]
    [EnableRateLimiting("refresh")]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest? request, CancellationToken ct)
    {
        var refreshToken = request?.RefreshToken;
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            Request.Cookies.TryGetValue("refreshToken", out refreshToken);
        }

        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            await _authService.RevokeRefreshTokenAsync(refreshToken, ct);
        }
        
        ClearAuthCookies();
        return Ok(new MessageResponseDto("Logged out successfully."));
    }

    [Authorize]
    [HttpPost("change-password")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idClaim, out var userId))
        {
            return Unauthorized();
        }

        try
        {
            await _authService.ChangePasswordAsync(userId, request, ct);
            return Ok(new MessageResponseDto("Password changed successfully."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new MessageResponseDto(ex.Message));
        }
        catch (KeyNotFoundException)
        {
            return Unauthorized();
        }
    }

    [EnableRateLimiting("auth")]
    [HttpPost("forgot-password")]
    [ProducesResponseType(typeof(ForgotPasswordResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        var token = await _authService.ForgotPasswordAsync(request, ct);

        // Include resetToken in response only during development to simplify testing in Scalar
        var debugToken = _environment.IsDevelopment() ? token : null;

        return Ok(new ForgotPasswordResponseDto(
            "If an account with that email exists, a password reset token has been generated.",
            debugToken
        ));
    }
    [EnableRateLimiting("auth")]
    [HttpPost("reset-password")]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        try
        {
            await _authService.ResetPasswordAsync(request, ct);
            return Ok(new MessageResponseDto("Password has been reset successfully. Please log in with your new password."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new MessageResponseDto(ex.Message));
        }
    }

    // -- HELPERS --

    private void SetAuthCookies(string accessToken, string? refreshToken)
    {
        if (!int.TryParse(_configuration["Jwt:AccessTokenExpirationMinutes"], out var accessMinutes)) accessMinutes = 15;
        if (!int.TryParse(_configuration["Jwt:RefreshTokenExpirationDays"], out var refreshDays)) refreshDays = 7;

        var isSecure = Request.IsHttps;

        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = isSecure,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddMinutes(accessMinutes),
            Path = "/"
        };

        var refreshCookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = isSecure,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddDays(refreshDays),
            Path = RefreshCookiePath
        };

        Response.Cookies.Append("accessToken", accessToken, cookieOptions);
        if (!string.IsNullOrEmpty(refreshToken))
        {
            Response.Cookies.Append("refreshToken", refreshToken, refreshCookieOptions);
        }
    }

    private void ClearAuthCookies()
    {
        var isSecure = Request.IsHttps;

        Response.Cookies.Delete("accessToken", new CookieOptions
        {
            HttpOnly = true,
            Secure = isSecure,
            SameSite = SameSiteMode.Lax,
            Path = "/"
        });

        Response.Cookies.Delete("refreshToken", new CookieOptions
        {
            HttpOnly = true,
            Secure = isSecure,
            SameSite = SameSiteMode.Lax,
            Path = RefreshCookiePath
        });
    }
}