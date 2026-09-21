using Microsoft.AspNetCore.Mvc;

namespace Kue.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    [HttpPost("register")]
    public IActionResult Register(){
        return Ok(new
        {
            message = "User registered successfully!"
        });
    }

     [HttpPost("login")]
    public IActionResult Login(){
        return Ok(new
        {
            message = "User logged in successfully!"
        });
    }

     [HttpPost("refresh")]
    public IActionResult Refresh(){
        return Ok(new
        {
            message = "User refreshed successfully!"
        });
    }

     [HttpPost("logout")]
    public IActionResult Logout(){
        return Ok(new
        {
            message = "User logged out successfully!"
        });
    }

     [HttpPost("change-password")]
    public IActionResult ChangePassword(){
        return Ok(new
        {
            message = "User changed password successfully!"
        });
    }

     [HttpPost("forgot-password")]
    public IActionResult ForgotPassword(){
        return Ok(new
        {
            message = "User forgot password successfully!"
        });
    }

     [HttpPost("reset-password")]
    public IActionResult ResetPassword(){
        return Ok(new
        {
            message = "User reset password successfully!"
        });
    }
}