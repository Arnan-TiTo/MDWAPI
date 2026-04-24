using System.Data;
using System.Text.Json;
using Dapper;
using MDWAPI.Data;
using MDWAPI.Entities;
using MDWAPI.Services;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Repos;

public interface IUnifiedOrderLabelDocumentRepo
{
    /// <summary>
    /// Upsert from Shopee get_shipping_document_data_info response.
    /// orderSn and packageNumber must be supplied by caller (not in response body).
    /// response = the "response" JsonElement (contains shipping_document_info + recipient_address_info).
    /// </summary>
    Task UpsertAsync(string platform, long shopId, string orderSn, string? packageNumber,
        JsonElement response, CancellationToken ct = default);

    Task<List<UnifiedOrderLabelDocument>> GetByOrderSnAsync(string platform, long shopId,
        string orderSn, CancellationToken ct = default);
}

public class UnifiedOrderLabelDocumentRepo : IUnifiedOrderLabelDocumentRepo
{
    private readonly AppDbContext _db;
    private readonly OcrService _ocr;

    public UnifiedOrderLabelDocumentRepo(AppDbContext db, OcrService ocr)
    {
        _db = db;
        _ocr = ocr;
    }

    private static string? Str(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return v.GetString();
    }

    private static string? NestedStr(JsonElement el, string obj, string key)
    {
        if (!el.TryGetProperty(obj, out var nested) || nested.ValueKind != JsonValueKind.Object) return null;
        return Str(nested, key);
    }

    private static decimal? ParseDecimal(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDecimal();
        // Shopee sometimes returns number as string e.g. "221"
        if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), out var d)) return d;
        return null;
    }

    public async Task UpsertAsync(string platform, long shopId, string orderSn, string? packageNumber,
        JsonElement response, CancellationToken ct = default)
    {
        var conn = _db.Database.GetDbConnection();
        var needClose = conn.State != ConnectionState.Open;
        if (needClose) await conn.OpenAsync(ct);

        try
        {
            // ── shipping_document_info ──────────────────────────────────────────
            JsonElement sdi = default;
            bool hasSdi = response.TryGetProperty("shipping_document_info", out sdi);

            string? trackingNumber = hasSdi ? (Str(sdi, "tracking_number") ?? Str(sdi, "shopee_tracking_number")) : null;
            string? logisticsChannelName = hasSdi ? Str(sdi, "shipping_carrier") : null;
            decimal? codAmount = hasSdi ? ParseDecimal(sdi, "cod_amount") : null;
            int? orderWeight = null;
            if (hasSdi && sdi.TryGetProperty("order_weight", out var ow) && ow.ValueKind == JsonValueKind.Number)
                orderWeight = ow.GetInt32();

            // Sort codes — try recipient_sort_code first
            string? asSortCode = null, cpSortCode = null, fmSortCode = null;
            if (hasSdi && sdi.TryGetProperty("recipient_sort_code", out var rsc) && rsc.ValueKind == JsonValueKind.Object)
            {
                asSortCode = Str(rsc, "first_recipient_sort_code");
                cpSortCode = Str(rsc, "second_recipient_sort_code");
                fmSortCode = Str(rsc, "third_recipient_sort_code");
            }

            // QR code plain text from third_party_logistic_info.qrcode
            string? qrCodeData = hasSdi ? NestedStr(sdi, "third_party_logistic_info", "qrcode") : null;

            // Raw JSON of shipping_document_info (no images to strip here — all text)
            string? rawJson = hasSdi ? sdi.GetRawText() : null;

            // ── recipient_address_info ─────────────────────────────────────────
            string? recipientAddrRaw = null, recipientAddrDecoded = null;
            string? recipientName = null, recipientPhone = null, recipientFullAddress = null;
            string? recipientTown = null, recipientDistrict = null, recipientCity = null;
            string? recipientState = null, recipientRegion = null, recipientZipcode = null;

            if (response.TryGetProperty("recipient_address_info", out var rai) && rai.ValueKind == JsonValueKind.Array)
            {
                recipientAddrRaw = rai.GetRawText();
                var decoded = DecodeAddressInfoToDict(rai);
                recipientAddrDecoded = DictToJson(decoded);

                decoded.TryGetValue("name", out recipientName);
                decoded.TryGetValue("phone", out recipientPhone);
                decoded.TryGetValue("full_address", out recipientFullAddress);
                decoded.TryGetValue("town", out recipientTown);
                decoded.TryGetValue("district", out recipientDistrict);
                decoded.TryGetValue("city", out recipientCity);
                decoded.TryGetValue("state", out recipientState);
                decoded.TryGetValue("region", out recipientRegion);
                decoded.TryGetValue("zipcode", out recipientZipcode);
            }

            // ── sender_address_info (ถ้ามี) ────────────────────────────────────
            string? senderAddrRaw = null, senderAddrDecoded = null;
            string? senderName = null, senderPhone = null, senderFullAddress = null, senderZipcode = null;

            if (response.TryGetProperty("sender_address_info", out var sai) && sai.ValueKind == JsonValueKind.Array)
            {
                senderAddrRaw = sai.GetRawText();
                var decoded = DecodeAddressInfoToDict(sai);
                senderAddrDecoded = DictToJson(decoded);

                decoded.TryGetValue("name", out senderName);
                decoded.TryGetValue("phone", out senderPhone);
                decoded.TryGetValue("full_address", out senderFullAddress);
                decoded.TryGetValue("zipcode", out senderZipcode);
            }

            // ── MERGE ──────────────────────────────────────────────────────────
            const string sql = @"
MERGE mdw.UnifiedOrderLabelDocument AS tgt
USING (SELECT @Platform AS Platform, @ShopId AS ShopId, @OrderSn AS OrderSn,
              NULLIF(@PackageNumber,'') AS PackageNumber) AS src
ON  tgt.Platform      = src.Platform
AND tgt.ShopId        = src.ShopId
AND tgt.OrderSn       = src.OrderSn
AND (tgt.PackageNumber = src.PackageNumber OR (tgt.PackageNumber IS NULL AND src.PackageNumber IS NULL))
WHEN MATCHED THEN UPDATE SET
    TrackingNumber             = @TrackingNumber,
    LogisticsChannelName       = @LogisticsChannelName,
    CodAmount                  = @CodAmount,
    ParcelChargeableWeightGram = @ParcelChargeableWeightGram,
    AsSortCode                 = @AsSortCode,
    CpSortCode                 = @CpSortCode,
    FmSortCode                 = @FmSortCode,
    QrCodeData                 = @QrCodeData,
    RawJson                    = @RawJson,
    RecipientName              = @RecipientName,
    RecipientPhone             = @RecipientPhone,
    RecipientFullAddress       = @RecipientFullAddress,
    RecipientTown              = @RecipientTown,
    RecipientDistrict          = @RecipientDistrict,
    RecipientCity              = @RecipientCity,
    RecipientState             = @RecipientState,
    RecipientRegion            = @RecipientRegion,
    RecipientZipcode           = @RecipientZipcode,
    RecipientAddrRawJson       = @RecipientAddrRawJson,
    RecipientAddrDecodedJson   = @RecipientAddrDecodedJson,
    SenderName                 = @SenderName,
    SenderPhone                = @SenderPhone,
    SenderFullAddress          = @SenderFullAddress,
    SenderZipcode              = @SenderZipcode,
    SenderAddrRawJson          = @SenderAddrRawJson,
    SenderAddrDecodedJson      = @SenderAddrDecodedJson,
    UpdatedAt                  = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT (
    Platform, ShopId, OrderSn, PackageNumber,
    TrackingNumber, LogisticsChannelName, CodAmount, ParcelChargeableWeightGram,
    AsSortCode, CpSortCode, FmSortCode, QrCodeData, RawJson,
    RecipientName, RecipientPhone, RecipientFullAddress,
    RecipientTown, RecipientDistrict, RecipientCity, RecipientState, RecipientRegion, RecipientZipcode,
    RecipientAddrRawJson, RecipientAddrDecodedJson,
    SenderName, SenderPhone, SenderFullAddress, SenderZipcode,
    SenderAddrRawJson, SenderAddrDecodedJson,
    FetchedAt
) VALUES (
    @Platform, @ShopId, @OrderSn, NULLIF(@PackageNumber,''),
    @TrackingNumber, @LogisticsChannelName, @CodAmount, @ParcelChargeableWeightGram,
    @AsSortCode, @CpSortCode, @FmSortCode, @QrCodeData, @RawJson,
    @RecipientName, @RecipientPhone, @RecipientFullAddress,
    @RecipientTown, @RecipientDistrict, @RecipientCity, @RecipientState, @RecipientRegion, @RecipientZipcode,
    @RecipientAddrRawJson, @RecipientAddrDecodedJson,
    @SenderName, @SenderPhone, @SenderFullAddress, @SenderZipcode,
    @SenderAddrRawJson, @SenderAddrDecodedJson,
    SYSUTCDATETIME()
);";

            await conn.ExecuteAsync(sql, new
            {
                Platform = platform,
                ShopId = shopId,
                OrderSn = orderSn,
                PackageNumber = packageNumber,
                TrackingNumber = trackingNumber,
                LogisticsChannelName = logisticsChannelName,
                CodAmount = codAmount,
                ParcelChargeableWeightGram = orderWeight,
                AsSortCode = asSortCode,
                CpSortCode = cpSortCode,
                FmSortCode = fmSortCode,
                QrCodeData = qrCodeData,
                RawJson = rawJson,
                RecipientName = recipientName,
                RecipientPhone = recipientPhone,
                RecipientFullAddress = recipientFullAddress,
                RecipientTown = recipientTown,
                RecipientDistrict = recipientDistrict,
                RecipientCity = recipientCity,
                RecipientState = recipientState,
                RecipientRegion = recipientRegion,
                RecipientZipcode = recipientZipcode,
                RecipientAddrRawJson = recipientAddrRaw,
                RecipientAddrDecodedJson = recipientAddrDecoded,
                SenderName = senderName,
                SenderPhone = senderPhone,
                SenderFullAddress = senderFullAddress,
                SenderZipcode = senderZipcode,
                SenderAddrRawJson = senderAddrRaw,
                SenderAddrDecodedJson = senderAddrDecoded
            });
        }
        finally
        {
            if (needClose) await conn.CloseAsync();
        }
    }

    public async Task<List<UnifiedOrderLabelDocument>> GetByOrderSnAsync(
        string platform, long shopId, string orderSn, CancellationToken ct = default)
    {
        return await _db.UnifiedOrderLabelDocuments
            .Where(x => x.Platform == platform && x.ShopId == shopId && x.OrderSn == orderSn)
            .OrderBy(x => x.Id)
            .ToListAsync(ct);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Decode recipient_address_info / sender_address_info array to dictionary {key → decoded_text}.
    /// Items with null image are skipped.
    /// </summary>
    private Dictionary<string, string> DecodeAddressInfoToDict(JsonElement array)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in array.EnumerateArray())
        {
            if (!item.TryGetProperty("key", out var keyEl)) continue;
            var key = keyEl.GetString();
            if (string.IsNullOrEmpty(key)) continue;

            if (!item.TryGetProperty("image", out var imgEl) || imgEl.ValueKind == JsonValueKind.Null) continue;
            var imgRaw = imgEl.GetString();
            if (string.IsNullOrEmpty(imgRaw)) continue;

            var decoded = _ocr.OcrDataUrl(imgRaw);
            if (!string.IsNullOrEmpty(decoded))
                result[key] = decoded;
        }

        return result;
    }

    private static string DictToJson(Dictionary<string, string> dict)
    {
        using var ms = new System.IO.MemoryStream();
        using var w = new Utf8JsonWriter(ms);
        w.WriteStartObject();
        foreach (var kv in dict)
            w.WriteString(kv.Key, kv.Value);
        w.WriteEndObject();
        w.Flush();
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }
}
