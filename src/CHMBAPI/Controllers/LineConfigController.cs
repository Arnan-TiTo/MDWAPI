using CHMBAPI.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CHMBAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LineConfigController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public LineConfigController(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    [HttpGet("liff")]
    public async Task<IActionResult> GetLiffConfig()
    {
        var mode = _config["LiffSettings:Mode"] ?? "Local";

        if (mode.Equals("Local", StringComparison.OrdinalIgnoreCase))
        {
            var liffId = _config["LiffSettings:LocalLiffId"];
            return Ok(new { liffId });
        }

        var oaConfig = await _db.LineOaConfigs
            .Where(x => x.IsActive)
            .OrderBy(x => x.LineOaConfigId)
            .FirstOrDefaultAsync();

        if (oaConfig == null || string.IsNullOrEmpty(oaConfig.LiffId))
        {
            return NotFound(new { error = "LINE LIFF configuration was not found for production mode." });
        }

        return Ok(new { liffId = oaConfig.LiffId });
    }
}
