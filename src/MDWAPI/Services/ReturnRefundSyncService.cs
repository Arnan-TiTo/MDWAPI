using System.Text.Json;
using MDWAPI.Data;
using MDWAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Services;

/// <summary>
/// Sync return/refund data from Shopee → UnifiedReturns + update UnifiedOrders
/// </summary>
public class ReturnRefundSyncService
{
    private readonly AppDbContext _db;
    private readonly ShopeeOrderService _shopeeOrder;
    private readonly IUnifiedOrderWriter _writer;
    private readonly ILogger<ReturnRefundSyncService> _log;

    public ReturnRefundSyncService(
        AppDbContext db,
        ShopeeOrderService shopeeOrder,
        IUnifiedOrderWriter writer,
        ILogger<ReturnRefundSyncService> log)
    {
        _db = db;
        _shopeeOrder = shopeeOrder;
        _writer = writer;
        _log = log;
    }

    // สถานะที่ถือว่า "จบแล้ว" — ไม่ต้อง re-process อีก
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "CLOSED", "REFUND_PAID", "CANCELLED"
    };

    /// <summary>
    /// Sync returns from Shopee for a given shop and time range (update_time).
    /// Shopee API: max 15-day window per call → auto-chunk.
    /// page_no starts at 0 (per Shopee doc).
    /// </summary>
    public async Task<SyncReturnResult> SyncShopeeReturnsAsync(
        long shopId, long timeFrom, long timeTo, string? status = null, CancellationToken ct = default)
    {
        var result = new SyncReturnResult();
        const long MaxWindowSec = 15 * 24 * 3600; // 15 days in seconds

        // chunk time range into 15-day windows
        long windowFrom = timeFrom;
        while (windowFrom < timeTo)
        {
            long windowTo = Math.Min(windowFrom + MaxWindowSec, timeTo);

            _log.LogInformation(
                "SyncShopeeReturns: shop={ShopId} window={From}~{To}",
                shopId, windowFrom, windowTo);

            await FetchReturnWindowAsync(shopId, windowFrom, windowTo, status, result, ct);

            windowFrom = windowTo; // move to next window
        }

        _log.LogInformation(
            "SyncShopeeReturns done: shop={ShopId} found={Found} processed={Processed} failed={Failed} skipped={Skipped}",
            shopId, result.TotalFound, result.Processed, result.Failed, result.Skipped);

        return result;
    }

    /// <summary>
    /// Fetch one 15-day window of returns (handles pagination).
    /// </summary>
    private async Task FetchReturnWindowAsync(
        long shopId, long timeFrom, long timeTo, string? status,
        SyncReturnResult result, CancellationToken ct)
    {
        int pageNo = 0; // Shopee doc: page_no starts at 0
        bool hasMore = true;

        while (hasMore)
        {
            string listJson;
            try
            {
                listJson = await _shopeeOrder.GetReturnListRawAsync(
                    shopId, timeFrom, timeTo, pageNo, 50, status, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "get_return_list failed: shop={ShopId} page={Page}", shopId, pageNo);
                result.Errors.Add($"get_return_list page {pageNo}: {ex.Message}");
                break;
            }

            using var listDoc = JsonDocument.Parse(listJson);
            var root = listDoc.RootElement;

            // check API error
            if (root.TryGetProperty("error", out var errEl)
                && errEl.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(errEl.GetString()))
            {
                var errCode = errEl.GetString();
                var msg = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : errCode;
                _log.LogWarning("Shopee get_return_list error: code={Code} msg={Msg}", errCode, msg);
                result.Errors.Add($"API error: [{errCode}] {msg}");
                break;
            }

            if (!root.TryGetProperty("response", out var resp))
            {
                _log.LogWarning("No response property in get_return_list");
                break;
            }

            // parse return list
            if (!resp.TryGetProperty("return_list", out var returnList)
                || returnList.ValueKind != JsonValueKind.Array)
            {
                _log.LogInformation("Empty return_list on page {Page}", pageNo);
                break;
            }

            var returns = returnList.EnumerateArray().ToList();
            if (returns.Count == 0) break;

            result.TotalFound += returns.Count;

            foreach (var retItem in returns)
            {
                long returnSn = 0;
                if (retItem.TryGetProperty("return_sn", out var rsnEl))
                    returnSn = rsnEl.GetInt64();

                string? orderSn = null;
                if (retItem.TryGetProperty("order_sn", out var osnEl))
                    orderSn = osnEl.GetString();

                if (returnSn == 0)
                {
                    result.Skipped++;
                    continue;
                }

                // ถ้า return นี้มีใน DB แล้ว และ status เป็น terminal → skip
                var existingReturn = await _db.UnifiedReturns
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Channel == "Shopee"
                                           && r.ExternalReturnId == returnSn.ToString()
                                           && r.ShopId == shopId, ct);
                if (existingReturn != null
                    && !string.IsNullOrWhiteSpace(existingReturn.ReturnStatus)
                    && TerminalStatuses.Contains(existingReturn.ReturnStatus))
                {
                    _log.LogDebug("Skip return {ReturnSn}: already {Status}",
                        returnSn, existingReturn.ReturnStatus);
                    result.Skipped++;
                    continue;
                }

                try
                {
                    await ProcessSingleReturnAsync(shopId, returnSn, orderSn, ct);
                    result.Processed++;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Failed to process return {ReturnSn}", returnSn);
                    result.Failed++;
                    result.Errors.Add($"return_sn={returnSn}: {ex.Message}");
                }

                // rate limit protection
                await Task.Delay(300, ct);
            }

            // check pagination
            hasMore = resp.TryGetProperty("more", out var moreEl)
                      && moreEl.ValueKind == JsonValueKind.True;
            pageNo++;

            if (pageNo > 100) break; // safety limit
        }
    }

    /// <summary>
    /// Sync returns for a specific order (by order_sn).
    /// Searches get_return_list (last 90 days) filtering by order_sn.
    /// Also re-fetches order detail to update UnifiedOrders.
    /// </summary>
    public async Task<SyncReturnResult> SyncShopeeReturnByOrderAsync(
        long shopId, string orderSn, string? status = null, CancellationToken ct = default)
    {
        var result = new SyncReturnResult();

        _log.LogInformation("SyncShopeeReturnByOrder: shop={ShopId} order={OrderSn}", shopId, orderSn);

        // 1. Re-fetch order detail → update UnifiedOrders (status, refundAmount)
        try
        {
            var orderDetailJson = await _shopeeOrder.GetOrderDetailRawAsync(shopId, orderSn, ct);
            var (nativeOrder, extractErr) = ExtractNativeOrder(orderDetailJson);
            if (nativeOrder != null)
            {
                await _writer.UpsertFromShopeeRawAsync(shopId, null, nativeOrder, null, ct);
                _log.LogInformation("Re-fetched order {OrderSn} before return scan", orderSn);
            }
            else
            {
                _log.LogWarning("Cannot extract order {OrderSn} before scan: {Reason}", orderSn, extractErr);
                result.Errors.Add($"re-fetch order {orderSn}: {extractErr}");
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to re-fetch order {OrderSn}", orderSn);
            result.Errors.Add($"re-fetch order: {ex.Message}");
        }

        // 2. Search returns: scan last 90 days in 15-day windows to find returns for this order
        var now = DateTimeOffset.UtcNow;
        var scanFrom = now.AddDays(-90).ToUnixTimeSeconds();
        var scanTo = now.ToUnixTimeSeconds();
        const long MaxWindowSec = 15 * 24 * 3600;
        bool foundAny = false;

        long windowFrom = scanFrom;
        while (windowFrom < scanTo && !foundAny)
        {
            long windowTo = Math.Min(windowFrom + MaxWindowSec, scanTo);
            int pageNo = 0; // Shopee doc: page_no starts at 0
            bool hasMore = true;

            while (hasMore)
            {
                string listJson;
                try
                {
                    listJson = await _shopeeOrder.GetReturnListRawAsync(shopId, windowFrom, windowTo, pageNo, 50, status, ct);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "get_return_list failed while searching for order={OrderSn}", orderSn);
                    result.Errors.Add($"get_return_list: {ex.Message}");
                    hasMore = false;
                    break;
                }

                using var listDoc = JsonDocument.Parse(listJson);
                var root = listDoc.RootElement;

                if (root.TryGetProperty("error", out var errEl)
                    && errEl.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(errEl.GetString()))
                {
                    var msg = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : errEl.GetString();
                    result.Errors.Add($"API error: {msg}");
                    hasMore = false;
                    break;
                }

                if (!root.TryGetProperty("response", out var resp)) break;
                if (!resp.TryGetProperty("return_list", out var returnList)
                    || returnList.ValueKind != JsonValueKind.Array) break;

                var returns = returnList.EnumerateArray().ToList();
                if (returns.Count == 0) break;

                result.TotalFound += returns.Count;

                // filter by order_sn
                foreach (var retItem in returns)
                {
                    string? retOrderSn = null;
                    if (retItem.TryGetProperty("order_sn", out var osnEl))
                        retOrderSn = osnEl.GetString();

                    if (!string.Equals(retOrderSn, orderSn, StringComparison.OrdinalIgnoreCase))
                        continue;

                    foundAny = true;
                    long returnSn = 0;
                    if (retItem.TryGetProperty("return_sn", out var rsnEl))
                        returnSn = rsnEl.GetInt64();

                    if (returnSn == 0) { result.Skipped++; continue; }

                    // ถ้า return นี้มีใน DB แล้ว และ status เป็น terminal → skip
                    var existingReturn = await _db.UnifiedReturns
                        .AsNoTracking()
                        .FirstOrDefaultAsync(r => r.Channel == "Shopee"
                                               && r.ExternalReturnId == returnSn.ToString()
                                               && r.ShopId == shopId, ct);
                    if (existingReturn != null
                        && !string.IsNullOrWhiteSpace(existingReturn.ReturnStatus)
                        && TerminalStatuses.Contains(existingReturn.ReturnStatus))
                    {
                        _log.LogDebug("Skip return {ReturnSn}: already {Status}",
                            returnSn, existingReturn.ReturnStatus);
                        result.Skipped++;
                        continue;
                    }

                    try
                    {
                        await ProcessSingleReturnAsync(shopId, returnSn, orderSn, ct);
                        result.Processed++;
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "Failed to process return {ReturnSn} for order {OrderSn}", returnSn, orderSn);
                        result.Failed++;
                        result.Errors.Add($"return_sn={returnSn}: {ex.Message}");
                    }

                    await Task.Delay(300, ct);
                }

                hasMore = resp.TryGetProperty("more", out var moreEl)
                          && moreEl.ValueKind == JsonValueKind.True;
                pageNo++;
                if (pageNo > 100) break;
            }

            windowFrom = windowTo; // next 15-day window
        }

        if (!foundAny)
        {
            _log.LogInformation("No returns found for order {OrderSn} in last 90 days", orderSn);
            result.Errors.Add($"No returns found for order {orderSn} in last 90 days (order detail was still updated)");
        }

        return result;
    }

    /// <summary>
    /// Extract native Shopee order JSON from API response wrapper.
    /// Returns (json, errorMessage) — errorMessage is non-null when Shopee returns an API-level error or empty list.
    /// </summary>
    private static (string? Json, string? Error) ExtractNativeOrder(string apiResponse)
    {
        using var doc = JsonDocument.Parse(apiResponse);
        var root = doc.RootElement;

        // Shopee API-level error (HTTP 200 but error field set)
        if (root.TryGetProperty("error", out var errEl)
            && errEl.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(errEl.GetString()))
        {
            var msg = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : errEl.GetString();
            return (null, $"Shopee API error [{errEl.GetString()}]: {msg}");
        }

        if (root.TryGetProperty("response", out var resp)
            && resp.TryGetProperty("order_list", out var arr)
            && arr.ValueKind == JsonValueKind.Array)
        {
            if (arr.GetArrayLength() > 0)
                return (arr[0].GetRawText(), null);

            return (null, "order_list is empty — order may not exist or token lacks permission");
        }

        // bare order object (fallback)
        if (root.TryGetProperty("order_sn", out _))
            return (apiResponse, null);

        return (null, $"Unexpected response structure: {apiResponse[..Math.Min(200, apiResponse.Length)]}");
    }

    /// <summary>
    /// Process a single return: fetch detail, upsert UnifiedReturns, re-fetch order
    /// </summary>
    private async Task ProcessSingleReturnAsync(
        long shopId, long returnSn, string? orderSn, CancellationToken ct)
    {
        // 1. Fetch return detail
        var detailJson = await _shopeeOrder.GetReturnDetailRawAsync(shopId, returnSn, ct);

        using var detailDoc = JsonDocument.Parse(detailJson);
        var root = detailDoc.RootElement;

        // check API error
        if (root.TryGetProperty("error", out var errEl)
            && errEl.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(errEl.GetString()))
        {
            var msg = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : errEl.GetString();
            throw new InvalidOperationException($"get_return_detail error: {msg}");
        }

        JsonElement detail;
        if (root.TryGetProperty("response", out var resp))
            detail = resp;
        else
            detail = root;

        // Extract fields
        var externalReturnId = returnSn.ToString();
        var externalOrderId = orderSn;
        if (detail.TryGetProperty("order_sn", out var osnEl2))
            externalOrderId = osnEl2.GetString() ?? externalOrderId;

        var returnStatus = detail.TryGetProperty("return_status", out var rsEl) ? rsEl.GetString() : null;
        var returnReason = detail.TryGetProperty("reason", out var rrEl) ? rrEl.GetString() : null;
        if (rrEl.ValueKind == JsonValueKind.Number)
            returnReason = rrEl.GetInt32().ToString();
        var textReason = detail.TryGetProperty("text_reason", out var trEl) ? trEl.GetString() : null;
        var returnType = detail.TryGetProperty("return_type", out var rtEl) ? rtEl.GetString() : null;
        var returnSolution = detail.TryGetProperty("return_solution", out var rsolEl) ? rsolEl.GetString() : null;
        var negotiationStatus = detail.TryGetProperty("negotiation_status", out var nsEl) ? nsEl.GetString() : null;

        decimal? refundAmount = null;
        if (detail.TryGetProperty("refund_amount", out var raEl))
        {
            if (raEl.ValueKind == JsonValueKind.Number) refundAmount = raEl.GetDecimal();
        }

        var currency = detail.TryGetProperty("currency", out var curEl) ? curEl.GetString() : null;

        // items & images as JSON
        string? itemsJson = null;
        if (detail.TryGetProperty("item", out var itemsEl))
            itemsJson = itemsEl.GetRawText();
        else if (detail.TryGetProperty("item_list", out var il2))
            itemsJson = il2.GetRawText();

        string? imagesJson = null;
        if (detail.TryGetProperty("images", out var imgEl))
            imagesJson = imgEl.GetRawText();
        else if (detail.TryGetProperty("user_proof", out var upEl))
            imagesJson = upEl.GetRawText();

        // timestamps
        DateTime? createdAtUtc = null;
        if (detail.TryGetProperty("create_time", out var ctEl) && ctEl.ValueKind == JsonValueKind.Number)
            createdAtUtc = DateTimeOffset.FromUnixTimeSeconds(ctEl.GetInt64()).UtcDateTime;

        DateTime? updatedAtUtc = null;
        if (detail.TryGetProperty("update_time", out var utEl) && utEl.ValueKind == JsonValueKind.Number)
            updatedAtUtc = DateTimeOffset.FromUnixTimeSeconds(utEl.GetInt64()).UtcDateTime;

        // 2. Find linked UnifiedOrder
        long? unifiedOrderId = null;
        if (!string.IsNullOrWhiteSpace(externalOrderId))
        {
            unifiedOrderId = await _db.UnifiedOrders
                .Where(o => o.Channel == "Shopee" && o.ExternalOrderId == externalOrderId)
                .Select(o => (long?)o.UnifiedOrderId)
                .FirstOrDefaultAsync(ct);
        }

        // 3. Upsert UnifiedReturns
        var existing = await _db.UnifiedReturns
            .FirstOrDefaultAsync(r => r.Channel == "Shopee"
                                   && r.ExternalReturnId == externalReturnId
                                   && r.ShopId == shopId, ct);

        if (existing is null)
        {
            var newReturn = new UnifiedReturns
            {
                UnifiedOrderId = unifiedOrderId,
                Channel = "Shopee",
                ShopId = shopId,
                ExternalOrderId = externalOrderId,
                ExternalReturnId = externalReturnId,
                ReturnStatus = returnStatus,
                ReturnReason = returnReason,
                TextReason = textReason,
                ReturnType = returnType,
                ReturnSolution = returnSolution,
                NegotiationStatus = negotiationStatus,
                RefundAmount = refundAmount,
                Currency = currency,
                ReturnItemsJson = itemsJson,
                ImagesJson = imagesJson,
                CreatedAtUtc = createdAtUtc,
                UpdatedAtUtc = updatedAtUtc,
                IngestedAtUtc = DateTime.UtcNow,
                RawJson = detailJson
            };
            _db.UnifiedReturns.Add(newReturn);
            _log.LogInformation("Created UnifiedReturn: return_sn={ReturnSn} order_sn={OrderSn} status={Status}",
                returnSn, externalOrderId, returnStatus);
        }
        else
        {
            existing.UnifiedOrderId = unifiedOrderId ?? existing.UnifiedOrderId;
            existing.ReturnStatus = returnStatus;
            existing.ReturnReason = returnReason;
            existing.TextReason = textReason;
            existing.ReturnType = returnType;
            existing.ReturnSolution = returnSolution;
            existing.NegotiationStatus = negotiationStatus;
            existing.RefundAmount = refundAmount;
            existing.Currency = currency;
            existing.ReturnItemsJson = itemsJson;
            existing.ImagesJson = imagesJson;
            existing.UpdatedAtUtc = updatedAtUtc;
            existing.IngestedAtUtc = DateTime.UtcNow;
            existing.RawJson = detailJson;
            _log.LogInformation("Updated UnifiedReturn: return_sn={ReturnSn} status={Status}",
                returnSn, returnStatus);
        }

        await _db.SaveChangesAsync(ct);

        // 4. Re-fetch order from Shopee → update UnifiedOrders (status, refundAmount, etc.)
        if (!string.IsNullOrWhiteSpace(externalOrderId))
        {
            try
            {
                var orderDetailJson = await _shopeeOrder.GetOrderDetailRawAsync(shopId, externalOrderId, ct);
                var (nativeOrder, extractErr) = ExtractNativeOrder(orderDetailJson);

                if (nativeOrder != null)
                {
                    await _writer.UpsertFromShopeeRawAsync(shopId, null, nativeOrder, null, ct);
                    _log.LogInformation("Re-fetched and updated order {OrderSn} after return sync", externalOrderId);
                }
                else
                {
                    _log.LogWarning("Cannot extract order {OrderSn} after return sync: {Reason}", externalOrderId, extractErr);
                    throw new InvalidOperationException($"order detail: {extractErr}");
                }
            }
            catch (InvalidOperationException) { throw; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to re-fetch order {OrderSn} after return sync", externalOrderId);
                throw;
            }
        }
    }
}

/// <summary>
/// Result of sync operation
/// </summary>
public class SyncReturnResult
{
    public int TotalFound { get; set; }
    public int Processed { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public List<string> Errors { get; set; } = new();
}
