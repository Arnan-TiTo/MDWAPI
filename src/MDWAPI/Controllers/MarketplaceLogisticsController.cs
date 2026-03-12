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
                    var json = await _shopee.GetTrackingNumberAsync(shopId, refId, ct);
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
