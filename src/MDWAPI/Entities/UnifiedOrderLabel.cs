using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

[Table("UnifiedOrderLabels", Schema = "mdw")]
public class UnifiedOrderLabel
{
    [Key]
    public long Id { get; set; }

    [Required, MaxLength(20)]
    public string Channel { get; set; } = default!;

    public long? ShopId { get; set; }

    [Required, MaxLength(100)]
    public string OrderExternalNo { get; set; } = default!;

    [Required, MaxLength(500)]
    public string Location { get; set; } = default!;     // relative path on server

    [Required, MaxLength(260)]
    public string FileName { get; set; } = default!;

    [MaxLength(50)]
    public string? DocumentType { get; set; }             // e.g. NORMAL_AIR_WAYBILL

    public long FileSizeBytes { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
