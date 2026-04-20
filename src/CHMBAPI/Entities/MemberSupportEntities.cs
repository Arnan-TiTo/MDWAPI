using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CHMBAPI.Entities;

[Table("LineOaConfigs", Schema = "mbw")]
public class LineOaConfig
{
    [Key] public int LineOaConfigId { get; set; }

    public int CompanysId { get; set; }
    [Required, MaxLength(200)] public string LineOaName { get; set; } = default!;
    [MaxLength(50)] public string? LoginChannelId { get; set; }
    [MaxLength(100)] public string? LoginChannelSecret { get; set; }
    [MaxLength(500)] public string? LoginCallbackUrl { get; set; }
    [MaxLength(100)] public string? MsgChannelSecret { get; set; }
    [Required, MaxLength(500)] public string MsgChannelToken { get; set; } = default!;
    [MaxLength(50)] public string? LiffId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

[Table("OrderMemberLinks", Schema = "mbw")]
public class OrderMemberLink
{
    [Key] public long OrderMemberLinkId { get; set; }

    public long UnifiedOrderId { get; set; }
    public long MemberId { get; set; }
    public long? MemberPlatformAccountId { get; set; }
    [Required, MaxLength(30)] public string LinkMethod { get; set; } = default!;
    public DateTime LinkedAt { get; set; }
    [MaxLength(100)] public string? LinkedBy { get; set; }
}
