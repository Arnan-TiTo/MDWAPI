using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

// ─── 15. RewardCatalog ────────────────────────────
[Table("RewardCatalog", Schema = "mbw")]
public class RewardCatalog
{
    [Key] public int RewardId { get; set; }

    [Required, MaxLength(200)] public string RewardName { get; set; } = default!;
    [MaxLength(1000)] public string? Description { get; set; }
    [MaxLength(20)] public string? PlatformType { get; set; }
    [Required, MaxLength(30)] public string RewardType { get; set; } = "DISCOUNT_CODE";
    public int PointsCost { get; set; }
    public int StockTotal { get; set; }
    public int StockRemaining { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    [MaxLength(500)] public string? ImageUrl { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public List<RewardCode> Codes { get; set; } = new();
    public List<RewardRedemption> Redemptions { get; set; } = new();
}

// ─── 16. RewardCodes ──────────────────────────────
[Table("RewardCodes", Schema = "mbw")]
public class RewardCode
{
    [Key] public long RewardCodeId { get; set; }

    public int RewardId { get; set; }
    [Required, MaxLength(100)] public string Code { get; set; } = default!;
    [Required, MaxLength(20)] public string Status { get; set; } = "Available"; // Available / Reserved / Issued / Used / Expired / Voided
    public DateTime? ReservedAt { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime? ExpiredAt { get; set; }
    public long? RedemptionId { get; set; }

    // Navigation
    [ForeignKey(nameof(RewardId))]
    public RewardCatalog Reward { get; set; } = default!;

    public RewardRedemption? Redemption { get; set; }
}

// ─── 17. RewardRedemptions ────────────────────────
[Table("RewardRedemptions", Schema = "mbw")]
public class RewardRedemption
{
    [Key] public long RedemptionId { get; set; }

    [Required, MaxLength(50)] public string RedemptionCode { get; set; } = default!;
    public long MemberId { get; set; }
    public int RewardId { get; set; }
    public long? RewardCodeId { get; set; }
    [Required, MaxLength(200)] public string RewardNameSnapshot { get; set; } = default!;
    [MaxLength(30)] public string? RewardTypeSnapshot { get; set; }
    public int PointsSpent { get; set; }
    [Required, MaxLength(20)] public string Status { get; set; } = "Reserved"; // Reserved / Completed / Cancelled / Failed
    [MaxLength(200)] public string? CouponCode { get; set; }
    public string? QrPayload { get; set; }
    public DateTime ReservedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public long? LedgerId { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(MemberId))]
    public Member Member { get; set; } = default!;

    [ForeignKey(nameof(RewardId))]
    public RewardCatalog Reward { get; set; } = default!;

    [ForeignKey(nameof(RewardCodeId))]
    public RewardCode? RewardCode { get; set; }

    [ForeignKey(nameof(LedgerId))]
    public PointLedgerEntry? LedgerEntry { get; set; }

    public RewardFulfillment? Fulfillment { get; set; }
    public List<RewardRedemptionHistory> StatusHistories { get; set; } = new();
}

// ─── 18. OutboxMessages ───────────────────────────
[Table("OutboxMessages", Schema = "mbw")]
public class OutboxMessage
{
    [Key] public long OutboxId { get; set; }

    public long MemberId { get; set; }
    [Required, MaxLength(30)] public string MessageType { get; set; } = default!;
    [Required, MaxLength(20)] public string Channel { get; set; } = "LINE";
    public string? Payload { get; set; }
    [Required, MaxLength(20)] public string Status { get; set; } = "Pending";
    public DateTime? SentAt { get; set; }
    public int RetryCount { get; set; }
    [MaxLength(1000)] public string? LastError { get; set; }
    [MaxLength(200)] public string? DeliveryRef { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(MemberId))]
    public Member Member { get; set; } = default!;
}
