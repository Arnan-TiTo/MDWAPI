using CHMBAPI.Data;
using CHMBAPI.DTOs;
using CHMBAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace CHMBAPI.Services;

public class RewardService
{
    private readonly AppDbContext _db;
    private readonly PointService _pointService;
    private readonly LineNotificationService _notify;
    private readonly ILogger<RewardService> _logger;

    public RewardService(
        AppDbContext db,
        PointService pointService,
        LineNotificationService notify,
        ILogger<RewardService> logger)
    {
        _db = db;
        _pointService = pointService;
        _notify = notify;
        _logger = logger;
    }

    public async Task<List<RewardListItemDto>> ListActiveAsync()
    {
        var now = DateTime.UtcNow;
        return await _db.RewardCatalogs
            .Where(r => r.IsActive
                && (r.StockRemaining > 0 || r.Codes.Any(c => c.Status == "Available"))
                && (r.ValidFrom == null || r.ValidFrom <= now)
                && (r.ValidTo == null || r.ValidTo >= now))
            .OrderBy(r => r.PointsCost)
            .Select(r => new RewardListItemDto
            {
                RewardId = r.RewardId,
                RewardName = r.RewardName,
                Description = r.Description,
                PlatformType = r.PlatformType,
                RewardType = (r.RewardType != "CODE" && r.RewardType != "DISCOUNT_CODE" && r.RewardType != "COUPON" && r.Codes.Any())
                    ? "DISCOUNT_CODE"
                    : r.RewardType,
                PointsCost = r.PointsCost,
                StockRemaining = (r.RewardType == "CODE" || r.RewardType == "DISCOUNT_CODE" || r.RewardType == "COUPON" || r.Codes.Any())
                    ? r.Codes.Count(c => c.Status == "Available")
                    : r.StockRemaining,
                ImageUrl = r.ImageUrl,
                ValidFrom = r.ValidFrom,
                ValidTo = r.ValidTo,
                IsActive = r.IsActive
            })
            .ToListAsync();
    }

    public async Task<RedemptionResultDto> RedeemAsync(RedeemRequestDto req)
    {
        var reward = await _db.RewardCatalogs.FindAsync(req.RewardId)
            ?? throw new KeyNotFoundException("Reward not found");

        if (!reward.IsActive || reward.StockRemaining <= 0)
            throw new InvalidOperationException("Reward is not available");

        var maxId = await _db.MemberRedemptions.MaxAsync(r => (long?)r.RedemptionId) ?? 0L;
        var seq = maxId + 1;
        var redemption = new MemberRedemption
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
        _db.MemberRedemptions.Add(redemption);
        await _db.SaveChangesAsync();

        try
        {
            var reserveEntry = await _pointService.ReserveAsync(
                req.MemberId,
                reward.PointsCost,
                redemption.RedemptionId.ToString());
            redemption.LedgerId = reserveEntry.LedgerId;

            RewardCode? code = null;
            var requiresCode =
                reward.RewardType == "CODE" ||
                reward.RewardType == "DISCOUNT_CODE" ||
                reward.RewardType == "COUPON" ||
                await _db.RewardCodes.AnyAsync(c => c.RewardId == req.RewardId);

            if (requiresCode)
            {
                code = await _db.RewardCodes
                    .Where(c => c.RewardId == req.RewardId && c.Status == "Available")
                    .FirstOrDefaultAsync();

                if (code == null)
                    throw new InvalidOperationException("Reward code is out of stock");

                code.Status = "Issued";
                code.IssuedAt = DateTime.UtcNow;
                code.RedemptionId = redemption.RedemptionId;
                redemption.RewardCodeId = code.RewardCodeId;
                redemption.CouponCode = code.Code;
            }

            await _pointService.BurnAsync(
                req.MemberId,
                reward.PointsCost,
                redemption.RedemptionId.ToString());

            reward.StockRemaining--;
            redemption.Status = "Completed";
            redemption.CompletedAt = DateTime.UtcNow;

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

            try
            {
                await _notify.SendRedemptionMessageAsync(
                    req.MemberId,
                    reward.RewardName,
                    code?.Code ?? redemption.RedemptionCode);
            }
            catch (Exception nex)
            {
                _logger.LogWarning(nex, "Notify redemption failed");
            }

            return new RedemptionResultDto
            {
                RedemptionId = redemption.RedemptionId,
                RedemptionCode = redemption.RedemptionCode,
                Status = redemption.Status,
                PointsSpent = reward.PointsCost,
                Code = code?.Code,
                Message = (reward.RewardType == "CODE" && code == null)
                    ? "Reward redeemed but no code available"
                    : "Redemption successful"
            };
        }
        catch
        {
            try
            {
                await _pointService.ReleaseAsync(
                    req.MemberId,
                    reward.PointsCost,
                    redemption.RedemptionId.ToString());
            }
            catch
            {
            }

            redemption.Status = "Failed";
            redemption.CancelledAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            throw;
        }
    }

    public async Task<List<RedemptionListDto>> ListRedemptionsAsync(string? status = null, int page = 1, int pageSize = 20)
    {
        var query = _db.MemberRedemptions
            .Include(r => r.Member)
            .Include(r => r.Reward)
            .Include(r => r.RewardCode)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(r => r.Status == status);

        return await query
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

    public async Task CancelRedemptionAsync(long redemptionId, string cancelledBy, string? reason = null)
    {
        var redemption = await _db.MemberRedemptions
            .Include(r => r.RewardCode)
            .FirstOrDefaultAsync(r => r.RedemptionId == redemptionId)
            ?? throw new KeyNotFoundException($"Redemption {redemptionId} not found");

        if (redemption.Status != "Completed" && redemption.Status != "Reserved")
            throw new InvalidOperationException($"Cannot cancel redemption with status '{redemption.Status}'");

        if (redemption.RewardCode != null)
        {
            redemption.RewardCode.Status = "Available";
            redemption.RewardCode.IssuedAt = null;
            redemption.RewardCode.RedemptionId = null;
        }

        var reward = await _db.RewardCatalogs.FindAsync(redemption.RewardId);
        if (reward != null)
            reward.StockRemaining++;

        try
        {
            if (redemption.Status == "Completed")
            {
                var account = await _db.PointAccounts.FirstOrDefaultAsync(a => a.MemberId == redemption.MemberId);
                if (account != null)
                {
                    account.AvailablePoints += redemption.PointsSpent;
                    account.TotalBurned -= redemption.PointsSpent;
                    account.LastActivityAt = DateTime.UtcNow;
                    account.UpdatedAt = DateTime.UtcNow;

                    _db.PointLedgerEntries.Add(new PointLedgerEntry
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
                await _pointService.ReleaseAsync(
                    redemption.MemberId,
                    redemption.PointsSpent,
                    redemptionId.ToString());
            }
        }
        catch
        {
        }

        redemption.Status = "Cancelled";
        redemption.CancelledAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<List<MemberRedemptionDto>> GetMemberRedemptionsAsync(long memberId, int page = 1, int pageSize = 20)
    {
        return await _db.MemberRedemptions
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
                Code = r.RewardCode != null
                    ? r.RewardCode.Code
                    : (!string.IsNullOrEmpty(r.CouponCode)
                        ? r.CouponCode
                        : ((r.RewardTypeSnapshot == "CODE" || r.RewardTypeSnapshot == "DISCOUNT_CODE" || r.RewardTypeSnapshot == "COUPON")
                            ? "PENDING-CODE"
                            : null)),
                Status = r.Status,
                RedeemedAt = r.CompletedAt ?? r.ReservedAt,
                ImageUrl = r.Reward.ImageUrl,
                Fulfillment = r.Fulfillment == null
                    ? null
                    : new RedemptionFulfillmentDto
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
    public string RedemptionCode { get; set; } = "";
    public string RewardName { get; set; } = "";
    public string? RewardType { get; set; }
    public int PointsSpent { get; set; }
    public string? Code { get; set; }
    public string Status { get; set; } = "";
    public DateTime RedeemedAt { get; set; }
    public string? ImageUrl { get; set; }
    public RedemptionFulfillmentDto? Fulfillment { get; set; }
}
