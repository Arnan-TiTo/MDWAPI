using MDWAPI.Common;
using MDWAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MDWAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/market/orders")]
public class MarketplaceOrdersController : ControllerBase
{
    private readonly ShopeeOrderService _shopee;
    private readonly LazadaOrderService _lazada;
    private readonly TiktokOrderService _tiktok;
    private readonly ILogger<MarketplaceOrdersController> _log;

    public MarketplaceOrdersController(
        ShopeeOrderService shopee,
        LazadaOrderService lazada,
        TiktokOrderService tiktok,
        ILogger<MarketplaceOrdersController> log)
    {
        _shopee = shopee;
        _lazada = lazada;
        _tiktok = tiktok;
        _log = log;
    }

    [HttpGet("shop-info")]
    public async Task<IActionResult> GetShopInfo([FromQuery] long shopId, CancellationToken ct)
    {
        var json = await _shopee.GetShopProfileRawAsync(shopId, ct);
        return Content(json, "application/json");
    }

    // GET /api/market/orders/detail?platform=Shopee&shopId=...&orderRef=...&responseOptionalFields=total_amount,reverse_shipping_fee
    [HttpGet("detail")]
    public async Task<IActionResult> GetOrderDetail(
        [FromQuery] Platform platform,
        [FromQuery] long shopId,
        [FromQuery] string orderRef,
        [FromQuery] string? responseOptionalFields,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orderRef))
            return BadRequest("orderRef is required");

        switch (platform)
        {
            case Platform.Shopee:
                {
                    var json = await _shopee.GetOrderDetailRawAsync(shopId, orderRef, ct);
                    return Content(json, "application/json");
                }
            case Platform.Lazada:
                {
                    var json = await _lazada.GetOrderDetailRawAsync(shopId, orderRef, ct);
                    return Content(json, "application/json");
                }
            case Platform.TikTok:
                {
                    var json = await _tiktok.GetOrderDetailRawAsync(shopId, orderRef, ct);
                    return Content(json, "application/json");
                }
            default:
                return BadRequest("Unsupported platform");
        }
    }

    // GET /api/market/orders/list?platform=Shopee&...&responseOptionalFields=...
    [HttpGet("list")]
    public async Task<IActionResult> GetOrderList(
        [FromQuery] Platform platform,
        [FromQuery] long shopId,
        [FromQuery] string? timeRangeField,
        [FromQuery] long? timeFrom,
        [FromQuery] long? timeTo,
        [FromQuery] string? createdAfterIso,
        [FromQuery] string? createdBeforeIso,
        [FromQuery] int? offset,
        [FromQuery] int? limit,
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromQuery] string? status,
        [FromQuery] string? responseOptionalFields,
        CancellationToken ct)
    {
        switch (platform)
        {
            case Platform.Shopee:
                {
                    if (string.IsNullOrWhiteSpace(timeRangeField) || timeFrom is null || timeTo is null)
                        return BadRequest("For Shopee, require timeRangeField, timeFrom, timeTo (Unix seconds).");

                    var json = await _shopee.GetOrderListRawAsync(
                        shopId: shopId,
                        timeRangeField: timeRangeField!,
                        timeFrom: timeFrom.Value,
                        timeTo: timeTo.Value,
                        pageSize: pageSize ?? 50,
                        cursor: cursor,
                        orderStatus: status,
                        ct: ct);
                    return Content(json, "application/json");
                }

            case Platform.Lazada:
                {
                    if (string.IsNullOrWhiteSpace(createdAfterIso) || string.IsNullOrWhiteSpace(createdBeforeIso))
                        return BadRequest("For Lazada, require createdAfterIso and createdBeforeIso (ISO-8601).");

                    var json = await _lazada.GetOrderListRawAsync(
                        shopId: shopId,
                        createdAfterIso: createdAfterIso!,
                        createdBeforeIso: createdBeforeIso!,
                        offset: offset ?? 0,
                        limit: limit ?? 50,
                        status: status,
                        ct: ct);
                    return Content(json, "application/json");
                }

            case Platform.TikTok:
                {
                    if (timeFrom is null || timeTo is null)
                        return BadRequest("For TikTok, require timeFrom and timeTo (Unix seconds).");

                    var json = await _tiktok.GetOrderListRawAsync(
                        shopId: shopId,
                        timeFrom: timeFrom.Value,
                        timeTo: timeTo.Value,
                        pageSize: pageSize ?? 50,
                        cursor: cursor,
                        status: status,
                        ct: ct);
                    return Content(json, "application/json");
                }

            default:
                return BadRequest("Unsupported platform");
        }
    }

    // Lazada only (ตามของเดิม)
    [HttpGet("items")]
    public async Task<IActionResult> GetOrderItems(
        [FromQuery] Platform platform,
        [FromQuery] long shopId,
        [FromQuery] string orderRef,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orderRef))
            return BadRequest("orderRef is required");

        switch (platform)
        {
            case Platform.Lazada:
                {
                    var json = await _lazada.GetOrderItemsRawAsync(shopId, orderRef, ct);
                    return Content(json, "application/json");
                }
            case Platform.Shopee:
            case Platform.TikTok:
                return BadRequest("Order items endpoint is not implemented for this platform yet.");
            default:
                return BadRequest("Unsupported platform");
        }
    }
}
