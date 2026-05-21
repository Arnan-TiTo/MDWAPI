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
    private readonly IUnifiedOrderWriter _writer;
    private readonly ILogger<MarketplaceOrdersController> _log;

    public MarketplaceOrdersController(
        ShopeeOrderService shopee,
        LazadaOrderService lazada,
        TiktokOrderService tiktok,
        IUnifiedOrderWriter writer,
        ILogger<MarketplaceOrdersController> log)
    {
        _shopee = shopee;
        _lazada = lazada;
        _tiktok = tiktok;
        _writer = writer;
        _log = log;
    }

    [HttpGet("shop-info")]
    public async Task<IActionResult> GetShopInfo([FromQuery] long shopId, CancellationToken ct)
    {
        var json = await _shopee.GetShopProfileRawAsync(shopId, ct);
        return Content(json, "application/json");
    }

    // เพิ่ม shopCipher (ใช้เฉพาะ TikTok, ส่งค่าว่างได้)
    [HttpGet("detail")]
    public async Task<IActionResult> GetOrderDetail(
        [FromQuery] Platform platform,
        [FromQuery] long shopId,
        [FromQuery] string orderRef,
        [FromQuery] string? responseOptionalFields,
        [FromQuery] string? shopCipher,       // <== NEW
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orderRef))
            return BadRequest("orderRef is required");

        switch (platform)
        {
            case Platform.Shopee:
                {
                    var json = await _shopee.GetOrderDetailRawAsync(
                        shopId,
                        orderRef,
                        ct,
                        responseOptionalFields);
                    return Content(json, "application/json");
                }
            case Platform.Lazada:
                {
                    var json = await _lazada.GetOrderDetailRawAsync(shopId, orderRef, ct);
                    return Content(json, "application/json");
                }
            case Platform.TikTok:
                {
                    var json = await _tiktok.GetOrderDetailRawAsync(
                        shopId: shopId,
                        orderRef: orderRef,
                        shopCipher: shopCipher,  // <== pass through
                        ct: ct);
                    return Content(json, "application/json");
                }
            default:
                return BadRequest("Unsupported platform");
        }
    }

    /// <summary>
    /// GET /api/market/orders/escrow-detail?shopId=X&amp;orderSn=Y
    /// ดึง income breakdown จาก Shopee escrow API (เฉพาะ order ที่ชำระแล้ว)
    /// </summary>
    [HttpGet("escrow-detail")]
    public async Task<IActionResult> GetEscrowDetail(
        [FromQuery] long shopId,
        [FromQuery] string orderSn,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orderSn))
            return BadRequest("orderSn is required");

        var json = await _shopee.GetEscrowDetailRawAsync(shopId, orderSn, ct);

        try
        {
            await _writer.UpsertShopeeEscrowAsync(orderSn, json, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Shopee escrow detail fetched but sync failed for {OrderSn}", orderSn);
        }

        return Content(json, "application/json");
    }

    [HttpGet("returns/list")]
    public async Task<IActionResult> GetReturnList(
        [FromQuery] Platform platform,
        [FromQuery] long shopId,
        [FromQuery] long timeFrom,
        [FromQuery] long timeTo,
        [FromQuery] int pageNo = 0,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? status = null,
        [FromQuery] string timeRangeField = "create_time",
        CancellationToken ct = default)
    {
        if (platform != Platform.Shopee)
            return BadRequest("Return list is implemented for Shopee only.");

        var json = await _shopee.GetReturnListRawAsync(
            shopId,
            timeFrom,
            timeTo,
            pageNo,
            pageSize,
            status,
            timeRangeField,
            ct);
        return Content(json, "application/json");
    }

    [HttpGet("returns/detail")]
    public async Task<IActionResult> GetReturnDetail(
        [FromQuery] Platform platform,
        [FromQuery] long shopId,
        [FromQuery] string returnSn,
        CancellationToken ct = default)
    {
        if (platform != Platform.Shopee)
            return BadRequest("Return detail is implemented for Shopee only.");
        if (string.IsNullOrWhiteSpace(returnSn))
            return BadRequest("returnSn is required");

        var json = await _shopee.GetReturnDetailRawAsync(shopId, returnSn, ct);
        return Content(json, "application/json");
    }

    // เพิ่ม shopCipher (ใช้เฉพาะ TikTok, ส่งค่าว่างได้)
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
        [FromQuery] string? shopCipher,       // <== NEW
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
                        ct: ct,
                        responseOptionalFields: responseOptionalFields);
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
                        pageSize: pageSize ?? 20,
                        cursor: cursor,
                        status: status,
                        shopCipher: shopCipher,
                        ct: ct);
                    return Content(json, "application/json");
                }

            default:
                return BadRequest("Unsupported platform");
        }
    }

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
