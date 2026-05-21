namespace MDWAPI.Models
{
    public class VwOrderMergedItem
    {
        // จาก uoi.*
        public long UnifiedOrderItemId { get; set; }
        public long UnifiedOrderId { get; set; }
        public string ProductName { get; set; } = default!;
        public string? VariationName { get; set; }
        public string? SellerSku { get; set; }
        public int QtyOrdered { get; set; }
        public int? QtyCanceled { get; set; }
        public int? QtyShipped { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? LineTotal { get; set; }

        // keys ที่ต้องใช้ filter
        public string Channel { get; set; } = default!;
        public long? ShopId { get; set; }

        // IDW aliases
        public string? ItemName { get; set; }        // itemName
        public string? ItemVariantUnit { get; set; } // itemVariantUnit
        public string? ItemSkd { get; set; }         // itemSkd

        // computed
        public int QtyTotal { get; set; }            // qtyTotal
    }
}
