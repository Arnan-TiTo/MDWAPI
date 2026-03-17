using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

// ─── LineOaConfigs: LINE OA credentials per company ───
[Table("LineOaConfigs", Schema = "mbw")]
public class LineOaConfig
{
    [Key] public int LineOaConfigId { get; set; }

    public int CompanysId { get; set; }                     // FK → dbo.Companys.Id
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
