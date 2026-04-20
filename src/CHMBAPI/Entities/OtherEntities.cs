using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CHMBAPI.Entities;

[Table("TierMasters", Schema = "mbw")]
public class TierMaster
{
    [Key] public int TierId { get; set; }

    [Required, MaxLength(50)] public string TierCode { get; set; } = default!;
    [Required, MaxLength(200)] public string TierName { get; set; } = default!;
    public decimal MinPoints { get; set; }
    public decimal? MaxPoints { get; set; }
    public decimal MinSpendAmount { get; set; }
    public decimal? MaxSpendAmount { get; set; }
    [MaxLength(20)] public string? TierColor { get; set; }
    [MaxLength(500)] public string? IconUrl { get; set; }
    [MaxLength(1000)] public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

[Table("MemberTierHistories", Schema = "mbw")]
public class MemberTierHistory
{
    [Key] public long MemberTierHistoryId { get; set; }

    public long MemberId { get; set; }
    public int TierId { get; set; }
    public int? PreviousTierId { get; set; }
    public decimal TierPoints { get; set; }
    public decimal SpendAmount { get; set; }
    public DateTime? WindowStartDate { get; set; }
    public DateTime? WindowEndDate { get; set; }
    public DateTime CalculatedAt { get; set; }
    [MaxLength(500)] public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
}

[Table("MemberChannels", Schema = "mbw")]
public class MemberChannel
{
    [Key] public int ChannelId { get; set; }

    [Required, MaxLength(50)] public string ChannelCode { get; set; } = default!;
    [Required, MaxLength(200)] public string ChannelName { get; set; } = default!;
    [MaxLength(500)] public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

[Table("RegistrationProductOptions", Schema = "mbw")]
public class RegistrationProductOption
{
    [Key] public int OptionId { get; set; }

    [Required, MaxLength(50)] public string OptionCode { get; set; } = default!;
    [Required, MaxLength(200)] public string OptionName { get; set; } = default!;
    public bool IsAllowOtherText { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

[Table("MemberRegistrationAnswers", Schema = "mbw")]
public class MemberRegistrationAnswer
{
    [Key] public long AnswerId { get; set; }

    public long MemberId { get; set; }
    public int OptionId { get; set; }
    [MaxLength(500)] public string? OtherText { get; set; }
    public DateTime CreatedAt { get; set; }
}

[Table("MemberConsentLogs", Schema = "mbw")]
public class MemberConsentLog
{
    [Key] public long ConsentLogId { get; set; }

    public long MemberId { get; set; }
    public long DocumentId { get; set; }
    public bool AcceptedFlag { get; set; }
    public DateTime AcceptedAt { get; set; }
    [Required, MaxLength(30)] public string AcceptedFromChannel { get; set; } = "LIFF";
    [MaxLength(50)] public string? AcceptedIp { get; set; }
    [MaxLength(1000)] public string? AcceptedUserAgent { get; set; }
    public DateTime CreatedAt { get; set; }
}

[Table("MemberNotifications", Schema = "mbw")]
public class MemberNotification
{
    [Key] public long NotificationId { get; set; }

    public long MemberId { get; set; }
    [Required, MaxLength(50)] public string NotificationType { get; set; } = default!;
    [Required, MaxLength(200)] public string Title { get; set; } = default!;
    [Required, MaxLength(1000)] public string Message { get; set; } = default!;
    [MaxLength(50)] public string? RefType { get; set; }
    [MaxLength(100)] public string? RefId { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime? DisplayUntil { get; set; }
    public DateTime CreatedAt { get; set; }
}

[Table("ContentDocuments", Schema = "mbw")]
public class ContentDocument
{
    [Key] public long DocumentId { get; set; }

    [Required, MaxLength(50)] public string DocumentType { get; set; } = default!; // TERMS, PRIVACY, POLICY
    [Required, MaxLength(30)] public string VersionNo { get; set; } = default!;
    [Required, MaxLength(10)] public string LanguageCode { get; set; } = "th";
    [Required, MaxLength(300)] public string Title { get; set; } = default!;
    [Required] public string ContentHtml { get; set; } = default!;
    public string? ContentText { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public DateTime PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

// Audit
public class AuditLog
{
    public long AuditId { get; set; }
    public string Action { get; set; } = "";
    public string? Description { get; set; }
    public string PerformedBy { get; set; } = "";
    public long? MemberId { get; set; }
    public DateTime PerformedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

// Admin User
public class User
{
    public int UserId { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}
