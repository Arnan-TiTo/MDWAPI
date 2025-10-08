using System.Text.Json;
using MDWAPI.Common;
using MDWAPI.DTOs;

namespace MDWAPI.Normalization;

public static class TikTokNormalizer
{
    public static UnifiedOrderDto Normalize(JsonElement root, long? shopId, string? sellerId, long rawId, string rawJson, string? batchNo)
    {
        var orderId = root.GetString("order_id") ?? throw new ArgumentException("order_id missing");
        var currency = root.GetString("currency");
        var statusRaw = root.GetString("order_status");
        var orderStat = StatusMapper.Order("tiktok", statusRaw);

        var created = JsonExt.FromUnixSeconds(root.GetLong("create_time"));
        var updated = JsonExt.FromUnixSeconds(root.GetLong("update_time"));
        var paid = JsonExt.FromUnixSeconds(root.GetLong("pay_time"));
        var shipped = JsonExt.FromUnixSeconds(root.GetLong("ship_time"));
        var delivered = JsonExt.FromUnixSeconds(root.GetLong("deliver_time"));
        var canceled = JsonExt.FromUnixSeconds(root.GetLong("cancel_time"));

        UnifiedAddress? shipTo = null;
        if (root.TryGetProperty("address_info", out var a))
        {
            shipTo = new UnifiedAddress
            {
                Name = a.GetString("receiver_name"),
                Phone = a.GetString("receiver_phone"),
                Country = a.GetString("country"),
                State = a.GetString("state"),
                City = a.GetString("city"),
                District = a.GetString("district"),
                PostalCode = a.GetString("zipcode"),
                Address1 = a.GetString("detail_address"),
                FullAddress = a.GetString("full_address")
            };
        }

        var items = new List<UnifiedOrderItem>();
        if (root.TryGetProperty("items", out var itemArr) && itemArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var it in itemArr.EnumerateArray())
            {
                var qty = (int)(it.GetLong("quantity") ?? 0);
                var price = it.GetDecimal("sale_price") ?? it.GetDecimal("price") ?? 0m;
                var orig = it.GetDecimal("original_price") ?? price;

                items.Add(new UnifiedOrderItem
                {
                    ExternalItemId = it.GetString("sku_id") ?? it.GetString("product_id"),
                    ProductName = it.GetString("product_name") ?? "N/A",
                    VariationName = it.GetString("sku_name"),
                    SellerSku = it.GetString("seller_sku"),
                    PlatformSku = it.GetString("sku_code"),
                    QtyOrdered = qty,
                    UnitPrice = price,
                    OriginalPrice = orig,
                    LineTotal = qty * price
                });
            }
        }

        var payments = new List<UnifiedPayment>();
        if (root.TryGetProperty("payment_info", out var pay))
        {
            payments.Add(new UnifiedPayment
            {
                Method = pay.GetString("payment_method"),
                ChannelTxnId = pay.GetString("transaction_id"),
                PaidAmount = pay.GetDecimal("paid_amount"),
                Currency = currency,
                PaidTimeUtc = paid,
                IsCOD = string.Equals(pay.GetString("payment_method"), "COD", StringComparison.OrdinalIgnoreCase)
            });
        }

        var shipments = new List<UnifiedShipment>();
        if (root.TryGetProperty("logistics_info", out var lg))
        {
            shipments.Add(new UnifiedShipment
            {
                Provider = lg.GetString("shipping_provider"),
                ServiceCode = lg.GetString("service_code"),
                TrackingNo = lg.GetString("tracking_number"),
                Status = lg.GetString("logistics_status"),
                ShippedTimeUtc = shipped,
                DeliveredTimeUtc = delivered
            });
        }

        var shippingFee = root.GetDecimal("shipping_fee");
        var discountSeller = root.GetDecimal("seller_discount") ?? 0m;
        var discountPlatform = root.GetDecimal("platform_discount") ?? 0m;
        var voucher = root.GetDecimal("voucher_discount") ?? 0m;
        var subtotal = items.Sum(i => i.LineTotal ?? 0m);
        var total = (subtotal - discountSeller - discountPlatform - voucher) + (shippingFee ?? 0m);

        return new UnifiedOrderDto
        {
            Channel = "TikTok",
            ShopId = shopId,
            SellerId = sellerId,
            ExternalOrderId = orderId,
            ExternalOrderNo = root.GetString("order_number") ?? orderId,

            OrderStatus = orderStat,
            FulfillmentStatus = root.GetString("logistics_status"),
            PaymentStatus = payments.Count > 0 && payments[0].PaidAmount.GetValueOrDefault() > 0 ? "PAID" : "UNPAID",
            Currency = currency,

            SubtotalAmount = subtotal,
            DiscountSellerAmount = discountSeller,
            DiscountPlatformAmount = discountPlatform,
            VoucherAmount = voucher,
            ShippingFeeAmount = shippingFee,
            TaxAmount = root.GetDecimal("tax_amount"),
            TotalAmount = total,
            PaidAmount = payments.FirstOrDefault()?.PaidAmount,
            RefundAmount = root.GetDecimal("refund_amount"),

            PaymentMethod = payments.FirstOrDefault()?.Method,
            ShipmentProvider = shipments.FirstOrDefault()?.Provider,
            ShipmentServiceCode = shipments.FirstOrDefault()?.ServiceCode,
            TrackingNo = shipments.FirstOrDefault()?.TrackingNo,

            BuyerUserId = root.GetString("buyer_user_id"),
            BuyerName = root.GetString("buyer_nickname"),
            BuyerPhone = root.GetString("buyer_phone"),
            BuyerEmail = root.GetString("buyer_email"),

            ShipTo = shipTo,

            CreatedTimeUtc = created,
            UpdatedTimeUtc = updated,
            PaidTimeUtc = paid,
            CancelTimeUtc = canceled,
            ShippedTimeUtc = shipped,
            DeliveredTimeUtc = delivered,
            CompletedTimeUtc = root.GetLong("complete_time") is long ct ? JsonExt.FromUnixSeconds(ct) : delivered,

            Items = items,
            Payments = payments,
            Shipments = shipments,

            SourceRawId = rawId,
            SourcePayloadHash = JsonExt.Sha256(rawJson),
            IngestBatchNo = batchNo
        };
    }
}
