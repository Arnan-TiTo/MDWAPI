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
    [MaxLength(20)] public string? Phone { get; set; }
    [MaxLength(200)] public string? Email { get; set; }
    [MaxLength(50)] public string? MemberType { get; set; }
    [MaxLength(100)] public string? FirstName { get; set; }
    [MaxLength(100)] public string? LastName { get; set; }
    public DateTime? BirthDate { get; set; }
    public int? Age { get; set; }
    [MaxLength(20)] public string? Gender { get; set; }
    [MaxLength(500)] public string? Address { get; set; }
    [MaxLength(100)] public string? Subdistrict { get; set; }
    [MaxLength(100)] public string? District { get; set; }
    [MaxLength(100)] public string? Province { get; set; }
    [MaxLength(20)] public string? ZipCode { get; set; }

    [MaxLength(100)] public string? MembershipTier { get; set; }
    [MaxLength(1000)] public string? Tags { get; set; }
    [MaxLength(100)] public string? Branch { get; set; }
    public decimal PointsForTier { get; set; }
    public int UsageCount { get; set; }
    public DateTime? LastActiveAt { get; set; }
    public int? LastActiveDays { get; set; }

    [Required, MaxLength(20)] public string Status { get; set; } = "Active";
    public bool ConsentAccepted { get; set; }
    public DateTime? ConsentedAt { get; set; }
    public DateTime RegisteredAt { get; set; }
    public int? CompanysId { get; set; }                    // FK → dbo.Companys.Id
    public int? RegisterChannelId { get; set; }             // FK → mbw.MemberChannels
    public int? CurrentTierId { get; set; }                 // FK → mbw.TierMasters
    [MaxLength(10)] public string? PreferredLanguage { get; set; }
    [MaxLength(10)] public string? PhoneCountryCode { get; set; }
    [MaxLength(1000)] public string? HowYouKnowMe { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Navigation
    public List<MemberIdentity> Identities { get; set; } = new();
    public List<MemberPlatformAccount> PlatformAccounts { get; set; } = new();
    public List<MemberMappingRequest> MappingRequests { get; set; } = new();
    public List<MemberRegistrationAnswer> RegistrationAnswers { get; set; } = new();
    public PointAccount? PointAccount { get; set; }

    [ForeignKey(nameof(RegisterChannelId))]
    public MemberChannel? RegisterChannel { get; set; }

    [ForeignKey(nameof(CurrentTierId))]
    public TierMaster? CurrentTier { get; set; }
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
