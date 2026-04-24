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

<<<<<<< HEAD
    /// <summary>ดึงค่า LiffId ตาม Mode (Local/Production) ที่ตั้งไว้ใน appsettings.json</summary>
=======
>>>>>>> 2961fdec8de78fe692e89e3203f173cc374dbbab
    [HttpGet("liff")]
    public async Task<IActionResult> GetLiffConfig()
    {
        var mode = _config["LiffSettings:Mode"] ?? "Local";
<<<<<<< HEAD
        
=======

>>>>>>> 2961fdec8de78fe692e89e3203f173cc374dbbab
        if (mode.Equals("Local", StringComparison.OrdinalIgnoreCase))
        {
            var liffId = _config["LiffSettings:LocalLiffId"];
            return Ok(new { liffId });
        }

<<<<<<< HEAD
        // Production Mode: ดึงจากฐานข้อมูลตาราง mbw.LineOaConfigs
=======
>>>>>>> 2961fdec8de78fe692e89e3203f173cc374dbbab
        var oaConfig = await _db.LineOaConfigs
            .Where(x => x.IsActive)
            .OrderBy(x => x.LineOaConfigId)
            .FirstOrDefaultAsync();

        if (oaConfig == null || string.IsNullOrEmpty(oaConfig.LiffId))
        {
<<<<<<< HEAD
            return NotFound(new { error = "ไม่พบการตั้งค่า LIFF ในฐานข้อมูลสำหรับโหมด Production" });
=======
            return NotFound(new { error = "LINE LIFF configuration was not found for production mode." });
>>>>>>> 2961fdec8de78fe692e89e3203f173cc374dbbab
        }

        return Ok(new { liffId = oaConfig.LiffId });
    }
}
