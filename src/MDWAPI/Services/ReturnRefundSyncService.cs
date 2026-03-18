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

    /// <summary>
    /// Sync returns from Shopee for a given shop and time range.
    /// Returns count of returns processed.
    /// </summary>
    public async Task<SyncReturnResult> SyncShopeeReturnsAsync(
        long shopId, long timeFrom, long timeTo, CancellationToken ct = default)
    {
        var result = new SyncReturnResult();
        int pageNo = 1;
        bool hasMore = true;

        while (hasMore)
        {
            _log.LogInformation(
                "Fetching Shopee return list: shop={ShopId} page={Page} from={From} to={To}",
                shopId, pageNo, timeFrom, timeTo);

            string listJson;
            try
            {
                listJson = await _shopeeOrder.GetReturnListRawAsync(
                    shopId, timeFrom, timeTo, pageNo, 50, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "get_return_list failed for shop={ShopId} page={Page}", shopId, pageNo);
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
                var msg = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : errEl.GetString();
                _log.LogWarning("Shopee get_return_list error: {Error}", msg);
                result.Errors.Add($"API error: {msg}");
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

        _log.LogInformation(
            "SyncShopeeReturns done: shop={ShopId} found={Found} processed={Processed} failed={Failed}",
            shopId, result.TotalFound, result.Processed, result.Failed);

        return result;
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

                // Extract native order from response wrapper
                string nativeOrder;
                using var orderDoc = JsonDocument.Parse(orderDetailJson);
                var orderRoot = orderDoc.RootElement;
                if (orderRoot.TryGetProperty("response", out var oResp)
                    && oResp.TryGetProperty("order_list", out var oArr)
                    && oArr.ValueKind == JsonValueKind.Array
                    && oArr.GetArrayLength() > 0)
                {
                    nativeOrder = oArr[0].GetRawText();
                }
                else if (orderRoot.TryGetProperty("order_sn", out _))
                {
                    nativeOrder = orderDetailJson;
                }
                else
                {
                    _log.LogWarning("Cannot extract native order from re-fetch for {OrderSn}", externalOrderId);
                    return;
                }

                await _writer.UpsertFromShopeeRawAsync(shopId, null, nativeOrder, null, ct);
                _log.LogInformation("Re-fetched and updated order {OrderSn} after return sync", externalOrderId);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to re-fetch order {OrderSn} after return sync", externalOrderId);
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
