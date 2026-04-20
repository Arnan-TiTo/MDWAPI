using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CHMBAPI.Entities;

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

    public List<RewardCode> Codes { get; set; } = new();
    public List<MemberRedemption> Redemptions { get; set; } = new();
}

[Table("RewardCodes", Schema = "mbw")]
public class RewardCode
{
    [Key] public long RewardCodeId { get; set; }

    public int RewardId { get; set; }
    [Required, MaxLength(100)] public string Code { get; set; } = default!;
    [Required, MaxLength(20)] public string Status { get; set; } = "Available";
    public DateTime? ReservedAt { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime? ExpiredAt { get; set; }
    public long? RedemptionId { get; set; }

    [ForeignKey(nameof(RewardId))]
    public RewardCatalog Reward { get; set; } = default!;

}

[Table("RewardRedemptions", Schema = "mbw")]
public class MemberRedemption
{
    [Key] public long RedemptionId { get; set; }

    [Required, MaxLength(50)] public string RedemptionCode { get; set; } = default!;
    public long MemberId { get; set; }
    public int RewardId { get; set; }
    public long? RewardCodeId { get; set; }
    [Required, MaxLength(200)] public string RewardNameSnapshot { get; set; } = default!;
    [MaxLength(30)] public string? RewardTypeSnapshot { get; set; }
    public int PointsSpent { get; set; }
    [Required, MaxLength(20)] public string Status { get; set; } = "Reserved";
    [MaxLength(200)] public string? CouponCode { get; set; }
    public string? QrPayload { get; set; }
    public DateTime ReservedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public long? LedgerId { get; set; }
    public DateTime CreatedAt { get; set; }

    [ForeignKey(nameof(MemberId))]
    public Members_Mbw Member { get; set; } = default!;

    [ForeignKey(nameof(RewardId))]
    public RewardCatalog Reward { get; set; } = default!;

    [ForeignKey(nameof(RewardCodeId))]
    public RewardCode? RewardCode { get; set; }

    [ForeignKey(nameof(LedgerId))]
    public PointLedgerEntry? LedgerEntry { get; set; }

    public RewardFulfillment? Fulfillment { get; set; }
    public List<RewardRedemptionHistory> StatusHistories { get; set; } = new();
}

[Table("RewardFulfillments", Schema = "mbw")]
public class RewardFulfillment
{
    [Key] public long FulfillmentId { get; set; }

    public long RedemptionId { get; set; }
    [Required, MaxLength(30)] public string FulfillmentType { get; set; } = default!;
    [Required, MaxLength(30)] public string FulfillmentStatus { get; set; } = default!;
    [MaxLength(200)] public string? RecipientName { get; set; }
    [MaxLength(30)] public string? Phone { get; set; }
    [MaxLength(1000)] public string? AddressSnapshot { get; set; }
    [MaxLength(100)] public string? CarrierName { get; set; }
    [MaxLength(100)] public string? TrackingNo { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(RedemptionId))]
    public MemberRedemption Redemption { get; set; } = default!;
}

[Table("RewardRedemptionHistories", Schema = "mbw")]
public class RewardRedemptionHistory
{
    [Key] public long RedemptionHistoryId { get; set; }

    public long RedemptionId { get; set; }
    [MaxLength(30)] public string? OldStatus { get; set; }
    [Required, MaxLength(30)] public string NewStatus { get; set; } = default!;
    public DateTime ChangedAt { get; set; }
    [MaxLength(100)] public string? ChangedBy { get; set; }
    [MaxLength(500)] public string? Remark { get; set; }

    [ForeignKey(nameof(RedemptionId))]
    public MemberRedemption Redemption { get; set; } = default!;
}
