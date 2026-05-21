using MDWAPI.Data;
using MDWAPI.Entities;
using MDWAPI.Models;
using MDWAPI.Repos;
using MDWAPI.Services;
using MDWAPI.Normalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MDWAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/market/normalize")]
public class NormalizeController : ControllerBase
{
    private readonly OrderNormalizeService _svc;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<NormalizeController> _log;
    private readonly IChannelTokenRepo _chanRepo;
    private readonly IUnifiedOrderWriter _writer;
    
    public NormalizeController(
        OrderNormalizeService svc, 
        IHttpClientFactory httpFactory, 
        IMemoryCache cache, 
        ILogger<NormalizeController> log,
        IChannelTokenRepo chanRepo,
        IUnifiedOrderWriter writer)
    {
        _svc = svc;
        _httpFactory = httpFactory;
        _cache = cache;
        _log = log;
        _chanRepo = chanRepo;
        _writer = writer;
    }

    // -----------------
    // 2) by-ref
    // -----------------
    [HttpPost("by-ref")]
    public async Task<IActionResult> NormalizeByRef(
        [FromQuery] string platform,
        [FromQuery] long shopId,
        [FromQuery] string orderRef,
        [FromQuery] string? sellerId,
        [FromQuery] string? batchNo,
        [FromQuery] string? select,
        [FromQuery] string? env,
        [FromQuery] int? partnersId,
        [FromServices] IIngestionAuditService audit
    )
    {
        var ct = HttpContext.RequestAborted;
        var client = _httpFactory.CreateClient("OrdersApi");
        if (Request.Headers.TryGetValue("Authorization", out var auth))
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", auth.ToString());

        var trans = new UnifiedOrderTrans
        {
            Platform = platform,
            ShopId = shopId,
            SellerId = sellerId,
            BatchNo = string.IsNullOrWhiteSpace(batchNo) ? null : batchNo,
            Env = env,
            Mode = "by-ref",
            TotalRefs = 1
        };
        var transId = await audit.BeginAsync(trans, ct);

        await RefreshTokenIfNeededAsync(platform, shopId, partnersId, env, ct);

        var sellerIdQs = string.IsNullOrWhiteSpace(sellerId) ? "" : $"&sellerId={Uri.EscapeDataString(sellerId)}";
        var envQs = string.IsNullOrWhiteSpace(env) ? "" : $"&env={Uri.EscapeDataString(env)}";
        var detailUrl = $"/api/market/orders/detail?platform={Uri.EscapeDataString(platform)}&shopId={shopId}&orderRef={Uri.EscapeDataString(orderRef)}{sellerIdQs}{envQs}";

        using var resp = await client.GetAsync(detailUrl, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            await audit.AddItemAsync(transId, new UnifiedOrderTransItem
            {
                OrderRef = orderRef,
                Result = "Failed",
                ErrorMessage = $"fetch detail failed: {json}"
            }, ct);
            await audit.CompleteAsync(transId, h => { h.Attempted = 1; h.FailedCount = 1; }, ct);

            return StatusCode((int)resp.StatusCode, new { message = "Fetch detail failed", detailUrl, err = json, batchNo = trans.BatchNo });
        }

        List<string> natives;
        using (var doc = JsonDocument.Parse(json))
        {
            // รองรับ error รูปแบบต่าง ๆ
            if (HasExplicitError(doc.RootElement))
            {
                await audit.AddItemAsync(transId, new UnifiedOrderTransItem
                {
                    OrderRef = orderRef,
                    Result = "Failed",
                    ErrorMessage = $"detail error payload: {json}"
                }, ct);
                await audit.CompleteAsync(transId, h => { h.Attempted = 1; h.FailedCount = 1; }, ct);
                return BadRequest(new { message = "detail returned error", detailUrl, payload = json, batchNo = trans.BatchNo });
            }

            // default selector สำหรับ TikTok
            var effSelect = select;
            if (string.IsNullOrWhiteSpace(effSelect) && platform.Equals("TikTok", StringComparison.OrdinalIgnoreCase))
                effSelect = "data.orders.0";

            var roots = string.IsNullOrWhiteSpace(effSelect) ? new List<JsonElement> { doc.RootElement } : SelectPathInDoc(doc.RootElement, effSelect);
            natives = ExtractNativeOrdersInDoc(platform, roots);

            // Fallbacks: Shopee / TikTok
            if (natives.Count == 0)
            {
                if (platform.Equals("Shopee", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var g in new[] { "response.order_list.0", "response.orders.0", "result.order_list.0", "data.order.0", "data.order", "response", "result", "data" })
                    {
                        var gs = SelectPathInDoc(doc.RootElement, g);
                        if (gs.Count == 0) continue;
                        natives = ExtractNativeOrdersInDoc(platform, gs);
                        if (natives.Count > 0) break;
                    }
                }
                else if (platform.Equals("TikTok", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var g in new[] { "data.orders.0", "data.orders", "orders", "data" })
                    {
                        var gs = SelectPathInDoc(doc.RootElement, g);
                        if (gs.Count == 0) continue;
                        natives = ExtractNativeOrdersInDoc(platform, gs);
                        if (natives.Count > 0) break;
                    }
                }
            }
        }
        if (natives.Count == 0)
        {
            await audit.AddItemAsync(transId, new UnifiedOrderTransItem
            {
                OrderRef = orderRef,
                Result = "Failed",
                ErrorMessage = "no native order object"
            }, ct);
            await audit.CompleteAsync(transId, h => { h.Attempted = 1; h.FailedCount = 1; }, ct);

            return BadRequest(new { message = "no native order object", detailUrl, batchNo = trans.BatchNo });
        }

        var raw = natives[0];
        var extId = TryGetExternalId(platform, raw);
        if (!string.IsNullOrWhiteSpace(extId) && !extId.Equals(orderRef, StringComparison.OrdinalIgnoreCase))
        {
            await audit.AddItemAsync(transId, new UnifiedOrderTransItem
            {
                OrderRef = orderRef,
                ExternalOrderId = extId,
                Result = "Skipped",
                ErrorMessage = "detail external id mismatch"
            }, ct);
            await audit.CompleteAsync(transId, h => { h.Attempted = 1; }, ct);
            return Ok(new { count = 0, unifiedOrderIds = Array.Empty<long>(), note = "mismatch external id", batchNo = trans.BatchNo });
        }

        try
        {
            var r = platform.ToLowerInvariant() switch
            {
                "shopee" => await _svc.NormalizeShopeeWithResultAsync(shopId, sellerId, raw, trans.BatchNo, ct),
                "tiktok" => await _svc.NormalizeTiktokWithResultAsync(shopId, sellerId, raw, trans.BatchNo, ct),
                "lazada" => await _svc.NormalizeLazadaWithResultAsync(shopId, sellerId, raw, trans.BatchNo, ct),
                _ => throw new ArgumentException("Unsupported platform")
            };

            var escrowResult = await TryFetchAndUpsertShopeeEscrowAsync(
                platform,
                shopId,
                r.ExternalOrderId,
                client,
                ct);

            await audit.AddItemAsync(transId, new UnifiedOrderTransItem
            {
                OrderRef = orderRef,
                ExternalOrderId = r.ExternalOrderId,
                RawHash = r.RawHash,
                UnifiedOrderId = r.UnifiedOrderId,
                Result = r.Outcome.ToString(),
                ErrorMessage = escrowResult is not null && !escrowResult.Equals("synced", StringComparison.OrdinalIgnoreCase)
                    ? $"escrow {escrowResult}"
                    : null
            }, ct);

            await audit.CompleteAsync(transId, h =>
            {
                h.Attempted = 1;
                if (r.Outcome == NormalizeOutcome.Created) h.CreatedCount = 1;
                else if (r.Outcome == NormalizeOutcome.Updated) h.UpdatedCount = 1;
                else h.UnchangedCount = 1;
            }, ct);

            return Ok(new
            {
                count = 1,
                unifiedOrderIds = new[] { r.UnifiedOrderId },
                outcome = r.Outcome.ToString(),
                escrow = escrowResult,
                batchNo = trans.BatchNo
            });
        }
        catch (Exception ex)
        {
            await audit.AddItemAsync(transId, new UnifiedOrderTransItem
            {
                OrderRef = orderRef,
                ExternalOrderId = extId,
                Result = "Failed",
                ErrorMessage = $"{ex.Message} | {ex.InnerException?.Message}"
            }, ct);

            await audit.CompleteAsync(transId, h => { h.Attempted = 1; h.FailedCount = 1; }, ct);
            return StatusCode(500, new { 
                message = "normalize failed", 
                error = ex.Message, 
                innerError = ex.InnerException?.Message,
                stack = ex.StackTrace,
                batchNo = trans.BatchNo 
            });
        }
    }

    // -----------------
    // 3) by-list
    // -----------------
    [HttpPost("by-list")]
    public async Task<IActionResult> NormalizeByList(
        [FromQuery] string platform,
        [FromQuery] long? shopId,
        [FromQuery] string timeRangeField,
        [FromQuery] long timeFrom,
        [FromQuery] long timeTo,
        [FromServices] IIngestionAuditService audit,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? sellerId = null,
        [FromQuery] string? batchNo = null,
        [FromQuery] string? env = null,
        [FromQuery] string? listSelect = null,
        [FromQuery] string? detailSelect = null,
        [FromQuery] int? partnersId = null)
    {
        try
        {
            var ct = HttpContext.RequestAborted;
            var client = _httpFactory.CreateClient("OrdersApi");
            if (Request.Headers.TryGetValue("Authorization", out var auth))
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", auth.ToString());

            var trans = new UnifiedOrderTrans
            {
                Platform = platform,
                ShopId = shopId,
                SellerId = sellerId,
                BatchNo = string.IsNullOrWhiteSpace(batchNo) ? null : batchNo,
                Env = env,
                Mode = "by-list",
                TimeRangeField = timeRangeField,
                TimeFromEpoch = timeFrom,
                TimeToEpoch = timeTo
            };
            var transId = await audit.BeginAsync(trans, ct);

            var chkExp = await RefreshTokenIfNeededAsync(platform, shopId ?? 0, partnersId, env, ct);

            var sellerIdQs = string.IsNullOrWhiteSpace(sellerId) ? "" : $"&sellerId={Uri.EscapeDataString(sellerId)}";
            var envQs = string.IsNullOrWhiteSpace(env) ? "" : $"&env={Uri.EscapeDataString(env)}";

            // =========================
            // ดึง list แบบรองรับหน้า (next_page_token) สำหรับ TikTok
            // =========================
            var pageToken = "";
            var allRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pageNo = 0;

            do
            {
                pageNo++;
                var pageQs = string.IsNullOrEmpty(pageToken) ? "" : $"&pageToken={Uri.EscapeDataString(pageToken)}";
                var listUrl = $"/api/market/orders/list?platform={Uri.EscapeDataString(platform)}&shopId={shopId}&timeRangeField={Uri.EscapeDataString(timeRangeField)}&timeFrom={timeFrom}&timeTo={timeTo}&pageSize={pageSize}{sellerIdQs}{envQs}{pageQs}";

                using var listResp = await client.GetAsync(listUrl, ct);
                var listJson = await listResp.Content.ReadAsStringAsync(ct);
                if (!listResp.IsSuccessStatusCode)
                {
                    await audit.CompleteAsync(transId, h =>
                    {
                        h.TotalRefs = 0; h.Attempted = 0; h.FailedCount = 0; h.Notes = $"fetch list failed: {listJson}";
                    }, ct);
                    return StatusCode((int)listResp.StatusCode, new { message = "Fetch list failed", listUrl, err = listJson, batchNo = trans.BatchNo });
                }

                using var doc = JsonDocument.Parse(listJson);

                if (HasExplicitError(doc.RootElement))
                {
                    await audit.CompleteAsync(transId, h =>
                    {
                        h.TotalRefs = 0; h.Attempted = 0; h.FailedCount = 0; h.Notes = $"list error payload: {listJson}";
                    }, ct);
                    return BadRequest(new { message = "list returned error", listUrl, payload = listJson, batchNo = trans.BatchNo });
                }

                // default selector สำหรับ TikTok
                var effListSelect = listSelect;
                if (string.IsNullOrWhiteSpace(effListSelect) && platform.Equals("TikTok", StringComparison.OrdinalIgnoreCase))
                    effListSelect = "data.orders";

                var roots = string.IsNullOrWhiteSpace(effListSelect) ? new List<JsonElement> { doc.RootElement } : SelectPathInDoc(doc.RootElement, effListSelect);
                var pageRefs = ExtractOrderRefsInDoc(platform, roots)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Where(s => IsLikelyOrderRef(platform, s))
                    .Distinct()
                    .ToList();

                // Fallbacks: Shopee / TikTok
                if (pageRefs.Count == 0)
                {
                    if (platform.Equals("Shopee", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var p in new[] { "response.order_list", "response.orders", "result.order_list", "result.orders", "data.order_list", "data.orders", "orders" })
                        {
                            var r2 = SelectPathInDoc(doc.RootElement, p);
                            if (r2.Count == 0) continue;
                            pageRefs = ExtractOrderRefsInDoc(platform, r2)
                                .Where(s => !string.IsNullOrWhiteSpace(s))
                                .Where(s => IsLikelyOrderRef(platform, s))
                                .Distinct()
                                .ToList();
                            if (pageRefs.Count > 0) break;
                        }
                        if (pageRefs.Count == 0)
                        {
                            _log.LogWarning("Shopee list parse yielded 0 refs. Sample: {body}",
                                listJson.Length > 500 ? listJson[..500] + "..." : listJson);
                        }
                    }
                    else if (platform.Equals("TikTok", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var p in new[] { "data.orders", "orders", "data" })
                        {
                            var r2 = SelectPathInDoc(doc.RootElement, p);
                            if (r2.Count == 0) continue;
                            pageRefs = ExtractOrderRefsInDoc(platform, r2)
                                .Where(s => !string.IsNullOrWhiteSpace(s))
                                .Distinct()
                                .ToList();
                            if (pageRefs.Count > 0) break;
                        }
                    }
                }

                foreach (var r in pageRefs) allRefs.Add(r);

                // next_page_token สำหรับ TikTok
                pageToken = ExtractNextPageToken(doc.RootElement) ?? "";
            }
            while (!string.IsNullOrEmpty(pageToken));

            var orderRefs = allRefs.ToList();
            var totalRefs = orderRefs.Count;

            int attempted = 0, created = 0, updated = 0, unchanged = 0, failed = 0;
            int escrowSynced = 0, escrowFailed = 0;
            var unifiedIds = new List<long>();

            // =========================
            // ดึง detail + normalize
            // =========================
            foreach (var orderRef in orderRefs)
            {
                attempted++;
                var detailUrl = $"/api/market/orders/detail?platform={Uri.EscapeDataString(platform)}&shopId={shopId}&orderRef={Uri.EscapeDataString(orderRef)}{sellerIdQs}{envQs}";

                try
                {
                    using var dResp = await client.GetAsync(detailUrl, ct);
                    var dJson = await dResp.Content.ReadAsStringAsync(ct);
                    if (!dResp.IsSuccessStatusCode)
                    {
                        failed++;
                        await audit.AddItemAsync(transId, new UnifiedOrderTransItem
                        {
                            OrderRef = orderRef,
                            Result = "Failed",
                            ErrorMessage = $"fetch detail failed: {dJson}"
                        }, ct);
                        continue;
                    }

                    List<string> natives;
                    using (var dDoc = JsonDocument.Parse(dJson))
                    {
                        if (HasExplicitError(dDoc.RootElement))
                        {
                            failed++;
                            await audit.AddItemAsync(transId, new UnifiedOrderTransItem
                            {
                                OrderRef = orderRef,
                                Result = "Failed",
                                ErrorMessage = $"detail error payload: {dJson}"
                            }, ct);
                            continue;
                        }

                        // default selector สำหรับ TikTok
                        var effDetailSelect = detailSelect;
                        if (string.IsNullOrWhiteSpace(effDetailSelect) && platform.Equals("TikTok", StringComparison.OrdinalIgnoreCase))
                            effDetailSelect = "data.orders";

                        var dRoots = string.IsNullOrWhiteSpace(effDetailSelect) ? new List<JsonElement> { dDoc.RootElement } : SelectPathInDoc(dDoc.RootElement, effDetailSelect);
                        natives = ExtractNativeOrdersInDoc(platform, dRoots);

                        // Fallbacks
                        if (natives.Count == 0)
                        {
                            if (platform.Equals("Shopee", StringComparison.OrdinalIgnoreCase))
                            {
                                foreach (var g in new[] { "response.order_list.0", "response.orders.0", "result.order_list.0", "data.order.0", "data.order", "response", "result", "data" })
                                {
                                    var gs = SelectPathInDoc(dDoc.RootElement, g);
                                    if (gs.Count == 0) continue;
                                    natives = ExtractNativeOrdersInDoc(platform, gs);
                                    if (natives.Count > 0) break;
                                }
                            }
                            else if (platform.Equals("TikTok", StringComparison.OrdinalIgnoreCase))
                            {
                                foreach (var g in new[] { "data.orders.0", "data.orders", "orders", "data" })
                                {
                                    var gs = SelectPathInDoc(dDoc.RootElement, g);
                                    if (gs.Count == 0) continue;
                                    natives = ExtractNativeOrdersInDoc(platform, gs);
                                    if (natives.Count > 0) break;
                                }
                            }
                        }
                    }

                    if (natives.Count == 0)
                    {
                        failed++;
                        await audit.AddItemAsync(transId, new UnifiedOrderTransItem
                        {
                            OrderRef = orderRef,
                            Result = "Failed",
                            ErrorMessage = "no native order object in detail"
                        }, ct);
                        continue;
                    }

                    var raw = natives[0];
                    var extId = TryGetExternalId(platform, raw);
                    if (!string.IsNullOrWhiteSpace(extId) && !extId.Equals(orderRef, StringComparison.OrdinalIgnoreCase))
                    {
                        await audit.AddItemAsync(transId, new UnifiedOrderTransItem
                        {
                            OrderRef = orderRef,
                            ExternalOrderId = extId,
                            Result = "Skipped",
                            ErrorMessage = "detail external id mismatch"
                        }, ct);
                        continue;
                    }

                    var r = platform.ToLowerInvariant() switch
                    {
                        "shopee" => await _svc.NormalizeShopeeWithResultAsync(shopId, sellerId, raw, trans.BatchNo, ct),
                        "tiktok" => await _svc.NormalizeTiktokWithResultAsync(shopId, sellerId, raw, trans.BatchNo, ct),
                        "lazada" => await _svc.NormalizeLazadaWithResultAsync(shopId, sellerId, raw, trans.BatchNo, ct),
                        _ => throw new ArgumentException("Unsupported platform")
                    };

                    var escrowResult = await TryFetchAndUpsertShopeeEscrowAsync(
                        platform,
                        shopId,
                        r.ExternalOrderId,
                        client,
                        ct);
                    if (escrowResult is not null)
                    {
                        if (escrowResult.Equals("synced", StringComparison.OrdinalIgnoreCase)) escrowSynced++;
                        else escrowFailed++;
                    }

                    await audit.AddItemAsync(transId, new UnifiedOrderTransItem
                    {
                        OrderRef = orderRef,
                        ExternalOrderId = r.ExternalOrderId,
                        RawHash = r.RawHash,
                        UnifiedOrderId = r.UnifiedOrderId,
                        Result = r.Outcome.ToString(),
                        ErrorMessage = escrowResult is not null && !escrowResult.Equals("synced", StringComparison.OrdinalIgnoreCase)
                            ? $"escrow {escrowResult}"
                            : null
                    }, ct);

                    unifiedIds.Add(r.UnifiedOrderId);
                    if (r.Outcome == NormalizeOutcome.Created) created++;
                    else if (r.Outcome == NormalizeOutcome.Updated) updated++;
                    else unchanged++;
                }
                catch (Exception ex)
                {
                    failed++;
                    await audit.AddItemAsync(transId, new UnifiedOrderTransItem
                    {
                        OrderRef = orderRef,
                        Result = "Failed",
                        ErrorMessage = $"{ex.Message} | {ex.InnerException?.Message}"
                    }, ct);
                }
            }

            await audit.CompleteAsync(transId, h =>
            {
                h.TotalRefs = totalRefs; h.Attempted = attempted; h.CreatedCount = created;
                h.UpdatedCount = updated; h.UnchangedCount = unchanged; h.FailedCount = failed;
                h.Notes = $"RangeField={timeRangeField}, From={timeFrom}, To={timeTo}, EscrowSynced={escrowSynced}, EscrowFailed={escrowFailed}";
            }, ct);

            return Ok(new
            {
                platform,
                shopId,
                timeRangeField,
                timeFrom,
                timeTo,
                batchNo = trans.BatchNo,
                totalRefs,
                inserted = created + updated + unchanged,
                created,
                updated,
                unchanged,
                failed,
                escrowSynced,
                escrowFailed,
                unifiedOrderIds = unifiedIds
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "NormalizeByList failed");
            return StatusCode(500, new { message = "Critical failure in NormalizeByList", error = ex.Message, stack = ex.StackTrace });
        }
    }

    // ========================
    // Helpers
    // ========================
    private async Task<string?> TryFetchAndUpsertShopeeEscrowAsync(
        string platform,
        long? shopId,
        string orderSn,
        HttpClient client,
        CancellationToken ct)
    {
        if (!platform.Equals("Shopee", StringComparison.OrdinalIgnoreCase))
            return null;

        if (shopId is null)
            return "skipped: missing shopId";

        var escrowUrl = $"/api/market/orders/escrow-detail?shopId={shopId.Value}&orderSn={Uri.EscapeDataString(orderSn)}";

        try
        {
            using var resp = await client.GetAsync(escrowUrl, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("Shopee escrow fetch failed for {OrderSn}: {Status} {Body}",
                    orderSn, (int)resp.StatusCode, json);
                return $"failed: HTTP {(int)resp.StatusCode}";
            }

            using (var doc = JsonDocument.Parse(json))
            {
                if (HasExplicitError(doc.RootElement))
                {
                    _log.LogWarning("Shopee escrow returned error payload for {OrderSn}: {Body}", orderSn, json);
                    return "failed: error payload";
                }
            }

            await _writer.UpsertShopeeEscrowAsync(orderSn, json, ct);
            return "synced";
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Shopee escrow sync failed for {OrderSn}", orderSn);
            return $"failed: {ex.Message}";
        }
    }

    private async Task<bool> RefreshTokenIfNeededAsync(
        string platform,
        long shopId,
        int? partnersId,          // ✅ ใช้ค่าจริงที่รับมา (ไม่ fix)
        string? env,              // ✅ ใช้ค่าจริงที่รับมา (ไม่ fix)
        CancellationToken ct,
        TimeSpan? cooldown = null)
    {
        var cd = cooldown ?? TimeSpan.FromMinutes(10);

        // cache เฉพาะเมื่อเพิ่ง refresh สำเร็จ เพื่อกัน spam
        var cacheKey = $"auth-refresh:{platform}:{shopId}:{partnersId}:{env}";
        if (_cache.TryGetValue(cacheKey, out _))
            return false;

        // 1) เช็คอายุ token จากฐาน: < UTC+10 นาที => "refresh", otherwise "use token"
        var decision = await _chanRepo.GetCheckExpireAsync(
            channel: platform,
            environment: env ?? "prod",                // ถ้าอยากไม่ filter env ให้ส่ง null เข้ามา
            partnerId: partnersId,
            appKey: null,
            accountIdBig: null,
            accountIdStr: shopId.ToString(),
            graceMinutes: 10,
            ct: ct
        );

        if (!"refresh".Equals(decision, StringComparison.OrdinalIgnoreCase))
        {
            // "use token" -> ไม่ต้องทำอะไร ปล่อย flow ไปต่อ
            return false;
        }

        // 2) ต้อง refresh → เรียก POST /api/market/auth/refresh?platform=...&shopId=...[&partnersId=...][&env=...]
        var client = _httpFactory.CreateClient("OrdersApi");
        if (Request.Headers.TryGetValue("Authorization", out var auth))
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", auth.ToString());

        var url = $"/api/market/auth/refresh?platform={Uri.EscapeDataString(platform)}&shopId={shopId}";
        if (partnersId.HasValue) url += $"&partnersId={partnersId.Value}";
        if (!string.IsNullOrWhiteSpace(env)) url += $"&env={Uri.EscapeDataString(env)}";  // เผื่อ backend รองรับ

        using var resp = await client.PostAsync(url, new StringContent(""), ct); // ✅ POST
        if (resp.IsSuccessStatusCode)
        {
            _cache.Set(cacheKey, true, cd);  // กันเรียกถี่หลังเพิ่ง refresh สำเร็จ
            return true;
        }

        // log ไว้ช่วย debug ถ้า refresh ล้มเหลว
        var body = await resp.Content.ReadAsStringAsync(ct);
        _log.LogWarning("Auth refresh failed for {Platform}/{Shop} (partnersId={PartnersId}, env={Env}) => {Status} {Body}",
            platform, shopId, partnersId, env, (int)resp.StatusCode, body);

        return false;
    }
    private static bool HasExplicitError(JsonElement root)
    {
        // กรณี API ส่ง "error": "..." (แบบเดิม)
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var errEl) &&
            errEl.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(errEl.GetString()))
            return true;

        // รูปแบบ TikTok: {"code": 0, "message": "Success", ...}
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("code", out var codeEl) &&
            codeEl.ValueKind == JsonValueKind.Number && codeEl.TryGetInt32(out var code) && code != 0)
            return true;

        return false;
    }

    private static string? ExtractNextPageToken(JsonElement root)
    {
        // TikTok: $.data.next_page_token
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty("next_page_token", out var tok) &&
            tok.ValueKind == JsonValueKind.String)
        {
            var v = tok.GetString();
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }
        return null;
    }

    private static List<JsonElement> SelectPathInDoc(JsonElement root, string path)
    {
        var segs = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var current = new List<JsonElement> { root };

        foreach (var seg in segs)
        {
            var next = new List<JsonElement>();
            var isIndex = int.TryParse(seg, out var idx);

            foreach (var node in current)
            {
                if (isIndex)
                {
                    if (node.ValueKind == JsonValueKind.Array && idx >= 0 && idx < node.GetArrayLength())
                        next.Add(node[idx]);
                }
                else
                {
                    if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty(seg, out var child))
                        next.Add(child);
                }
            }

            current = next;
            if (current.Count == 0) break;
        }

        return current;
    }

    private static string[] GetIdKeys(string platform)
    {
        switch (platform.ToLowerInvariant())
        {
            case "shopee": return new[] { "order_sn", "orderSn" };
            case "tiktok": return new[] { "id", "order_id", "orderId", "order_number", "orderNumber" };
            case "lazada": return new[] { "order_id", "orderId", "trade_order_id" };
            default: return Array.Empty<string>();
        }
    }

    private static List<string> ExtractNativeOrdersInDoc(string platform, List<JsonElement> roots)
    {
        var keys = GetIdKeys(platform);
        var bag = new List<string>();

        foreach (var r in roots)
            Scan(r, keys, bag);

        return bag;

        static void Scan(JsonElement node, string[] keys, List<string> bag)
        {
            switch (node.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var k in keys)
                    {
                        if (node.TryGetProperty(k, out _))
                        {
                            bag.Add(node.GetRawText());
                            return;
                        }
                    }
                    foreach (var prop in node.EnumerateObject())
                        Scan(prop.Value, keys, bag);
                    break;

                case JsonValueKind.Array:
                    foreach (var el in node.EnumerateArray())
                        Scan(el, keys, bag);
                    break;
            }
        }
    }

    private static List<string> ExtractOrderRefsInDoc(string platform, List<JsonElement> roots)
    {
        var keys = GetIdKeys(platform);
        var refs = new List<string>();

        foreach (var r in roots)
            ScanRefs(r, keys, refs);

        return refs;

        static void ScanRefs(JsonElement node, string[] keys, List<string> refs)
        {
            switch (node.ValueKind)
            {
                case JsonValueKind.String:
                    refs.Add(node.GetString()!);
                    break;

                case JsonValueKind.Number:
                    refs.Add(node.ToString());
                    break;

                case JsonValueKind.Object:
                    foreach (var k in keys)
                    {
                        if (node.TryGetProperty(k, out var idEl))
                        {
                            if (idEl.ValueKind == JsonValueKind.String)
                                refs.Add(idEl.GetString()!);
                            else if (idEl.ValueKind == JsonValueKind.Number)
                                refs.Add(idEl.ToString());
                            return;
                        }
                    }
                    foreach (var prop in node.EnumerateObject())
                        ScanRefs(prop.Value, keys, refs);
                    break;

                case JsonValueKind.Array:
                    foreach (var el in node.EnumerateArray())
                        ScanRefs(el, keys, refs);
                    break;
            }
        }
    }

    private static bool IsLikelyOrderRef(string platform, string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        var p = platform.ToLowerInvariant();

        if (p == "shopee")
        {
            if (s.Contains(':')) return false;
            return Regex.IsMatch(s, @"^[A-Z0-9]{8,20}$");
        }

        return true;
    }

    private static string? TryGetExternalId(string platform, string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            switch (platform.ToLowerInvariant())
            {
                case "shopee":
                    if (root.TryGetProperty("order_sn", out var osn) && osn.ValueKind == JsonValueKind.String)
                        return osn.GetString();
                    if (root.TryGetProperty("orderSn", out var osn2) && osn2.ValueKind == JsonValueKind.String)
                        return osn2.GetString();
                    break;

                case "tiktok":
                    if (root.TryGetProperty("id", out var tid0) && (tid0.ValueKind is JsonValueKind.String or JsonValueKind.Number))
                        return tid0.ToString();
                    if (root.TryGetProperty("order_id", out var oid) && (oid.ValueKind is JsonValueKind.String or JsonValueKind.Number))
                        return oid.ToString();
                    if (root.TryGetProperty("orderId", out var oid2) && oid2.ValueKind == JsonValueKind.String)
                        return oid2.GetString();
                    if (root.TryGetProperty("order_number", out var on) && on.ValueKind == JsonValueKind.String)
                        return on.GetString();
                    if (root.TryGetProperty("orderNumber", out var on2) && on2.ValueKind == JsonValueKind.String)
                        return on2.GetString();
                    break;

                case "lazada":
                    if (root.TryGetProperty("order_id", out var lid) && (lid.ValueKind == JsonValueKind.String || lid.ValueKind == JsonValueKind.Number))
                        return lid.ToString();
                    if (root.TryGetProperty("orderId", out var lid2) && (lid2.ValueKind == JsonValueKind.String || lid2.ValueKind == JsonValueKind.Number))
                        return lid2.ToString();
                    if (root.TryGetProperty("trade_order_id", out var tid) && (tid.ValueKind == JsonValueKind.String || tid.ValueKind == JsonValueKind.Number))
                        return tid.ToString();
                    break;
            }
        }
        catch { /* ignore */ }
        return null;
    }

    // -----------------
    // TEST ENDPOINT: Direct JSON normalization
    // -----------------
    [HttpPost("test-shopee")]
    public async Task<IActionResult> TestShopeeNormalize(
        [FromBody] JsonElement payload,
        [FromQuery] long? shopId = null,
        [FromQuery] string? sellerId = null,
        [FromQuery] string? batchNo = "TEST")
    {
        try
        {
            var ct = HttpContext.RequestAborted;
            var rawJson = payload.GetRawText();
            
            // Normalize using ShopeeNormalizer
            var dto = ShopeeNormalizer.Normalize(payload, shopId, sellerId, 0, rawJson, batchNo);
            
            // Try to upsert to database
            var result = await _writer.UpsertFromShopeeRawAsync(shopId, sellerId, rawJson, batchNo, ct);
            
            return Ok(new
            {
                success = true,
                message = "Normalization successful",
                outcome = result.Outcome.ToString(),
                unifiedOrderId = result.UnifiedOrderId,
                externalOrderId = result.ExternalOrderId,
                normalizedData = new
                {
                    channel = dto.Channel,
                    orderSn = dto.ExternalOrderId,
                    orderStatus = dto.OrderStatus,
                    buyerUserId = dto.BuyerUserId,
                    buyerUsername = dto.BuyerUsername,
                    buyerName = dto.BuyerName,
                    totalAmount = dto.TotalAmount,
                    currency = dto.Currency,
                    itemCount = dto.Items?.Count ?? 0,
                    paymentMethod = dto.PaymentMethod,
                    shippingProvider = dto.ShipmentProvider
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                error = ex.Message,
                innerError = ex.InnerException?.Message,
                stack = ex.StackTrace,
                type = ex.GetType().FullName
            });
        }
    }

    [HttpPost("test-tiktok")]
    public async Task<IActionResult> TestTikTokNormalize(
        [FromBody] JsonElement payload,
        [FromQuery] long? shopId = null,
        [FromQuery] string? sellerId = null,
        [FromQuery] string? batchNo = "TEST")
    {
        try
        {
            var ct = HttpContext.RequestAborted;
            var rawJson = payload.GetRawText();
            
            // Normalize using TikTokNormalizer
            var dto = TikTokNormalizer.Normalize(payload, shopId, sellerId, 0, rawJson, batchNo);
            
            // Try to upsert to database
            var result = await _writer.UpsertFromTiktokRawAsync(shopId, sellerId, rawJson, batchNo, ct);
            
            return Ok(new
            {
                success = true,
                message = "TikTok Normalization successful",
                outcome = result.Outcome.ToString(),
                unifiedOrderId = result.UnifiedOrderId,
                externalOrderId = result.ExternalOrderId,
                normalizedData = new
                {
                    channel = dto.Channel,
                    orderId = dto.ExternalOrderId,
                    orderStatus = dto.OrderStatus,
                    buyerUserId = dto.BuyerUserId,
                    buyerEmail = dto.BuyerEmail,
                    buyerName = dto.BuyerName,
                    totalAmount = dto.TotalAmount,
                    currency = dto.Currency,
                    itemCount = dto.Items?.Count ?? 0,
                    paymentMethod = dto.PaymentMethod,
                    shippingProvider = dto.ShipmentProvider
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                error = ex.Message,
                innerError = ex.InnerException?.Message,
                stack = ex.StackTrace,
                type = ex.GetType().FullName
            });
        }
    }
}
