using MDWAPI.Data;
using MDWAPI.DTOs;
using MDWAPI.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace MDWAPI.Controllers;

/// <summary>API Admin สำหรับจัดการ Rewards Catalog + Codes</summary>
[ApiController]
[Route("api/admin/rewards")]
[Tags("Admin-Rewards")]
[Authorize]
public class AdminRewardController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminRewardController(AppDbContext db) => _db = db;

    // ─── Catalog ──────────────────────────────────
    /// <summary>ดู reward ทั้งหมด (admin)</summary>
    [HttpGet]
    public async Task<IActionResult> ListAll()
    {
        var items = await _db.RewardCatalog
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new RewardListItemDto
            {
                RewardId = r.RewardId,
                RewardName = r.RewardName,
                Description = r.Description,
                PlatformType = r.PlatformType,
                RewardType = r.RewardType,
                PointsCost = r.PointsCost,
                StockRemaining = r.StockRemaining,
                ImageUrl = r.ImageUrl,
                ValidFrom = r.ValidFrom,
                ValidTo = r.ValidTo,
                IsActive = r.IsActive
            })
            .ToListAsync();

        return Ok(items);
    }

    /// <summary>สร้าง reward ใหม่</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RewardCatalog reward)
    {
        reward.CreatedAt = DateTime.UtcNow;
        reward.StockRemaining = reward.StockTotal;
        _db.RewardCatalog.Add(reward);
        await _db.SaveChangesAsync();
        return Ok(reward);
    }

    /// <summary>อัปเดต reward</summary>
    [HttpPut("{rewardId:int}")]
    public async Task<IActionResult> Update(int rewardId, [FromBody] RewardCatalog input)
    {
        var reward = await _db.RewardCatalog.FindAsync(rewardId);
        if (reward == null) return NotFound();

        reward.RewardName = input.RewardName;
        reward.Description = input.Description;
        reward.PlatformType = input.PlatformType;
        reward.RewardType = input.RewardType;
        reward.PointsCost = input.PointsCost;
        reward.StockTotal = input.StockTotal;
        reward.StockRemaining = input.StockRemaining;
        reward.IsActive = input.IsActive;
        reward.ValidFrom = input.ValidFrom;
        reward.ValidTo = input.ValidTo;
        reward.ImageUrl = input.ImageUrl;

        await _db.SaveChangesAsync();
        return Ok(reward);
    }

    /// <summary>Toggle active/inactive</summary>
    [HttpPatch("{rewardId:int}/toggle-active")]
    public async Task<IActionResult> ToggleActive(int rewardId)
    {
        var reward = await _db.RewardCatalog.FindAsync(rewardId);
        if (reward == null) return NotFound();

        reward.IsActive = !reward.IsActive;
        await _db.SaveChangesAsync();
        return Ok(new { reward.RewardId, reward.IsActive });
    }

    // ─── Codes ────────────────────────────────────
    /// <summary>เพิ่ม codes ให้ reward</summary>
    [HttpPost("{rewardId:int}/codes")]
    public async Task<IActionResult> AddCodes(int rewardId, [FromBody] List<string> codes)
    {
        var reward = await _db.RewardCatalog.FindAsync(rewardId);
        if (reward == null) return NotFound();

        foreach (var code in codes)
        {
            _db.RewardCodes.Add(new RewardCode
            {
                RewardId = rewardId,
                Code = code,
                Status = "Available"
            });
        }

        reward.StockTotal += codes.Count;
        reward.StockRemaining += codes.Count;

        await _db.SaveChangesAsync();
        return Ok(new { added = codes.Count, totalStock = reward.StockTotal });
    }

    /// <summary>ดู codes ของ reward</summary>
    [HttpGet("{rewardId:int}/codes")]
    public async Task<IActionResult> ListCodes(int rewardId, string? status = null)
    {
        var q = _db.RewardCodes.Where(c => c.RewardId == rewardId);
        if (!string.IsNullOrEmpty(status))
            q = q.Where(c => c.Status == status);

        var codes = await q
            .OrderBy(c => c.RewardCodeId)
            .Select(c => new
            {
                c.RewardCodeId,
                c.Code,
                c.Status,
                c.IssuedAt,
                c.UsedAt,
                c.RedemptionId
            })
            .ToListAsync();

        return Ok(codes);
    }
}
