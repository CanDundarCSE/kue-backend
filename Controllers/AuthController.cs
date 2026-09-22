using Kue.Api.Dtos.Auth;
using Kue.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Kue.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        try 
        {
            var response = await _authService.RegisterAsync(request, ct);
            SetAuthCookies(response.AccessToken, response.RefreshToken);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
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
            return Unauthorized(new { message = "Invalid email or password." });
        }
    }

    [HttpPost("refresh")]
    [EnableRateLimiting("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue("refreshToken", out var refreshToken) || string.IsNullOrWhiteSpace(refreshToken))
        {
            return Unauthorized(new { message = "Refresh token missing." });
        }

        try 
        {
            var response = await _authService.RefreshTokenAsync(refreshToken, ct);
            SetAuthCookies(response.AccessToken, response.RefreshToken);
            return Ok(response);
        }
        catch (UnauthorizedAccessException)
        {
            ClearAuthCookies();
            return Unauthorized(new { message = "Invalid refresh token." });
        }
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (Request.Cookies.TryGetValue("refreshToken", out var refreshToken) && !string.IsNullOrWhiteSpace(refreshToken))
        {
            await _authService.RevokeRefreshTokenAsync(refreshToken, ct);
        }
        
        ClearAuthCookies();
        return Ok(new { message = "Logged out successfully" });
    }

    [HttpPost("change-password")]
    public IActionResult ChangePassword()
    {
        return Ok(new
        {
            message = "User changed password successfully!"
        });
    }

    [HttpPost("forgot-password")]
    public IActionResult ForgotPassword()
    {
        return Ok(new
        {
            message = "User forgot password successfully!"
        });
    }

    [HttpPost("reset-password")]
    public IActionResult ResetPassword()
    {
        return Ok(new
        {
            message = "User reset password successfully!"
        });
    }

    // -- HELPERS --

    private void SetAuthCookies(string accessToken, string? refreshToken)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = false, // Production'da true olacak
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddMinutes(15)
        };

        var refreshCookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = false,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddDays(7)
        };

        Response.Cookies.Append("accessToken", accessToken, cookieOptions);
        if (!string.IsNullOrEmpty(refreshToken))
        {
            Response.Cookies.Append("refreshToken", refreshToken, refreshCookieOptions);
        }
    }

    private void ClearAuthCookies()
    {
        Response.Cookies.Delete("accessToken");
        Response.Cookies.Delete("refreshToken");
    }
}