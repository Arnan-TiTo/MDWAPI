using CHMBAPI.Data;
using CHMBAPI.DTOs;
using CHMBAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace CHMBAPI.Services;

public class PointService
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly TierService _tierService;
    private readonly PointPolicyEngine _policyEngine;

    public PointService(AppDbContext db, AuditService audit, TierService tierService, PointPolicyEngine policyEngine)
    {
        _db = db;
        _audit = audit;
        _tierService = tierService;
        _policyEngine = policyEngine;
    }

    public async Task InitializeAccountAsync(long memberId)
    {
        await GetOrCreateAccountAsync(memberId);
    }

    public async Task<PointBalanceDto> GetBalanceAsync(long memberId)
    {
        var account = await _db.PointAccounts.FirstOrDefaultAsync(x => x.MemberId == memberId);
        if (account == null)
            return new PointBalanceDto();

        var expiringSoon = await _db.PointExpirations
            .Where(e => e.MemberId == memberId && e.Status == "Active" && e.RemainingPoints > 0
                && e.ExpiresAt <= DateTime.UtcNow.AddDays(30))
            .SumAsync(e => e.RemainingPoints);

        var nextExpiry = await _db.PointExpirations
            .Where(e => e.MemberId == memberId && e.Status == "Active" && e.RemainingPoints > 0)
            .OrderBy(e => e.ExpiresAt)
            .Select(e => (DateTime?)e.ExpiresAt)
            .FirstOrDefaultAsync();

        return new PointBalanceDto
        {
            AvailablePoints = account.AvailablePoints,
            PendingPoints = account.PendingPoints,
            ReservedPoints = account.ReservedPoints,
            TotalEarned = account.TotalEarned,
            TotalBurned = account.TotalBurned,
            TotalExpired = account.TotalExpired,
            LastActivityAt = account.LastActivityAt,
            ExpiringPoints = expiringSoon,
            NextExpiryDate = nextExpiry
        };
    }

    public async Task<List<PointHistoryDto>> GetHistoryAsync(long memberId, int page = 1, int pageSize = 20)
    {
        return await (from l in _db.PointLedgerEntries
                      join e in _db.PointExpirations on l.LedgerId equals e.SourceLedgerId into ej
                      from e in ej.DefaultIfEmpty()
                      where l.MemberId == memberId
                      orderby l.OccurredAt descending
                      select new PointHistoryDto
                      {
                          LedgerId = l.LedgerId,
                          TxnType = l.TxnType,
                          Points = l.Points,
                          BalanceAfter = l.BalanceAfter,
                          RefType = l.RefType,
                          RefId = l.RefId,
                          OccurredAt = l.OccurredAt,
                          CreatedBy = l.CreatedBy,
                          ExpiresAt = e != null ? (DateTime?)e.ExpiresAt : null
                      })
                      .Skip((page - 1) * pageSize)
                      .Take(pageSize)
                      .ToListAsync();
    }

    public async Task<PointLedgerEntry?> EarnAsync(long memberId, int points, int? policyId, string refId, string? createdBy = null)
    {
        if (points <= 0) return null;

        var idempotencyKey = $"EARN-ORDER-{refId}";
        if (await _db.PointLedgerEntries.AnyAsync(l => l.IdempotencyKey == idempotencyKey))
            return null;

        var account = await GetOrCreateAccountAsync(memberId);
        account.PendingPoints += points;
        account.TotalEarned += points;
        account.LastActivityAt = DateTime.UtcNow;
        account.UpdatedAt = DateTime.UtcNow;

        var member = await _db.Members_Mbw.FindAsync(memberId);
        if (member != null)
        {
            member.PointsForTier += points;
            await _db.SaveChangesAsync();
            await _tierService.UpdateMemberTierAsync(memberId, $"Earn from {refId}");
        }

        var entry = new PointLedgerEntry
        {
            MemberId = memberId,
            TxnType = "EARN",
            Points = points,
            BalanceAfter = account.AvailablePoints,
            PolicyId = policyId,
            RefType = "ORDER",
            RefId = refId,
            IsPending = true,
            ReadyAt = DateTime.UtcNow.AddDays(7),
            OccurredAt = DateTime.UtcNow,
            CreatedBy = createdBy ?? "SYSTEM",
            IdempotencyKey = idempotencyKey,
            CreatedAt = DateTime.UtcNow
        };

        _db.PointLedgerEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    public async Task<PointLedgerEntry?> EarnReversalAsync(long memberId, string refId, string? createdBy = null)
    {
        var reversalKey = $"EARN_REVERSAL-ORDER-{refId}";
        if (await _db.PointLedgerEntries.AnyAsync(l => l.IdempotencyKey == reversalKey))
            return null;

        var earnKey = $"EARN-ORDER-{refId}";
        var originalEarn = await _db.PointLedgerEntries
            .FirstOrDefaultAsync(l => l.IdempotencyKey == earnKey && l.TxnType == "EARN");

        if (originalEarn == null) return null;

        var pointsToReverse = originalEarn.Points;
        var account = await GetOrCreateAccountAsync(memberId);
        account.AvailablePoints -= pointsToReverse;
        account.TotalEarned -= pointsToReverse;
        account.LastActivityAt = DateTime.UtcNow;
        account.UpdatedAt = DateTime.UtcNow;

        var entry = new PointLedgerEntry
        {
            MemberId = memberId,
            TxnType = "EARN_REVERSAL",
            Points = -pointsToReverse,
            BalanceAfter = account.AvailablePoints,
            PolicyId = originalEarn.PolicyId,
            RefType = "ORDER",
            RefId = refId,
            OccurredAt = DateTime.UtcNow,
            CreatedBy = createdBy ?? "SYSTEM",
            IdempotencyKey = reversalKey,
            CreatedAt = DateTime.UtcNow
        };

        _db.PointLedgerEntries.Add(entry);

        var expiration = await _db.PointExpirations
            .FirstOrDefaultAsync(e => e.SourceLedgerId == originalEarn.LedgerId && e.Status == "Active");
        if (expiration != null)
        {
            expiration.RemainingPoints = 0;
            expiration.Status = "Reversed";
        }

        await _db.SaveChangesAsync();
        return entry;
    }

    public async Task<PointLedgerEntry> ReserveAsync(long memberId, int points, string refId)
    {
        var account = await GetOrCreateAccountAsync(memberId);
        if (account.AvailablePoints < points)
            throw new InvalidOperationException($"Insufficient points. Available: {account.AvailablePoints}, Requested: {points}");

        account.AvailablePoints -= points;
        account.ReservedPoints += points;
        account.LastActivityAt = DateTime.UtcNow;
        account.UpdatedAt = DateTime.UtcNow;

        await ConsumeFifoAsync(memberId, points);

        var entry = new PointLedgerEntry
        {
            MemberId = memberId,
            TxnType = "RESERVE",
            Points = -points,
            BalanceAfter = account.AvailablePoints,
            RefType = "REDEMPTION",
            RefId = refId,
            OccurredAt = DateTime.UtcNow,
            CreatedBy = "SYSTEM",
            IdempotencyKey = $"RESERVE-{refId}",
            CreatedAt = DateTime.UtcNow
        };

        _db.PointLedgerEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    public async Task<PointLedgerEntry> BurnAsync(long memberId, int points, string refId)
    {
        var account = await GetOrCreateAccountAsync(memberId);
        account.ReservedPoints -= points;
        account.TotalBurned += points;
        account.LastActivityAt = DateTime.UtcNow;
        account.UpdatedAt = DateTime.UtcNow;

        var entry = new PointLedgerEntry
        {
            MemberId = memberId,
            TxnType = "BURN",
            Points = -points,
            BalanceAfter = account.AvailablePoints,
            RefType = "REDEMPTION",
            RefId = refId,
            OccurredAt = DateTime.UtcNow,
            CreatedBy = "SYSTEM",
            IdempotencyKey = $"BURN-{refId}",
            CreatedAt = DateTime.UtcNow
        };

        _db.PointLedgerEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    public async Task ReleaseAsync(long memberId, int points, string refId)
    {
        var account = await GetOrCreateAccountAsync(memberId);
        account.ReservedPoints -= points;
        account.AvailablePoints += points;
        account.LastActivityAt = DateTime.UtcNow;
        account.UpdatedAt = DateTime.UtcNow;

        await RestoreFifoAsync(memberId, points);

        _db.PointLedgerEntries.Add(new PointLedgerEntry
        {
            MemberId = memberId,
            TxnType = "RELEASE",
            Points = points,
            BalanceAfter = account.AvailablePoints,
            RefType = "REDEMPTION",
            RefId = refId,
            OccurredAt = DateTime.UtcNow,
            CreatedBy = "SYSTEM",
            IdempotencyKey = $"RELEASE-{refId}",
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
    }

    public async Task<PointAdjustment> AdjustAsync(PointAdjustRequest req, int adminUserId, string adminUsername)
    {
        var account = await GetOrCreateAccountAsync(req.MemberId);
        var delta = req.AdjustType == "ADD" ? req.Points : -req.Points;
        account.AvailablePoints += delta;
        if (req.AdjustType == "ADD") account.TotalEarned += req.Points;
        else account.TotalBurned += req.Points;
        account.LastActivityAt = DateTime.UtcNow;
        account.UpdatedAt = DateTime.UtcNow;

        var member = await _db.Members_Mbw.FindAsync(req.MemberId);
        if (member != null)
        {
            member.PointsForTier += delta;
            await _tierService.UpdateMemberTierAsync(req.MemberId, $"Adjustment: {req.Reason}");
        }

        var entry = new PointLedgerEntry
        {
            MemberId = req.MemberId,
            TxnType = "ADJUST",
            Points = delta,
            BalanceAfter = account.AvailablePoints,
            RefType = "ADJUSTMENT",
            OccurredAt = DateTime.UtcNow,
            CreatedBy = adminUsername,
            IdempotencyKey = $"ADJUST-{req.MemberId}-{req.Reason}-{DateTime.UtcNow.Ticks}",
            CreatedAt = DateTime.UtcNow
        };
        _db.PointLedgerEntries.Add(entry);
        await _db.SaveChangesAsync();

        if (req.AdjustType == "ADD")
        {
            _db.PointExpirations.Add(new PointExpiration
            {
                MemberId = req.MemberId,
                SourceLedgerId = entry.LedgerId,
                OriginalPoints = req.Points,
                RemainingPoints = req.Points,
                ExpiresAt = DateTime.UtcNow.AddDays(365),
                Status = "Active"
            });
        }

        var adjustment = new PointAdjustment
        {
            MemberId = req.MemberId,
            AdjustType = req.AdjustType,
            Points = req.Points,
            Reason = req.Reason,
            ApprovedBy = adminUsername,
            ApprovedAt = DateTime.UtcNow,
            LedgerId = entry.LedgerId,
            CreatedBy = adminUsername,
            CreatedAt = DateTime.UtcNow
        };
        _db.PointAdjustments.Add(adjustment);

        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            "ADJUST_POINTS",
            $"{req.AdjustType} {req.Points} pts: {req.Reason}",
            adminUsername,
            req.MemberId);

        return adjustment;
    }

    public Task ReservePointsAsync(long memberId, int points, string reason)
        => ReserveAsync(memberId, points, reason);

    public Task BurnPointsAsync(long memberId, int points, string refType, string refId, string createdBy)
        => BurnAsync(memberId, points, refId);

    public async Task<List<PointExpirationDto>> GetExpiringPointsAsync(long memberId)
    {
        return await _db.PointExpirations
            .Where(e => e.MemberId == memberId && e.Status == "Active" && e.RemainingPoints > 0)
            .OrderBy(e => e.ExpiresAt)
            .Select(e => new PointExpirationDto
            {
                ExpirationId = e.ExpirationId,
                OriginalPoints = e.OriginalPoints,
                RemainingPoints = e.RemainingPoints,
                ExpiresAt = e.ExpiresAt
            })
            .ToListAsync();
    }

    public async Task<List<PointPolicyDto>> ListPoliciesAsync()
    {
        return await _db.PointPolicies
            .OrderByDescending(p => p.IsActive)
            .ThenByDescending(p => p.EffectiveFrom)
            .Select(p => MapPolicyDto(p))
            .ToListAsync();
    }

    public async Task<PointPolicyDto> CreatePolicyAsync(PointPolicyCreateDto dto, string createdBy)
    {
        var policy = new PointPolicy
        {
            PolicyName = dto.PolicyName,
            PlatformType = dto.PlatformType.ToUpper(),
            EarnFormula = dto.EarnFormula,
            EarnRate = dto.EarnRate,
            MinOrderAmount = dto.MinOrderAmount,
            EligibleStatuses = dto.EligibleStatuses,
            ExpiryDays = dto.ExpiryDays,
            EffectiveFrom = dto.EffectiveFrom,
            EffectiveTo = dto.EffectiveTo,
            IsActive = true,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow
        };
        _db.PointPolicies.Add(policy);
        await _db.SaveChangesAsync();
        return MapPolicyDto(policy);
    }

    public async Task<PointPolicyDto> UpdatePolicyAsync(int policyId, PointPolicyCreateDto dto, string updatedBy)
    {
        var policy = await _db.PointPolicies.FindAsync(policyId)
            ?? throw new KeyNotFoundException($"Policy {policyId} not found");

        policy.PolicyName = dto.PolicyName;
        policy.PlatformType = dto.PlatformType.ToUpper();
        policy.EarnFormula = dto.EarnFormula;
        policy.EarnRate = dto.EarnRate;
        policy.MinOrderAmount = dto.MinOrderAmount;
        policy.EligibleStatuses = dto.EligibleStatuses;
        policy.ExpiryDays = dto.ExpiryDays;
        policy.EffectiveFrom = dto.EffectiveFrom;
        policy.EffectiveTo = dto.EffectiveTo;
        await _db.SaveChangesAsync();
        return MapPolicyDto(policy);
    }

    public async Task<PointPolicyDto> TogglePolicyAsync(int policyId)
    {
        var policy = await _db.PointPolicies.FindAsync(policyId)
            ?? throw new KeyNotFoundException($"Policy {policyId} not found");
        policy.IsActive = !policy.IsActive;
        await _db.SaveChangesAsync();
        return MapPolicyDto(policy);
    }

    public Task<int> CalculateEarnPointsAsync(string platformType, decimal orderAmount, string orderStatus)
        => _policyEngine.CalculatePointsAsync(platformType, orderAmount, orderStatus);

    public async Task AwardPointsAsync(long memberId, int points, string platformType, string refType, string refId, int? expiryDays = null)
    {
        var account = await GetOrCreateAccountAsync(memberId);
        var expiryAt = expiryDays.HasValue ? DateTime.UtcNow.AddDays(expiryDays.Value) : (DateTime?)null;

        account.AvailablePoints += points;
        account.TotalEarned += points;
        account.LastActivityAt = DateTime.UtcNow;
        account.UpdatedAt = DateTime.UtcNow;

        var ledger = new PointLedgerEntry
        {
            MemberId = memberId,
            TxnType = "EARN",
            Points = points,
            BalanceAfter = account.AvailablePoints,
            RefType = refType,
            RefId = refId,
            OccurredAt = DateTime.UtcNow,
            CreatedBy = "SYSTEM",
            IdempotencyKey = $"AWARD-{refType}-{refId}",
            CreatedAt = DateTime.UtcNow
        };

        _db.PointLedgerEntries.Add(ledger);
        await _db.SaveChangesAsync();

        if (expiryDays.HasValue)
        {
            _db.PointExpirations.Add(new PointExpiration
            {
                MemberId = memberId,
                SourceLedgerId = ledger.LedgerId,
                OriginalPoints = points,
                RemainingPoints = points,
                ExpiresAt = expiryAt!.Value,
                Status = "Active"
            });
            await _db.SaveChangesAsync();
        }
    }

    private async Task<PointAccount> GetOrCreateAccountAsync(long memberId)
    {
        var account = await _db.PointAccounts.FirstOrDefaultAsync(x => x.MemberId == memberId);
        if (account == null)
        {
            account = new PointAccount { MemberId = memberId, UpdatedAt = DateTime.UtcNow };
            _db.PointAccounts.Add(account);
            await _db.SaveChangesAsync();
        }
        return account;
    }

    private async Task ConsumeFifoAsync(long memberId, int points)
    {
        var expirations = await _db.PointExpirations
            .Where(e => e.MemberId == memberId && e.Status == "Active" && e.RemainingPoints > 0)
            .OrderBy(e => e.ExpiresAt)
            .ToListAsync();

        var remaining = points;
        foreach (var exp in expirations)
        {
            if (remaining <= 0) break;
            var consume = Math.Min(remaining, exp.RemainingPoints);
            exp.RemainingPoints -= consume;
            remaining -= consume;
        }
    }

    private async Task RestoreFifoAsync(long memberId, int points)
    {
        var expirations = await _db.PointExpirations
            .Where(e => e.MemberId == memberId && e.Status == "Active" && e.RemainingPoints < e.OriginalPoints)
            .OrderByDescending(e => e.ExpiresAt)
            .ToListAsync();

        var remaining = points;
        foreach (var exp in expirations)
        {
            if (remaining <= 0) break;
            var canRestore = exp.OriginalPoints - exp.RemainingPoints;
            var restore = Math.Min(remaining, canRestore);
            exp.RemainingPoints += restore;
            remaining -= restore;
        }
    }

    private static PointPolicyDto MapPolicyDto(PointPolicy p) => new()
    {
        PolicyId = p.PolicyId,
        PolicyName = p.PolicyName,
        PlatformType = p.PlatformType,
        EarnFormula = p.EarnFormula,
        EarnRate = p.EarnRate,
        MinOrderAmount = p.MinOrderAmount,
        EligibleStatuses = p.EligibleStatuses,
        ExpiryDays = p.ExpiryDays,
        EffectiveFrom = p.EffectiveFrom,
        EffectiveTo = p.EffectiveTo,
        IsActive = p.IsActive,
        CreatedBy = p.CreatedBy,
        CreatedAt = p.CreatedAt
    };
}

public class PointPolicyDto
{
    public int PolicyId { get; set; }
    public string PolicyName { get; set; } = "";
    public string PlatformType { get; set; } = "ALL";
    public string EarnFormula { get; set; } = "";
    public decimal EarnRate { get; set; }
    public decimal? MinOrderAmount { get; set; }
    public string? EligibleStatuses { get; set; }
    public int? ExpiryDays { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public bool IsActive { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}
