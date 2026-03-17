using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

// ─── 1. Members ───────────────────────────────────
[Table("Members", Schema = "mbw")]
public class Member
{
    [Key] public long MemberId { get; set; }

    [Required, MaxLength(50)] public string MemberCode { get; set; } = default!;
    [MaxLength(200)] public string? DisplayName { get; set; }
    [MaxLength(30)] public string? Phone { get; set; }
    [MaxLength(200)] public string? Email { get; set; }
    [Required, MaxLength(20)] public string Status { get; set; } = "Active";
    public bool ConsentAccepted { get; set; }
    public DateTime? ConsentedAt { get; set; }
    public DateTime RegisteredAt { get; set; }
    public int? CompanysId { get; set; }                    // FK → dbo.Companys.Id
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Navigation
    public List<MemberIdentity> Identities { get; set; } = new();
    public List<MemberPlatformAccount> PlatformAccounts { get; set; } = new();
    public List<MemberMappingRequest> MappingRequests { get; set; } = new();
    public PointAccount? PointAccount { get; set; }
}

// ─── 2. MemberIdentities ─────────────────────────
[Table("MemberIdentities", Schema = "mbw")]
public class MemberIdentity
{
    [Key] public long MemberIdentityId { get; set; }

    public long MemberId { get; set; }
    [Required, MaxLength(30)] public string ProviderType { get; set; } = default!;
    [Required, MaxLength(200)] public string ProviderUserKey { get; set; } = default!;
    [MaxLength(200)] public string? DisplayName { get; set; }
    [MaxLength(500)] public string? PictureUrl { get; set; }
    public int? CompanysId { get; set; }                    // FK → dbo.Companys.Id
    public DateTime LinkedAt { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    [ForeignKey(nameof(MemberId))]
    public Member Member { get; set; } = default!;
}
