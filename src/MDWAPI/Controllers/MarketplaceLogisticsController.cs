using System.Text.Json;
using MDWAPI.Common;
using MDWAPI.Models;
using MDWAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MDWAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/market/logistics")]
public class MarketplaceLogisticsController : ControllerBase
{
    private readonly ShopeeLogisticsService _shopee;
    private readonly LazadaLogisticsService _lazada;
    private readonly TiktokLogisticsService _tiktok;
    private readonly ILogger<MarketplaceLogisticsController> _log;

    public MarketplaceLogisticsController(
        ShopeeLogisticsService shopee,
        LazadaLogisticsService lazada,
        TiktokLogisticsService tiktok,
        ILogger<MarketplaceLogisticsController> log)
    {
        _shopee = shopee;
        _lazada = lazada;
        _tiktok = tiktok;
        _log = log;
    }

    // ดึงเลขหรือข้อมูลติดตามแบบรวมแพลตฟอร์ม
    [HttpGet("tracking")]
    public async Task<IActionResult> GetTracking(
        [FromQuery] Platform platform,
        [FromQuery] long shopId,
        [FromQuery] string refId,
        CancellationToken ct)
    {
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

    // ยืนยันการจัดส่งแบบรวมแพลตฟอร์ม
    [HttpPost("ship")]
    public async Task<IActionResult> Ship(
        [FromQuery] Platform platform,
        [FromQuery] long shopId,
        [FromBody] JsonElement body,
        CancellationToken ct)
    {
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

    // ดาวน์โหลดเอกสารจัดส่ง (เช่น Label/Waybill) แบบรวมแพลตฟอร์ม
    [HttpPost("label/download")]
    public async Task<IActionResult> DownloadLabel(
        [FromQuery] Platform platform,
        [FromQuery] long shopId,
        [FromBody] LabelDownloadRequest req,
        CancellationToken ct)
    {
        switch (platform)
        {
            case Platform.Shopee:
                {
                    var bytes = await _shopee.DownloadLabelAsync(shopId, req, ct);
                    return File(bytes, "application/pdf", "shopee-label.pdf");
                }
            case Platform.Lazada:
                {
                    // For Lazada, we still use the legacy way if no OrderSn/Dates provided
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

    // สำหรับ Shopee เท่านั้น: ดู shipping parameter ของออเดอร์
    [HttpGet("shipping-parameter")]
    public async Task<IActionResult> GetShippingParameter(
        [FromQuery] long shopId,
        [FromQuery] string orderSn,
        CancellationToken ct)
    {
        var json = await _shopee.GetShippingParameterAsync(shopId, orderSn, ct);
        return Content(json, "application/json");
    }
}
