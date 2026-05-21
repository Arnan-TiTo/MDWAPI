using CHMBAPI.Data;
using CHMBAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace CHMBAPI.Services;

public class PointExpiryProcessingService
{
    private readonly AppDbContext _db;

    public PointExpiryProcessingService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<int> ProcessExpiringPointsAsync()
    {
        var now = DateTime.UtcNow;
        var expiredPoints = 0;

        // Find expired points
        var expiredExpirations = await _db.PointExpirations
            .Where(pe => pe.Status == "Active" &&
                         pe.RemainingPoints > 0 &&
                         pe.ExpiresAt <= now)
            .ToListAsync();

        foreach (var expiration in expiredExpirations)
        {
            var pointsToExpire = expiration.RemainingPoints;

            var account = await _db.PointAccounts
                .FirstOrDefaultAsync(pa => pa.MemberId == expiration.MemberId);

            if (account != null)
            {
                account.AvailablePoints -= pointsToExpire;
                account.TotalExpired += pointsToExpire;
                account.LastActivityAt = now;
                account.UpdatedAt = now;

                _db.PointLedgerEntries.Add(new PointLedgerEntry
                {
                    MemberId = expiration.MemberId,
                    TxnType = "EXPIRE",
                    Points = -pointsToExpire,
                    BalanceAfter = account.AvailablePoints,
                    RefType = "EXPIRATION",
                    RefId = expiration.ExpirationId.ToString(),
                    OccurredAt = now,
                    CreatedBy = "SYSTEM",
                    IdempotencyKey = $"EXPIRE-{expiration.ExpirationId}",
                    CreatedAt = now
                });
            }

            expiration.RemainingPoints = 0;
            expiration.Status = "Expired";
            expiration.ExpiredAt = now;
            expiredPoints += pointsToExpire;
        }

        await _db.SaveChangesAsync();
        return expiredPoints;
    }
}
