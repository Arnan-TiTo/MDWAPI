using MDWAPI.DTOs;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

[Table("UnifiedOrders", Schema = "mdw")]
public class UnifiedOrders
{
    [Key] public long UnifiedOrderId { get; set; }

    [Required, MaxLength(20)] public string Channel { get; set; } = default!;
    public long? ShopId { get; set; }
    [MaxLength(100)] public string? SellerId { get; set; }
    [Required, MaxLength(100)] public string ExternalOrderId { get; set; } = default!;
    [MaxLength(100)] public string? ExternalOrderNo { get; set; }

    [MaxLength(40)] public string? OrderStatus { get; set; }
    [MaxLength(40)] public string? FulfillmentStatus { get; set; }
    [MaxLength(40)] public string? PaymentStatus { get; set; }
    [MaxLength(8)] public string? Currency { get; set; }

    public decimal? SubtotalAmount { get; set; }
    public decimal? DiscountSellerAmount { get; set; }
    public decimal? DiscountPlatformAmount { get; set; }
    public decimal? VoucherAmount { get; set; }
    public decimal? ShippingFeeAmount { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? OtherFeeAmount { get; set; }
    public decimal? TotalAmount { get; set; }
    public decimal? PaidAmount { get; set; }
    public decimal? RefundAmount { get; set; }

    [MaxLength(60)] public string? PaymentMethod { get; set; }
    [MaxLength(120)] public string? ShipmentProvider { get; set; }
    [MaxLength(80)] public string? ShipmentServiceCode { get; set; }
    [MaxLength(120)] public string? TrackingNo { get; set; }
    [MaxLength(80)] public string? WarehouseCode { get; set; }

    [MaxLength(120)] public string? BuyerUserId { get; set; }
    [MaxLength(200)] public string? BuyerUsername { get; set; }
    [MaxLength(200)] public string? BuyerName { get; set; }
    [MaxLength(60)] public string? BuyerPhone { get; set; }
    [MaxLength(200)] public string? BuyerEmail { get; set; }

    public long? ShipToAddressId { get; set; }
    public long? BillToAddressId { get; set; }

    public DateTime? CreatedTimeUtc { get; set; }
    public DateTime? UpdatedTimeUtc { get; set; }
    public DateTime? PaidTimeUtc { get; set; }
    public DateTime? CancelTimeUtc { get; set; }
    public DateTime? ShippedTimeUtc { get; set; }
    public DateTime? DeliveredTimeUtc { get; set; }
    public DateTime? CompletedTimeUtc { get; set; }

    public string? NoteBuyer { get; set; }
    public string? NoteSeller { get; set; }

    public long? SourceRawId { get; set; }
    public byte[]? SourcePayloadHash { get; set; }
    [MaxLength(40)] public string? IngestBatchNo { get; set; }
    public DateTime IngestedAtUtc { get; set; }

    public List<UnifiedOrderItems> Items { get; set; } = new();
    public List<UnifiedOrderPayments> Payments { get; set; } = new();
    public List<UnifiedOrderShipments> Shipments { get; set; } = new();
}
