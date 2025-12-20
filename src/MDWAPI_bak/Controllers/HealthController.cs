using Microsoft.AspNetCore.Mvc;

namespace MDWAPI.Controllers;

[ApiController]
public class HealthController : ControllerBase
{
    [HttpGet("/health")]
    public IActionResult Health() => Ok(new { status = "OK", nowUtc = DateTime.UtcNow });
}
