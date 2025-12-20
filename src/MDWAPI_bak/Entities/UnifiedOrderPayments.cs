using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

[Table("UnifiedOrderPayments", Schema = "mdw")]
public class UnifiedOrderPayments
{
    [Key] public long UnifiedOrderPaymentId { get; set; }

    [ForeignKey(nameof(Order))] public long UnifiedOrderId { get; set; }
    public UnifiedOrders Order { get; set; } = default!;

    [MaxLength(60)] public string? Method { get; set; }
    [MaxLength(120)] public string? ChannelTxnId { get; set; }
    public decimal? PaidAmount { get; set; }
    [MaxLength(8)] public string? Currency { get; set; }
    public DateTime? PaidTimeUtc { get; set; }
    public decimal? FeeAmount { get; set; }
    public string? FeeDetailsJson { get; set; }
    public bool? IsCOD { get; set; }
}
