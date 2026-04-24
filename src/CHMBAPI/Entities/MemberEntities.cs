using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CHMBAPI.Entities;

[Table("Members", Schema = "mbw")]
public class Members_Mbw
{
    [Key] public long MemberId { get; set; }

    [Required, MaxLength(50)] public string MemberCode { get; set; } = default!;
    [MaxLength(200)] public string? DisplayName { get; set; }
    [MaxLength(50)] public string? NamePrefix { get; set; }
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
    public int? CompanysId { get; set; }
    public int? RegisterChannelId { get; set; }
    public int? CurrentTierId { get; set; }
    [MaxLength(10)] public string? PreferredLanguage { get; set; }
    [MaxLength(10)] public string? PhoneCountryCode { get; set; }
    [MaxLength(1000)] public string? HowYouKnowMe { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public List<MemberIdentity> Identities { get; set; } = new();
    public List<MemberPlatformAccount> PlatformAccounts { get; set; } = new();
    public List<MemberMappingRequest> MappingRequests { get; set; } = new();
    public List<MemberRegistrationAnswer> RegistrationAnswers { get; set; } = new();
    public PointAccount? PointAccount { get; set; }
    public MemberChannel? RegisterChannel { get; set; }
    public TierMaster? CurrentTier { get; set; }
}

[Table("MemberIdentities", Schema = "mbw")]
public class MemberIdentity
{
    [Key] public long MemberIdentityId { get; set; }

    public long MemberId { get; set; }
    [Required, MaxLength(30)] public string ProviderType { get; set; } = default!;
    [Required, MaxLength(200)] public string ProviderUserKey { get; set; } = default!;
    [MaxLength(200)] public string? DisplayName { get; set; }
    [MaxLength(500)] public string? PictureUrl { get; set; }
    public int? CompanysId { get; set; }
    public DateTime LinkedAt { get; set; }
    public bool IsActive { get; set; } = true;

    [ForeignKey(nameof(MemberId))]
    public Members_Mbw Member { get; set; } = default!;
}

[Table("MemberPlatformAccounts", Schema = "mbw")]
public class MemberPlatformAccount
{
    [Key] public long MemberPlatformAccountId { get; set; }

    public long MemberId { get; set; }
    [Required, MaxLength(20)] public string PlatformType { get; set; } = default!;
    public int? ShopId { get; set; }
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

    [ForeignKey(nameof(MemberId))]
    public Members_Mbw Member { get; set; } = default!;
}

[Table("MemberMappingRequests", Schema = "mbw")]
public class MemberMappingRequest
{
    [Key] public long RequestId { get; set; }

    public long MemberId { get; set; }
    [Required, MaxLength(20)] public string PlatformType { get; set; } = default!;
    public int? ShopId { get; set; }
    [Required, MaxLength(200)] public string PlatformAccountKey { get; set; } = default!;
    [MaxLength(200)] public string? PlatformAccountName { get; set; }
    [Required, MaxLength(30)] public string SourceType { get; set; } = "ADMIN";
    [Required, MaxLength(20)] public string RequestStatus { get; set; } = "Pending";
    public decimal? ConfidenceScore { get; set; }
    [MaxLength(100)] public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    [MaxLength(1000)] public string? ReviewNote { get; set; }
    public DateTime CreatedAt { get; set; }

    [ForeignKey(nameof(MemberId))]
    public Members_Mbw Member { get; set; } = default!;

    public List<MemberMappingEvidence> Evidences { get; set; } = new();
}

[Table("MemberMappingEvidence", Schema = "mbw")]
public class MemberMappingEvidence
{
    [Key] public long EvidenceId { get; set; }

    public long RequestId { get; set; }
    [Required, MaxLength(30)] public string EvidenceType { get; set; } = default!;
    public string? EvidenceValue { get; set; }
    public DateTime CreatedAt { get; set; }

    [ForeignKey(nameof(RequestId))]
    public MemberMappingRequest Request { get; set; } = default!;
}
