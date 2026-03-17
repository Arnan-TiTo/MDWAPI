using MDWAPI.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

// ─── 3. MemberPlatformAccounts ────────────────────
[Table("MemberPlatformAccounts", Schema = "mbw")]
public class MemberPlatformAccount
{
    [Key] public long MemberPlatformAccountId { get; set; }

    public long MemberId { get; set; }
    [Required, MaxLength(20)] public string PlatformType { get; set; } = default!;
    public int? ShopId { get; set; }                   // FK → mdw.Shops (nullable for self-service)
    [Required, MaxLength(200)] public string PlatformAccountKey { get; set; } = default!;
    [MaxLength(200)] public string? PlatformAccountName { get; set; }
    [Required, MaxLength(20)] public string VerifiedStatus { get; set; } = "Pending";
    public DateTime? VerifiedAt { get; set; }
    [MaxLength(100)] public string? VerifiedBy { get; set; }
    [Required, MaxLength(20)] public string LinkMethod { get; set; } = "MANUAL";
    public decimal? ConfidenceScore { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(MemberId))]
    public Member Member { get; set; } = default!;

    [ForeignKey(nameof(ShopId))]
    public Shops? Shop { get; set; }                    // cross-schema → mdw.Shops (nullable)
}
