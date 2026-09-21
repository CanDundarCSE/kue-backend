using Microsoft.AspNetCore.Mvc;

namespace Kue.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TestController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            message = "Kue API works!"
        });
    }
}