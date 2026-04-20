using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CHMBAPI.Entities;

[Table("PointPolicies", Schema = "mbw")]
public class PointPolicy
{
    [Key] public int PolicyId { get; set; }

    [Required, MaxLength(200)] public string PolicyName { get; set; } = default!;
    [Required, MaxLength(20)] public string PlatformType { get; set; } = "ALL";
    [Required, MaxLength(50)] public string EarnFormula { get; set; } = "AMOUNT_DIV_100";
    public decimal EarnRate { get; set; } = 1.0m;
    public decimal? MinOrderAmount { get; set; }
    [MaxLength(500)] public string? EligibleStatuses { get; set; }
    public int? ExpiryDays { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    [MaxLength(100)] public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

[Table("PointAccounts", Schema = "mbw")]
public class PointAccount
{
    [Key] public long PointAccountId { get; set; }

    public long MemberId { get; set; }
    public int AvailablePoints { get; set; }
    public int PendingPoints { get; set; }
    public int ReservedPoints { get; set; }
    public int TotalEarned { get; set; }
    public int TotalBurned { get; set; }
    public int TotalExpired { get; set; }
    public DateTime? LastActivityAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    [ForeignKey(nameof(MemberId))]
    public Members_Mbw Member { get; set; } = default!;
}

[Table("PointLedger", Schema = "mbw")]
public class PointLedgerEntry
{
    [Key] public long LedgerId { get; set; }

    public long MemberId { get; set; }
    [Required, MaxLength(20)] public string TxnType { get; set; } = default!;
    public int Points { get; set; }
    public int BalanceAfter { get; set; }
    public int? PolicyId { get; set; }
    [MaxLength(30)] public string? RefType { get; set; }
    [MaxLength(100)] public string? RefId { get; set; }
    public bool IsPending { get; set; }
    public DateTime? ReadyAt { get; set; }
    public DateTime OccurredAt { get; set; }
    [MaxLength(100)] public string? CreatedBy { get; set; }
    [MaxLength(200)] public string? IdempotencyKey { get; set; }
    public DateTime CreatedAt { get; set; }
}

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
}

[Table("PointAdjustments", Schema = "mbw")]
public class PointAdjustment
{
    [Key] public long AdjustmentId { get; set; }

    public long MemberId { get; set; }
    [Required, MaxLength(10)] public string AdjustType { get; set; } = default!;
    public int Points { get; set; }
    [Required, MaxLength(500)] public string Reason { get; set; } = default!;
    [MaxLength(100)] public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public long? LedgerId { get; set; }
    [Required, MaxLength(100)] public string CreatedBy { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
}
