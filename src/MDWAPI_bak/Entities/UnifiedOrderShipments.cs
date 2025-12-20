using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

[Table("UnifiedOrderShipments", Schema = "mdw")]
public class UnifiedOrderShipments
{
    [Key] public long UnifiedOrderShipmentId { get; set; }

    [ForeignKey(nameof(Order))] public long UnifiedOrderId { get; set; }
    public UnifiedOrders Order { get; set; } = default!;

    [MaxLength(120)] public string? Provider { get; set; }
    [MaxLength(80)] public string? ServiceCode { get; set; }
    [MaxLength(120)] public string? TrackingNo { get; set; }
    [MaxLength(40)] public string? Status { get; set; }

    public DateTime? PickupTimeUtc { get; set; }
    public DateTime? ShippedTimeUtc { get; set; }
    public DateTime? DeliveredTimeUtc { get; set; }

    [MaxLength(120)] public string? FirstMileCarrier { get; set; }
    [MaxLength(120)] public string? LastMileCarrier { get; set; }

    public string? RawJson { get; set; }
}
