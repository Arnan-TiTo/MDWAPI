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

    /// <summary>ดึงค่า LiffId ตาม Mode (Local/Production) ที่ตั้งไว้ใน appsettings.json</summary>
    [HttpGet("liff")]
    public async Task<IActionResult> GetLiffConfig()
    {
        var mode = _config["LiffSettings:Mode"] ?? "Local";
        
        if (mode.Equals("Local", StringComparison.OrdinalIgnoreCase))
        {
            var liffId = _config["LiffSettings:LocalLiffId"];
            return Ok(new { liffId });
        }

        // Production Mode: ดึงจากฐานข้อมูลตาราง mbw.LineOaConfigs
        var oaConfig = await _db.LineOaConfigs
            .Where(x => x.IsActive)
            .OrderBy(x => x.LineOaConfigId)
            .FirstOrDefaultAsync();

        if (oaConfig == null || string.IsNullOrEmpty(oaConfig.LiffId))
        {
            return NotFound(new { error = "ไม่พบการตั้งค่า LIFF ในฐานข้อมูลสำหรับโหมด Production" });
        }

        return Ok(new { liffId = oaConfig.LiffId });
    }
}
