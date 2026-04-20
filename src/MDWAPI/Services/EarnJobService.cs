using MDWAPI.Data;
using MDWAPI.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MDWAPI.Services;

/// <summary>Scoped service: scan completed orders → link to members → earn points</summary>
public class EarnProcessingService
{
    private readonly AppDbContext _db;
    private readonly PointPolicyEngine _policyEngine;
    private readonly PointService _pointService;
    private readonly LineNotificationService _notify;
    private readonly ILogger<EarnProcessingService> _logger;

    public EarnProcessingService(AppDbContext db, PointPolicyEngine policyEngine,
        PointService pointService, LineNotificationService notify, ILogger<EarnProcessingService> logger)
    {
        _db = db;
        _policyEngine = policyEngine;
        _pointService = pointService;
        _notify = notify;
        _logger = logger;
    }

    /// <summary>Process pending orders: link to members → calculate points → earn</summary>
    /// <returns>(linked order count, earned point entries count)</returns>
    public async Task<(int linked, int earned)> ProcessPendingOrdersAsync(CancellationToken ct = default)
    {
        var verifiedAccounts = await _db.MemberPlatformAccounts
            .Where(a => a.VerifiedStatus == "Verified")
            .ToListAsync(ct);

        if (!verifiedAccounts.Any()) return (0, 0);

        int totalLinked = 0, totalEarned = 0;

        foreach (var account in verifiedAccounts)
        {
            // Match by: Channel + BuyerUsername/BuyerUserId
            // ShopId: ถ้า account มี ShopId → ต้องตรง, ถ้าไม่มี → match ทุก shop
            var ordersQuery = _db.UnifiedOrders
                .Where(o => o.Channel == account.PlatformType
                    && (o.BuyerUsername == account.PlatformAccountKey
                        || o.BuyerUserId == account.PlatformAccountKey)
                    && (o.OrderStatus == "COMPLETED" || o.OrderStatus == "DELIVERED")
                    && !_db.OrderMemberLinks.Any(l => l.UnifiedOrderId == o.UnifiedOrderId));

            if (account.ShopId.HasValue)
                ordersQuery = ordersQuery.Where(o => o.ShopId == account.ShopId.Value);

            var unlinkedOrders = await ordersQuery
                .OrderBy(o => o.CreatedTimeUtc)
                .Take(100)
                .ToListAsync(ct);

            foreach (var order in unlinkedOrders)
            {
                try
                {
                    // 1. Link order กับ member
                    _db.OrderMemberLinks.Add(new OrderMemberLink
                    {
                        UnifiedOrderId = order.UnifiedOrderId,
                        MemberId = account.MemberId,
                        MemberPlatformAccountId = account.MemberPlatformAccountId,
                        LinkMethod = "VERIFIED_ACCOUNT",
                        LinkedAt = DateTime.UtcNow,
                        LinkedBy = "SYSTEM"
                    });
                    await _db.SaveChangesAsync(ct);
                    totalLinked++;

                    // 2. คำนวณแต้ม
                    var amount = order.TotalAmount ?? 0m;
                    var orderDate = order.CompletedTimeUtc ?? order.CreatedTimeUtc ?? DateTime.UtcNow;
                    var (points, policyId) = await _policyEngine.CalculateEarnAsync(
                        account.PlatformType, amount, orderDate);

                    // 3. Earn points (idempotency key ป้องกันซ้ำ)
                    if (points > 0)
                    {
                        var earned = await _pointService.EarnAsync(
                            account.MemberId, points, policyId,
                            order.UnifiedOrderId.ToString(), "EARN_JOB");
                        if (earned != null)
                        {
                            totalEarned++;
                            // ส่ง LINE notification
                            try
                            {
                                var bal = await _pointService.GetBalanceAsync(account.MemberId);
                                await _notify.NotifyEarnAsync(account.MemberId, points,
                                    order.ExternalOrderId, bal.AvailablePoints);
                            }
                            catch (Exception nex) { _logger.LogWarning(nex, "Notify earn failed"); }
                        }

                        _logger.LogInformation(
                            "Earned {Points} pts for Member {MemberId} from Order {OrderId} ({Channel}/{Buyer})",
                            points, account.MemberId, order.ExternalOrderId,
                            order.Channel, order.BuyerUsername);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to process Order {OrderId} for Member {MemberId}",
                        order.ExternalOrderId, account.MemberId);
                }
            }
        }

        // ─── Phase 1.5: Linked แล้วแต่ยังไม่ Earn (เช่น ตอน link ยังไม่มี Policy) ───
        try
        {
            // หา orders ที่ linked + completed แล้ว
            var allLinkedCompleted = await (
                from link in _db.OrderMemberLinks
                join order in _db.UnifiedOrders on link.UnifiedOrderId equals order.UnifiedOrderId
                where order.OrderStatus == "COMPLETED" || order.OrderStatus == "DELIVERED"
                select new
                {
                    link.MemberId,
                    link.UnifiedOrderId,
                    order.Channel,
                    order.TotalAmount,
                    order.CompletedTimeUtc,
                    order.CreatedTimeUtc,
                    order.ExternalOrderId,
                    order.BuyerUsername
                }
            ).OrderBy(o => o.UnifiedOrderId).Take(200).ToListAsync(ct);

            _logger.LogInformation("Phase 1.5: Found {Count} linked completed orders to check", allLinkedCompleted.Count);

            // เช็คว่า order ไหนยังไม่ earn
            var earnKeys = allLinkedCompleted
                .Select(o => $"EARN-ORDER-{o.UnifiedOrderId}")
                .ToList();

            var existingKeys = await _db.PointLedger
                .Where(pl => earnKeys.Contains(pl.IdempotencyKey!))
                .Select(pl => pl.IdempotencyKey)
                .ToListAsync(ct);

            var notEarned = allLinkedCompleted
                .Where(o => !existingKeys.Contains($"EARN-ORDER-{o.UnifiedOrderId}"))
                .ToList();

            _logger.LogInformation("Phase 1.5: {Count} orders need retroactive earn", notEarned.Count);

            foreach (var item in notEarned)
            {
                try
                {
                    var amount = item.TotalAmount ?? 0m;
                    var orderDate = item.CompletedTimeUtc ?? item.CreatedTimeUtc ?? DateTime.UtcNow;

                    _logger.LogInformation(
                        "Phase 1.5: Calculating for Order {OrderId}, Channel={Channel}, Amount={Amount}, Date={Date}",
                        item.ExternalOrderId, item.Channel, amount, orderDate);

                    var (points, policyId) = await _policyEngine.CalculateEarnAsync(
                        item.Channel, amount, orderDate);

                    _logger.LogInformation(
                        "Phase 1.5: Order {OrderId} → points={Points}, policyId={PolicyId}",
                        item.ExternalOrderId, points, policyId);

                    if (points > 0)
                    {
                        var earned = await _pointService.EarnAsync(
                            item.MemberId, points, policyId,
                            item.UnifiedOrderId.ToString(), "EARN_JOB");
                        if (earned != null)
                        {
                            totalEarned++;
                            // ส่ง LINE notification
                            try
                            {
                                var bal = await _pointService.GetBalanceAsync(item.MemberId);
                                await _notify.NotifyEarnAsync(item.MemberId, points,
                                    item.ExternalOrderId, bal.AvailablePoints);
                            }
                            catch (Exception nex) { _logger.LogWarning(nex, "Notify retroactive earn failed"); }

                            _logger.LogInformation(
                                "Retroactive Earned {Points} pts for Member {MemberId} from Order {OrderId} ({Channel}/{Buyer})",
                                points, item.MemberId, item.ExternalOrderId,
                                item.Channel, item.BuyerUsername);
                        }
                    }
                    else
                    {
                        _logger.LogDebug(
                            "Phase 1.5: Order {OrderId} earned 0 pts (Channel={Channel}, Amount={Amount})",
                            item.ExternalOrderId, item.Channel, amount);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed retroactive earn Order {OrderId} for Member {MemberId}",
                        item.ExternalOrderId, item.MemberId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Phase 1.5 (retroactive earn) error");
        }

        // ─── Phase 2: Return/Refund → หักแต้มคืน ───
        int totalReversed = 0;
        try
        {
            // หา orders ที่เคย link + earn แล้ว แต่ตอนนี้ status เปลี่ยนเป็น Return/Refund
            var linkedOrders = await (
                from link in _db.OrderMemberLinks
                join order in _db.UnifiedOrders on link.UnifiedOrderId equals order.UnifiedOrderId
                where order.OrderStatus == "RETURNED"
                   || order.OrderStatus == "RETURN"
                   || order.OrderStatus == "REFUNDED"
                   || order.OrderStatus == "REFUND"
                   || order.OrderStatus == "CANCELLED"
                   || order.OrderStatus == "CANCELED"
                select new { link.MemberId, link.UnifiedOrderId, order.ExternalOrderId, order.OrderStatus }
            ).ToListAsync(ct);

            foreach (var item in linkedOrders)
            {
                try
                {
                    var reversed = await _pointService.EarnReversalAsync(
                        item.MemberId,
                        item.UnifiedOrderId.ToString(),
                        "EARN_JOB");

                    if (reversed != null)
                    {
                        totalReversed++;
                        // ส่ง LINE notification
                        try
                        {
                            var bal = await _pointService.GetBalanceAsync(item.MemberId);
                            await _notify.NotifyReversalAsync(item.MemberId, Math.Abs(reversed.Points),
                                item.ExternalOrderId, bal.AvailablePoints);
                        }
                        catch (Exception nex) { _logger.LogWarning(nex, "Notify reversal failed"); }

                        _logger.LogInformation(
                            "Reversed {Points} pts for Member {MemberId} from Order {OrderId} (status: {Status})",
                            reversed.Points, item.MemberId, item.ExternalOrderId, item.OrderStatus);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to reverse Order {OrderId} for Member {MemberId}",
                        item.ExternalOrderId, item.MemberId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Phase 2 (Return/Refund reversal) error");
        }

        if (totalLinked > 0 || totalReversed > 0)
            _logger.LogInformation(
                "EarnProcessing: Linked {Linked}, Earned {Earned}, Reversed {Reversed}",
                totalLinked, totalEarned, totalReversed);

        return (totalLinked, totalEarned);
    }
}

/// <summary>Background hosted service that runs EarnProcessingService periodically</summary>
public class EarnJobService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EarnJobService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    public EarnJobService(IServiceScopeFactory scopeFactory, ILogger<EarnJobService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EarnJobService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<EarnProcessingService>();
                await processor.ProcessPendingOrdersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EarnJobService error");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
