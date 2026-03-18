using System.Text.Json;
using MDWAPI.Common;
using MDWAPI.Data;
using MDWAPI.Entities;
using MDWAPI.Repos;
using MDWAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace MDWAPI.Controllers;

/// <summary>
/// Orchestration endpoints:
/// รับแค่ shopId → หา partnersId/env จาก DB → refresh token → เรียก platform → update DB
/// </summary>
[ApiController]
[Authorize]
[Route("api/market/orders/actions")]
public class MarketplaceOrderActionsController : ControllerBase
{
    private readonly ShopeeOrderService _shopeeOrder;
    private readonly ShopeeLogisticsService _shopeeLogi;
    private readonly TiktokOrderService _tiktokOrder;
    private readonly IUnifiedOrderWriter _writer;
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IShopRepo _shopRepo;
    private readonly IPartnerRepo _partnerRepo;
    private readonly IChannelTokenRepo _chanRepo;
    private readonly IMemoryCache _cache;
    private readonly ReturnRefundSyncService _returnSync;
    private readonly ILogger<MarketplaceOrderActionsController> _log;
    private readonly string _labelBasePath;
    private readonly bool _useMockOnFailure;

    public MarketplaceOrderActionsController(
        ShopeeOrderService shopeeOrder,
        ShopeeLogisticsService shopeeLogi,
        TiktokOrderService tiktokOrder,
        IUnifiedOrderWriter writer,
        AppDbContext db,
        IHttpClientFactory httpFactory,
        IShopRepo shopRepo,
        IPartnerRepo partnerRepo,
        IChannelTokenRepo chanRepo,
        IMemoryCache cache,
        ReturnRefundSyncService returnSync,
        IConfiguration config,
        ILogger<MarketplaceOrderActionsController> log)
    {
        _shopeeOrder = shopeeOrder;
        _shopeeLogi = shopeeLogi;
        _tiktokOrder = tiktokOrder;
        _writer = writer;
        _db = db;
        _httpFactory = httpFactory;
        _shopRepo = shopRepo;
        _partnerRepo = partnerRepo;
        _chanRepo = chanRepo;
        _cache = cache;
        _returnSync = returnSync;
        _log = log;
        _labelBasePath = config["LabelStorage:BasePath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "Labels");
        _useMockOnFailure = string.Equals(config["LabelStorage:UseMockOnFailure"], "true", StringComparison.OrdinalIgnoreCase);
    }

    // ====== DTOs (แค่ shopId + orderRef เป็นหลัก) ======

    public sealed record ProcessShipmentRequest(
        Platform Platform,
        long ShopId,
        string OrderRef
    );

    public sealed record CreateLabelRequest(
        Platform Platform,
        long ShopId,
        string OrderRef
    );

    public sealed record CancelRequest(
        Platform Platform,
        long ShopId,
        string OrderRef,
        string CancelReason,
        string? ShopCipher = null   // TikTok only
    );

    public sealed record HandleCancellationRequest(
        long ShopId,
        string OrderRef,
        string Operation           // ACCEPT or REJECT
    );

    public sealed record SetNoteRequest(
        long ShopId,
        string OrderRef,
        string Note
    );

    // ====== 0) Sync Returns/Refunds ======

    /// <summary>
    /// POST /api/market/orders/actions/sync-returns
    /// ดึงข้อมูล return/refund จาก platform มาบันทึกใน UnifiedReturns + อัปเดต UnifiedOrders
    /// </summary>
    [HttpPost("sync-returns")]
    public async Task<IActionResult> SyncReturns(
        [FromQuery] Platform platform,
        [FromQuery] long shopId,
        [FromQuery] long? timeFrom = null,
        [FromQuery] long? timeTo = null,
        CancellationToken ct = default)
    {
        // default: last 30 days
        var now = DateTimeOffset.UtcNow;
        var from = timeFrom ?? now.AddDays(-30).ToUnixTimeSeconds();
        var to = timeTo ?? now.ToUnixTimeSeconds();

        await RefreshTokenIfNeededAsync(platform.ToString(), shopId, ct);

        switch (platform)
        {
            case Platform.Shopee:
                var result = await _returnSync.SyncShopeeReturnsAsync(shopId, from, to, ct);
                return Ok(new
                {
                    message = $"Sync completed: {result.Processed} processed, {result.Failed} failed out of {result.TotalFound} found.",
                    result
                });
            default:
                return BadRequest(new { message = $"Platform {platform} return sync not yet supported." });
        }
    }

    /// <summary>
    /// GET /api/market/orders/actions/returns
    /// ดึงรายการ return/refund จาก DB
    /// </summary>
    [HttpGet("returns")]
    public async Task<IActionResult> GetReturns(
        [FromQuery] Platform? platform = null,
        [FromQuery] long? shopId = null,
        [FromQuery] string? orderRef = null,
        [FromQuery] string? status = null,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var q = _db.UnifiedReturns.AsNoTracking().AsQueryable();

        if (platform.HasValue)
            q = q.Where(r => r.Channel == platform.Value.ToString());
        if (shopId.HasValue)
            q = q.Where(r => r.ShopId == shopId.Value);
        if (!string.IsNullOrWhiteSpace(orderRef))
            q = q.Where(r => r.ExternalOrderId == orderRef);
        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(r => r.ReturnStatus == status);

        var items = await q
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(take)
            .Select(r => new
            {
                r.UnifiedReturnId,
                r.UnifiedOrderId,
                r.Channel,
                r.ShopId,
                r.ExternalOrderId,
                r.ExternalReturnId,
                r.ReturnStatus,
                r.ReturnReason,
                r.TextReason,
                r.ReturnType,
                r.ReturnSolution,
                r.RefundAmount,
                r.Currency,
                r.CreatedAtUtc,
                r.UpdatedAtUtc,
                r.IngestedAtUtc
            })
            .ToListAsync(ct);

        return Ok(new { count = items.Count, items });
    }

    // ====== 0.5) Batch Process — สำหรับ cron job ======

    [HttpPost("process-shipment-batch")]
    public async Task<IActionResult> ProcessShipmentBatch(
        [FromQuery] Platform platform,
        [FromQuery] long shopId,
        [FromQuery] long? timeFrom = null,
        [FromQuery] long? timeTo = null,
        CancellationToken ct = default)
    {
        // ดึง order ที่พร้อม ship: PAID + LOGISTICS_READY
        var q = _db.UnifiedOrders.AsNoTracking()
            .Where(o => o.OrderStatus == "PAID"
                     && o.FulfillmentStatus == "LOGISTICS_READY"
                     && o.Channel == platform.ToString()
                     && o.ShopId == shopId);

        // กรองตาม CreatedTimeUtc (unix seconds → DateTime)
        if (timeFrom.HasValue)
        {
            var from = DateTimeOffset.FromUnixTimeSeconds(timeFrom.Value).UtcDateTime;
            q = q.Where(o => o.CreatedTimeUtc >= from);
        }
        if (timeTo.HasValue)
        {
            var to = DateTimeOffset.FromUnixTimeSeconds(timeTo.Value).UtcDateTime;
            q = q.Where(o => o.CreatedTimeUtc <= to);
        }

        var pendingOrders = await q
            .OrderBy(o => o.CreatedTimeUtc)
            .Select(o => new { o.Channel, o.ShopId, o.ExternalOrderNo })
            .Take(100) // จำกัด batch ละไม่เกิน 100 orders
            .ToListAsync(ct);

        if (pendingOrders.Count == 0)
            return Ok(new { message = "No pending orders (PAID + LOGISTICS_READY).", processed = 0 });

        _log.LogInformation("Batch process-shipment: found {Count} pending orders", pendingOrders.Count);

        var results = new List<object>();
        int success = 0, failed = 0, alreadyShipped = 0;

        foreach (var po in pendingOrders)
        {
            if (string.IsNullOrWhiteSpace(po.ExternalOrderNo))
            {
                results.Add(new { orderRef = po.ExternalOrderNo, status = "SKIPPED", reason = "Missing orderRef" });
                failed++;
                continue;
            }

            try
            {
                _log.LogInformation("Batch: processing {Platform}/{OrderRef} (shop={ShopId})",
                    platform, po.ExternalOrderNo, shopId);

                var result = await ProcessSingleShipmentAsync(platform, shopId, po.ExternalOrderNo, ct);
                results.Add(new { orderRef = po.ExternalOrderNo, status = result.Status, label = result.LabelMessage });

                if (result.Status == "SHIPPED" || result.Status == "ALREADY_SHIPPED") 
                {
                    success++;
                    if (result.Status == "ALREADY_SHIPPED") alreadyShipped++;
                }
                else failed++;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Batch: failed {OrderRef}", po.ExternalOrderNo);
                results.Add(new { orderRef = po.ExternalOrderNo, status = "ERROR", label = ex.Message });
                failed++;
            }

            // delay ระหว่าง order เพื่อไม่ให้ hit rate limit
            await Task.Delay(500, ct);
        }

        return Ok(new
        {
            message = $"Batch completed: {success} shipped, {alreadyShipped} already shipped, {failed} failed.",
            total = pendingOrders.Count,
            success,
            alreadyShipped,
            failed,
            results
        });
    }

    /// <summary>
    /// Logic หลักของ process-shipment สำหรับ Shopee order 1 ตัว
    /// ใช้ร่วมกันทั้ง endpoint เดี่ยว และ batch
    /// </summary>
    private async Task<(string Status, string? LabelMessage)> ProcessSingleShipmentAsync(
        Platform platform, long shopIdVal, string orderRef, CancellationToken ct)
    {
        await RefreshTokenIfNeededAsync(platform.ToString(), shopIdVal, ct);

        if (platform != Platform.Shopee)
            return ("UNSUPPORTED", $"Platform {platform} not supported in batch.");

        // 1. ดึง shipping parameter
        var paramJson = await _shopeeLogi.GetShippingParameterAsync(shopIdVal, orderRef, ct);
        _log.LogInformation("Batch param for {OrderRef}: {Param}", orderRef, paramJson);

        // ตรวจ error
        using (var paramDoc = JsonDocument.Parse(paramJson))
        {
            var paramRoot = paramDoc.RootElement;
            if (paramRoot.TryGetProperty("error", out var errEl)
                && errEl.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(errEl.GetString())
                && !string.Equals(errEl.GetString(), "", StringComparison.Ordinal))
            {
                var errMsg = paramRoot.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : "";

                // already shipped
                if (errMsg != null && errMsg.Contains("not eligible for rescheduling", StringComparison.OrdinalIgnoreCase))
                {
                    // sync status จาก platform ผ่าน normalize/by-ref
                    await SyncOrderFromPlatformAsync(platform.ToString(), shopIdVal, orderRef, ct);

                    // auto create-label
                    string? labelMsg;
                    try { labelMsg = await CreateAndSaveLabelAsync("Shopee", shopIdVal, orderRef, ct); }
                    catch (Exception ex) { labelMsg = $"Label failed: {ex.Message}"; }

                    return ("ALREADY_SHIPPED", labelMsg);
                }

                return ("ERROR", $"get_shipping_parameter: {errMsg}");
            }
        }

        // 2. enrich address เมื่อจำเป็น
        paramJson = await EnrichPickupAddressIfEmpty(paramJson, shopIdVal, ct);

        // 3. build ship body
        var shipBody = BuildShopeeShipBody(orderRef, paramJson);
        var shipResponse = await _shopeeLogi.ShipOrderAsync(shopIdVal, shipBody, ct);

        // 4. ตรวจ ship_order response ว่าสำเร็จไหม
        using (var shipDoc = JsonDocument.Parse(shipResponse))
        {
            var shipRoot = shipDoc.RootElement;
            if (shipRoot.TryGetProperty("error", out var shipErr)
                && shipErr.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(shipErr.GetString()))
            {
                var shipErrMsg = shipRoot.TryGetProperty("message", out var sMsg) ? sMsg.GetString() : "";
                _log.LogWarning("ship_order failed for {OrderRef}: {Error} - {Message}", orderRef, shipErr.GetString(), shipErrMsg);
                return ("ERROR", $"ship_order failed: {shipErr.GetString()} — {shipErrMsg}");
            }
        }

        // 5. รอให้ platform process แล้ว sync status ผ่าน normalize/by-ref
        _log.LogInformation("ship_order done for {OrderRef}, waiting 2s before sync...", orderRef);
        await Task.Delay(2000, ct);
        await SyncOrderFromPlatformAsync(platform.ToString(), shopIdVal, orderRef, ct);

        // 6. auto create-label
        string? lbl;
        try { lbl = await CreateAndSaveLabelAsync("Shopee", shopIdVal, orderRef, ct); }
        catch (Exception ex) { lbl = $"Label failed: {ex.Message}"; }

        return ("SHIPPED", lbl);
    }

    // ====== 1) Process Shipment (single) ======

    [HttpPost("process-shipment")]
    public async Task<IActionResult> ProcessShipment(
        [FromBody] ProcessShipmentRequest req,
        CancellationToken ct)
    {
        var order = await _db.UnifiedOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.ExternalOrderNo == req.OrderRef
                                   && o.Channel == req.Platform.ToString()
                                   && o.ShopId == req.ShopId, ct);

        if (order is null)
            return NotFound(new { message = $"Order {req.OrderRef} not found in DB." });

        if (!string.Equals(order.OrderStatus, "PAID", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(order.OrderStatus, "READY_TO_SHIP", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = $"Order status must be PAID or READY_TO_SHIP. Current: {order.OrderStatus}",
                currentStatus = order.OrderStatus,
                fulfillmentStatus = order.FulfillmentStatus
            });
        }

        await RefreshTokenIfNeededAsync(req.Platform.ToString(), req.ShopId, ct);

        string platformResponse;
        switch (req.Platform)
        {
            case Platform.Shopee:
                {
                    // 1. ดึง shipping parameter เพื่อดูว่า order นี้ใช้ pickup/dropoff/non_integrated
                    var paramJson = await _shopeeLogi.GetShippingParameterAsync(req.ShopId, req.OrderRef, ct);
                    _log.LogInformation("Shopee shipping param for {OrderRef}: {Param}", req.OrderRef, paramJson);

                    // ตรวจ error จาก get_shipping_parameter
                    using (var paramDoc = JsonDocument.Parse(paramJson))
                    {
                        var paramRoot = paramDoc.RootElement;
                        if (paramRoot.TryGetProperty("error", out var errEl)
                            && errEl.ValueKind == JsonValueKind.String
                            && !string.IsNullOrWhiteSpace(errEl.GetString())
                            && !string.Equals(errEl.GetString(), "", StringComparison.Ordinal))
                        {
                            var errMsg = paramRoot.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : "";

                            // "not eligible for rescheduling" → ship_order เคยสำเร็จแล้ว
                            if (errMsg != null && errMsg.Contains("not eligible for rescheduling", StringComparison.OrdinalIgnoreCase))
                            {
                                // re-fetch order จาก Shopee → update DB
                                try
                                {
                                    var refetchJson = await _shopeeOrder.GetOrderDetailRawAsync(req.ShopId, req.OrderRef, ct);
                                    var refetchOrder = ExtractShopeeNativeOrder(refetchJson);
                                    await _writer.UpsertFromShopeeRawAsync(req.ShopId, null, refetchOrder, null, ct);
                                }
                                catch (Exception ex) { _log.LogWarning(ex, "Re-fetch order failed: {OrderRef}", req.OrderRef); }

                                // auto-chain: create-label → save
                                string? labelMsg = null;
                                try
                                {
                                    labelMsg = await CreateAndSaveLabelAsync("Shopee", req.ShopId, req.OrderRef, ct);
                                }
                                catch (Exception ex)
                                {
                                    _log.LogWarning(ex, "Auto create-label failed for already-shipped {OrderRef}", req.OrderRef);
                                    labelMsg = $"Auto create-label failed: {ex.Message}";
                                }

                                return Ok(new
                                {
                                    message = "Order already shipped (package already created).",
                                    orderRef = req.OrderRef,
                                    label = labelMsg
                                });
                            }

                            return BadRequest(new
                            {
                                message = $"Cannot ship order — {errMsg}",
                                orderRef = req.OrderRef,
                                shippingParameterError = TryParseJson(paramJson)
                            });
                        }
                    }

                    // 2. ถ้า pickup.address_list ว่าง → fallback ไปดึงจาก get_address_list
                    paramJson = await EnrichPickupAddressIfEmpty(paramJson, req.ShopId, ct);

                    // 3. สร้าง ship body ตาม shipping type (default: pickup)
                    object shipBody;
                    try
                    {
                        shipBody = BuildShopeeShipBody(req.OrderRef, paramJson);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return BadRequest(new
                        {
                            message = ex.Message,
                            orderRef = req.OrderRef,
                            shippingParameter = TryParseJson(paramJson)
                        });
                    }

                    var shipBodyJson = JsonSerializer.Serialize(shipBody);
                    _log.LogInformation("Shopee ship body for {OrderRef}: {Body}", req.OrderRef, shipBodyJson);

                    platformResponse = await _shopeeLogi.ShipOrderAsync(req.ShopId, shipBody, ct);

                    // 4. ตรวจ ship_order response ว่าสำเร็จไหม
                    using (var shipDoc = JsonDocument.Parse(platformResponse))
                    {
                        var shipRoot = shipDoc.RootElement;
                        if (shipRoot.TryGetProperty("error", out var shipErr)
                            && shipErr.ValueKind == JsonValueKind.String
                            && !string.IsNullOrWhiteSpace(shipErr.GetString()))
                        {
                            return BadRequest(new
                            {
                                message = "ship_order failed.",
                                orderRef = req.OrderRef,
                                platformResponse = TryParseJson(platformResponse)
                            });
                        }
                    }

                    // 5. รอ platform process แล้ว sync status ผ่าน normalize/by-ref
                    await Task.Delay(2000, ct);
                    await SyncOrderFromPlatformAsync(req.Platform.ToString(), req.ShopId, req.OrderRef, ct);

                    // 6. Auto-chain: create-label → save to disk → insert DB
                    string? labelMessage = null;
                    try
                    {
                        labelMessage = await CreateAndSaveLabelAsync("Shopee", req.ShopId, req.OrderRef, ct);
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Auto create-label failed for {OrderRef}", req.OrderRef);
                        labelMessage = $"Auto create-label failed: {ex.Message}";
                    }

                    return Ok(new
                    {
                        message = "Shipment processed successfully.",
                        orderRef = req.OrderRef,
                        platform = req.Platform.ToString(),
                        label = labelMessage,
                        platformResponse = TryParseJson(platformResponse)
                    });
                }
            case Platform.TikTok:
                return BadRequest(new { message = "Use api/market/logistics/ship for TikTok shipments." });
            default:
                return BadRequest(new { message = "Unsupported platform for process-shipment." });
        }

        return Ok(new
        {
            message = "Shipment processed successfully.",
            orderRef = req.OrderRef,
            platform = req.Platform.ToString(),
            platformResponse = TryParseJson(platformResponse)
        });
    }

    // ====== 2) Create Label ======

    [HttpPost("create-label")]
    public async Task<IActionResult> CreateLabel(
        [FromBody] CreateLabelRequest req,
        CancellationToken ct)
    {
        var order = await _db.UnifiedOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.ExternalOrderNo == req.OrderRef
                                   && o.Channel == req.Platform.ToString()
                                   && o.ShopId == req.ShopId, ct);

        if (order is null)
            return NotFound(new { message = $"Order {req.OrderRef} not found in DB." });

        await RefreshTokenIfNeededAsync(req.Platform.ToString(), req.ShopId, ct);

        switch (req.Platform)
        {
            case Platform.Shopee:
                {
                    // Step 1: get_shipping_document_parameter → ดูว่ามี document type อะไรบ้าง
                    var paramBody = new { order_list = new[] { new { order_sn = req.OrderRef } } };
                    var paramJson = await _shopeeLogi.GetShippingDocumentParameterAsync(req.ShopId, paramBody, ct);
                    _log.LogInformation("Shopee doc param for {OrderRef}: {Param}", req.OrderRef, paramJson);

                    // ดึง shipping_document_type จาก response
                    string docType = "NORMAL_AIR_WAYBILL"; // default
                    using (var pDoc = JsonDocument.Parse(paramJson))
                    {
                        var pRoot = pDoc.RootElement;
                        if (pRoot.TryGetProperty("response", out var pResp)
                            && pResp.TryGetProperty("result_list", out var resultList)
                            && resultList.ValueKind == JsonValueKind.Array
                            && resultList.GetArrayLength() > 0)
                        {
                            var first = resultList[0];
                            if (first.TryGetProperty("suggest_shipping_document_type", out var suggestType))
                                docType = suggestType.GetString() ?? docType;
                            else if (first.TryGetProperty("selectable_component", out var selectable)
                                     && selectable.ValueKind == JsonValueKind.Array
                                     && selectable.GetArrayLength() > 0)
                            {
                                // ใช้ตัวแรกที่มี
                                docType = selectable[0].GetString() ?? docType;
                            }
                        }
                    }

                    _log.LogInformation("Using shipping_document_type={DocType} for {OrderRef}", docType, req.OrderRef);

                    // Step 2: create_shipping_document
                    var createBody = new
                    {
                        order_list = new[]
                        {
                            new { order_sn = req.OrderRef, shipping_document_type = docType }
                        }
                    };
                    var createJson = await _shopeeLogi.CreateShippingDocumentAsync(req.ShopId, createBody, ct);
                    _log.LogInformation("Shopee create doc for {OrderRef}: {Result}", req.OrderRef, createJson);

                    // ตรวจ error จาก create_shipping_document → fallback ลอง download ตรง
                    bool createFailed = false;
                    using (var cDoc = JsonDocument.Parse(createJson))
                    {
                        var cRoot = cDoc.RootElement;
                        if (cRoot.TryGetProperty("error", out var cErr)
                            && cErr.ValueKind == JsonValueKind.String
                            && !string.IsNullOrWhiteSpace(cErr.GetString()))
                        {
                            createFailed = true;
                            _log.LogWarning("create_shipping_document failed for {OrderRef}: {Error}, trying direct download...",
                                req.OrderRef, createJson);
                        }
                    }

                    // ถ้า create ล้มเหลว → ลอง download ตรงๆ (บาง case Shopee สร้าง doc ให้อัตโนมัติ)
                    if (createFailed)
                    {
                        try
                        {
                            var directBody = new
                            {
                                order_list = new[]
                                {
                                    new { order_sn = req.OrderRef, shipping_document_type = docType }
                                }
                            };
                            var pdfDirect = await _shopeeLogi.DownloadShippingDocumentAsync(req.ShopId, directBody, ct);
                            if (pdfDirect.Length > 0)
                                return File(pdfDirect, "application/pdf", $"label-{req.OrderRef}.pdf");
                        }
                        catch (Exception ex)
                        {
                            _log.LogWarning(ex, "Direct download also failed for {OrderRef}", req.OrderRef);
                        }

                        return BadRequest(new
                        {
                            message = "create_shipping_document failed and direct download also failed. " +
                                      "Sandbox: tracking number ยังไม่ถูก assign → label ยังสร้างไม่ได้ " +
                                      "Production: ควรทำงานได้ปกติ",
                            orderRef = req.OrderRef,
                            docType,
                            createResponse = TryParseJson(createJson),
                            paramResponse = TryParseJson(paramJson)
                        });
                    }

                    // Step 3: poll get_shipping_document_result
                    string resultJson = "";
                    var maxRetries = 10;
                    var retryCount = 0;
                    var docReady = false;

                    do
                    {
                        await Task.Delay(1500, ct);
                        var resultBody = new
                        {
                            order_list = new[]
                            {
                                new { order_sn = req.OrderRef, shipping_document_type = docType }
                            }
                        };
                        resultJson = await _shopeeLogi.GetShippingDocumentResultAsync(req.ShopId, resultBody, ct);

                        using var rDoc = JsonDocument.Parse(resultJson);
                        if (rDoc.RootElement.TryGetProperty("response", out var rResp)
                            && rResp.TryGetProperty("result_list", out var rList))
                        {
                            foreach (var item in rList.EnumerateArray())
                            {
                                if (item.TryGetProperty("status", out var statusEl))
                                {
                                    var status = statusEl.GetString();
                                    if (string.Equals(status, "READY", StringComparison.OrdinalIgnoreCase))
                                    {
                                        docReady = true;
                                        break;
                                    }
                                }
                            }
                        }

                        retryCount++;
                    } while (!docReady && retryCount < maxRetries);

                    if (!docReady)
                    {
                        return Ok(new
                        {
                            message = "Document creation initiated but not yet READY after polling.",
                            orderRef = req.OrderRef,
                            docType,
                            lastResult = TryParseJson(resultJson)
                        });
                    }

                    // Step 4: download_shipping_document
                    var downloadBody = new
                    {
                        order_list = new[]
                        {
                            new { order_sn = req.OrderRef, shipping_document_type = docType }
                        }
                    };
                    var pdfBytes = await _shopeeLogi.DownloadShippingDocumentAsync(req.ShopId, downloadBody, ct);
                    return File(pdfBytes, "application/pdf", $"label-{req.OrderRef}.pdf");
                }
            default:
                return BadRequest(new { message = "Use api/market/logistics/label/download for non-Shopee platforms." });
        }
    }

    // ====== 3) Cancel Order ======

    [HttpPost("cancel")]
    public async Task<IActionResult> CancelOrder(
        [FromBody] CancelRequest req,
        CancellationToken ct)
    {
        await RefreshTokenIfNeededAsync(req.Platform.ToString(), req.ShopId, ct);

        string platformResponse;
        switch (req.Platform)
        {
            case Platform.Shopee:
                {
                    platformResponse = await _shopeeOrder.CancelOrderAsync(
                        req.ShopId, req.OrderRef, req.CancelReason, ct: ct);
                    try
                    {
                        var detailJson = await _shopeeOrder.GetOrderDetailRawAsync(req.ShopId, req.OrderRef, ct);
                        var nativeOrder = ExtractShopeeNativeOrder(detailJson);
                        await _writer.UpsertFromShopeeRawAsync(req.ShopId, null, nativeOrder, null, ct);
                    }
                    catch (Exception ex) { _log.LogWarning(ex, "Re-fetch after cancel failed: {OrderRef}", req.OrderRef); }
                    break;
                }
            case Platform.TikTok:
                {
                    platformResponse = await _tiktokOrder.CancelOrderAsync(
                        req.ShopId, req.OrderRef, req.CancelReason, req.ShopCipher, ct);
                    try
                    {
                        var detailJson = await _tiktokOrder.GetOrderDetailRawAsync(req.ShopId, req.OrderRef, req.ShopCipher, ct);
                        await _writer.UpsertFromTiktokRawAsync(req.ShopId, null, detailJson, null, ct);
                    }
                    catch (Exception ex) { _log.LogWarning(ex, "Re-fetch after cancel failed: {OrderRef}", req.OrderRef); }
                    break;
                }
            default:
                return BadRequest(new { message = "Unsupported platform for cancel." });
        }

        return Ok(new
        {
            message = "Order cancelled successfully.",
            orderRef = req.OrderRef,
            platform = req.Platform.ToString(),
            platformResponse = TryParseJson(platformResponse)
        });
    }

    // ====== 4) Handle Buyer Cancellation (Shopee only) ======

    [HttpPost("handle-cancellation")]
    public async Task<IActionResult> HandleBuyerCancellation(
        [FromBody] HandleCancellationRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Operation) ||
            (!string.Equals(req.Operation, "ACCEPT", StringComparison.OrdinalIgnoreCase)
             && !string.Equals(req.Operation, "REJECT", StringComparison.OrdinalIgnoreCase)))
            return BadRequest(new { message = "Operation must be ACCEPT or REJECT." });

        await RefreshTokenIfNeededAsync("Shopee", req.ShopId, ct);

        var platformResponse = await _shopeeOrder.HandleBuyerCancellationAsync(
            req.ShopId, req.OrderRef, req.Operation.ToUpperInvariant(), ct);

        try
        {
            var detailJson = await _shopeeOrder.GetOrderDetailRawAsync(req.ShopId, req.OrderRef, ct);
            var nativeOrder = ExtractShopeeNativeOrder(detailJson);
            await _writer.UpsertFromShopeeRawAsync(req.ShopId, null, nativeOrder, null, ct);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Re-fetch after handle-cancellation failed: {OrderRef}", req.OrderRef); }

        return Ok(new
        {
            message = $"Buyer cancellation {req.Operation.ToUpperInvariant()}ED.",
            orderRef = req.OrderRef,
            platformResponse = TryParseJson(platformResponse)
        });
    }

    // ====== 5) Set Note (Shopee only) ======

    [HttpPost("set-note")]
    public async Task<IActionResult> SetNote(
        [FromBody] SetNoteRequest req,
        CancellationToken ct)
    {
        await RefreshTokenIfNeededAsync("Shopee", req.ShopId, ct);

        var platformResponse = await _shopeeOrder.SetNoteAsync(req.ShopId, req.OrderRef, req.Note, ct);

        var order = await _db.UnifiedOrders
            .FirstOrDefaultAsync(o => o.ExternalOrderNo == req.OrderRef
                                   && o.Channel == "Shopee"
                                   && o.ShopId == req.ShopId, ct);

        if (order is not null)
        {
            order.NoteSeller = req.Note;
            order.UpdatedTimeUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return Ok(new
        {
            message = "Note set successfully.",
            orderRef = req.OrderRef,
            platformResponse = TryParseJson(platformResponse)
        });
    }

    // ====== Token Refresh — auto-resolve partnersId/env จาก shopId ======

    private async Task<bool> RefreshTokenIfNeededAsync(
        string platform,
        long shopId,
        CancellationToken ct)
    {
        // ดึง partnersId + env จาก DB อัตโนมัติ
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

    /// <summary>
    /// ถ้า get_shipping_parameter return pickup.address_list ว่าง
    /// → fallback ไปเรียก get_address_list แล้ว inject address_id กลับ + เก็บ time_slot_list เดิม
    /// </summary>
    private async Task<string> EnrichPickupAddressIfEmpty(string paramJson, long shopId, CancellationToken ct)
    {
        using var paramDoc = JsonDocument.Parse(paramJson);
        var root = paramDoc.RootElement;

        if (!root.TryGetProperty("response", out var resp)) return paramJson;
        if (!resp.TryGetProperty("pickup", out var pickup)) return paramJson;

        // เช็คว่า address_list ว่างจริงไหม
        bool addrEmpty = true;
        if (pickup.TryGetProperty("address_list", out var addrList)
            && addrList.ValueKind == JsonValueKind.Array
            && addrList.GetArrayLength() > 0)
        {
            addrEmpty = false;
        }

        if (!addrEmpty) return paramJson; // มี address แล้ว ไม่ต้อง fallback

        _log.LogInformation("Shopee address_list is empty for shop {ShopId}, calling get_address_list...", shopId);

        try
        {
            var addrJson = await _shopeeLogi.GetAddressListAsync(shopId, ct);
            using var addrDoc = JsonDocument.Parse(addrJson);

            if (!addrDoc.RootElement.TryGetProperty("response", out var addrResp)) return paramJson;
            if (!addrResp.TryGetProperty("address_list", out var fullAddrList)
                || fullAddrList.ValueKind != JsonValueKind.Array
                || fullAddrList.GetArrayLength() == 0) return paramJson;

            // หา PICKUP_ADDRESS ก่อน ถ้าไม่มีใช้ DEFAULT_ADDRESS
            long? pickupAddrId = null;
            long? defaultAddrId = null;

            foreach (var addr in fullAddrList.EnumerateArray())
            {
                if (!addr.TryGetProperty("address_id", out var aidEl)) continue;
                var aid = aidEl.GetInt64();

                if (addr.TryGetProperty("address_type", out var types) && types.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in types.EnumerateArray())
                    {
                        var typeStr = t.GetString() ?? "";
                        if (typeStr == "PICKUP_ADDRESS") pickupAddrId = aid;
                        if (typeStr == "DEFAULT_ADDRESS") defaultAddrId = aid;
                    }
                }

                if (pickupAddrId.HasValue) break;
            }

            var resolvedAddrId = pickupAddrId ?? defaultAddrId;
            if (!resolvedAddrId.HasValue) return paramJson;

            _log.LogInformation("Resolved pickup address_id={AddressId} from get_address_list", resolvedAddrId.Value);

            // เก็บ time_slot_list เดิมจาก get_shipping_parameter (ถ้ามี)
            string originalTimeSlotJson = "[]";
            if (pickup.TryGetProperty("time_slot_list", out var origTimeSlots)
                && origTimeSlots.ValueKind == JsonValueKind.Array
                && origTimeSlots.GetArrayLength() > 0)
            {
                originalTimeSlotJson = origTimeSlots.GetRawText();
            }

            // สร้าง paramJson ใหม่ — inject แค่ address_id + เก็บ time_slot_list เดิม
            var enrichedJson = $@"{{
                ""error"": """",
                ""message"": """",
                ""response"": {{
                    ""info_needed"": {{ ""pickup"": [""address_id"", ""pickup_time_id""] }},
                    ""pickup"": {{
                        ""address_list"": [{{ ""address_id"": {resolvedAddrId.Value} }}],
                        ""time_slot_list"": {originalTimeSlotJson}
                    }}
                }}
            }}";

            return enrichedJson;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to fallback to get_address_list for shop {ShopId}", shopId);
            return paramJson;
        }
    }

    /// <summary>
    /// อัพเดท OrderStatus + FulfillmentStatus ใน DB ตรงๆ
    /// ป้องกัน batch หยิบ order ซ้ำ
    /// </summary>
    private async Task UpdateOrderStatusAsync(
        string channel, long shopId, string orderRef,
        string newOrderStatus, string newFulfillmentStatus,
        CancellationToken ct)
    {
        try
        {
            var order = await _db.UnifiedOrders
                .FirstOrDefaultAsync(o => o.Channel == channel
                                       && o.ShopId == shopId
                                       && o.ExternalOrderNo == orderRef, ct);
            if (order is not null)
            {
                order.OrderStatus = newOrderStatus;
                order.FulfillmentStatus = newFulfillmentStatus;
                order.ShippedTimeUtc ??= DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                _log.LogInformation("Updated DB status: {OrderRef} → {Status}/{Fulfillment}",
                    orderRef, newOrderStatus, newFulfillmentStatus);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to update DB status for {OrderRef}", orderRef);
        }
    }

    /// <summary>
    /// Sync order status จาก platform ผ่าน normalize/by-ref
    /// → เรียก internal HTTP → normalize pipeline จะ fetch order จริงจาก Shopee → update DB
    /// ถ้า normalize ไม่สำเร็จ → fallback อัพเดท status ตรงใน DB
    /// </summary>
    private async Task SyncOrderFromPlatformAsync(
        string channel, long shopId, string orderRef, CancellationToken ct)
    {
        try
        {
            var client = _httpFactory.CreateClient("OrdersApi");
            if (Request.Headers.TryGetValue("Authorization", out var auth))
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", auth.ToString());

            var url = $"/api/market/normalize/by-ref" +
                      $"?platform={Uri.EscapeDataString(channel)}" +
                      $"&shopId={shopId}" +
                      $"&orderRef={Uri.EscapeDataString(orderRef)}";

            _log.LogInformation("SyncOrder: calling normalize/by-ref for {OrderRef}...", orderRef);
            var resp = await client.PostAsync(url, null, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (resp.IsSuccessStatusCode)
            {
                _log.LogInformation("SyncOrder: normalize/by-ref success for {OrderRef}: {Body}",
                    orderRef, body.Length > 500 ? body[..500] : body);
                return; // DB updated ผ่าน normalize pipeline แล้ว
            }

            _log.LogWarning("SyncOrder: normalize/by-ref failed ({Status}) for {OrderRef}: {Body}",
                resp.StatusCode, orderRef, body.Length > 300 ? body[..300] : body);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "SyncOrder: normalize/by-ref exception for {OrderRef}", orderRef);
        }

        // fallback: อัพเดท status ตรงใน DB
        _log.LogInformation("SyncOrder: fallback direct DB update for {OrderRef}", orderRef);
        await UpdateOrderStatusAsync(channel, shopId, orderRef, "READY_TO_SHIP", "SHIPPED", ct);
    }

    private static object? TryParseJson(string json)
    {
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch { return json; }
    }

    /// <summary>
    /// ดึง native order object จาก Shopee API response
    /// Shopee ส่ง {"response":{"order_list":[{...}]}} แต่ normalizer ต้องการ {...} ตรงๆ
    /// </summary>
    private static string ExtractShopeeNativeOrder(string apiResponse)
    {
        using var doc = JsonDocument.Parse(apiResponse);
        var root = doc.RootElement;

        // ลอง response.order_list[0]
        if (root.TryGetProperty("response", out var resp)
            && resp.TryGetProperty("order_list", out var arr)
            && arr.ValueKind == JsonValueKind.Array
            && arr.GetArrayLength() > 0)
        {
            return arr[0].GetRawText();
        }

        // ถ้า root มี order_sn อยู่แล้ว → ใช้ได้เลย
        if (root.TryGetProperty("order_sn", out _))
            return apiResponse;

        throw new InvalidOperationException("Cannot extract native Shopee order from API response");
    }

    /// <summary>
    /// อ่าน get_shipping_parameter response แล้วสร้าง ship_order body ที่ถูกต้อง
    /// Shopee ต้องเลือก 1 type: pickup / dropoff / non_integrated
    /// </summary>
    private static object BuildShopeeShipBody(string orderSn, string paramJson)
    {
        using var doc = JsonDocument.Parse(paramJson);
        var root = doc.RootElement;

        // ดึง warning (ถ้ามี)
        string? warning = null;
        if (root.TryGetProperty("warning", out var warnEl) && warnEl.ValueKind == JsonValueKind.String)
            warning = warnEl.GetString();

        JsonElement infoNeeded = default;
        JsonElement responseEl = default;
        var hasResponse = root.TryGetProperty("response", out responseEl);

        if (hasResponse && responseEl.TryGetProperty("info_needed", out infoNeeded))
        {
            // ===== PICKUP =====
            if (infoNeeded.TryGetProperty("pickup", out _) && responseEl.TryGetProperty("pickup", out var pickupEl))
            {
                long? addressId = null;
                string? pickupTimeId = null;

                if (pickupEl.TryGetProperty("address_list", out var addrList)
                    && addrList.ValueKind == JsonValueKind.Array
                    && addrList.GetArrayLength() > 0)
                {
                    var firstAddr = addrList[0];
                    if (firstAddr.TryGetProperty("address_id", out var aid))
                        addressId = aid.GetInt64();
                }

                if (pickupEl.TryGetProperty("time_slot_list", out var timeSlots)
                    && timeSlots.ValueKind == JsonValueKind.Array
                    && timeSlots.GetArrayLength() > 0)
                {
                    var firstSlot = timeSlots[0];
                    if (firstSlot.TryGetProperty("pickup_time_id", out var tid))
                        pickupTimeId = tid.ToString();
                    else if (firstSlot.TryGetProperty("date", out var dateEl))
                        pickupTimeId = dateEl.ToString();
                }

                // address_id ต้องมี
                if (addressId is null)
                {
                    throw new InvalidOperationException(
                        "Shopee pickup data incomplete: address_id (address_list empty — ตั้งค่าที่อยู่รับพัสดุใน Shopee Seller Centre)" +
                        (warning != null ? $" | warning: {warning}" : ""));
                }

                // ถ้า time_slot_list ว่าง → ส่ง pickup แค่ address_id (ไม่ต้อง pickup_time_id)
                if (pickupTimeId is null)
                {
                    return new
                    {
                        order_sn = orderSn,
                        pickup = new
                        {
                            address_id = addressId
                        }
                    };
                }

                return new
                {
                    order_sn = orderSn,
                    pickup = new
                    {
                        address_id = addressId,
                        pickup_time_id = pickupTimeId
                    }
                };
            }

            // ===== DROPOFF =====
            if (infoNeeded.TryGetProperty("dropoff", out _) && responseEl.TryGetProperty("dropoff", out var dropoffEl))
            {
                long? branchId = null;

                if (dropoffEl.TryGetProperty("branch_list", out var branchList)
                    && branchList.ValueKind == JsonValueKind.Array
                    && branchList.GetArrayLength() > 0)
                {
                    var firstBranch = branchList[0];
                    if (firstBranch.TryGetProperty("branch_id", out var bid))
                        branchId = bid.GetInt64();
                }

                if (branchId is null)
                    throw new InvalidOperationException("Shopee dropoff data incomplete: branch_list empty");

                return new
                {
                    order_sn = orderSn,
                    dropoff = new { branch_id = branchId }
                };
            }

            // ===== NON-INTEGRATED =====
            if (infoNeeded.TryGetProperty("non_integrated", out _))
            {
                return new
                {
                    order_sn = orderSn,
                    non_integrated = new { tracking_number = (string?)null }
                };
            }
        }

        throw new InvalidOperationException(
            "Cannot determine shipping type from get_shipping_parameter response" +
            (warning != null ? $" | warning: {warning}" : ""));
    }

    // ====== Auto Create Label + Save to Disk ======

    /// <summary>
    /// สร้าง label → save ลง disk → insert record ใน UnifiedOrderLabels
    /// ถ้า platform download ไม่ได้ + UseMockOnFailure = true → สร้าง mock PDF แทน
    /// </summary>
    private async Task<string> CreateAndSaveLabelAsync(string channel, long shopId, string orderRef, CancellationToken ct)
    {
        byte[]? pdfBytes = null;
        string docType = "NORMAL_AIR_WAYBILL";
        bool isMock = false;

        try
        {
            // Step 1: get_shipping_document_parameter
            var paramBody = new { order_list = new[] { new { order_sn = orderRef } } };
            var paramJson = await _shopeeLogi.GetShippingDocumentParameterAsync(shopId, paramBody, ct);

            using (var pDoc = JsonDocument.Parse(paramJson))
            {
                var pRoot = pDoc.RootElement;
                if (pRoot.TryGetProperty("response", out var pResp)
                    && pResp.TryGetProperty("result_list", out var resultList)
                    && resultList.ValueKind == JsonValueKind.Array
                    && resultList.GetArrayLength() > 0)
                {
                    var first = resultList[0];
                    if (first.TryGetProperty("suggest_shipping_document_type", out var suggestType))
                        docType = suggestType.GetString() ?? docType;
                }
            }

            // Step 2: create_shipping_document
            var createBody = new
            {
                order_list = new[] { new { order_sn = orderRef, shipping_document_type = docType } }
            };
            var createJson = await _shopeeLogi.CreateShippingDocumentAsync(shopId, createBody, ct);
            _log.LogInformation("Auto create-label for {OrderRef}: {Result}", orderRef, createJson);

            // Step 3: poll get_shipping_document_result
            var maxRetries = 10;
            var docReady = false;

            for (int i = 0; i < maxRetries && !docReady; i++)
            {
                await Task.Delay(1500, ct);
                var resultBody = new { order_list = new[] { new { order_sn = orderRef, shipping_document_type = docType } } };
                var resultJson = await _shopeeLogi.GetShippingDocumentResultAsync(shopId, resultBody, ct);

                using var rDoc = JsonDocument.Parse(resultJson);
                if (rDoc.RootElement.TryGetProperty("response", out var rResp)
                    && rResp.TryGetProperty("result_list", out var rList))
                {
                    foreach (var item in rList.EnumerateArray())
                    {
                        if (item.TryGetProperty("status", out var statusEl)
                            && string.Equals(statusEl.GetString(), "READY", StringComparison.OrdinalIgnoreCase))
                        {
                            docReady = true;
                            break;
                        }
                    }
                }
            }

            if (!docReady)
                throw new InvalidOperationException("Label document not ready after polling.");

            // Step 4: download
            var downloadBody = new { order_list = new[] { new { order_sn = orderRef, shipping_document_type = docType } } };
            pdfBytes = await _shopeeLogi.DownloadShippingDocumentAsync(shopId, downloadBody, ct);

            if (pdfBytes.Length == 0)
                throw new InvalidOperationException("Downloaded label was empty.");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Real label download failed for {OrderRef}, mock={UseMock}", orderRef, _useMockOnFailure);

            if (!_useMockOnFailure)
                return $"Label download failed: {ex.Message}";

            // สร้าง mock PDF
            pdfBytes = GenerateMockLabelPdf(orderRef);
            isMock = true;
            docType = "MOCK";
        }

        // Step 5: save to disk
        var dateFolder = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var folderPath = Path.Combine(_labelBasePath, channel, shopId.ToString(), dateFolder);
        Directory.CreateDirectory(folderPath);

        var suffix = isMock ? "MOCK" : docType;
        var fileName = $"{orderRef}_{suffix}_{DateTime.UtcNow:HHmmss}.pdf";
        var fullPath = Path.Combine(folderPath, fileName);
        await System.IO.File.WriteAllBytesAsync(fullPath, pdfBytes, ct);

        // relative path for DB
        var relativePath = Path.Combine(channel, shopId.ToString(), dateFolder);

        // Step 6: insert record
        var label = new UnifiedOrderLabel
        {
            Channel = channel,
            ShopId = shopId,
            OrderExternalNo = orderRef,
            Location = relativePath,
            FileName = fileName,
            DocumentType = isMock ? "MOCK" : docType,
            FileSizeBytes = pdfBytes.Length,
            CreatedDate = DateTime.UtcNow
        };
        _db.UnifiedOrderLabels.Add(label);
        await _db.SaveChangesAsync(ct);

        var mockNote = isMock ? " (MOCK — ปิดได้โดย LabelStorage:UseMockOnFailure = false)" : "";
        _log.LogInformation("Label saved: {Path}/{File} ({Size} bytes) for {OrderRef}{Mock}",
            relativePath, fileName, pdfBytes.Length, orderRef, mockNote);

        return $"Label saved: {relativePath}/{fileName} ({pdfBytes.Length} bytes){mockNote}";
    }

    /// <summary>
    /// สร้าง PDF จำลองแบบ raw (ไม่ต้องใช้ library ภายนอก)
    /// แสดงข้อความ: OrderExternalNo : {orderRef} ไม่สามารถดึงไฟล์ป้ายที่อยู่ลูกค้าได้สำเร็จ
    /// </summary>
    private static byte[] GenerateMockLabelPdf(string orderRef)
    {
        var line1 = $"OrderExternalNo : {orderRef}";
        var line2 = "Unable to download shipping label from platform.";
        var line3 = "[MOCK LABEL - Sandbox Test]";
        var dateStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");

        var pageW = 420;
        var pageH = 297;

        var stream =
            "BT\n" +
            "/F1 16 Tf\n" +
            "50 230 Td\n" +
            $"({EscapePdfString(line1)}) Tj\n" +
            "0 -30 Td\n" +
            "/F1 12 Tf\n" +
            $"({EscapePdfString(line2)}) Tj\n" +
            "0 -30 Td\n" +
            "/F1 11 Tf\n" +
            $"({EscapePdfString(line3)}) Tj\n" +
            "0 -25 Td\n" +
            "/F1 9 Tf\n" +
            $"({EscapePdfString($"Generated: {dateStr}")}) Tj\n" +
            "ET\n";

        var streamBytes = System.Text.Encoding.ASCII.GetBytes(stream);

        using var ms = new MemoryStream();
        using var w = new StreamWriter(ms, System.Text.Encoding.ASCII);
        w.NewLine = "\n";

        w.Write("%PDF-1.4\n");
        w.Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        w.Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        w.Write($"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageW} {pageH}] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>\nendobj\n");
        w.Write($"4 0 obj\n<< /Length {streamBytes.Length} >>\nstream\n");
        w.Flush();
        ms.Write(streamBytes);
        w.Write("\nendstream\nendobj\n");
        w.Write("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

        w.Write("xref\n0 6\n");
        w.Write("0000000000 65535 f \n");
        w.Write("0000000009 00000 n \n");
        w.Write("0000000058 00000 n \n");
        w.Write("0000000115 00000 n \n");
        w.Write("0000000300 00000 n \n");
        w.Write("0000000450 00000 n \n");
        w.Write("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n9\n%%EOF\n");
        w.Flush();

        return ms.ToArray();
    }

    private static string EscapePdfString(string s) =>
        s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    // ====== 6) Download saved label ======

    [HttpGet("download-label")]
    public async Task<IActionResult> DownloadSavedLabel(
        [FromQuery] string orderRef,
        [FromQuery] long? labelId = null,
        CancellationToken ct = default)
    {
        UnifiedOrderLabel? label;

        if (labelId.HasValue)
        {
            label = await _db.UnifiedOrderLabels.FindAsync(new object[] { labelId.Value }, ct);
        }
        else
        {
            label = await _db.UnifiedOrderLabels
                .Where(l => l.OrderExternalNo == orderRef)
                .OrderByDescending(l => l.CreatedDate)
                .FirstOrDefaultAsync(ct);
        }

        if (label is null)
            return NotFound(new { message = $"No label found for {orderRef}" });

        var fullPath = Path.Combine(_labelBasePath, label.Location, label.FileName);
        if (!System.IO.File.Exists(fullPath))
            return NotFound(new { message = $"Label file not found on disk: {label.Location}/{label.FileName}" });

        var bytes = await System.IO.File.ReadAllBytesAsync(fullPath, ct);
        return File(bytes, "application/pdf", label.FileName);
    }

    // ====== 7) List saved labels ======

    [HttpGet("list-labels")]
    public async Task<IActionResult> ListLabels(
        [FromQuery] long? shopId = null,
        [FromQuery] string? channel = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var q = _db.UnifiedOrderLabels.AsNoTracking().AsQueryable();

        if (shopId.HasValue) q = q.Where(l => l.ShopId == shopId.Value);
        if (!string.IsNullOrWhiteSpace(channel)) q = q.Where(l => l.Channel == channel);
        if (fromDate.HasValue) q = q.Where(l => l.CreatedDate >= fromDate.Value);
        if (toDate.HasValue) q = q.Where(l => l.CreatedDate <= toDate.Value);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(l => l.CreatedDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new
            {
                l.Id,
                l.Channel,
                l.ShopId,
                l.OrderExternalNo,
                l.Location,
                l.FileName,
                l.DocumentType,
                l.FileSizeBytes,
                l.CreatedDate
            })
            .ToListAsync(ct);

        return Ok(new { total, page, pageSize, items });
    }
}
