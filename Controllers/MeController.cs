using Microsoft.AspNetCore.Mvc;

namespace Kue.Api.Controllers;

[ApiController]
[Route("api/v1/me")]
public class MeController : ControllerBase
{
    [HttpGet]
    public IActionResult GetMe()
    {
        return Ok(new
        {
            message = "User information retrieved successfully!"
        });
    }

    [HttpPut]
    public IActionResult UpdateMe()
    {
        return Ok(new
        {
            message = "User information updated successfully!"
        });
    }

    [HttpGet("activity")]
    public IActionResult GetActivity()
    {
        return Ok(new
        {
            message = "User activity retrieved successfully!"
        });
    }

     [HttpDelete]
    public IActionResult DeleteMe()
    {
        return Ok(new
        {
            message = "User account deleted successfully!"
        });
    }
} 