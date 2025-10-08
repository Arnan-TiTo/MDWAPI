using MDWAPI.Entities;
using MDWAPI.Models;
using MDWAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
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

    public NormalizeController(OrderNormalizeService svc, IHttpClientFactory httpFactory, IMemoryCache cache)
    {
        _svc = svc;
        _httpFactory = httpFactory;
        _cache = cache;
    }
   
    // ========================
    // 1) ยิง JSON native ตรงเข้า normalizer
    // ========================

    // POST /api/market/normalize/shopee
    [HttpPost("shopee")]
    public async Task<IActionResult> NormalizeShopee(
        [FromQuery] long? shopId,
        [FromQuery] string? sellerId,
        [FromQuery] string? batchNo)
    {
        using var reader = new StreamReader(Request.Body);
        var raw = await reader.ReadToEndAsync();
        var id = await _svc.NormalizeShopeeAsync(shopId, sellerId, raw, batchNo, HttpContext.RequestAborted);
        return Ok(new { unifiedOrderId = id });
    }

    // POST /api/market/normalize/tiktok
    [HttpPost("tiktok")]
    public async Task<IActionResult> NormalizeTiktok(
        [FromQuery] long? shopId,
        [FromQuery] string? sellerId,
        [FromQuery] string? batchNo)
    {
        using var reader = new StreamReader(Request.Body);
        var raw = await reader.ReadToEndAsync();
        var id = await _svc.NormalizeTiktokAsync(shopId, sellerId, raw, batchNo, HttpContext.RequestAborted);
        return Ok(new { unifiedOrderId = id });
    }

    // POST /api/market/normalize/lazada
    [HttpPost("lazada")]
    public async Task<IActionResult> NormalizeLazada(
        [FromQuery] long? shopId,
        [FromQuery] string? sellerId,
        [FromQuery] string? batchNo)
    {
        using var reader = new StreamReader(Request.Body);
        var raw = await reader.ReadToEndAsync();
        var id = await _svc.NormalizeLazadaAsync(shopId, sellerId, raw, batchNo, HttpContext.RequestAborted);
        return Ok(new { unifiedOrderId = id });
    }

    // ========================
    // 2) Proxy by-ref: เซิร์ฟเวอร์ไป GET detail เอง แล้ว normalize ต่อ
    // ========================

    // POST /api/market/normalize/by-ref?platform=Shopee|TikTok|Lazada&shopId=...&orderRef=...&sellerId=...&batchNo=...&select=...&env=...
    // ========================
    // 2) Proxy by-ref
    // ========================
    [HttpPost("by-ref")]
    public async Task<IActionResult> NormalizeByRef(
        [FromQuery] string platform,
        [FromQuery] long shopId,
        [FromQuery] string orderRef,
        [FromQuery] string? sellerId,
        [FromQuery] string? batchNo,
        [FromQuery] string? select,
        [FromQuery] string? env,
        [FromServices] IIngestionAuditService audit
    )
    {
        var ct = HttpContext.RequestAborted;
        var client = _httpFactory.CreateClient("OrdersApi");
        if (Request.Headers.TryGetValue("Authorization", out var auth))
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", auth.ToString());

        // begin audit (ได้ batchNo อัตโนมัติหากไม่ส่งมา)
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
        var transId = await audit.BeginAsync(trans, ct);   // << trans.BatchNo ถูกกำหนดแน่นอน ณ จุดนี้

        // refresh token
        await RefreshTokenIfNeededAsync(platform, shopId , sellerId, env, ct);

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

            return StatusCode((int)resp.StatusCode, new
            {
                message = "Fetch detail failed",
                detailUrl,
                err = json,
                batchNo = trans.BatchNo           // << include
            });
        }

        List<string> natives;
        using (var doc = JsonDocument.Parse(json))
        {
            var roots = string.IsNullOrWhiteSpace(select) ? new List<JsonElement> { doc.RootElement } : SelectPathInDoc(doc.RootElement, select);
            natives = ExtractNativeOrdersInDoc(platform, roots);
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

            return BadRequest(new { message = "no native order object", detailUrl, batchNo = trans.BatchNo }); // <<
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

            return Ok(new
            {
                count = 0,
                unifiedOrderIds = Array.Empty<long>(),
                note = "mismatch external id",
                batchNo = trans.BatchNo            // <<
            });
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

            await audit.AddItemAsync(transId, new UnifiedOrderTransItem
            {
                OrderRef = orderRef,
                ExternalOrderId = r.ExternalOrderId,
                RawHash = r.RawHash,
                UnifiedOrderId = r.UnifiedOrderId,
                Result = r.Outcome.ToString()
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
                batchNo = trans.BatchNo            // <<
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
            return StatusCode(500, new { message = "normalize failed", error = ex.Message, batchNo = trans.BatchNo }); // <<
        }
    }

    // ========================
    // 3) Batch: ไป GET /orders/list → loop detail → normalize
    // ========================

    // POST /api/market/normalize/by-list?platform=Shopee&shopId=225987929&timeRangeField=update_time&timeFrom=...&timeTo=...&pageSize=50
    // optional: &sellerId=sho001&batchNo=TEST&env=sandbox&listSelect=data.orders&detailSelect=data.order
    [HttpPost("by-list")]
    public async Task<IActionResult> NormalizeByList(
        [FromQuery] string platform,
        [FromQuery] long? shopId,
        [FromQuery] string timeRangeField,
        [FromQuery] long timeFrom,
        [FromQuery] long timeTo,

        // ต้องมาก่อน optional
        [FromServices] IIngestionAuditService audit,

        // optional
        [FromQuery] int pageSize = 50,
        [FromQuery] string? sellerId = null,
        [FromQuery] string? batchNo = null,
        [FromQuery] string? env = null,
        [FromQuery] string? listSelect = null,
        [FromQuery] string? detailSelect = null
    )
    {
        var ct = HttpContext.RequestAborted;
        var client = _httpFactory.CreateClient("OrdersApi");
        if (Request.Headers.TryGetValue("Authorization", out var auth))
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", auth.ToString());

        // Begin audit (ถ้า batchNo ซ้ำ IngestionAuditService จะสลับเป็น auto และ gen ให้)
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
        var transId = await audit.BeginAsync(trans, ct);   // << ได้ trans.BatchNo แน่นอน ณ จุดนี้

        // refresh token
        await RefreshTokenIfNeededAsync(platform, shopId ?? 0, sellerId, env, ct);

        var listUrl = $"/api/market/orders/list?platform={Uri.EscapeDataString(platform)}&shopId={shopId}&timeRangeField={Uri.EscapeDataString(timeRangeField)}&timeFrom={timeFrom}&timeTo={timeTo}&pageSize={pageSize}"
                    + (string.IsNullOrWhiteSpace(sellerId) ? "" : $"&sellerId={Uri.EscapeDataString(sellerId)}")
                    + (string.IsNullOrWhiteSpace(env) ? "" : $"&env={Uri.EscapeDataString(env)}");

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

        List<string> orderRefs;
        using (var doc = JsonDocument.Parse(listJson))
        {
            var roots = string.IsNullOrWhiteSpace(listSelect) ? new List<JsonElement> { doc.RootElement } : SelectPathInDoc(doc.RootElement, listSelect);
            orderRefs = ExtractOrderRefsInDoc(platform, roots)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Where(s => IsLikelyOrderRef(platform, s))
                .Distinct()
                .ToList();
        }

        var totalRefs = orderRefs.Count;
        int attempted = 0, created = 0, updated = 0, unchanged = 0, failed = 0;
        var unifiedIds = new List<long>();
        var sellerIdQs = string.IsNullOrWhiteSpace(sellerId) ? "" : $"&sellerId={Uri.EscapeDataString(sellerId)}";
        var envQs = string.IsNullOrWhiteSpace(env) ? "" : $"&env={Uri.EscapeDataString(env)}";

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
                    var dRoots = string.IsNullOrWhiteSpace(detailSelect) ? new List<JsonElement> { dDoc.RootElement } : SelectPathInDoc(dDoc.RootElement, detailSelect);
                    natives = ExtractNativeOrdersInDoc(platform, dRoots);
                    if (natives.Count == 0 && platform.Equals("Shopee", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var g in new[] { "data", "data.order", "result", "result.order", "response", "response.order", "orders.0", "data.orders.0" })
                        {
                            var gs = SelectPathInDoc(dDoc.RootElement, g);
                            if (gs.Count == 0) continue;
                            natives = ExtractNativeOrdersInDoc(platform, gs);
                            if (natives.Count > 0) break;
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

                // <<< ใช้ trans.BatchNo เสมอ >>>
                var r = platform.ToLowerInvariant() switch
                {
                    "shopee" => await _svc.NormalizeShopeeWithResultAsync(shopId, sellerId, raw, trans.BatchNo, ct),
                    "tiktok" => await _svc.NormalizeTiktokWithResultAsync(shopId, sellerId, raw, trans.BatchNo, ct),
                    "lazada" => await _svc.NormalizeLazadaWithResultAsync(shopId, sellerId, raw, trans.BatchNo, ct),
                    _ => throw new ArgumentException("Unsupported platform")
                };

                await audit.AddItemAsync(transId, new UnifiedOrderTransItem
                {
                    OrderRef = orderRef,
                    ExternalOrderId = r.ExternalOrderId,
                    RawHash = r.RawHash,
                    UnifiedOrderId = r.UnifiedOrderId,
                    Result = r.Outcome.ToString()
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
            h.TotalRefs = totalRefs;
            h.Attempted = attempted;
            h.CreatedCount = created;
            h.UpdatedCount = updated;
            h.UnchangedCount = unchanged;
            h.FailedCount = failed;
            h.Notes = $"RangeField={timeRangeField}, From={timeFrom}, To={timeTo}";
        }, ct);

        return Ok(new
        {
            platform,
            shopId,
            timeRangeField,
            timeFrom,
            timeTo,
            batchNo = trans.BatchNo,                 // <<< ใส่ใน response
            totalRefs,
            inserted = created + updated + unchanged,
            created,
            updated,
            unchanged,
            failed,
            unifiedOrderIds = unifiedIds
        });
    }

    // ========================
    // Helpers
    // ========================
    private async Task<bool> RefreshTokenIfNeededAsync(
        string platform, long shopId, string? sellerId, string? env, CancellationToken ct,
        TimeSpan? cooldown = null)
    {
        // กันยิงถี่ด้วย cache
        var cd = cooldown ?? TimeSpan.FromMinutes(10);
        var cacheKey = $"auth-refresh:{platform}:{shopId}:{sellerId}:{env}";
        if (_cache.TryGetValue(cacheKey, out _)) return false; // เพิ่ง refresh ไป

        var client = _httpFactory.CreateClient("OrdersApi");
        if (Request.Headers.TryGetValue("Authorization", out var auth))
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", auth.ToString());

        var sellerIdQs = string.IsNullOrWhiteSpace(sellerId) ? "" : $"&sellerId={Uri.EscapeDataString(sellerId)}";
        var envQs = string.IsNullOrWhiteSpace(env) ? "" : $"&env={Uri.EscapeDataString(env)}";
        var url = $"/api/market/auth/refresh?platform={Uri.EscapeDataString(platform)}&shopId={shopId}{sellerIdQs}{envQs}";

        using var resp = await client.GetAsync(url, ct);
        // ไม่ต้อง fail งานทั้งหมดถ้า refresh ไม่ผ่าน — ให้ไปลุ้นตอนยิงจริงอีกที
        if (resp.IsSuccessStatusCode)
        {
            _cache.Set(cacheKey, true, cd); // กันยิงซ้ำตาม cooldown
            return true;
        }
        return false;
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
            case "tiktok": return new[] { "order_id", "orderId", "order_number", "orderNumber" };
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
                    if (root.TryGetProperty("order_id", out var oid) && oid.ValueKind == JsonValueKind.String)
                        return oid.GetString();
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
}
