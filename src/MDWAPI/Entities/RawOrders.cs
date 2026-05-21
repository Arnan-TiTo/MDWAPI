using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

[Table("UnifiedRawOrders", Schema = "mdw")]
public class UnifiedRawOrders
{
    [Key] public long RawId { get; set; }
    [Required, MaxLength(20)] public string Channel { get; set; } = default!;
    public long? ShopId { get; set; }
    [MaxLength(100)] public string? SellerId { get; set; }
    [Required, MaxLength(100)] public string ExternalOrderId { get; set; } = default!;
    [Required] public string PayloadJson { get; set; } = default!;
    [Required] public byte[] PayloadHash { get; set; } = default!;
    public DateTime PulledAtUtc { get; set; }
    [MaxLength(40)] public string? BatchNo { get; set; }
}
