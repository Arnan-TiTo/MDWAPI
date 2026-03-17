using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

// ─── 7. OrderMemberLinks ──────────────────────────
[Table("OrderMemberLinks", Schema = "mbw")]
public class OrderMemberLink
{
    [Key] public long OrderMemberLinkId { get; set; }

    public long UnifiedOrderId { get; set; }           // FK → mdw.UnifiedOrders
    public long MemberId { get; set; }
    public long? MemberPlatformAccountId { get; set; }
    [Required, MaxLength(30)] public string LinkMethod { get; set; } = default!; // VERIFIED_ACCOUNT / CLAIM / COUPON
    public DateTime LinkedAt { get; set; }
    [MaxLength(100)] public string? LinkedBy { get; set; }

    // Navigation
    [ForeignKey(nameof(UnifiedOrderId))]
    public UnifiedOrders UnifiedOrder { get; set; } = default!;

    [ForeignKey(nameof(MemberId))]
    public Member Member { get; set; } = default!;

    [ForeignKey(nameof(MemberPlatformAccountId))]
    public MemberPlatformAccount? PlatformAccount { get; set; }
}

// ─── 8. OrderClaims ───────────────────────────────
[Table("OrderClaims", Schema = "mbw")]
public class OrderClaim
{
    [Key] public long ClaimId { get; set; }

    public long MemberId { get; set; }
    public long UnifiedOrderId { get; set; }           // FK → mdw.UnifiedOrders
    [Required, MaxLength(20)] public string ClaimStatus { get; set; } = "Pending";
    public string? EvidenceJson { get; set; }
    [MaxLength(100)] public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(MemberId))]
    public Member Member { get; set; } = default!;

    [ForeignKey(nameof(UnifiedOrderId))]
    public UnifiedOrders UnifiedOrder { get; set; } = default!;
}

// ─── 9. OrderStatusHistory ────────────────────────
[Table("OrderStatusHistory", Schema = "mbw")]
public class OrderStatusHistory
{
    [Key] public long StatusHistoryId { get; set; }

    public long UnifiedOrderId { get; set; }           // FK → mdw.UnifiedOrders
    [MaxLength(40)] public string? OldStatus { get; set; }
    [Required, MaxLength(40)] public string NewStatus { get; set; } = default!;
    public DateTime ChangedAt { get; set; }
    [Required, MaxLength(20)] public string Source { get; set; } = "SYNC"; // SYNC / MANUAL / WEBHOOK

    // Navigation
    [ForeignKey(nameof(UnifiedOrderId))]
    public UnifiedOrders UnifiedOrder { get; set; } = default!;
}
