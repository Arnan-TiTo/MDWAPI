using MDWAPI.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Controllers;

[ApiController]
public class HealthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<HealthController> _logger;

    public HealthController(AppDbContext db, ILogger<HealthController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet("/health")]
    public IActionResult Health() => Ok(new { status = "OK", nowUtc = DateTime.UtcNow });

    [HttpGet("/health/db")]
    public async Task<IActionResult> DatabaseHealth()
    {
        var conn = _db.Database.GetDbConnection();

        try
        {
            var canConnect = await _db.Database.CanConnectAsync();
            return Ok(new
            {
                status = canConnect ? "OK" : "FAILED",
                canConnect,
                dataSource = conn.DataSource,
                database = conn.Database,
                nowUtc = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database health check failed");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                status = "FAILED",
                error = ex.GetType().Name,
                message = ex.Message,
                dataSource = conn.DataSource,
                database = conn.Database,
                nowUtc = DateTime.UtcNow
            });
        }
    }
}
