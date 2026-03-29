using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

[Table("RewardFulfillments", Schema = "mbw")]
public class RewardFulfillment
{
    [Key] public long FulfillmentId { get; set; }

    public long RedemptionId { get; set; }
    [Required, MaxLength(30)] public string FulfillmentType { get; set; } = default!; // DIGITAL, PHYSICAL
    [Required, MaxLength(30)] public string FulfillmentStatus { get; set; } = default!; // PENDING, PROCESSING, SHIPPED, DELIVERED, CANCELLED
    [MaxLength(200)] public string? RecipientName { get; set; }
    [MaxLength(30)] public string? Phone { get; set; }
    [MaxLength(1000)] public string? AddressSnapshot { get; set; }
    [MaxLength(100)] public string? CarrierName { get; set; }
    [MaxLength(100)] public string? TrackingNo { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(RedemptionId))]
    public RewardRedemption Redemption { get; set; } = default!;
}
