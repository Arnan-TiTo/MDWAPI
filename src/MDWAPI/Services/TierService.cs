using MDWAPI.Data;
using MDWAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Services;

/// <summary>
/// จัดการตรรกะการจัดระดับสมาชิก (Auto-Tiering) และประวัติการเปลี่ยนระดับ
/// </summary>
public class TierService
{
    private readonly AppDbContext _db;
    private readonly ILogger<TierService> _logger;

    public TierService(AppDbContext db, ILogger<TierService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// คำนวณและอัปเดตระดับสมาชิกปัจจุบันตามคะแนน PointsForTier
    /// </summary>
    public async Task UpdateMemberTierAsync(long memberId, string reason)
    {
        var member = await _db.Members_Mbw
            .Include(m => m.CurrentTier)
            .FirstOrDefaultAsync(m => m.MemberId == memberId);

        if (member == null) return;

        // 1. ดึงเกณฑ์ระดับทั้งหมด (เรียงตามคะแนนจากมากไปน้อย)
        var tiers = await _db.TierMasters
            .Where(t => t.IsActive)
            .OrderByDescending(t => t.MinPoints)
            .ToListAsync();

        if (!tiers.Any()) return;

        // 2. หาว่าด้วยคะแนนปัจจุบัน (PointsForTier) ควรอยู่ระดับไหน
        var matchedTier = tiers.FirstOrDefault(t => member.PointsForTier >= t.MinPoints);
        
        // ถ้าไม่เจอเลย (คะแนนน้อยกว่าระดับต่ำสุด) ให้ใช้ระดับที่ SortOrder น้อยสุด
        if (matchedTier == null)
        {
            matchedTier = tiers.Last(); // ตัวที่ SortOrder น้อยสุด (MinPoints น้อยสุด) เพราะเรียง Desc ไว้
        }

        // 3. ตรวจสอบว่าระดับเปลี่ยนหรือไม่
        if (member.CurrentTierId != matchedTier.TierId)
        {
            var previousTierId = member.CurrentTierId;
            
            // อัปเดตข้อมูลสมาชิก
            member.CurrentTierId = matchedTier.TierId;
            member.MembershipTier = matchedTier.TierName;
            member.UpdatedAt = DateTime.UtcNow;

            // 4. บันทึกประวัติการเปลี่ยนระดับ (MemberTierHistories)
            _db.MemberTierHistories.Add(new MemberTierHistory
            {
                MemberId = member.MemberId,
                TierId = matchedTier.TierId,
                PreviousTierId = previousTierId,
                TierPoints = member.PointsForTier,
                CalculatedAt = DateTime.UtcNow,
                Reason = reason,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
            _logger.LogInformation("Member {MemberId} tier updated to {TierName}. Reason: {Reason}", 
                memberId, matchedTier.TierName, reason);
        }
        else if (await _db.MemberTierHistories.AnyAsync(h => h.MemberId == memberId) == false)
        {
            // เคสสมัครใหม่: บันทึกประวัติครั้งแรกแม้ระดับจะไม่เปลี่ยน (จาก NULL เป็นระดับแรก)
            _db.MemberTierHistories.Add(new MemberTierHistory
            {
                MemberId = member.MemberId,
                TierId = matchedTier.TierId,
                PreviousTierId = null,
                TierPoints = member.PointsForTier,
                CalculatedAt = DateTime.UtcNow,
                Reason = "Initial Registration Tier",
                CreatedAt = DateTime.UtcNow
            });
            
            // อัปเดตข้อมูลหน้าบัตรด้วย
            member.CurrentTierId = matchedTier.TierId;
            member.MembershipTier = matchedTier.TierName;
            
            await _db.SaveChangesAsync();
        }
    }
}
