using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

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
