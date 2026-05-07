using System.Text.Json;
using MDWAPI.Common;
using MDWAPI.Models;
using MDWAPI.Repos;
using MDWAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace MDWAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/market/logistics")]
public class MarketplaceLogisticsController : ControllerBase
{
    private readonly ShopeeLogisticsService _shopee;
    private readonly LazadaLogisticsService _lazada;
    private readonly TiktokLogisticsService _tiktok;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IShopRepo _shopRepo;
    private readonly IPartnerRepo _partnerRepo;
    private readonly IChannelTokenRepo _chanRepo;
    private readonly IUnifiedOrderLabelDocumentRepo _labelDocRepo;
    private readonly OcrService _ocr;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MarketplaceLogisticsController> _log;

    public MarketplaceLogisticsController(
        ShopeeLogisticsService shopee,
        LazadaLogisticsService lazada,
        TiktokLogisticsService tiktok,
        IHttpClientFactory httpFactory,
        IShopRepo shopRepo,
        IPartnerRepo partnerRepo,
        IChannelTokenRepo chanRepo,
        IUnifiedOrderLabelDocumentRepo labelDocRepo,
        OcrService ocr,
        IMemoryCache cache,
        ILogger<MarketplaceLogisticsController> log)
    {
        _shopee = shopee;
        _lazada = lazada;
        _tiktok = tiktok;
        _httpFactory = httpFactory;
        _shopRepo = shopRepo;
        _partnerRepo = partnerRepo;
        _chanRepo = chanRepo;
        _labelDocRepo = labelDocRepo;
        _ocr = ocr;
        _cache = cache;
        _log = log;
    }

    [HttpGet("tracking")]
    public async Task<IActionResult> GetTracking(
        [FromQuery] Platform platform,
        [FromQuery] long shopId,
        [FromQuery] string refId,
        CancellationToken ct)
    {
        await RefreshTokenIfNeededAsync(platform.ToString(), shopId, ct);

        switch (platform)
        {
            case Platform.Shopee:
                {
                    var json = await _shopee.GetTrackingNumberAsync(shopId, refId, ct: ct);
                    return Content(json, "application/json");
                }
            case Platform.Lazada:
                {
                    var json = await _lazada.GetTrackingAsync(
                        sellerId: shopId.ToString(),
                        parameters: new() { ["waybill"] = refId },
                        ct: ct);
                    return Content(json, "application/json");
                }
            case Platform.TikTok:
                {
                    var json = await _tiktok.GetShippingInfoAsync(
                        accountIdBig: null,
                        accountIdStr: shopId.ToString(),
                        orderId: refId,
                        ct: ct);
                    return Content(json, "application/json");
                }
            default:
                return BadRequest("Unsupported platform");
        }
    }

    [HttpPost("ship")]
    public async Task<IActionResult> Ship(
        [FromQuery] Platform platform,
        [FromQuery] long shopId,
        [FromBody] JsonElement body,
        CancellationToken ct)
    {
        await RefreshTokenIfNeededAsync(platform.ToString(), shopId, ct);

        switch (platform)
        {
            case Platform.Shopee:
                {
                    var json = await _shopee.ShipOrderAsync(shopId, body, ct);
                    return Content(json, "application/json");
                }
            case Platform.Lazada:
                {
                    var json = await _lazada.ShipOrderAsync(
                        sellerId: shopId.ToString(),
                        body: body,
                        ct: ct);
                    return Content(json, "application/json");
                }
            case Platform.TikTok:
                {
                    var json = await _tiktok.ConfirmShipAsync(
                        accountIdBig: null,
                        accountIdStr: shopId.ToString(),
                        body: body,
                        ct: ct);
                    return Content(json, "application/json");
                }
            default:
                return BadRequest("Unsupported platform");
        }
    }

    [HttpPost("label/download")]
    public async Task<IActionResult> DownloadLabel(
        [FromQuery] Platform platform,
        [FromQuery] long shopId,
        [FromBody] LabelDownloadRequest req,
        CancellationToken ct)
    {
        await RefreshTokenIfNeededAsync(platform.ToString(), shopId, ct);

        switch (platform)
        {
            case Platform.Shopee:
                {
                    var bytes = await _shopee.DownloadLabelAsync(shopId, req, ct);
                    return File(bytes, "application/pdf", "shopee-label.pdf");
                }
            case Platform.Lazada:
                {
                    if (string.IsNullOrEmpty(req.OrderSn) && !req.FromDate.HasValue && req.RawBody.HasValue)
                    {
                        var dict = JsonSerializer.Deserialize<Dictionary<string, string?>>(req.RawBody.Value.GetRawText())
                                   ?? new Dictionary<string, string?>();
                        var legacyBytes = await _lazada.PrintWaybillAsync(sellerId: shopId.ToString(), parameters: dict, ct: ct);
                        return File(legacyBytes, "application/pdf", "lazada-waybill.pdf");
                    }
                    var bytes = await _lazada.DownloadLabelAsync(shopId, req, ct);
                    return File(bytes, "application/pdf", "lazada-waybill.pdf");
                }
            case Platform.TikTok:
                {
                    var bytes = await _tiktok.DownloadLabelAsync(shopId, req, ct);
                    return File(bytes, "application/pdf", "tiktok-label.pdf");
                }
            default:
                return BadRequest("Unsupported platform");
        }
    }

    [HttpGet("shipping-parameter")]
    public async Task<IActionResult> GetShippingParameter(
        [FromQuery] long shopId,
        [FromQuery] string orderSn,
        CancellationToken ct)
    {
        await RefreshTokenIfNeededAsync("Shopee", shopId, ct);

        var json = await _shopee.GetShippingParameterAsync(shopId, orderSn, ct);
        return Content(json, "application/json");
    }

    /// <summary>
    /// GET /api/market/logistics/shopee/shipping-doc-data-info
    /// Fetch จาก Shopee, UPSERT ลง DB แล้ว return ข้อมูลทุก field จาก mdw.UnifiedOrderLabelDocument
    /// </summary>
    [HttpGet("shopee/shipping-doc-data-info")]
    public async Task<IActionResult> GetShopeeShippingDocDataInfo(
        [FromQuery] long shopId,
        [FromQuery] string orderSn,
        [FromQuery] string? trackingNumber,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orderSn)) return BadRequest("orderSn is required");

        await RefreshTokenIfNeededAsync("Shopee", shopId, ct);

        string shopeeRequestUrl;
        try
        {
            var (url, _) = await _shopee.GetShippingDocumentDataInfoAsync(shopId, orderSn, trackingNumber, ct);
            shopeeRequestUrl = url;
        }
        catch (ShopeeApiException ex)
        {
            var statusCode = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : 500;
            return StatusCode(statusCode, new { error = ex.Message, shopeeRequestUrl = ex.RequestUrl });
        }
        catch (HttpRequestException ex) when (ex.StatusCode.HasValue)
        {
            return StatusCode((int)ex.StatusCode.Value, new { error = ex.Message });
        }

        var records = await _labelDocRepo.GetByOrderSnAsync("Shopee", shopId, orderSn, ct);
        return Ok(new { shopeeRequestUrl, data = records });
    }

    /// <summary>
    /// GET /api/market/logistics/shopee/shipping-doc-data-info/stored
    /// ดึงข้อมูลที่เก็บไว้ใน DB โดยไม่ call Shopee
    /// </summary>
    [HttpGet("shopee/shipping-doc-data-info/stored")]
    public async Task<IActionResult> GetStoredShippingDocDataInfo(
        [FromQuery] long shopId,
        [FromQuery] string orderSn,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orderSn)) return BadRequest("orderSn is required");

        var records = await _labelDocRepo.GetByOrderSnAsync("Shopee", shopId, orderSn, ct);
        if (records.Count == 0) return NotFound(new { message = "No stored data. Call shipping-doc-data-info first." });
        return Ok(records);
    }

    /// <summary>
    /// POST /api/market/logistics/shopee/shipping-doc-data-info/ingest
    /// ส่ง Shopee get_shipping_document_data_info response เข้ามาโดยตรง (ไม่ call Shopee)
    /// UPSERT ลง DB แล้ว return ข้อมูลจาก mdw.UnifiedOrderLabelDocument — ใช้สำหรับทดสอบ / sandbox
    /// Body: { "response": { ...shopee response... } }  หรือส่ง response object ตรงๆ ก็ได้
    /// </summary>
    [HttpPost("shopee/shipping-doc-data-info/ingest")]
    public async Task<IActionResult> IngestShippingDocDataInfo(
        [FromQuery] long shopId,
        [FromQuery] string orderSn,
        [FromQuery] string? packageNumber,
        [FromBody] JsonElement body,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orderSn)) return BadRequest("orderSn is required");

        var response = body.TryGetProperty("response", out var r) ? r : body;

        await _labelDocRepo.UpsertAsync("Shopee", shopId, orderSn, packageNumber, response, ct);

        var records = await _labelDocRepo.GetByOrderSnAsync("Shopee", shopId, orderSn, ct);
        return Ok(records);
    }

    /// <summary>
    /// POST /api/market/logistics/shopee/decode-label-response
    /// Test endpoint: ส่ง Shopee get_shipping_document_data_info response เข้ามา
    /// ใช้ Tesseract OCR ถอด text จาก image ใน recipient_address_info / sender_address_info
    /// ไม่ save ลง DB — ใช้สำหรับทดสอบเท่านั้น
    /// </summary>
    [HttpPost("shopee/decode-label-response")]
    public IActionResult DecodeLabelResponse([FromBody] JsonElement body)
    {
        if (!_ocr.IsTessdataReady())
            return StatusCode(503, new
            {
                error = "tessdata_not_ready",
                message = "วาง .traineddata files ใน tessdata/ folder ก่อน เช่น chi_tra.traineddata, eng.traineddata"
            });

        var result = new Dictionary<string, object?>();

        // รองรับทั้ง full response และ response wrapper
        var response = body.TryGetProperty("response", out var r) ? r : body;

        if (response.TryGetProperty("recipient_address_info", out var rai) && rai.ValueKind == JsonValueKind.Array)
            result["recipient_address_info"] = OcrAddressInfoArray(rai);

        if (response.TryGetProperty("sender_address_info", out var sai) && sai.ValueKind == JsonValueKind.Array)
            result["sender_address_info"] = OcrAddressInfoArray(sai);

        // qrcode plain text ไม่ต้อง OCR — เป็น string ตรงๆ อยู่แล้ว
        if (response.TryGetProperty("shipping_document_info", out var sdi) &&
            sdi.TryGetProperty("third_party_logistic_info", out var tpli) &&
            tpli.TryGetProperty("qrcode", out var qr) && qr.ValueKind != JsonValueKind.Null)
        {
            result["qr_code_data"] = qr.GetString();
        }

        if (result.Count == 0)
            return BadRequest(new { message = "No recipient_address_info / sender_address_info / qrcode found." });

        return Ok(result);
    }

    private List<object> OcrAddressInfoArray(JsonElement array)
    {
        var list = new List<object>();
        foreach (var item in array.EnumerateArray())
        {
            string? key = item.TryGetProperty("key", out var k) ? k.GetString() : null;
            string? decoded = null;
            string status;

            if (item.TryGetProperty("image", out var img) && img.ValueKind != JsonValueKind.Null)
            {
                var raw = img.GetString();
                if (!string.IsNullOrEmpty(raw))
                {
                    decoded = _ocr.OcrDataUrl(raw);
                    status = string.IsNullOrEmpty(decoded) ? "ocr_failed" : "ok";
                }
                else
                {
                    status = "image_empty";
                }
            }
            else
            {
                status = "no_image";
            }

            list.Add(new { key, decoded_text = decoded, status });
        }
        return list;
    }

    // ====== Token Refresh — auto-resolve partnersId/env จาก shopId ======

    private async Task<bool> RefreshTokenIfNeededAsync(
        string platform,
        long shopId,
        CancellationToken ct)
    {
        int? partnersId = null;
        string? env = null;

        try
        {
            var (pId, _, _) = await _shopRepo.GetShopBindingAsync(shopId, ct);
            partnersId = pId;

            var cfg = await _partnerRepo.GetConfigByPartnersIdAsync(pId, ct);
            if (cfg is not null)
                env = cfg.Environment;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Could not resolve partnersId/env for shopId={ShopId}, using defaults", shopId);
        }

        var cd = TimeSpan.FromMinutes(10);
        var cacheKey = $"auth-refresh:{platform}:{shopId}:{partnersId}:{env}";
        if (_cache.TryGetValue(cacheKey, out _))
            return false;

        var decision = await _chanRepo.GetCheckExpireAsync(
            channel: platform,
            environment: env ?? "prod",
            partnerId: partnersId,
            appKey: null,
            accountIdBig: null,
            accountIdStr: shopId.ToString(),
            graceMinutes: 10,
            ct: ct
        );

        if (!"refresh".Equals(decision, StringComparison.OrdinalIgnoreCase))
            return false;

        var client = _httpFactory.CreateClient("OrdersApi");
        if (Request.Headers.TryGetValue("Authorization", out var auth))
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", auth.ToString());

        var url = $"/api/market/auth/refresh?platform={Uri.EscapeDataString(platform)}&shopId={shopId}";
        if (partnersId.HasValue) url += $"&partnersId={partnersId.Value}";
        if (!string.IsNullOrWhiteSpace(env)) url += $"&env={Uri.EscapeDataString(env)}";

        using var resp = await client.PostAsync(url, new StringContent(""), ct);
        if (resp.IsSuccessStatusCode)
        {
            _cache.Set(cacheKey, true, cd);
            return true;
        }

        var body = await resp.Content.ReadAsStringAsync(ct);
        _log.LogWarning("Auth refresh failed for {Platform}/{Shop} => {Status} {Body}",
            platform, shopId, (int)resp.StatusCode, body);

        return false;
    }
}
