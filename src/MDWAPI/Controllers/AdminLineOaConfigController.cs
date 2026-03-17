using MDWAPI.Data;
using MDWAPI.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Controllers;

[ApiController]
[Route("api/admin/line-oa-configs")]
[Tags("Admin LINE OA")]
public class AdminLineOaConfigController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminLineOaConfigController(AppDbContext db) => _db = db;

    /// <summary>ดูรายการ LINE OA Configs ทั้งหมด</summary>
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var list = await _db.LineOaConfigs
            .OrderBy(c => c.CompanysId)
            .Select(c => new
            {
                c.LineOaConfigId,
                c.CompanysId,
                c.LineOaName,
                c.LoginChannelId,
                LoginChannelSecret = MaskSecret(c.LoginChannelSecret),
                c.LoginCallbackUrl,
                MsgChannelSecret = MaskSecret(c.MsgChannelSecret),
                MsgChannelToken = MaskSecret(c.MsgChannelToken),
                c.LiffId,
                c.IsActive,
                c.CreatedAt,
                c.UpdatedAt
            })
            .ToListAsync();

        return Ok(list);
    }

    /// <summary>ดูรายละเอียด</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var c = await _db.LineOaConfigs.FindAsync(id);
        if (c == null) return NotFound();

        return Ok(new
        {
            c.LineOaConfigId,
            c.CompanysId,
            c.LineOaName,
            c.LoginChannelId,
            c.LoginChannelSecret,
            c.LoginCallbackUrl,
            c.MsgChannelSecret,
            c.MsgChannelToken,
            c.LiffId,
            c.IsActive,
            c.CreatedAt,
            c.UpdatedAt
        });
    }

    /// <summary>สร้าง LINE OA Config ใหม่</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] LineOaConfigDto dto)
    {
        var entity = new LineOaConfig
        {
            CompanysId = dto.CompanysId,
            LineOaName = dto.LineOaName,
            LoginChannelId = dto.LoginChannelId,
            LoginChannelSecret = dto.LoginChannelSecret,
            LoginCallbackUrl = dto.LoginCallbackUrl,
            MsgChannelSecret = dto.MsgChannelSecret,
            MsgChannelToken = dto.MsgChannelToken,
            LiffId = dto.LiffId,
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        _db.LineOaConfigs.Add(entity);
        await _db.SaveChangesAsync();
        return Ok(new { entity.LineOaConfigId, message = "Created" });
    }

    /// <summary>แก้ไข LINE OA Config</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] LineOaConfigDto dto)
    {
        var entity = await _db.LineOaConfigs.FindAsync(id);
        if (entity == null) return NotFound();

        entity.CompanysId = dto.CompanysId;
        entity.LineOaName = dto.LineOaName;
        entity.LoginChannelId = dto.LoginChannelId;
        entity.LoginCallbackUrl = dto.LoginCallbackUrl;
        entity.LiffId = dto.LiffId;
        entity.IsActive = dto.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;

        // อัปเดต secrets เฉพาะถ้าส่งค่ามาจริง (ไม่ใช่ mask "***...")
        if (!string.IsNullOrEmpty(dto.LoginChannelSecret) && !dto.LoginChannelSecret.StartsWith("***"))
            entity.LoginChannelSecret = dto.LoginChannelSecret;
        if (!string.IsNullOrEmpty(dto.MsgChannelSecret) && !dto.MsgChannelSecret.StartsWith("***"))
            entity.MsgChannelSecret = dto.MsgChannelSecret;
        if (!string.IsNullOrEmpty(dto.MsgChannelToken) && !dto.MsgChannelToken.StartsWith("***"))
            entity.MsgChannelToken = dto.MsgChannelToken;

        await _db.SaveChangesAsync();
        return Ok(new { message = "Updated" });
    }

    /// <summary>ลบ LINE OA Config</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await _db.LineOaConfigs.FindAsync(id);
        if (entity == null) return NotFound();

        _db.LineOaConfigs.Remove(entity);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Deleted" });
    }

    /// <summary>Toggle active/inactive</summary>
    [HttpPatch("{id:int}/toggle")]
    public async Task<IActionResult> Toggle(int id)
    {
        var entity = await _db.LineOaConfigs.FindAsync(id);
        if (entity == null) return NotFound();

        entity.IsActive = !entity.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { entity.IsActive, message = entity.IsActive ? "Activated" : "Deactivated" });
    }

    private static string? MaskSecret(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Length <= 8) return "***";
        return value[..4] + "***" + value[^4..];
    }
}

public class LineOaConfigDto
{
    public int CompanysId { get; set; }
    public string LineOaName { get; set; } = "";
    public string? LoginChannelId { get; set; }
    public string? LoginChannelSecret { get; set; }
    public string? LoginCallbackUrl { get; set; }
    public string? MsgChannelSecret { get; set; }
    public string MsgChannelToken { get; set; } = "";
    public string? LiffId { get; set; }
    public bool IsActive { get; set; } = true;
}
