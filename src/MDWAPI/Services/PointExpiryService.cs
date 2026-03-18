using MDWAPI.Data;
using MDWAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Services;

/// <summary>Scoped service: process expired points + ส่งแจ้งเตือนใกล้หมดอายุ</summary>
public class PointExpiryProcessingService
{
    private readonly AppDbContext _db;
    private readonly LineNotificationService _notify;
    private readonly ILogger<PointExpiryProcessingService> _logger;

    public PointExpiryProcessingService(AppDbContext db, LineNotificationService notify,
        ILogger<PointExpiryProcessingService> logger)
    {
        _db = db;
        _notify = notify;
        _logger = logger;
    }

    /// <summary>Process: (1) หมดอายุ → หักออก (2) ใกล้หมดอายุ → แจ้งเตือน</summary>
    public async Task ProcessAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        int totalExpired = 0, totalWarned = 0;

        // ═══ Phase 1: หักแต้มที่หมดอายุแล้ว ═══
        var expiredBatches = await _db.PointExpirations
            .Where(e => e.Status == "Active" && e.RemainingPoints > 0 && e.ExpiresAt <= now)
            .ToListAsync(ct);

        // Group by member เพื่อ update PointAccount ครั้งเดียว
        var byMember = expiredBatches.GroupBy(e => e.MemberId);

        foreach (var group in byMember)
        {
            try
            {
                var memberId = group.Key;
                var account = await _db.PointAccounts.FirstOrDefaultAsync(a => a.MemberId == memberId, ct);
                if (account == null) continue;

                int totalPtsExpired = 0;

                foreach (var exp in group)
                {
                    totalPtsExpired += exp.RemainingPoints;

                    // Mark expired
                    exp.Status = "Expired";
                    exp.ExpiredAt = now;
                    exp.RemainingPoints = 0;
                }

                // Update account
                account.AvailablePoints -= totalPtsExpired;
                account.TotalExpired += totalPtsExpired;
                account.LastActivityAt = now;
                account.UpdatedAt = now;

                // Write ledger entry
                _db.PointLedger.Add(new PointLedgerEntry
                {
                    MemberId = memberId,
                    TxnType = "EXPIRE",
                    Points = -totalPtsExpired,
                    BalanceAfter = account.AvailablePoints,
                    RefType = "EXPIRY",
                    OccurredAt = now,
                    CreatedBy = "SYSTEM",
                    IdempotencyKey = $"EXPIRE-{memberId}-{now:yyyyMMddHH}",
                    CreatedAt = now
                });

                await _db.SaveChangesAsync(ct);
                totalExpired++;

                // LINE notify
                try
                {
                    await _notify.NotifyExpiredAsync(memberId, totalPtsExpired, account.AvailablePoints);
                }
                catch (Exception nex) { _logger.LogWarning(nex, "Notify expired failed for Member {MemberId}", memberId); }

                _logger.LogInformation(
                    "Expired {Points} pts for Member {MemberId}, balance now {Balance}",
                    totalPtsExpired, memberId, account.AvailablePoints);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process expiry for Member {MemberId}", group.Key);
            }
        }

        // ═══ Phase 2: แจ้งเตือนใกล้หมดอายุ (7 วัน) ═══
        var warningDate = now.AddDays(7);
        var warningBatches = await _db.PointExpirations
            .Where(e => e.Status == "Active" && e.RemainingPoints > 0
                && e.ExpiresAt > now && e.ExpiresAt <= warningDate)
            .GroupBy(e => e.MemberId)
            .Select(g => new { MemberId = g.Key, TotalExpiring = g.Sum(e => e.RemainingPoints), EarliestExpiry = g.Min(e => e.ExpiresAt) })
            .ToListAsync(ct);

        foreach (var w in warningBatches)
        {
            try
            {
                // ส่งแจ้งเตือนวันละครั้ง (ใช้ idempotency key วัน)
                var warningKey = $"EXPIRY_WARN-{w.MemberId}-{now:yyyyMMdd}";
                if (await _db.PointLedger.AnyAsync(l => l.IdempotencyKey == warningKey, ct))
                    continue; // แจ้งไปแล้ววันนี้

                await _notify.NotifyExpiryWarningAsync(w.MemberId, w.TotalExpiring,
                    (int)(w.EarliestExpiry - now).TotalDays);

                // เขียน ledger mark ว่าแจ้งแล้ว (ไม่กระทบยอด)
                _db.PointLedger.Add(new PointLedgerEntry
                {
                    MemberId = w.MemberId,
                    TxnType = "EXPIRE_WARN",
                    Points = 0,
                    BalanceAfter = 0,
                    RefType = "EXPIRY",
                    OccurredAt = now,
                    CreatedBy = "SYSTEM",
                    IdempotencyKey = warningKey,
                    CreatedAt = now
                });
                await _db.SaveChangesAsync(ct);
                totalWarned++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed expiry warning for Member {MemberId}", w.MemberId);
            }
        }

        if (totalExpired > 0 || totalWarned > 0)
            _logger.LogInformation("PointExpiry: Expired {Expired} members, Warned {Warned} members",
                totalExpired, totalWarned);
    }
}

/// <summary>Background hosted service: run PointExpiryProcessingService every hour</summary>
public class PointExpiryJobService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PointExpiryJobService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(1);

    public PointExpiryJobService(IServiceScopeFactory scopeFactory, ILogger<PointExpiryJobService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PointExpiryJobService started (interval: {Interval})", _interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<PointExpiryProcessingService>();
                await processor.ProcessAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PointExpiryJobService error");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
