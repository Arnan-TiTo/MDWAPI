using MDWAPI.Data;
using MDWAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Services;

/// <summary>Scoped service: moves pending points to available after 7 days maturation</summary>
public class PointMaturationProcessingService
{
    private readonly AppDbContext _db;
    private readonly LineNotificationService _notify;
    private readonly ILogger<PointMaturationProcessingService> _logger;

    public PointMaturationProcessingService(AppDbContext db, LineNotificationService notify,
        ILogger<PointMaturationProcessingService> logger)
    {
        _db = db;
        _notify = notify;
        _logger = logger;
    }

    public async Task ProcessAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        
        // 1. Find pending points that are ready
        var readyEntries = await _db.PointLedger
            .Where(l => l.IsPending && l.ReadyAt <= now)
            .OrderBy(l => l.ReadyAt)
            .ToListAsync(ct);

        if (!readyEntries.Any()) return;

        foreach (var entry in readyEntries)
        {
            try
            {
                var account = await _db.PointAccounts.FirstOrDefaultAsync(a => a.MemberId == entry.MemberId, ct);
                if (account == null) continue;

                var points = entry.Points;

                // Move from Pending to Available
                account.PendingPoints -= points;
                account.AvailablePoints += points;
                account.UpdatedAt = now;

                // Update entry
                entry.IsPending = false;
                entry.BalanceAfter = account.AvailablePoints;

                // 2. Create Expiration (FIFO) - 365 Days
                var expiryDays = 365;
                if (entry.PolicyId.HasValue)
                {
                    var policy = await _db.PointPolicies.FindAsync(entry.PolicyId.Value);
                    if (policy?.ExpiryDays.HasValue == true) expiryDays = policy.ExpiryDays.Value;
                }

                _db.PointExpirations.Add(new PointExpiration
                {
                    MemberId = entry.MemberId,
                    SourceLedgerId = entry.LedgerId,
                    OriginalPoints = points,
                    RemainingPoints = points,
                    ExpiresAt = now.AddDays(expiryDays),
                    Status = "Active"
                });

                await _db.SaveChangesAsync(ct);

                _logger.LogInformation("PointMaturation: Moved {Points} pts for Member {MemberId} to Available.", points, entry.MemberId);

                // Optional: Notify user
                try
                {
                    await _notify.NotifyPointsAvailableAsync(entry.MemberId, points, account.AvailablePoints);
                }
                catch (Exception nex) { _logger.LogWarning(nex, "Notify points available failed for Member {MemberId}", entry.MemberId); }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed point maturation for Ledger {LedgerId}", entry.LedgerId);
            }
        }
    }
}

/// <summary>Background hosted service: run PointMaturationProcessingService every 30 minutes</summary>
public class PointMaturationJobService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PointMaturationJobService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(30);

    public PointMaturationJobService(IServiceScopeFactory scopeFactory, ILogger<PointMaturationJobService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PointMaturationJobService started (interval: {Interval})", _interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<PointMaturationProcessingService>();
                await processor.ProcessAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PointMaturationJobService error");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
