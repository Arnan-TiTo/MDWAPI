using System.Text.Json;
using MDWAPI.Common;
using MDWAPI.DTOs;

namespace MDWAPI.Normalization;

public static class LazadaNormalizer
{
    private static DateTimeOffset? ParseIso(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : DateTimeOffset.Parse(s).ToUniversalTime();

    public static UnifiedOrderDto Normalize(JsonElement root, long? shopId, string? sellerId, long rawId, string rawJson, string? batchNo)
    {
        var orderId = root.GetString("order_id") ?? throw new ArgumentException("order_id missing");
        var orderNo = root.GetString("order_number") ?? orderId;
        var currency = root.GetString("currency");
        var statusRaw = root.GetString("status") ?? root.GetString("statuses");
        var orderStat = StatusMapper.Order("lazada", statusRaw);

        var created = ParseIso(root.GetString("created_at"));
        var updated = ParseIso(root.GetString("updated_at"));
        var paid = ParseIso(root.GetString("paid_at"));
        var shipped = ParseIso(root.GetString("shipped_at"));
        var delivered = ParseIso(root.GetString("delivered_at"));
        var canceled = ParseIso(root.GetString("canceled_at"));
        var completed = ParseIso(root.GetString("finished_at")) ?? delivered;

        UnifiedAddress? shipTo = null;
        if (root.TryGetProperty("address_shipping", out var sh))
        {
            shipTo = new UnifiedAddress
            {
                Name = sh.GetString("first_name") ?? sh.GetString("name"),
                Phone = sh.GetString("phone"),
                Country = sh.GetString("country"),
                State = sh.GetString("province"),
                City = sh.GetString("city"),
                District = sh.GetString("district"),
                PostalCode = sh.GetString("post_code"),
                Address1 = sh.GetString("address1"),
                Address2 = sh.GetString("address2"),
                FullAddress = sh.GetString("address1")
            };
        }

        var items = new List<UnifiedOrderItem>();
        if (root.TryGetProperty("items", out var itemArr) && itemArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var it in itemArr.EnumerateArray())
            {
                var qty = (int)(it.GetLong("quantity") ?? 0);
                //var price = it.GetDecimal("item_price") ?? it.GetDecimal("paid_price") ?? 0m; // decimal
                decimal? price = it.GetDecimal("model_discounted_price") ?? it.GetDecimal("item_price"); // nullable
                var orig = it.GetDecimal("item_price") ?? price;

                items.Add(new UnifiedOrderItem
                {
                    ExternalItemId = it.GetString("sku_id") ?? it.GetString("item_id"),
                    ProductName = it.GetString("name") ?? "N/A",
                    VariationName = it.GetString("variation"),
                    SellerSku = it.GetString("seller_sku"),
                    PlatformSku = it.GetString("sku"),
                    QtyOrdered = qty,
                    UnitPrice = price,
                    OriginalPrice = orig,
                    DiscountSeller = it.GetDecimal("seller_discount"),
                    DiscountPlatform = it.GetDecimal("voucher_platform") ?? it.GetDecimal("voucher_seller"),
                    LineTotal = qty * (price ?? 0m)
                });
            }
        }

        var payments = new List<UnifiedPayment>();
        if (root.TryGetProperty("payments", out var payArr) && payArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in payArr.EnumerateArray())
            {
                payments.Add(new UnifiedPayment
                {
                    Method = p.GetString("method"),
                    ChannelTxnId = p.GetString("transaction_id"),
                    PaidAmount = p.GetDecimal("amount"),
                    Currency = currency,
                    PaidTimeUtc = ParseIso(p.GetString("paid_time")),
                    IsCOD = string.Equals(p.GetString("method"), "COD", StringComparison.OrdinalIgnoreCase)
                });
            }
        }
        else
        {
            payments.Add(new UnifiedPayment
            {
                Method = root.GetString("payment_method"),
                ChannelTxnId = root.GetString("payment_reference"),
                PaidAmount = root.GetDecimal("paid_price"),
                Currency = currency,
                PaidTimeUtc = paid
            });
        }

        var shipments = new List<UnifiedShipment>();
        if (root.TryGetProperty("shipment", out var sp))
        {
            shipments.Add(new UnifiedShipment
            {
                Provider = sp.GetString("provider"),
                ServiceCode = sp.GetString("service_code"),
                TrackingNo = sp.GetString("tracking_number"),
                Status = sp.GetString("status"),
                ShippedTimeUtc = shipped,
                DeliveredTimeUtc = delivered
            });
        }

        var shippingFee = root.GetDecimal("shipping_fee");
        var discountSeller = root.GetDecimal("seller_discount_total") ?? 0m;
        var discountPlatform = root.GetDecimal("platform_discount_total") ?? 0m;
        var voucher = root.GetDecimal("voucher_discount_total") ?? 0m;
        var subtotal = items.Sum(i => i.LineTotal ?? 0m);
        var total = (subtotal - discountSeller - discountPlatform - voucher) + (shippingFee ?? 0m);

        return new UnifiedOrderDto
        {
            Channel = "Lazada",
            ShopId = shopId,
            SellerId = sellerId,
            ExternalOrderId = orderId,
            ExternalOrderNo = orderNo,

            OrderStatus = orderStat,
            FulfillmentStatus = root.GetString("status"),
            PaymentStatus = payments.Sum(p => p.PaidAmount ?? 0m) > 0 ? "PAID" : "UNPAID",
            Currency = currency,

            SubtotalAmount = subtotal,
            DiscountSellerAmount = discountSeller,
            DiscountPlatformAmount = discountPlatform,
            VoucherAmount = voucher,
            ShippingFeeAmount = shippingFee,
            TaxAmount = root.GetDecimal("tax_amount"),
            TotalAmount = total,
            PaidAmount = payments.Sum(p => p.PaidAmount ?? 0m),
            RefundAmount = root.GetDecimal("refund_amount"),

            PaymentMethod = payments.FirstOrDefault()?.Method,
            ShipmentProvider = shipments.FirstOrDefault()?.Provider,
            ShipmentServiceCode = shipments.FirstOrDefault()?.ServiceCode,
            TrackingNo = shipments.FirstOrDefault()?.TrackingNo,

            BuyerUserId = root.GetString("buyer_id"),
            BuyerName = root.GetString("customer_first_name") ?? root.GetString("customer_name"),
            BuyerPhone = root.GetString("customer_phone"),
            BuyerEmail = root.GetString("customer_email"),

            ShipTo = shipTo,

            CreatedTimeUtc = created,
            UpdatedTimeUtc = updated,
            PaidTimeUtc = paid,
            CancelTimeUtc = canceled,
            ShippedTimeUtc = shipped,
            DeliveredTimeUtc = delivered,
            CompletedTimeUtc = completed,

            Items = items,
            Payments = payments,
            Shipments = shipments,

            SourceRawId = rawId,
            SourcePayloadHash = JsonExt.Sha256(rawJson),
            IngestBatchNo = batchNo
        };
    }
}
