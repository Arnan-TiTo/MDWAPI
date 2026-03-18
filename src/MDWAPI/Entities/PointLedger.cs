using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

// ─── 10. PointPolicies ────────────────────────────
[Table("PointPolicies", Schema = "mbw")]
public class PointPolicy
{
    [Key] public int PolicyId { get; set; }

    [Required, MaxLength(200)] public string PolicyName { get; set; } = default!;
    [Required, MaxLength(20)] public string PlatformType { get; set; } = "ALL";
    [Required, MaxLength(50)] public string EarnFormula { get; set; } = "AMOUNT_DIV_100";
    public decimal EarnRate { get; set; } = 1.0m;
    public decimal? MinOrderAmount { get; set; }
    [MaxLength(500)] public string? EligibleStatuses { get; set; } // JSON
    public int? ExpiryDays { get; set; }    // NULL = ไม่หมดอายุ, 365 = หมดอายุ 365 วันหลัง earn
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    [MaxLength(100)] public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ─── 11. PointAccounts ────────────────────────────
[Table("PointAccounts", Schema = "mbw")]
public class PointAccount
{
    [Key] public long PointAccountId { get; set; }

    public long MemberId { get; set; }
    public int AvailablePoints { get; set; }
    public int ReservedPoints { get; set; }
    public int TotalEarned { get; set; }
    public int TotalBurned { get; set; }
    public int TotalExpired { get; set; }
    public DateTime? LastActivityAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(MemberId))]
    public Member Member { get; set; } = default!;
}

// ─── 12. PointLedger ──────────────────────────────
[Table("PointLedger", Schema = "mbw")]
public class PointLedgerEntry
{
    [Key] public long LedgerId { get; set; }

    public long MemberId { get; set; }
    [Required, MaxLength(20)] public string TxnType { get; set; } = default!; // EARN / RESERVE / RELEASE / BURN / EARN_REVERSAL / EXPIRE / ADJUST
    public int Points { get; set; }
    public int BalanceAfter { get; set; }
    public int? PolicyId { get; set; }
    [MaxLength(30)] public string? RefType { get; set; } // ORDER / REDEMPTION / ADJUSTMENT / CLAIM
    [MaxLength(100)] public string? RefId { get; set; }
    public DateTime OccurredAt { get; set; }
    [MaxLength(100)] public string? CreatedBy { get; set; }
    [MaxLength(200)] public string? IdempotencyKey { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(MemberId))]
    public Member Member { get; set; } = default!;

    [ForeignKey(nameof(PolicyId))]
    public PointPolicy? Policy { get; set; }
}

// ─── 13. PointExpirations ─────────────────────────
[Table("PointExpirations", Schema = "mbw")]
public class PointExpiration
{
    [Key] public long ExpirationId { get; set; }

    public long MemberId { get; set; }
    public long SourceLedgerId { get; set; }
    public int OriginalPoints { get; set; }
    public int RemainingPoints { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ExpiredAt { get; set; }
    [Required, MaxLength(20)] public string Status { get; set; } = "Active";

    // Navigation
    [ForeignKey(nameof(MemberId))]
    public Member Member { get; set; } = default!;

    [ForeignKey(nameof(SourceLedgerId))]
    public PointLedgerEntry SourceLedger { get; set; } = default!;
}

// ─── 14. PointAdjustments ─────────────────────────
[Table("PointAdjustments", Schema = "mbw")]
public class PointAdjustment
{
    [Key] public long AdjustmentId { get; set; }

    public long MemberId { get; set; }
    [Required, MaxLength(10)] public string AdjustType { get; set; } = default!; // ADD / DEDUCT
    public int Points { get; set; }
    [Required, MaxLength(500)] public string Reason { get; set; } = default!;
    [MaxLength(100)] public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public long? LedgerId { get; set; }
    [Required, MaxLength(100)] public string CreatedBy { get; set; } = default!;
    public DateTime CreatedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(MemberId))]
    public Member Member { get; set; } = default!;

    [ForeignKey(nameof(LedgerId))]
    public PointLedgerEntry? LedgerEntry { get; set; }
}

// ─── EarnFormulas ──────────────────────────────
[Table("EarnFormulas", Schema = "mbw")]
public class EarnFormula
{
    [Key] public int FormulaId { get; set; }

    [Required, MaxLength(50)] public string FormulaCode { get; set; } = default!;
    [Required, MaxLength(200)] public string FormulaName { get; set; } = default!;
    [Required, MaxLength(500)] public string Expression { get; set; } = default!;
    [MaxLength(1000)] public string? Description { get; set; }
    [MaxLength(200)] public string? Variables { get; set; } // comma-separated: Amount,Rate
    public bool IsSystem { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
