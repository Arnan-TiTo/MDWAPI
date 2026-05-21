using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace MDWAPI.Models
{
    /// <summary>
    /// Keyless entity map กับ adw.vw_OrderMerged
    /// เลือกเฉพาะ fields ที่ FE ใช้จริง เพื่อลด payload
    /// </summary>
    public class VwOrderMerged
    {
        // ==== Keys/หลัก ๆ จาก u.* (เลือกเฉพาะที่ใช้ filter / แสดงผล) ====
        public long UnifiedOrderId { get; set; }
        public string Channel { get; set; } = default!;
        public long? ShopId { get; set; }
        public string ExternalOrderId { get; set; } = default!;
        public string? ExternalOrderNo { get; set; }
        public string? OrderStatus { get; set; }
        public string? FulfillmentStatus { get; set; }
        public string? PaymentStatus { get; set; }
        public string? Currency { get; set; }
        public decimal? TotalAmount { get; set; }
        public decimal? PaidAmount { get; set; }
        public DateTime? CreatedTimeUtc { get; set; }
        public DateTime? UpdatedTimeUtc { get; set; }

        // ==== สรุปจาก qty_agg, latest_ship ====
        public int? TotalQtyOrderedEffective { get; set; }
        public decimal? SumLineTotal { get; set; }
        public DateTime? LastShippedUtc { get; set; }
        public DateTime? LastDeliveredUtc { get; set; }

        // ==== Ship-To จาก mdw.UnifiedOrderAddresses (map ชื่อให้ camelCase) ====
        [Column("ShipTo_Type")]
        [JsonPropertyName("shipToType")]
        public string? ShipToType { get; set; }

        [Column("ShipTo_Name")]
        [JsonPropertyName("shipToName")]
        public string? ShipToName { get; set; }

        // ในวิว: ir.ReceiverName  AS ShipTo_ReceiverName
        [Column("ShipTo_ReceiverName")]
        [JsonPropertyName("shipToReceiverName")]
        public string? ShipToReceiverName { get; set; }

        [Column("ShipTo_Address1")]
        [JsonPropertyName("shipToAddress1")]
        public string? ShipToAddress1 { get; set; }

        [Column("ShipTo_Address2")]
        [JsonPropertyName("shipToAddress2")]
        public string? ShipToAddress2 { get; set; }

        [Column("ShipTo_District")]
        [JsonPropertyName("shipToDistrict")]
        public string? ShipToDistrict { get; set; }

        [Column("ShipTo_City")]
        [JsonPropertyName("shipToCity")]
        public string? ShipToCity { get; set; }

        [Column("ShipTo_State")]
        [JsonPropertyName("shipToState")]
        public string? ShipToState { get; set; }

        [Column("ShipTo_Country")]
        [JsonPropertyName("shipToCountry")]
        public string? ShipToCountry { get; set; }

        [Column("ShipTo_PostalCode")]
        [JsonPropertyName("shipToPostalCode")]
        public string? ShipToPostalCode { get; set; }

        [Column("ShipTo_Latitude")]
        [JsonPropertyName("shipToLatitude")]
        public decimal? ShipToLatitude { get; set; }

        [Column("ShipTo_Longitude")]
        [JsonPropertyName("shipToLongitude")]
        public decimal? ShipToLongitude { get; set; }

        [Column("ShipTo_Phone")]
        [JsonPropertyName("shipToPhone")]
        public string? ShipToPhone { get; set; }

        [Column("ShipTo_Email")]
        [JsonPropertyName("shipToEmail")]
        public string? ShipToEmail { get; set; }

        // ==== ฟิลด์จาก IDW ในวิว (alias เป็นตัวพิมพ์เล็ก) ====
        [Column("serdername")]
        [JsonPropertyName("serderName")]
        public string? SerderName { get; set; }

        [Column("serderaddress")]
        [JsonPropertyName("serderAddress")]
        public string? SerderAddress { get; set; }

        [Column("trackno")]
        [JsonPropertyName("trackNo")]
        public string? TrackNo { get; set; }
    }
}
