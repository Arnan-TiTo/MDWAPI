using System.Text.Json;
using MDWAPI.Common;
using MDWAPI.DTOs;

namespace MDWAPI.Normalization;

public static class ShopeeNormalizer
{
    public static UnifiedOrderDto Normalize(JsonElement root, long? shopId, string? sellerId, long rawId, string rawJson, string? batchNo)
    {
        // ===== Required =====
        var orderSn = root.GetString("order_sn") ?? throw new ArgumentException("order_sn missing");
        var currency = root.GetString("currency");
        var statusRaw = root.GetString("order_status");
        var orderStat = StatusMapper.Order("shopee", statusRaw);

        // ===== Times (epoch seconds; บางตัวอาจไม่มี) =====
        var created = JsonExt.FromUnixSeconds(root.GetLong("create_time"));
        var updated = JsonExt.FromUnixSeconds(root.GetLong("update_time"));
        var paid = JsonExt.FromUnixSeconds(root.GetLong("pay_time"));             // อาจเป็น null
        var shipped = JsonExt.FromUnixSeconds(root.GetLong("pickup_done_time"));     // ใช้ pickup_done_time เป็น shipped
        var delivered = JsonExt.FromUnixSeconds(root.GetLong("complete_time"));        // ถ้าไม่มี ไม่เป็นไร
        var canceled = JsonExt.FromUnixSeconds(root.GetLong("cancel_time"));

        // ===== Address (Shopee ใช้ region/zipcode/full_address) =====
        UnifiedAddress? shipTo = null;
        if (root.TryGetProperty("recipient_address", out var addr))
        {
            shipTo = new UnifiedAddress
            {
                Name = addr.GetString("name"),
                Phone = addr.GetString("phone"),
                Country = addr.GetString("region") ?? addr.GetString("country"),
                State = addr.GetString("state"),
                City = addr.GetString("city"),
                District = addr.GetString("district"),
                PostalCode = addr.GetString("zipcode") ?? addr.GetString("zip"),
                Address1 = null, // เก็บเต็มไว้ที่ FullAddress
                FullAddress = addr.GetString("full_address")
            };
        }

        // ===== Items =====
        var items = new List<UnifiedOrderItem>();
        if (root.TryGetProperty("item_list", out var itemArr) && itemArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var it in itemArr.EnumerateArray())
            {
                var qty = (int)(it.GetLong("model_quantity_purchased") ?? it.GetLong("item_quantity") ?? 0);
                var unit = it.GetDecimal("model_original_price") ?? it.GetDecimal("item_original_price") ?? 0m;
                var price = it.GetDecimal("model_discounted_price") ?? it.GetDecimal("item_price"); // nullable

                items.Add(new UnifiedOrderItem
                {
                    ExternalItemId = it.GetString("order_item_id") ?? it.GetString("item_id") ?? it.GetString("model_id"),
                    ProductName = it.GetString("item_name") ?? "N/A",
                    VariationName = it.GetString("model_name"),
                    SellerSku = it.GetString("model_sku") ?? it.GetString("item_sku"),
                    PlatformSku = it.GetString("item_sku"),
                    QtyOrdered = qty,
                    UnitPrice = price,
                    OriginalPrice = unit,
                    LineTotal = (qty > 0 && price.HasValue) ? qty * price.Value : null
                });
            }
        }

        // ===== Payments =====
        var method = root.GetString("payment_method");
        var isCod = !string.IsNullOrWhiteSpace(method) &&
                    method.Contains("cod", StringComparison.OrdinalIgnoreCase);
        // PaidAmount: ถ้ามี total_amount และมี paid time ใช้ total_amount เป็น PaidAmount (ปลอดภัยกว่า)
        var totalAmount = root.GetDecimal("total_amount");
        var paidAmountGuess = (paid != null ? totalAmount : null)
                              ?? root.GetDecimal("actual_price")
                              ?? root.GetDecimal("paid_amount");

        var payments = new List<UnifiedPayment>
        {
            new()
            {
                Method      = method,
                ChannelTxnId= root.GetString("transaction_sn"), // อาจไม่มี
                PaidAmount  = paidAmountGuess,
                Currency    = currency,
                PaidTimeUtc = paid,
                IsCOD       = isCod
            }
        };

        // ===== Shipments (จาก package_list เป็นหลัก) =====
        string? provider = root.GetString("shipping_carrier");
        string? tracking = null;
        string? service = null;
        string? shipStatus = null;

        if (root.TryGetProperty("package_list", out var pkgArr) && pkgArr.ValueKind == JsonValueKind.Array && pkgArr.GetArrayLength() > 0)
        {
            var p0 = pkgArr[0];
            tracking = p0.GetString("package_number");
            provider = p0.GetString("shipping_carrier") ?? provider;
            service = p0.GetLong("logistics_channel_id")?.ToString() ?? p0.GetString("logistics_channel_id");
            shipStatus = p0.GetString("logistics_status");
        }

        var shipments = new List<UnifiedShipment>
        {
            new()
            {
                Provider         = provider,
                ServiceCode      = service,
                TrackingNo       = tracking,
                Status           = shipStatus,
                PickupTimeUtc    = shipped,
                ShippedTimeUtc   = shipped,
                DeliveredTimeUtc = delivered
            }
        };

        // ===== Money fields =====
        var shippingFee = root.GetDecimal("estimated_shipping_fee") ?? root.GetDecimal("shipping_fee");
        var discountSeller = root.GetDecimal("seller_discount");
        var discountPlatform = root.GetDecimal("discount");
        var voucher = root.GetDecimal("voucher_amount");
        var tax = root.GetDecimal("tax_amount");

        // subtotal: ถ้าคิดจากไลน์ได้ ให้ใช้; ถ้าได้ 0 และมี total+shipping ให้ถอดกลับ
        var subtotalFromItems = items.Sum(i => i.LineTotal ?? 0m);
        decimal? subtotal = subtotalFromItems > 0
            ? subtotalFromItems
            : (totalAmount.HasValue && shippingFee.HasValue ? totalAmount - shippingFee : null);

        var total = totalAmount
                    ?? (subtotal ?? 0m) - (discountSeller ?? 0m) - (discountPlatform ?? 0m) - (voucher ?? 0m)
                       + (shippingFee ?? 0m);

        // ===== Buyer =====
        var buyerUserId = root.GetLong("buyer_user_id")?.ToString() ?? root.GetString("buyer_userid");
        var buyerName = shipTo?.Name ?? root.GetString("buyer_username"); // ให้ชื่อผู้รับเป็นหลัก
        var buyerPhone = shipTo?.Phone;

        // ===== Notes =====
        var noteBuyer = root.GetString("message_to_seller");
        var noteSeller = root.GetString("note"); // เดิมโค้ดเอา note_update_time มาใส่ซึ่งเป็นเลข

        // ===== Fulfillment/Payment status =====
        var fulfillment = shipStatus; // ใช้ logistics_status
        var paymentStatus = isCod
            ? (paid is not null ? "PAID" : "UNPAID")
            : (paid is not null ? "PAID" : "UNPAID");

        return new UnifiedOrderDto
        {
            Channel = "Shopee",
            ShopId = shopId,
            SellerId = sellerId,
            ExternalOrderId = orderSn,
            ExternalOrderNo = orderSn,

            OrderStatus = orderStat,
            FulfillmentStatus = fulfillment,
            PaymentStatus = paymentStatus,
            Currency = currency,

            SubtotalAmount = subtotal,
            DiscountSellerAmount = discountSeller,
            DiscountPlatformAmount = discountPlatform,
            VoucherAmount = voucher,
            ShippingFeeAmount = shippingFee,
            TaxAmount = tax,
            TotalAmount = total,
            PaidAmount = payments.First().PaidAmount,
            RefundAmount = root.GetDecimal("refund_amount"),

            PaymentMethod = payments.First().Method,
            ShipmentProvider = shipments.First().Provider,
            ShipmentServiceCode = shipments.First().ServiceCode,
            TrackingNo = shipments.First().TrackingNo,

            BuyerUserId = buyerUserId,
            BuyerName = buyerName,
            BuyerPhone = buyerPhone,

            ShipTo = shipTo,

            CreatedTimeUtc = created,
            UpdatedTimeUtc = updated,
            PaidTimeUtc = paid,
            CancelTimeUtc = canceled,
            ShippedTimeUtc = shipped,
            DeliveredTimeUtc = delivered,
            CompletedTimeUtc = delivered, // ถ้า Shopee มี complete_time ให้ map มาตรงนี้ด้วย

            NoteBuyer = noteBuyer,
            NoteSeller = noteSeller,

            Items = items,
            Payments = payments,
            Shipments = shipments,

            SourceRawId = rawId,
            SourcePayloadHash = JsonExt.Sha256(rawJson),
            IngestBatchNo = batchNo
        };
    }
}
