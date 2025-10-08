using System.Text;
using System.Text.Json;
using MDWAPI.Data;
using MDWAPI.DTOs;
using MDWAPI.Entities;
using MDWAPI.Models;
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
                Type = "SHIPPING",
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
                Type = "BILLING",
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
                ExternalOrderNo = dto.ExternalOrderNo,
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
            await tx.CommitAsync(ct);
            return h.UnifiedOrderId;
        }
        else
        {
            // ===== UPDATE (hash เปลี่ยน) =====
            if (shipAddrId.HasValue) existed.ShipToAddressId = shipAddrId;
            if (billAddrId.HasValue) existed.BillToAddressId = billAddrId;

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
            await tx.CommitAsync(ct);
            return existed.UnifiedOrderId;
        }
    }

    // =========================
    // REQUIRED BY INTERFACE:
    // Upsert from RAW (Shopee/TikTok/Lazada)
    // =========================

    public Task<NormalizeResult> UpsertFromShopeeRawAsync(long? shopId, string? sellerId, string rawJson, string? batchNo, CancellationToken ct)
        => UpsertFromRawGenericAsync(
            channel: "Shopee",
            shopId: shopId,
            sellerId: sellerId,
            externalOrderId: ExtractShopeeExternalId(rawJson),
            rawJson: rawJson,
            batchNo: batchNo,
            ct: ct
        );

    public Task<NormalizeResult> UpsertFromTiktokRawAsync(long? shopId, string? sellerId, string rawJson, string? batchNo, CancellationToken ct)
        => UpsertFromRawGenericAsync(
            channel: "TikTok",
            shopId: shopId,
            sellerId: sellerId,
            externalOrderId: ExtractTiktokExternalId(rawJson),
            rawJson: rawJson,
            batchNo: batchNo,
            ct: ct
        );

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

    // ---- Generic flow for 3 platforms ----
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

        // ตรวจสถานะก่อน เพื่อสรุป outcome ที่ถูกต้อง
        var existed = await _db.UnifiedOrders
            .Where(u => u.Channel == channel && u.ExternalOrderId == externalOrderId)
            .Select(u => new { u.UnifiedOrderId, u.SourcePayloadHash })
            .FirstOrDefaultAsync(ct);

        if (existed is not null &&
            existed.SourcePayloadHash is not null &&
            existed.SourcePayloadHash.SequenceEqual(hash))
        {
            // hash เดิม → ไม่ต้องทำอะไร
            return new NormalizeResult
            {
                Outcome = NormalizeOutcome.Unchanged,
                UnifiedOrderId = existed.UnifiedOrderId,
                ExternalOrderId = externalOrderId,
                RawHash = hash
            };
        }

        // Insert/Reuse RAW แถว (กันซ้ำด้วย hash)
        var rawId = await InsertRawAsync(channel, shopId, sellerId, externalOrderId, rawJson, batchNo, ct);

        // สร้าง DTO แบบ minimal (ถ้ายังไม่มี mapper รายละเอียด)
        var dto = BuildMinimalDto(channel, shopId, sellerId, externalOrderId, rawId, hash, batchNo);

        // Upsert ลง unified
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
        throw new ArgumentException("Shopee raw: missing order_sn/orderSn");
    }

    private static string ExtractTiktokExternalId(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var r = doc.RootElement;
        string? id =
            (r.TryGetProperty("order_id", out var a) && a.ValueKind == JsonValueKind.String) ? a.GetString() :
            (r.TryGetProperty("orderId", out var b) && b.ValueKind == JsonValueKind.String) ? b.GetString() :
            (r.TryGetProperty("order_number", out var c) && c.ValueKind == JsonValueKind.String) ? c.GetString() :
            (r.TryGetProperty("orderNumber", out var d) && d.ValueKind == JsonValueKind.String) ? d.GetString() :
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
            // ค่าอื่น ๆ ให้เป็น null ได้ — mapper รายละเอียดสามารถเติมในชั้นที่สูงกว่าได้
            Items = new List<UnifiedOrderItem>(),
            Payments = new List<UnifiedPayment>(),
            Shipments = new List<UnifiedShipment>(),
            SourceRawId = sourceRawId,
            SourcePayloadHash = sourcePayloadHash,
            IngestBatchNo = batchNo
        };
    }
}
