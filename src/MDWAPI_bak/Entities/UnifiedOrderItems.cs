using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

[Table("UnifiedOrderItems", Schema = "mdw")]
public class UnifiedOrderItems
{
    [Key] public long UnifiedOrderItemId { get; set; }

    [ForeignKey(nameof(Order))] public long UnifiedOrderId { get; set; }
    public UnifiedOrders Order { get; set; } = default!;

    [MaxLength(120)] public string? ExternalItemId { get; set; }
    [Required, MaxLength(500)] public string ProductName { get; set; } = default!;
    [MaxLength(300)] public string? VariationName { get; set; }
    [MaxLength(150)] public string? SellerSku { get; set; }
    [MaxLength(150)] public string? PlatformSku { get; set; }

    public int QtyOrdered { get; set; }
    public int? QtyCanceled { get; set; }
    public int? QtyShipped { get; set; }

    public decimal? UnitPrice { get; set; }
    public decimal? OriginalPrice { get; set; }
    public decimal? DiscountSeller { get; set; }
    public decimal? DiscountPlatform { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? ShippingAlloc { get; set; }
    public decimal? LineTotal { get; set; }

    public string? AttributesJson { get; set; }
}
