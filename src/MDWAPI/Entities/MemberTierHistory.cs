using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

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

    // Navigation
    [ForeignKey(nameof(MemberId))]
    public Member Member { get; set; } = default!;

    [ForeignKey(nameof(TierId))]
    public TierMaster Tier { get; set; } = default!;

    [ForeignKey(nameof(PreviousTierId))]
    public TierMaster? PreviousTier { get; set; }
}
