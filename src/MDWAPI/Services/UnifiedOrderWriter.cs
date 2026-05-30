using System.Text;
using System.Text.Json;
using MDWAPI.Data;
using MDWAPI.DTOs;
using MDWAPI.Entities;
using MDWAPI.Models;
using MDWAPI.Normalization;  // << ใช้ ShopeeNormalizer
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Services;

public class UnifiedOrderWriter : IUnifiedOrderWriter
{
    private readonly AppDbContext _db;
    public UnifiedOrderWriter(AppDbContext db) => _db = db;

    // =========================
    // RAW INSERT (idempotent)
    // =========================
    public async Task<long> InsertRawAsync(
        string channel,
        long? shopId,
        string? sellerId,
        string externalOrderId,
        string rawJson,
        string? batchNo,
        CancellationToken ct)
    {
        var hash = ComputeHash(rawJson);

        // กันซ้ำล่วงหน้า
        var existing = await _db.UnifiedRawOrders
            .Where(r => r.Channel == channel && r.ExternalOrderId == externalOrderId)
            .Select(r => new { r.RawId, r.PayloadHash })
            .ToListAsync(ct);

        var dup = existing.FirstOrDefault(x => x.PayloadHash != null && x.PayloadHash.SequenceEqual(hash));
        if (dup is not null) return dup.RawId;

        var row = new UnifiedRawOrders
        {
            Channel = channel,
            ShopId = shopId,
            SellerId = sellerId,
            ExternalOrderId = externalOrderId,
            PayloadJson = rawJson,
            PayloadHash = hash,
            BatchNo = batchNo
        };

        _db.Add(row);
        try
        {
            await _db.SaveChangesAsync(ct);
            return row.RawId;
        }
        catch (DbUpdateException)
        {
            // กัน race: ถ้าชน UNIQUE ให้ดึงตัวเดิมกลับมา
            var again = await _db.UnifiedRawOrders
                .Where(r => r.Channel == channel && r.ExternalOrderId == externalOrderId)
                .Select(r => new { r.RawId, r.PayloadHash })
                .ToListAsync(ct);

            var dup2 = again.FirstOrDefault(x => x.PayloadHash != null && x.PayloadHash.SequenceEqual(hash));
            if (dup2 is not null) return dup2.RawId;
            throw;
        }
    }

    // =========================
    // UNIFIED UPSERT (header+children)
    // =========================
    public async Task<long> UpsertAsync(UnifiedOrderDto dto, CancellationToken ct)
    {
        var existed = await _db.UnifiedOrders
            .FirstOrDefaultAsync(x => x.Channel == dto.Channel && x.ExternalOrderId == dto.ExternalOrderId, ct);

        if (existed is not null && dto.SourcePayloadHash is not null &&
            existed.SourcePayloadHash != null &&
            existed.SourcePayloadHash.SequenceEqual(dto.SourcePayloadHash))
        {
            // hash เท่าเดิม → ไม่ต้องแก้
            return existed.UnifiedOrderId;
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        long? shipAddrId = null, billAddrId = null;

        if (dto.ShipTo is not null)
        {
            var a = new UnifiedOrderAddresses
            {
                Type = "ShipTo",
                Name = dto.ShipTo.Name,
                Phone = dto.ShipTo.Phone,
                Email = dto.ShipTo.Email,
                Country = dto.ShipTo.Country,
                State = dto.ShipTo.State,
                City = dto.ShipTo.City,
                District = dto.ShipTo.District,
                PostalCode = dto.ShipTo.PostalCode,
                Address1 = dto.ShipTo.Address1,
                Address2 = dto.ShipTo.Address2,
                FullAddress = dto.ShipTo.FullAddress,
                Latitude = dto.ShipTo.Latitude,
                Longitude = dto.ShipTo.Longitude
            };
            _db.Add(a); await _db.SaveChangesAsync(ct); shipAddrId = a.UnifiedOrderAddressId;
        }
        if (dto.BillTo is not null)
        {
            var a = new UnifiedOrderAddresses
            {
                Type = "BillTo",
                Name = dto.BillTo.Name,
                Phone = dto.BillTo.Phone,
                Email = dto.BillTo.Email,
                Country = dto.BillTo.Country,
                State = dto.BillTo.State,
                City = dto.BillTo.City,
                District = dto.BillTo.District,
                PostalCode = dto.BillTo.PostalCode,
                Address1 = dto.BillTo.Address1,
                Address2 = dto.BillTo.Address2,
                FullAddress = dto.BillTo.FullAddress,
                Latitude = dto.BillTo.Latitude,
                Longitude = dto.BillTo.Longitude
            };
            _db.Add(a); await _db.SaveChangesAsync(ct); billAddrId = a.UnifiedOrderAddressId;
        }

        if (existed is null)
        {
            // ===== CREATE =====

            var h = new UnifiedOrders
            {
                Channel = dto.Channel,
                ShopId = dto.ShopId,
                SellerId = dto.SellerId,
                ExternalOrderId = dto.ExternalOrderId,
                ExternalOrderNo = string.IsNullOrWhiteSpace(dto.ExternalOrderNo) ? dto.ExternalOrderId : dto.ExternalOrderNo,
                OrderStatus = dto.OrderStatus,
                FulfillmentStatus = dto.FulfillmentStatus,
                PaymentStatus = dto.PaymentStatus,
                Currency = dto.Currency,
                SubtotalAmount = dto.SubtotalAmount,
                DiscountSellerAmount = dto.DiscountSellerAmount,
                DiscountPlatformAmount = dto.DiscountPlatformAmount,
                VoucherAmount = dto.VoucherAmount,
                ShippingFeeAmount = dto.ShippingFeeAmount,
                TaxAmount = dto.TaxAmount,
                OtherFeeAmount = dto.OtherFeeAmount,
                TotalAmount = dto.TotalAmount,
                PaidAmount = dto.PaidAmount,
                RefundAmount = dto.RefundAmount,
                PaymentMethod = dto.PaymentMethod,
                ShipmentProvider = dto.ShipmentProvider,
                ShipmentServiceCode = dto.ShipmentServiceCode,
                TrackingNo = dto.TrackingNo,
                WarehouseCode = dto.WarehouseCode,
                BuyerUserId = dto.BuyerUserId,
                BuyerUsername = dto.BuyerUsername,
                BuyerName = dto.BuyerName,
                BuyerPhone = dto.BuyerPhone,
                BuyerEmail = dto.BuyerEmail,
                ShipToAddressId = shipAddrId,
                BillToAddressId = billAddrId,
                CreatedTimeUtc = dto.CreatedTimeUtc?.UtcDateTime,
                UpdatedTimeUtc = dto.UpdatedTimeUtc?.UtcDateTime,
                PaidTimeUtc = dto.PaidTimeUtc?.UtcDateTime,
                CancelTimeUtc = dto.CancelTimeUtc?.UtcDateTime,
                ShippedTimeUtc = dto.ShippedTimeUtc?.UtcDateTime,
                DeliveredTimeUtc = dto.DeliveredTimeUtc?.UtcDateTime,
                CompletedTimeUtc = dto.CompletedTimeUtc?.UtcDateTime,
                NoteBuyer = dto.NoteBuyer,
                NoteSeller = dto.NoteSeller,
                SourceRawId = dto.SourceRawId,
                SourcePayloadHash = dto.SourcePayloadHash,
                IngestBatchNo = dto.IngestBatchNo
            };
            _db.Add(h);
            await _db.SaveChangesAsync(ct);

            // children
            if (dto.Items is not null)
            {
                foreach (var it in dto.Items)
                {
                    _db.Add(new UnifiedOrderItems
                    {
                        UnifiedOrderId = h.UnifiedOrderId,
                        ExternalItemId = it.ExternalItemId,
                        ProductName = it.ProductName,
                        VariationName = it.VariationName,
                        SellerSku = it.SellerSku,
                        PlatformSku = it.PlatformSku,
                        QtyOrdered = it.QtyOrdered,
                        QtyCanceled = it.QtyCanceled,
                        QtyShipped = it.QtyShipped,
                        UnitPrice = it.UnitPrice,
                        OriginalPrice = it.OriginalPrice,
                        DiscountSeller = it.DiscountSeller,
                        DiscountPlatform = it.DiscountPlatform,
                        TaxAmount = it.TaxAmount,
                        ShippingAlloc = it.ShippingAlloc,
                        LineTotal = it.LineTotal,
                        AttributesJson = it.Attributes is null ? null : JsonSerializer.Serialize(it.Attributes)
                    });
                }
            }
            if (dto.Payments is not null)
            {
                foreach (var p in dto.Payments)
                {
                    _db.Add(new UnifiedOrderPayments
                    {
                        UnifiedOrderId = h.UnifiedOrderId,
                        Method = p.Method,
                        ChannelTxnId = p.ChannelTxnId,
                        PaidAmount = p.PaidAmount,
                        Currency = p.Currency,
                        PaidTimeUtc = p.PaidTimeUtc?.UtcDateTime,
                        FeeAmount = p.FeeAmount,
                        FeeDetailsJson = p.FeeDetails is null ? null : JsonSerializer.Serialize(p.FeeDetails),
                        IsCOD = p.IsCOD
                    });
                }
            }
            if (dto.Shipments is not null)
            {
                foreach (var s in dto.Shipments)
                {
                    _db.Add(new UnifiedOrderShipments
                    {
                        UnifiedOrderId = h.UnifiedOrderId,
                        Provider = s.Provider,
                        ServiceCode = s.ServiceCode,
                        TrackingNo = s.TrackingNo,
                        Status = s.Status,
                        PickupTimeUtc = s.PickupTimeUtc?.UtcDateTime,
                        ShippedTimeUtc = s.ShippedTimeUtc?.UtcDateTime,
                        DeliveredTimeUtc = s.DeliveredTimeUtc?.UtcDateTime,
                        FirstMileCarrier = s.FirstMileCarrier,
                        LastMileCarrier = s.LastMileCarrier,
                        RawJson = s.Raw is null ? null : JsonSerializer.Serialize(s.Raw)
                    });
                }
            }

            await _db.SaveChangesAsync(ct);
            
            // POST-CHECK: Fix null ExternalOrderNo
            await _db.Database.ExecuteSqlAsync($@"
                UPDATE mdw.UnifiedOrders
                SET ExternalOrderNo = ExternalOrderId
                WHERE UnifiedOrderId = {h.UnifiedOrderId} AND (ExternalOrderNo IS NULL OR ExternalOrderNo = '')", ct);

            await tx.CommitAsync(ct);
            return h.UnifiedOrderId;
        }
        else
        {
            // ===== UPDATE (hash เปลี่ยน) =====
            if (shipAddrId.HasValue) existed.ShipToAddressId = shipAddrId;
            if (billAddrId.HasValue) existed.BillToAddressId = billAddrId;

            existed.ExternalOrderNo = string.IsNullOrWhiteSpace(dto.ExternalOrderNo) ? dto.ExternalOrderId : dto.ExternalOrderNo;

            if (existed.ExternalOrderNo.Length < 5 ) existed.ExternalOrderNo = dto.ExternalOrderId;

            existed.OrderStatus = dto.OrderStatus;
            existed.FulfillmentStatus = dto.FulfillmentStatus;
            existed.PaymentStatus = dto.PaymentStatus;
            existed.Currency = dto.Currency;
            existed.SubtotalAmount = dto.SubtotalAmount;
            existed.DiscountSellerAmount = dto.DiscountSellerAmount;
            existed.DiscountPlatformAmount = dto.DiscountPlatformAmount;
            existed.VoucherAmount = dto.VoucherAmount;
            existed.ShippingFeeAmount = dto.ShippingFeeAmount;
            existed.TaxAmount = dto.TaxAmount;
            existed.OtherFeeAmount = dto.OtherFeeAmount;
            existed.TotalAmount = dto.TotalAmount;
            existed.PaidAmount = dto.PaidAmount;
            existed.RefundAmount = dto.RefundAmount;
            existed.PaymentMethod = dto.PaymentMethod;
            existed.ShipmentProvider = dto.ShipmentProvider;
            existed.ShipmentServiceCode = dto.ShipmentServiceCode;
            existed.TrackingNo = dto.TrackingNo;
            existed.WarehouseCode = dto.WarehouseCode;
            existed.BuyerUserId = dto.BuyerUserId;
            existed.BuyerUsername = dto.BuyerUsername;
            existed.BuyerName = dto.BuyerName;
            existed.BuyerPhone = dto.BuyerPhone;
            existed.BuyerEmail = dto.BuyerEmail;

            existed.CreatedTimeUtc = dto.CreatedTimeUtc?.UtcDateTime;
            existed.UpdatedTimeUtc = dto.UpdatedTimeUtc?.UtcDateTime;
            existed.PaidTimeUtc = dto.PaidTimeUtc?.UtcDateTime;
            existed.CancelTimeUtc = dto.CancelTimeUtc?.UtcDateTime;
            existed.ShippedTimeUtc = dto.ShippedTimeUtc?.UtcDateTime;
            existed.DeliveredTimeUtc = dto.DeliveredTimeUtc?.UtcDateTime;
            existed.CompletedTimeUtc = dto.CompletedTimeUtc?.UtcDateTime;

            existed.NoteBuyer = dto.NoteBuyer;
            existed.NoteSeller = dto.NoteSeller;
            existed.SourceRawId = dto.SourceRawId;
            existed.SourcePayloadHash = dto.SourcePayloadHash;
            existed.IngestBatchNo = dto.IngestBatchNo;

            // replace children
            var oldItems = _db.UnifiedOrderItems.Where(x => x.UnifiedOrderId == existed.UnifiedOrderId);
            var oldPays = _db.UnifiedOrderPayments.Where(x => x.UnifiedOrderId == existed.UnifiedOrderId);
            var oldShips = _db.UnifiedOrderShipments.Where(x => x.UnifiedOrderId == existed.UnifiedOrderId);
            _db.RemoveRange(oldItems); _db.RemoveRange(oldPays); _db.RemoveRange(oldShips);
            await _db.SaveChangesAsync(ct);

            if (dto.Items is not null)
            {
                foreach (var it in dto.Items)
                {
                    _db.Add(new UnifiedOrderItems
                    {
                        UnifiedOrderId = existed.UnifiedOrderId,
                        ExternalItemId = it.ExternalItemId,
                        ProductName = it.ProductName,
                        VariationName = it.VariationName,
                        SellerSku = it.SellerSku,
                        PlatformSku = it.PlatformSku,
                        QtyOrdered = it.QtyOrdered,
                        QtyCanceled = it.QtyCanceled,
                        QtyShipped = it.QtyShipped,
                        UnitPrice = it.UnitPrice,
                        OriginalPrice = it.OriginalPrice,
                        DiscountSeller = it.DiscountSeller,
                        DiscountPlatform = it.DiscountPlatform,
                        TaxAmount = it.TaxAmount,
                        ShippingAlloc = it.ShippingAlloc,
                        LineTotal = it.LineTotal,
                        AttributesJson = it.Attributes is null ? null : JsonSerializer.Serialize(it.Attributes)
                    });
                }
            }
            if (dto.Payments is not null)
            {
                foreach (var p in dto.Payments)
                {
                    _db.Add(new UnifiedOrderPayments
                    {
                        UnifiedOrderId = existed.UnifiedOrderId,
                        Method = p.Method,
                        ChannelTxnId = p.ChannelTxnId,
                        PaidAmount = p.PaidAmount,
                        Currency = p.Currency,
                        PaidTimeUtc = p.PaidTimeUtc?.UtcDateTime,
                        FeeAmount = p.FeeAmount,
                        FeeDetailsJson = p.FeeDetails is null ? null : JsonSerializer.Serialize(p.FeeDetails),
                        IsCOD = p.IsCOD
                    });
                }
            }
            if (dto.Shipments is not null)
            {
                foreach (var s in dto.Shipments)
                {
                    _db.Add(new UnifiedOrderShipments
                    {
                        UnifiedOrderId = existed.UnifiedOrderId,
                        Provider = s.Provider,
                        ServiceCode = s.ServiceCode,
                        TrackingNo = s.TrackingNo,
                        Status = s.Status,
                        PickupTimeUtc = s.PickupTimeUtc?.UtcDateTime,
                        ShippedTimeUtc = s.ShippedTimeUtc?.UtcDateTime,
                        DeliveredTimeUtc = s.DeliveredTimeUtc?.UtcDateTime,
                        FirstMileCarrier = s.FirstMileCarrier,
                        LastMileCarrier = s.LastMileCarrier,
                        RawJson = s.Raw is null ? null : JsonSerializer.Serialize(s.Raw)
                    });
                }
            }

            await _db.SaveChangesAsync(ct);

            // POST-CHECK: Fix null ExternalOrderNo
            await _db.Database.ExecuteSqlAsync($@"
                UPDATE mdw.UnifiedOrders
                SET ExternalOrderNo = ExternalOrderId
                WHERE UnifiedOrderId = {existed.UnifiedOrderId} AND (ExternalOrderNo IS NULL OR ExternalOrderNo = '')", ct);
            
            await tx.CommitAsync(ct);
            return existed.UnifiedOrderId;
        }
    }

    // =========================
    // REQUIRED BY INTERFACE:
    // Upsert from RAW (Shopee/TikTok/Lazada)
    // =========================

    // >>>>>>> UPDATED: Shopee ใช้ Normalizer จริง <<<<<<<
    public async Task<NormalizeResult> UpsertFromShopeeRawAsync(
        long? shopId, string? sellerId, string rawJson, string? batchNo, CancellationToken ct)
    {
        // hash & external id
        var hash = ComputeHash(rawJson);
        string externalOrderId = ExtractShopeeExternalId(rawJson);

        // เช็ค outcome ล่วงหน้าจาก UnifiedOrders.SourcePayloadHash
        var existed = await _db.UnifiedOrders
            .Where(u => u.Channel == "Shopee" && u.ExternalOrderId == externalOrderId)
            .Select(u => new { u.UnifiedOrderId, u.SourcePayloadHash })
            .FirstOrDefaultAsync(ct);

        if (existed is not null &&
            existed.SourcePayloadHash is not null &&
            existed.SourcePayloadHash.SequenceEqual(hash))
        {
            // payload เดิมเป๊ะ → Unchanged
            return new NormalizeResult
            {
                Outcome = NormalizeOutcome.Unchanged,
                UnifiedOrderId = existed.UnifiedOrderId,
                ExternalOrderId = externalOrderId,
                RawHash = hash
            };
        }

        // Insert RAW (กันซ้ำด้วย hash)
        var rawId = await InsertRawAsync("Shopee", shopId, sellerId, externalOrderId, rawJson, batchNo, ct);

        // Normalize → DTO (เต็ม)
        using var doc = JsonDocument.Parse(rawJson);
        var dto = ShopeeNormalizer.Normalize(doc.RootElement, shopId, sellerId, rawId, rawJson, batchNo);

        // Upsert
        var unifiedId = await UpsertAsync(dto, ct);

        return new NormalizeResult
        {
            Outcome = existed is null ? NormalizeOutcome.Created : NormalizeOutcome.Updated,
            UnifiedOrderId = unifiedId,
            ExternalOrderId = externalOrderId,
            RawHash = hash
        };
    }

    public async Task UpsertShopeeEscrowAsync(string orderSn, string escrowJson, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orderSn))
            throw new ArgumentException("orderSn is required", nameof(orderSn));
        if (string.IsNullOrWhiteSpace(escrowJson))
            throw new ArgumentException("escrowJson is required", nameof(escrowJson));

        using var doc = JsonDocument.Parse(escrowJson);
        var root = doc.RootElement;
        var income = ExtractShopeeOrderIncome(root);
        var buyerPayment = ExtractShopeeBuyerPaymentInfo(root);

        var order = await _db.UnifiedOrders
            .FirstOrDefaultAsync(x => x.Channel == "Shopee" && x.ExternalOrderId == orderSn, ct)
            ?? throw new InvalidOperationException($"Shopee order not found in UnifiedOrders: {orderSn}");

        order.PayloadEscrowJson = escrowJson;
        order.EscrowAmount = GetDecimal(income, "escrow_amount");
        order.BuyerPaidShippingFee = GetDecimal(income, "buyer_paid_shipping_fee");
        order.ActualShippingFee = GetDecimal(income, "actual_shipping_fee");
        order.PlatformShippingRebate = GetDecimal(income, "shopee_shipping_rebate");

        var shippingFeeSst = GetDecimal(buyerPayment, "shipping_fee_sst_amount");
        if (shippingFeeSst.HasValue)
            order.ShippingFeeAmount = shippingFeeSst;
        order.CommissionFee = FeeAmount(GetDecimal(income, "commission_fee"));
        order.ServiceFee = FeeAmount(GetDecimal(income, "service_fee"));
        order.PlatformFee = FeeAmount(GetDecimalAnyNonZeroFirst(income, "platform_fee", "seller_platform_fee", "infrastructure_fee", "campaign_fee", "seller_order_processing_fee"));
        order.PaymentTransactionFee = FeeAmount(GetDecimalAny(income, "seller_transaction_fee", "credit_card_transaction_fee"));
        order.AmsCommissionFee = FeeAmount(GetDecimalAny(income, "order_ams_commission_fee", "ams_commission_fee", "ams_affiliate_commission_fee", "affiliate_commission_fee")
            ?? SumItemDecimals(income, "ams_commission_fee"));
        order.SellerVoucherCode = GetStringOrCsv(income, "seller_voucher_code");

        var sellerDiscount = Abs(GetDecimal(buyerPayment, "seller_voucher"))
            ?? SumDecimals(GetDecimal(income, "voucher_from_seller"), GetDecimal(income, "seller_discount"))
            ?? SumItemDecimals(income, "discount_from_voucher_seller");
        var shopeeDiscount = Abs(GetDecimal(buyerPayment, "shopee_voucher"))
            ?? SumDecimals(GetDecimal(income, "voucher_from_shopee"), GetDecimal(income, "shopee_discount"), GetDecimal(income, "original_shopee_discount"))
            ?? SumItemDecimals(income, "discount_from_voucher_shopee");
        var voucherAmount = SumDecimals(Abs(GetDecimal(buyerPayment, "seller_voucher")), Abs(GetDecimal(buyerPayment, "shopee_voucher")))
            ?? GetDecimal(income, "voucher_amount")
            ?? SumDecimals(GetDecimal(income, "voucher_from_seller"), GetDecimal(income, "voucher_from_shopee"));

        if (sellerDiscount.HasValue)
            order.DiscountSellerAmount = sellerDiscount;
        if (shopeeDiscount.HasValue)
            order.DiscountPlatformAmount = shopeeDiscount;
        if (voucherAmount.HasValue)
            order.VoucherAmount = voucherAmount;

        await _db.SaveChangesAsync(ct);
    }

    public async Task UpsertTiktokEscrowAsync(string orderId, string escrowJson, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            throw new ArgumentException("orderId is required", nameof(orderId));
        if (string.IsNullOrWhiteSpace(escrowJson))
            throw new ArgumentException("escrowJson is required", nameof(escrowJson));

        using var doc = JsonDocument.Parse(escrowJson);
        var root = doc.RootElement;

        // Check response code if present
        if (root.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number && codeEl.GetInt32() != 0)
        {
            throw new InvalidOperationException($"TikTok API returned error code {codeEl.GetInt32()}");
        }

        // Locate statement_transactions list
        JsonElement transactionsList = default;
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            if (data.TryGetProperty("statement_transactions", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                transactionsList = list;
            }
        }

        var order = await _db.UnifiedOrders
            .FirstOrDefaultAsync(x => x.Channel == "TikTok" && x.ExternalOrderId == orderId, ct)
            ?? await _db.UnifiedOrders
            .FirstOrDefaultAsync(x => x.Channel == "TikTok" && x.ExternalOrderNo == orderId, ct)
            ?? throw new InvalidOperationException($"TikTok order not found in UnifiedOrders: {orderId}");

        order.PayloadEscrowJson = escrowJson;

        if (transactionsList.ValueKind == JsonValueKind.Array && transactionsList.GetArrayLength() > 0)
        {
            decimal totalSettlement = 0;
            decimal totalBuyerShipping = 0;
            decimal totalPlatformDiscount = 0;
            decimal totalFee = 0;
            decimal totalRevenue = 0;
            decimal totalAfterSellerDiscountSubtotal = 0;

            foreach (var tx in transactionsList.EnumerateArray())
            {
                totalSettlement += GetDecimal(tx, "settlement_amount") ?? 0;
                totalBuyerShipping += GetDecimal(tx, "customer_shipping_fee_amount") ?? 0;
                totalPlatformDiscount += GetDecimal(tx, "platform_discount_amount") ?? 0;
                totalFee += GetDecimal(tx, "fee_amount") ?? 0;
                totalRevenue += GetDecimal(tx, "revenue_amount") ?? 0;
                totalAfterSellerDiscountSubtotal += GetDecimal(tx, "after_seller_discounts_subtotal_amount") ?? 0;
            }

            order.EscrowAmount = totalSettlement;
            order.BuyerPaidShippingFee = totalBuyerShipping;
            order.PlatformFee = totalFee; // map total fee to PlatformFee
            order.DiscountPlatformAmount = totalPlatformDiscount;

            // If there's a difference between revenue and after_seller_discounts_subtotal, that is the seller discount
            if (totalRevenue > totalAfterSellerDiscountSubtotal)
            {
                order.DiscountSellerAmount = totalRevenue - totalAfterSellerDiscountSubtotal;
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private static JsonElement ExtractShopeeOrderIncome(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("response", out var response) &&
            response.ValueKind == JsonValueKind.Object &&
            response.TryGetProperty("order_income", out var orderIncome) &&
            orderIncome.ValueKind == JsonValueKind.Object)
        {
            return orderIncome;
        }

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("order_income", out var directIncome) &&
            directIncome.ValueKind == JsonValueKind.Object)
        {
            return directIncome;
        }

        throw new ArgumentException("Shopee escrow payload missing response.order_income");
    }

    private static JsonElement? ExtractShopeeBuyerPaymentInfo(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("response", out var response) &&
            response.ValueKind == JsonValueKind.Object &&
            response.TryGetProperty("buyer_payment_info", out var buyerPayment) &&
            buyerPayment.ValueKind == JsonValueKind.Object)
        {
            return buyerPayment;
        }

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("buyer_payment_info", out var directBuyerPayment) &&
            directBuyerPayment.ValueKind == JsonValueKind.Object)
        {
            return directBuyerPayment;
        }

        return null;
    }

    private static decimal? GetDecimal(JsonElement? root, string key)
    {
        if (root is null) return null;
        return GetDecimal(root.Value, key);
    }

    private static decimal? GetDecimal(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number) return value.GetDecimal();
        if (value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(value.GetString(), out var parsed)) return parsed;
        return null;
    }

    private static decimal? Abs(decimal? value)
        => value.HasValue ? Math.Abs(value.Value) : null;

    private static decimal? FeeAmount(decimal? value)
        => Abs(value);

    private static decimal? SumItemDecimals(JsonElement root, string key)
    {
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return null;

        decimal total = 0m;
        var found = false;

        foreach (var item in items.EnumerateArray())
        {
            var value = GetDecimal(item, key);
            if (!value.HasValue) continue;
            total += value.Value;
            found = true;
        }

        return found ? total : null;
    }

    private static decimal? GetDecimalAny(JsonElement root, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = GetDecimal(root, key);
            if (value.HasValue) return value;
        }

        return null;
    }

    private static decimal? GetDecimalAnyNonZeroFirst(JsonElement root, params string[] keys)
    {
        decimal? zeroValue = null;

        foreach (var key in keys)
        {
            var value = GetDecimal(root, key);
            if (!value.HasValue) continue;
            if (value.Value != 0m) return value;
            zeroValue ??= value;
        }

        return zeroValue;
    }

    private static decimal? SumDecimals(params decimal?[] values)
    {
        decimal total = 0m;
        var found = false;

        foreach (var value in values)
        {
            if (!value.HasValue) continue;
            total += value.Value;
            found = true;
        }

        return found ? total : null;
    }

    private static string? GetStringOrCsv(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var value)) return null;
        if (value.ValueKind == JsonValueKind.String) return value.GetString();
        if (value.ValueKind == JsonValueKind.Array)
        {
            var values = value.EnumerateArray()
                .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x));
            return string.Join(",", values);
        }
        return value.ToString();
    }

    // (ยังคง generic minimal สำหรับแพลตฟอร์มอื่น จนกว่าจะทำ normalizer ของมัน)
    public async Task<NormalizeResult> UpsertFromTiktokRawAsync(long? shopId, string? sellerId, string rawJson, string? batchNo, CancellationToken ct)
    {
        // 1. Extract ID & Hash
        var hash = ComputeHash(rawJson);
        // Note: ExtractTiktokExternalId may need to handle numbers if TikTok sends them. 
        // Currently it requires String. If it fails, we might miss orders.
        // But assuming it works or we fix ExtractTiktokExternalId logic:
        string externalOrderId = ExtractTiktokExternalId(rawJson);

        // 2. Check if identical payload exists
        var existed = await _db.UnifiedOrders
            .Where(u => u.Channel == "TikTok" && u.ExternalOrderId == externalOrderId)
            .Select(u => new { u.UnifiedOrderId, u.SourcePayloadHash })
            .FirstOrDefaultAsync(ct);

        if (existed is not null &&
            existed.SourcePayloadHash is not null &&
            existed.SourcePayloadHash.SequenceEqual(hash))
        {
            return new NormalizeResult
            {
                Outcome = NormalizeOutcome.Unchanged,
                UnifiedOrderId = existed.UnifiedOrderId,
                ExternalOrderId = externalOrderId,
                RawHash = hash
            };
        }

        // 3. Insert RAW
        var rawId = await InsertRawAsync("TikTok", shopId, sellerId, externalOrderId, rawJson, batchNo, ct);

        // 4. Normalize
        using var doc = JsonDocument.Parse(rawJson);
        // Normalize using proper TikTok logic
        var dto = TikTokNormalizer.Normalize(doc.RootElement, shopId, sellerId, rawId, rawJson, batchNo);

        // 5. Upsert Unified
        var unifiedId = await UpsertAsync(dto, ct);

        return new NormalizeResult
        {
            Outcome = existed is null ? NormalizeOutcome.Created : NormalizeOutcome.Updated,
            UnifiedOrderId = unifiedId,
            ExternalOrderId = externalOrderId,
            RawHash = hash
        };
    }

    public Task<NormalizeResult> UpsertFromLazadaRawAsync(long? shopId, string? sellerId, string rawJson, string? batchNo, CancellationToken ct)
        => UpsertFromRawGenericAsync(
            channel: "Lazada",
            shopId: shopId,
            sellerId: sellerId,
            externalOrderId: ExtractLazadaExternalId(rawJson),
            rawJson: rawJson,
            batchNo: batchNo,
            ct: ct
        );

    // ---- Generic flow (fallback) ----
    private async Task<NormalizeResult> UpsertFromRawGenericAsync(
        string channel,
        long? shopId,
        string? sellerId,
        string externalOrderId,
        string rawJson,
        string? batchNo,
        CancellationToken ct)
    {
        var hash = ComputeHash(rawJson);

        var existed = await _db.UnifiedOrders
            .Where(u => u.Channel == channel && u.ExternalOrderId == externalOrderId)
            .Select(u => new { u.UnifiedOrderId, u.SourcePayloadHash })
            .FirstOrDefaultAsync(ct);

        if (existed is not null &&
            existed.SourcePayloadHash is not null &&
            existed.SourcePayloadHash.SequenceEqual(hash))
        {
            return new NormalizeResult
            {
                Outcome = NormalizeOutcome.Unchanged,
                UnifiedOrderId = existed.UnifiedOrderId,
                ExternalOrderId = externalOrderId,
                RawHash = hash
            };
        }

        var rawId = await InsertRawAsync(channel, shopId, sellerId, externalOrderId, rawJson, batchNo, ct);

        var dto = BuildMinimalDto(channel, shopId, sellerId, externalOrderId, rawId, hash, batchNo);
        var unifiedId = await UpsertAsync(dto, ct);

        return new NormalizeResult
        {
            Outcome = (existed is null ? NormalizeOutcome.Created : NormalizeOutcome.Updated),
            UnifiedOrderId = unifiedId,
            ExternalOrderId = externalOrderId,
            RawHash = hash
        };
    }

    // =========================
    // Helpers
    // =========================

    private static byte[] ComputeHash(string rawJson)
        => System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(rawJson));

    private static string ExtractShopeeExternalId(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var r = doc.RootElement;
        if (r.TryGetProperty("order_sn", out var a) && a.ValueKind == JsonValueKind.String) return a.GetString()!;
        if (r.TryGetProperty("orderSn", out var b) && b.ValueKind == JsonValueKind.String) return b.GetString()!;
        // กรณี payload ถูกห่อชั้นนอก ให้ลอง response.order_list[0]
        if (r.TryGetProperty("response", out var resp) && resp.ValueKind == JsonValueKind.Object)
        {
            if (resp.TryGetProperty("order_list", out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
            {
                var o0 = arr[0];
                if (o0.TryGetProperty("order_sn", out var osn) && osn.ValueKind == JsonValueKind.String)
                    return osn.GetString()!;
            }
        }
        throw new ArgumentException("Shopee raw: missing order_sn");
    }

    private static string ExtractTiktokExternalId(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var r = doc.RootElement;
        string? id =
            (r.TryGetProperty("id", out var z) && (z.ValueKind == JsonValueKind.String || z.ValueKind == JsonValueKind.Number)) ? z.ToString() :
            (r.TryGetProperty("order_id", out var a) && (a.ValueKind == JsonValueKind.String || a.ValueKind == JsonValueKind.Number)) ? a.ToString() :
            (r.TryGetProperty("orderId", out var b) && (b.ValueKind == JsonValueKind.String || b.ValueKind == JsonValueKind.Number)) ? b.ToString() :
            (r.TryGetProperty("order_number", out var c) && (c.ValueKind == JsonValueKind.String || c.ValueKind == JsonValueKind.Number)) ? c.ToString() :
            (r.TryGetProperty("orderNumber", out var d) && (d.ValueKind == JsonValueKind.String || d.ValueKind == JsonValueKind.Number)) ? d.ToString() :
            null;
        return id ?? throw new ArgumentException("TikTok raw: missing order id");
    }

    private static string ExtractLazadaExternalId(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var r = doc.RootElement;
        if (r.TryGetProperty("order_id", out var a)) return a.ToString();
        if (r.TryGetProperty("orderId", out var b)) return b.ToString();
        if (r.TryGetProperty("trade_order_id", out var c)) return c.ToString();
        throw new ArgumentException("Lazada raw: missing order id");
    }

    private static UnifiedOrderDto BuildMinimalDto(
        string channel,
        long? shopId,
        string? sellerId,
        string externalOrderId,
        long sourceRawId,
        byte[] sourcePayloadHash,
        string? batchNo)
    {
        return new UnifiedOrderDto
        {
            Channel = channel,
            ShopId = shopId,
            SellerId = sellerId,
            ExternalOrderId = externalOrderId,
            ExternalOrderNo = externalOrderId,
            Items = new List<UnifiedOrderItem>(),
            Payments = new List<UnifiedPayment>(),
            Shipments = new List<UnifiedShipment>(),
            SourceRawId = sourceRawId,
            SourcePayloadHash = sourcePayloadHash,
            IngestBatchNo = batchNo
        };
    }
}
