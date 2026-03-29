using MDWAPI.Data;
using MDWAPI.DTOs;
using MDWAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Services;

public class RewardService
{
    private readonly AppDbContext _db;
    private readonly PointService _pointService;
    private readonly LineNotificationService _notify;
    private readonly ILogger<RewardService> _logger;

    public RewardService(AppDbContext db, PointService pointService,
        LineNotificationService notify, ILogger<RewardService> logger)
    {
        _db = db;
        _pointService = pointService;
        _notify = notify;
        _logger = logger;
    }

    /// <summary>ดู reward ที่เปิดให้แลก</summary>
    public async Task<List<RewardListItemDto>> ListActiveAsync()
    {
        var now = DateTime.UtcNow;
        return await _db.RewardCatalog
            .Where(r => r.IsActive
                && r.StockRemaining > 0
                && (r.ValidFrom == null || r.ValidFrom <= now)
                && (r.ValidTo == null || r.ValidTo >= now))
            .OrderBy(r => r.PointsCost)
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
                ValidTo = r.ValidTo
            })
            .ToListAsync();
    }

    /// <summary>แลก reward (Reserve → assign code → Burn)</summary>
    public async Task<RedemptionResultDto> RedeemAsync(RedeemRequestDto req)
    {
        // 1. ตรวจสอบ reward
        var reward = await _db.RewardCatalog.FindAsync(req.RewardId)
            ?? throw new KeyNotFoundException("Reward not found");

        if (!reward.IsActive || reward.StockRemaining <= 0)
            throw new InvalidOperationException("Reward is not available");

        // 2. สร้าง redemption record (status = Reserved)
        var seq = await _db.RewardRedemptions.CountAsync() + 1;
        var redemption = new RewardRedemption
        {
            MemberId = req.MemberId,
            RewardId = req.RewardId,
            RedemptionCode = $"RD-{DateTime.UtcNow:yyyyMMdd}-{seq:D4}",
            RewardNameSnapshot = reward.RewardName,
            RewardTypeSnapshot = reward.RewardType,
            PointsSpent = reward.PointsCost,
            Status = "Reserved",
            ReservedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _db.RewardRedemptions.Add(redemption);
        await _db.SaveChangesAsync();

        try
        {
            // 3. Reserve points (Transactionally logic handled in point service)
            var reserveEntry = await _pointService.ReserveAsync(
                req.MemberId, reward.PointsCost,
                redemption.RedemptionId.ToString());
            redemption.LedgerId = reserveEntry.LedgerId;

            // 4. หา code ที่ available (ถ้าเป็นประเภท Digital Code)
            RewardCode? code = null;
            if (reward.RewardType == "CODE")
            {
                code = await _db.RewardCodes
                    .Where(c => c.RewardId == req.RewardId && c.Status == "Available")
                    .FirstOrDefaultAsync();

                if (code != null)
                {
                    code.Status = "Issued";
                    code.IssuedAt = DateTime.UtcNow;
                    code.RedemptionId = redemption.RedemptionId;
                    redemption.RewardCodeId = code.RewardCodeId;
                    redemption.CouponCode = code.Code;
                }
            }

            // 5. Burn points
            await _pointService.BurnAsync(
                req.MemberId, reward.PointsCost,
                redemption.RedemptionId.ToString());

            // 6. ลด stock
            reward.StockRemaining--;

            // 7. Complete
            redemption.Status = "Completed";
            redemption.CompletedAt = DateTime.UtcNow;

            // 8. Create fulfillment for physical rewards
            if (reward.RewardType == "PHYSICAL")
            {
                _db.RewardFulfillments.Add(new RewardFulfillment
                {
                    RedemptionId = redemption.RedemptionId,
                    FulfillmentType = "PHYSICAL",
                    FulfillmentStatus = "PENDING",
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync();

            // 9. ส่ง LINE notification
            try
            {
                var bal = await _pointService.GetBalanceAsync(req.MemberId);
                await _notify.NotifyRedemptionAsync(
                    req.MemberId, reward.RewardName, code?.Code,
                    reward.PointsCost, bal.AvailablePoints);
            }
            catch (Exception nex) { _logger.LogWarning(nex, "Notify redemption failed"); }

            return new RedemptionResultDto
            {
                RedemptionId = redemption.RedemptionId,
                RedemptionCode = redemption.RedemptionCode,
                Status = redemption.Status,
                PointsSpent = reward.PointsCost,
                Code = code?.Code,
                Message = (reward.RewardType == "CODE" && code == null) ? "Reward redeemed but no code available" : "Redemption successful"
            };
        }
        catch
        {
            // Rollback: release reserved points
            try { await _pointService.ReleaseAsync(req.MemberId, reward.PointsCost, redemption.RedemptionId.ToString()); } catch { }
            redemption.Status = "Failed";
            redemption.CancelledAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            throw;
        }
    }

    /// <summary>Admin: ดูรายการ redemption</summary>
    public async Task<List<RedemptionListDto>> ListRedemptionsAsync(string? status = null, int page = 1, int pageSize = 20)
    {
        var q = _db.RewardRedemptions
            .Include(r => r.Member)
            .Include(r => r.Reward)
            .Include(r => r.RewardCode)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(r => r.Status == status);

        return await q
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new RedemptionListDto
            {
                RedemptionId = r.RedemptionId,
                MemberId = r.MemberId,
                MemberName = r.Member.DisplayName,
                MemberCode = r.Member.MemberCode,
                RewardId = r.RewardId,
                RewardName = r.Reward.RewardName,
                PointsSpent = r.PointsSpent,
                Code = r.RewardCode != null ? r.RewardCode.Code : null,
                Status = r.Status,
                ReservedAt = r.ReservedAt,
                CompletedAt = r.CompletedAt,
                CancelledAt = r.CancelledAt
            })
            .ToListAsync();
    }

    /// <summary>Admin: ยกเลิก redemption (คืนแต้ม + คืน stock)</summary>
    public async Task CancelRedemptionAsync(long redemptionId, string cancelledBy, string? reason = null)
    {
        var redemption = await _db.RewardRedemptions
            .Include(r => r.RewardCode)
            .FirstOrDefaultAsync(r => r.RedemptionId == redemptionId)
            ?? throw new KeyNotFoundException($"Redemption {redemptionId} not found");

        if (redemption.Status != "Completed" && redemption.Status != "Reserved")
            throw new InvalidOperationException($"Cannot cancel redemption with status '{redemption.Status}'");

        // คืน code
        if (redemption.RewardCode != null)
        {
            redemption.RewardCode.Status = "Available";
            redemption.RewardCode.IssuedAt = null;
            redemption.RewardCode.RedemptionId = null;
        }

        // คืน stock
        var reward = await _db.RewardCatalog.FindAsync(redemption.RewardId);
        if (reward != null)
            reward.StockRemaining++;

        // คืนแต้ม
        try
        {
            if (redemption.Status == "Completed")
            {
                // Reverse the burn by adding points back
                var account = await _db.PointAccounts.FirstOrDefaultAsync(a => a.MemberId == redemption.MemberId);
                if (account != null)
                {
                    account.AvailablePoints += redemption.PointsSpent;
                    account.TotalBurned -= redemption.PointsSpent;
                    account.LastActivityAt = DateTime.UtcNow;
                    account.UpdatedAt = DateTime.UtcNow;

                    _db.PointLedger.Add(new PointLedgerEntry
                    {
                        MemberId = redemption.MemberId,
                        TxnType = "EARN_REVERSAL",
                        Points = redemption.PointsSpent,
                        BalanceAfter = account.AvailablePoints,
                        RefType = "REDEMPTION",
                        RefId = redemptionId.ToString(),
                        OccurredAt = DateTime.UtcNow,
                        CreatedBy = cancelledBy,
                        IdempotencyKey = $"CANCEL-REDEMPTION-{redemptionId}",
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
            else if (redemption.Status == "Reserved")
            {
                await _pointService.ReleaseAsync(redemption.MemberId, redemption.PointsSpent, redemptionId.ToString());
            }
        }
        catch { /* best effort point release */ }

        redemption.Status = "Cancelled";
        redemption.CancelledAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    /// <summary>Member: ดูประวัติสิ่งที่แลก + code</summary>
    public async Task<List<MemberRedemptionDto>> GetMemberRedemptionsAsync(long memberId, int page = 1, int pageSize = 20)
    {
        return await _db.RewardRedemptions
            .Include(r => r.Reward)
            .Include(r => r.RewardCode)
            .Include(r => r.Fulfillment)
            .Where(r => r.MemberId == memberId)
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new MemberRedemptionDto
            {
                RedemptionId = r.RedemptionId,
                RedemptionCode = r.RedemptionCode,
                RewardName = r.RewardNameSnapshot,
                RewardType = r.RewardTypeSnapshot,
                PointsSpent = r.PointsSpent,
                Code = r.CouponCode,
                Status = r.Status,
                RedeemedAt = r.CompletedAt ?? r.ReservedAt,
                ImageUrl = r.Reward.ImageUrl,
                Fulfillment = r.Fulfillment == null ? null : new RedemptionFulfillmentDto
                {
                    FulfillmentStatus = r.Fulfillment.FulfillmentStatus,
                    CarrierName = r.Fulfillment.CarrierName,
                    TrackingNo = r.Fulfillment.TrackingNo,
                    ShippedAt = r.Fulfillment.ShippedAt
                }
            })
            .ToListAsync();
    }
}

// ─── DTOs ──
public class RedemptionListDto
{
    public long RedemptionId { get; set; }
    public long MemberId { get; set; }
    public string? MemberName { get; set; }
    public string? MemberCode { get; set; }
    public int RewardId { get; set; }
    public string RewardName { get; set; } = "";
    public int PointsSpent { get; set; }
    public string? Code { get; set; }
    public string Status { get; set; } = "";
    public DateTime ReservedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
}

public class MemberRedemptionDto
{
    public long RedemptionId { get; set; }
    public string RedemptionCode { get; set; } = default!;
    public string RewardName { get; set; } = "";
    public string? RewardType { get; set; }
    public string? PlatformType { get; set; }
    public string? Description { get; set; }
    public int PointsSpent { get; set; }
    public string? Code { get; set; }
    public string Status { get; set; } = "";
    public DateTime RedeemedAt { get; set; }
    public string? ImageUrl { get; set; }
    public RedemptionFulfillmentDto? Fulfillment { get; set; }
}
