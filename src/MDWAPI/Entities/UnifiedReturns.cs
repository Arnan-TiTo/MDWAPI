using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

[Table("UnifiedReturns", Schema = "mdw")]
public class UnifiedReturns
{
    [Key] public long UnifiedReturnId { get; set; }

    public long? UnifiedOrderId { get; set; }
    [ForeignKey(nameof(UnifiedOrderId))]
    public UnifiedOrders? Order { get; set; }

    [Required, MaxLength(20)] public string Channel { get; set; } = default!;
    public long? ShopId { get; set; }
    [MaxLength(100)] public string? ExternalOrderId { get; set; }   // order_sn
    [Required, MaxLength(100)] public string ExternalReturnId { get; set; } = default!; // return_sn

    [MaxLength(40)] public string? ReturnStatus { get; set; }       // REQUESTED / ACCEPTED / REFUND_PAID / CLOSED / ...
    [MaxLength(200)] public string? ReturnReason { get; set; }      // reason code
    [MaxLength(500)] public string? TextReason { get; set; }        // buyer text
    [MaxLength(40)] public string? ReturnType { get; set; }         // RETURN_REFUND / REFUND_ONLY
    [MaxLength(40)] public string? ReturnSolution { get; set; }     // latest solution
    [MaxLength(40)] public string? NegotiationStatus { get; set; }

    public decimal? RefundAmount { get; set; }
    [MaxLength(8)] public string? Currency { get; set; }

    // items ที่ถูก return (เก็บ JSON array)
    public string? ReturnItemsJson { get; set; }

    // images (buyer proof)
    public string? ImagesJson { get; set; }

    public DateTime? CreatedAtUtc { get; set; }     // return create_time
    public DateTime? UpdatedAtUtc { get; set; }     // return update_time
    public DateTime IngestedAtUtc { get; set; } = DateTime.UtcNow;

    // keep raw
    public string? RawJson { get; set; }
}
