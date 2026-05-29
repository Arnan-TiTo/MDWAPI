using System.Text.Json;
using MDWAPI.Common;
using MDWAPI.DTOs;

namespace MDWAPI.Normalization;

public static class TikTokNormalizer
{
    public static UnifiedOrderDto Normalize(JsonElement root, long? shopId, string? sellerId, long rawId, string rawJson, string? batchNo)
    {
        var orderId = root.GetString("order_id") ?? root.GetString("id") ?? throw new ArgumentException("order_id missing");
        var currency = root.GetString("currency") ?? (root.TryGetProperty("payment", out var payObj) ? payObj.GetString("currency") : null);
        var statusRaw = root.GetString("order_status") ?? root.GetString("status");
        var orderStat = StatusMapper.Order("tiktok", statusRaw);

        var created = JsonExt.FromUnixSeconds(root.GetLong("create_time"));
        var updated = JsonExt.FromUnixSeconds(root.GetLong("update_time"));
        var paid = JsonExt.FromUnixSeconds(root.GetLong("pay_time") ?? root.GetLong("paid_time"));
        var shipped = JsonExt.FromUnixSeconds(root.GetLong("ship_time") ?? root.GetLong("rts_time"));
        var delivered = JsonExt.FromUnixSeconds(root.GetLong("deliver_time") ?? root.GetLong("delivery_time"));
        var canceled = JsonExt.FromUnixSeconds(root.GetLong("cancel_time"));

        UnifiedAddress? shipTo = null;
        if (root.TryGetProperty("recipient_address", out var a) || root.TryGetProperty("address_info", out a))
        {
            string? country = a.GetString("country");
            string? state = a.GetString("state") ?? a.GetString("province");
            string? district = a.GetString("district");

            if (a.TryGetProperty("district_info", out var distArr) && distArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in distArr.EnumerateArray())
                {
                    var lvl = el.GetString("address_level_name") ?? el.GetString("address_level");
                    var name = el.GetString("address_name");
                    if (lvl == "Country" || lvl == "L0") country = name;
                    else if (lvl == "province" || lvl == "L1") state = name;
                    else if (lvl == "district" || lvl == "L2") district = name;
                }
            }

            shipTo = new UnifiedAddress
            {
                Name = a.GetString("name") ?? a.GetString("receiver_name"),
                Phone = a.GetString("phone_number") ?? a.GetString("receiver_phone"),
                Country = country,
                State = state,
                City = a.GetString("city"),
                District = district,
                PostalCode = a.GetString("postal_code") ?? a.GetString("zipcode"),
                Address1 = a.GetString("address_line1") ?? a.GetString("detail_address"),
                Address2 = a.GetString("address_line2"),
                FullAddress = a.GetString("full_address")
            };
        }

        var items = new List<UnifiedOrderItem>();
        if ((root.TryGetProperty("line_items", out var itemArr) || root.TryGetProperty("items", out itemArr)) && itemArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var it in itemArr.EnumerateArray())
            {
                var qty = (int)(it.GetLong("quantity") ?? 1L);
                var price = it.GetDecimal("sale_price") ?? it.GetDecimal("price") ?? 0m;
                var orig = it.GetDecimal("original_price") ?? price;

                items.Add(new UnifiedOrderItem
                {
                    ExternalItemId = it.GetString("id") ?? it.GetString("sku_id") ?? it.GetString("product_id"),
                    ProductName = it.GetString("product_name") ?? "N/A",
                    VariationName = it.GetString("sku_name") ?? it.GetString("variation_name"),
                    SellerSku = it.GetString("seller_sku"),
                    PlatformSku = it.GetString("sku_id") ?? it.GetString("sku_code"),
                    QtyOrdered = qty,
                    UnitPrice = price,
                    OriginalPrice = orig,
                    LineTotal = qty * price,
                    DiscountSeller = it.GetDecimal("seller_discount"),
                    DiscountPlatform = it.GetDecimal("platform_discount")
                });
            }
        }

        var payments = new List<UnifiedPayment>();
        var paymentMethod = root.GetString("payment_method_name") ?? root.GetString("payment_method_code");
        
        JsonElement payInfo = default;
        if (root.TryGetProperty("payment_info", out payInfo))
        {
            paymentMethod ??= payInfo.GetString("payment_method");
        }

        decimal? paidAmt = null;
        if (root.TryGetProperty("payment", out var pObj))
        {
            paidAmt = pObj.GetDecimal("total_amount");
        }
        else if (payInfo.ValueKind != JsonValueKind.Undefined)
        {
            paidAmt = payInfo.GetDecimal("paid_amount");
        }

        if (paymentMethod != null || paidAmt.HasValue)
        {
            payments.Add(new UnifiedPayment
            {
                Method = paymentMethod,
                ChannelTxnId = payInfo.ValueKind != JsonValueKind.Undefined ? payInfo.GetString("transaction_id") : null,
                PaidAmount = paidAmt,
                Currency = currency,
                PaidTimeUtc = paid,
                IsCOD = string.Equals(paymentMethod, "COD", StringComparison.OrdinalIgnoreCase) || 
                         (root.TryGetProperty("is_cod", out var isCodEl) && isCodEl.ValueKind == JsonValueKind.True)
            });
        }

        var shipments = new List<UnifiedShipment>();
        string? provider = root.GetString("shipping_provider");
        string? tracking = root.GetString("tracking_number");
        string? service = root.GetString("shipping_type") ?? root.GetString("delivery_option_name");
        string? logisticsStatus = root.GetString("status");

        if (root.TryGetProperty("logistics_info", out var lg))
        {
            provider ??= lg.GetString("shipping_provider");
            tracking ??= lg.GetString("tracking_number");
            service ??= lg.GetString("service_code");
            logisticsStatus ??= lg.GetString("logistics_status");
        }

        if (provider != null || tracking != null || service != null)
        {
            shipments.Add(new UnifiedShipment
            {
                Provider = provider,
                ServiceCode = service,
                TrackingNo = tracking,
                Status = logisticsStatus,
                ShippedTimeUtc = shipped,
                DeliveredTimeUtc = delivered
            });
        }

        decimal? shippingFee = null;
        decimal discountSeller = 0m;
        decimal discountPlatform = 0m;
        decimal voucher = 0m;
        decimal? subtotal = null;
        decimal? total = null;
        decimal? tax = null;

        if (root.TryGetProperty("payment", out var paymentObj))
        {
            shippingFee = paymentObj.GetDecimal("shipping_fee");
            discountSeller = paymentObj.GetDecimal("seller_discount") ?? 0m;
            discountPlatform = paymentObj.GetDecimal("platform_discount") ?? 0m;
            voucher = paymentObj.GetDecimal("voucher_discount") ?? 0m;
            subtotal = paymentObj.GetDecimal("sub_total");
            total = paymentObj.GetDecimal("total_amount");
            tax = paymentObj.GetDecimal("tax");
        }
        else
        {
            shippingFee = root.GetDecimal("shipping_fee");
            discountSeller = root.GetDecimal("seller_discount") ?? 0m;
            discountPlatform = root.GetDecimal("platform_discount") ?? 0m;
            voucher = root.GetDecimal("voucher_discount") ?? 0m;
            subtotal = items.Sum(i => i.LineTotal ?? 0m);
            total = (subtotal - discountSeller - discountPlatform - voucher) + (shippingFee ?? 0m);
            tax = root.GetDecimal("tax_amount");
        }

        return new UnifiedOrderDto
        {
            Channel = "TikTok",
            ShopId = shopId,
            SellerId = sellerId,
            ExternalOrderId = orderId,
            ExternalOrderNo = orderId,

            OrderStatus = orderStat,
            FulfillmentStatus = logisticsStatus,
            PaymentStatus = payments.Count > 0 && payments[0].PaidAmount.GetValueOrDefault() > 0 ? "PAID" : "UNPAID",
            Currency = currency,

            SubtotalAmount = subtotal,
            DiscountSellerAmount = discountSeller,
            DiscountPlatformAmount = discountPlatform,
            VoucherAmount = voucher,
            ShippingFeeAmount = shippingFee,
            TaxAmount = tax,
            TotalAmount = total,
            PaidAmount = payments.FirstOrDefault()?.PaidAmount,
            RefundAmount = root.GetDecimal("refund_amount"),

            PaymentMethod = payments.FirstOrDefault()?.Method,
            ShipmentProvider = shipments.FirstOrDefault()?.Provider,
            ShipmentServiceCode = shipments.FirstOrDefault()?.ServiceCode,
            TrackingNo = shipments.FirstOrDefault()?.TrackingNo,

            BuyerUserId = root.GetString("buyer_user_id") ?? root.GetString("user_id"),
            BuyerName = root.GetString("buyer_nickname") ?? (root.TryGetProperty("recipient_address", out var recAddr) ? recAddr.GetString("name") : null),
            BuyerPhone = root.GetString("buyer_phone") ?? (root.TryGetProperty("recipient_address", out recAddr) ? recAddr.GetString("phone_number") : null),
            BuyerEmail = root.GetString("buyer_email"),

            ShipTo = shipTo,

            CreatedTimeUtc = created,
            UpdatedTimeUtc = updated,
            PaidTimeUtc = paid,
            CancelTimeUtc = canceled,
            ShippedTimeUtc = shipped,
            DeliveredTimeUtc = delivered,
            CompletedTimeUtc = root.GetLong("complete_time") is long ct ? JsonExt.FromUnixSeconds(ct) : (root.GetLong("update_time") is long ut && statusRaw == "COMPLETED" ? JsonExt.FromUnixSeconds(ut) : delivered),

            Items = items,
            Payments = payments,
            Shipments = shipments,

            SourceRawId = rawId,
            SourcePayloadHash = JsonExt.Sha256(rawJson),
            IngestBatchNo = batchNo
        };
    }
}
