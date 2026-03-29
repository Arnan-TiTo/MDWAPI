using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

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

    // Navigation
    [ForeignKey(nameof(RedemptionId))]
    public RewardRedemption Redemption { get; set; } = default!;
}
