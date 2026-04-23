using System;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Models
{
    /// <summary>
    /// แมพกับ mdw.v_UnifiedOrders (Keyless / View)
    /// </summary>
    [Keyless]
    [Table("v_UnifiedOrders", Schema = "mdw")]
    public class VUnifiedOrder
    {
        public long UnifiedOrderId { get; set; }
        public string? ExternalOrderNo { get; set; }
        public string? Channel { get; set; }   // (เช่น Shopee / TikTok / Lazada)
        public long ShopId { get; set; }
        public string? SellerId { get; set; }
        public string? OrderStatus { get; set; }
        public DateTime CreatedTimeUtc { get; set; }
        public DateTime? UpdatedTimeUtc { get; set; }

        public string? BuyerUserId { get; set; }
        public string? BuyerUsername { get; set; }

        // JSON columns from the view
        public string? ItemsJson { get; set; }
        public string? PaymentsJson { get; set; }
        public string? ShipmentsJson { get; set; }
        public string? ShipToJson { get; set; }

        // Escrow / income breakdown
        public decimal? EscrowAmount { get; set; }
        public decimal? BuyerPaidShippingFee { get; set; }
        public decimal? ActualShippingFee { get; set; }
        public decimal? PlatformShippingRebate { get; set; }
        public decimal? CommissionFee { get; set; }
        public decimal? ServiceFee { get; set; }
        public decimal? PlatformFee { get; set; }
        public decimal? PaymentTransactionFee { get; set; }
        public decimal? AmsCommissionFee { get; set; }
        public string? SellerVoucherCode { get; set; }
    }
}
