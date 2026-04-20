using CHMBAPI.Data;
using CHMBAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace CHMBAPI.Services;

public class TierService
{
    private readonly AppDbContext _db;
    private readonly ILogger<TierService> _logger;

    public TierService(AppDbContext db, ILogger<TierService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task UpdateMemberTierAsync(long memberId)
        => UpdateMemberTierAsync(memberId, "Automatic Tier Recalculation");

    public async Task UpdateMemberTierAsync(long memberId, string reason)
    {
        var member = await _db.Members_Mbw
            .Include(m => m.CurrentTier)
            .FirstOrDefaultAsync(m => m.MemberId == memberId);

        if (member == null) return;

        var tiers = await _db.TierMasters
            .Where(t => t.IsActive)
            .OrderByDescending(t => t.MinPoints)
            .ToListAsync();

        if (!tiers.Any()) return;

        var matchedTier = tiers.FirstOrDefault(t => member.PointsForTier >= t.MinPoints) ?? tiers.Last();

        if (member.CurrentTierId != matchedTier.TierId)
        {
            var previousTierId = member.CurrentTierId;

            member.CurrentTierId = matchedTier.TierId;
            member.MembershipTier = matchedTier.TierName;
            member.UpdatedAt = DateTime.UtcNow;

            _db.MemberTierHistories.Add(new MemberTierHistory
            {
                MemberId = member.MemberId,
                TierId = matchedTier.TierId,
                PreviousTierId = previousTierId,
                TierPoints = member.PointsForTier,
                SpendAmount = 0,
                CalculatedAt = DateTime.UtcNow,
                Reason = reason,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
            _logger.LogInformation(
                "Member {MemberId} tier updated to {TierName}. Reason: {Reason}",
                memberId,
                matchedTier.TierName,
                reason);
        }
        else if (await _db.MemberTierHistories.AnyAsync(h => h.MemberId == memberId) == false)
        {
            _db.MemberTierHistories.Add(new MemberTierHistory
            {
                MemberId = member.MemberId,
                TierId = matchedTier.TierId,
                PreviousTierId = null,
                TierPoints = member.PointsForTier,
                SpendAmount = 0,
                CalculatedAt = DateTime.UtcNow,
                Reason = "Initial Registration Tier",
                CreatedAt = DateTime.UtcNow
            });

            member.CurrentTierId = matchedTier.TierId;
            member.MembershipTier = matchedTier.TierName;

            await _db.SaveChangesAsync();
        }
    }
}
