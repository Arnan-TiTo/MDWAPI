using System.Text.Json;
using MDWAPI.Common;
using MDWAPI.DTOs;

namespace MDWAPI.Normalization;

public static class ShopeeNormalizer
{
    public static UnifiedOrderDto Normalize(JsonElement root, long? shopId, string? sellerId, long rawId, string rawJson, string? batchNo)
    {
        var orderSn = root.GetString("order_sn") ?? throw new ArgumentException("order_sn missing");
        var currency = root.GetString("currency");
        var statusRaw = root.GetString("order_status");
        var orderStat = StatusMapper.Order("shopee", statusRaw);

        var created = JsonExt.FromUnixSeconds(root.GetLong("create_time"));
        var updated = JsonExt.FromUnixSeconds(root.GetLong("update_time"));
        var paid = JsonExt.FromUnixSeconds(root.GetLong("pay_time"));
        var shipped = JsonExt.FromUnixSeconds(root.GetLong("ship_time"));
        var delivered = JsonExt.FromUnixSeconds(root.GetLong("complete_time"));
        var canceled = JsonExt.FromUnixSeconds(root.GetLong("cancel_time"));

        UnifiedAddress? shipTo = null;
        if (root.TryGetProperty("recipient_address", out var addr))
        {
            shipTo = new UnifiedAddress
            {
                Name = addr.GetString("name"),
                Phone = addr.GetString("phone"),
                Country = addr.GetString("country"),
                State = addr.GetString("state"),
                City = addr.GetString("city"),
                District = addr.GetString("district"),
                PostalCode = addr.GetString("zip"),
                Address1 = addr.GetString("full_address"),
                FullAddress = addr.GetString("full_address")
            };
        }

        var items = new List<UnifiedOrderItem>();
        if (root.TryGetProperty("item_list", out var itemArr) && itemArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var it in itemArr.EnumerateArray())
            {
                var qty = (int)(it.GetLong("model_quantity_purchased") ?? it.GetLong("item_quantity") ?? 0);
                var unit = it.GetDecimal("model_original_price") ?? it.GetDecimal("item_original_price") ?? 0m;
                //var price = it.GetDecimal("model_discounted_price") ?? it.GetDecimal("item_price") ?? unit;
                decimal? price = it.GetDecimal("model_discounted_price") ?? it.GetDecimal("item_price"); // nullable

                items.Add(new UnifiedOrderItem
                {
                    ExternalItemId = it.GetString("item_id") ?? it.GetString("model_id"),
                    ProductName = it.GetString("item_name") ?? "N/A",
                    VariationName = it.GetString("model_name"),
                    SellerSku = it.GetString("item_sku") ?? it.GetString("model_sku"),
                    PlatformSku = it.GetString("model_sku"),
                    QtyOrdered = qty,
                    UnitPrice = price,
                    OriginalPrice = unit,
                    LineTotal = (qty > 0 && price.HasValue) ? qty * price.Value : null
                });
            }
        }

        var payments = new List<UnifiedPayment>();
        var isCod = string.Equals(root.GetString("payment_method"), "COD", StringComparison.OrdinalIgnoreCase);
        payments.Add(new UnifiedPayment
        {
            Method = root.GetString("payment_method"),
            ChannelTxnId = root.GetString("transaction_sn"),
            PaidAmount = root.GetDecimal("actual_price") ?? root.GetDecimal("total_amount"),
            Currency = currency,
            PaidTimeUtc = paid,
            IsCOD = isCod
        });

        var shipments = new List<UnifiedShipment>
        {
            new()
            {
                Provider        = root.GetString("shipping_carrier"),
                ServiceCode     = root.GetString("logistics_status"),
                TrackingNo      = root.GetString("tracking_no"),
                Status          = root.GetString("logistics_status"),
                ShippedTimeUtc  = shipped,
                DeliveredTimeUtc= delivered
            }
        };

        var shippingFee = root.GetDecimal("estimated_shipping_fee") ?? root.GetDecimal("shipping_fee");
        var discountSeller = root.GetDecimal("seller_discount") ?? 0m;
        var discountPlatform = root.GetDecimal("discount") ?? 0m;
        var voucher = root.GetDecimal("voucher_amount") ?? 0m;
        var subtotal = items.Sum(i => i.LineTotal ?? 0m);
        var total = (subtotal - discountSeller - discountPlatform - voucher) + (shippingFee ?? 0m);

        return new UnifiedOrderDto
        {
            Channel = "Shopee",
            ShopId = shopId,
            SellerId = sellerId,
            ExternalOrderId = orderSn,
            ExternalOrderNo = orderSn,

            OrderStatus = orderStat,
            FulfillmentStatus = root.GetString("logistics_status"),
            PaymentStatus = isCod ? (paid is not null ? "PAID" : "UNPAID")
                                      : (paid is not null ? "PAID" : "UNPAID"),
            Currency = currency,

            SubtotalAmount = subtotal,
            DiscountSellerAmount = discountSeller,
            DiscountPlatformAmount = discountPlatform,
            VoucherAmount = voucher,
            ShippingFeeAmount = shippingFee,
            TaxAmount = root.GetDecimal("tax_amount"),
            TotalAmount = total,
            PaidAmount = payments.First().PaidAmount,
            RefundAmount = root.GetDecimal("refund_amount"),

            PaymentMethod = payments.First().Method,
            ShipmentProvider = shipments.First().Provider,
            ShipmentServiceCode = shipments.First().ServiceCode,
            TrackingNo = shipments.First().TrackingNo,

            BuyerUserId = root.GetString("buyer_userid"),
            BuyerName = root.GetString("buyer_username"),

            ShipTo = shipTo,

            CreatedTimeUtc = created,
            UpdatedTimeUtc = updated,
            PaidTimeUtc = paid,
            CancelTimeUtc = canceled,
            ShippedTimeUtc = shipped,
            DeliveredTimeUtc = delivered,
            CompletedTimeUtc = delivered,

            NoteBuyer = root.GetString("note"),
            NoteSeller = root.GetString("note_update_time"),

            Items = items,
            Payments = payments,
            Shipments = shipments,

            SourceRawId = rawId,
            SourcePayloadHash = JsonExt.Sha256(rawJson),
            IngestBatchNo = batchNo
        };
    }
}
