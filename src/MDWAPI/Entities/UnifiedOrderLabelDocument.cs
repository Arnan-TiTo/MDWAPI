using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

[Table("UnifiedOrderLabelDocument", Schema = "mdw")]
public class UnifiedOrderLabelDocument
{
    [Key] public long Id { get; set; }

    [Required, MaxLength(20)] public string Platform { get; set; } = default!;
    public long ShopId { get; set; }
    [Required, MaxLength(100)] public string OrderSn { get; set; } = default!;
    [MaxLength(100)] public string? PackageNumber { get; set; }

    // Status
    [MaxLength(50)] public string? Status { get; set; }
    [MaxLength(200)] public string? FailError { get; set; }
    [MaxLength(500)] public string? FailMessage { get; set; }
    [MaxLength(50)] public string? DocumentType { get; set; }

    // Logistics
    [MaxLength(100)] public string? TrackingNumber { get; set; }
    [MaxLength(200)] public string? LogisticsChannelName { get; set; }
    public decimal? CodAmount { get; set; }
    public int? ProductCount { get; set; }
    [MaxLength(100)] public string? PurchaseOrderNumber { get; set; }
    [MaxLength(50)] public string? PurchaseOrderPrefix { get; set; }
    [MaxLength(500)] public string? BuyerRemark { get; set; }
    public int? ParcelChargeableWeightGram { get; set; }
    public decimal? BillingWeight { get; set; }

    // Sender
    [MaxLength(200)] public string? SenderName { get; set; }
    [MaxLength(50)] public string? SenderPhone { get; set; }
    [MaxLength(500)] public string? SenderFullAddress { get; set; }
    [MaxLength(20)] public string? SenderZipcode { get; set; }

    // Recipient
    [MaxLength(200)] public string? RecipientName { get; set; }
    [MaxLength(50)] public string? RecipientPhone { get; set; }
    [MaxLength(100)] public string? RecipientTown { get; set; }
    [MaxLength(100)] public string? RecipientDistrict { get; set; }
    [MaxLength(100)] public string? RecipientCity { get; set; }
    [MaxLength(100)] public string? RecipientState { get; set; }
    [MaxLength(100)] public string? RecipientRegion { get; set; }
    [MaxLength(20)] public string? RecipientZipcode { get; set; }
    [MaxLength(500)] public string? RecipientFullAddress { get; set; }

    // Sort codes
    [MaxLength(100)] public string? AsSortCode { get; set; }
    [MaxLength(100)] public string? CpSortCode { get; set; }
    [MaxLength(100)] public string? FmSortCode { get; set; }

    // QR code plain text (from third_party_logistic_info.qrcode)
    public string? QrCodeData { get; set; }

    // JSON blobs
    public string? ItemListJson { get; set; }
    public string? RawJson { get; set; }

    // recipient_address_info: full array with base64 images
    public string? RecipientAddrRawJson { get; set; }
    // recipient_address_info decoded: {"name":"…","phone":"…","full_address":"…",…}
    public string? RecipientAddrDecodedJson { get; set; }

    // sender_address_info (if present)
    public string? SenderAddrRawJson { get; set; }
    public string? SenderAddrDecodedJson { get; set; }

    // Audit
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
