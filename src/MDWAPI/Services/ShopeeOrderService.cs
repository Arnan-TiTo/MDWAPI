using MDWAPI.Helpers;
using MDWAPI.Repos;
using Microsoft.AspNetCore.WebUtilities;

namespace MDWAPI.Services;

public class ShopeeOrderService
{
    private const string ClientName = "Shopee";

    private readonly IHttpClientFactory _http;
    private readonly IShopRepo _shopRepo;
    private readonly IPartnerRepo _partnerRepo;
    private readonly ChannelTokenResolver _resolver;
    private readonly ILogger<ShopeeOrderService> _log;

    private static class ShopeeOptionalFields
    {
        // ใช้กับ get_order_detail เท่านั้น
        public const string Detail =
            "buyer_username,recipient_address,payment_method,buyer_user_id,cod," +
            "estimated_shipping_fee,days_to_ship,item_list,package_list,note," +
            "note_update_time,message_to_seller,region,reverse_shipping_fee," +
            "actual_shipping_fee_confirmed,invoice_data,order_status,pickup_done_time," +
            "pay_time,shipping_carrier,total_amount,create_time,update_time," +
            "dropshipper,dropshipper_phone,fulfillment_flag,ship_by_date,split_up,currency";
        // get_order_list: อย่าส่งฟิลด์ที่ list ไม่รองรับ (ปลอดภัยสุด: ไม่ต้องส่ง)
        public const string List = "order_status,create_time,update_time";
    }

    public ShopeeOrderService(
        IHttpClientFactory http,
        IShopRepo shopRepo,
        IPartnerRepo partnerRepo,
        ChannelTokenResolver resolver,
        ILogger<ShopeeOrderService> log)
    {
        _http = http;
        _shopRepo = shopRepo;
        _partnerRepo = partnerRepo;
        _resolver = resolver;
        _log = log;
    }

    // ====== Public APIs (Raw string JSON) ======

    public async Task<string> GetShopProfileRawAsync(long shopId, CancellationToken ct = default)
    {
        var (url, http) = await BuildSignedGetAsync(
            apiPath: ShopeeApiPaths.ShopGetProfile,
            shopId: shopId,
            extraQuery: null!,
            ct: ct);

        var res = await http.GetAsync(url, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            _log.LogWarning("Shopee shop/get_profile failed: {Status} {Body}", res.StatusCode, body);
            throw new HttpRequestException($"Shopee shop/get_profile failed: {(int)res.StatusCode}");
        }
        return body;
    }

    public async Task<string> GetLogisticsChannelListRawAsync(long shopId, CancellationToken ct = default)
    {
        var (url, http) = await BuildSignedGetAsync(
            apiPath: ShopeeApiPaths.LogisticsGetChannelList,
            shopId: shopId,
            extraQuery: null!,
            ct: ct);

        var res = await http.GetAsync(url, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            _log.LogWarning("Shopee logistics/get_channel_list failed: {Status} {Body}", res.StatusCode, body);
            throw new HttpRequestException($"Shopee logistics/get_channel_list failed: {(int)res.StatusCode}");
        }
        return body;
    }

    // detail
    public async Task<string> GetOrderDetailRawAsync(long shopId, string orderSn, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(orderSn))
            throw new ArgumentException("orderSn is required", nameof(orderSn));

        var (url, http) = await BuildSignedGetAsync(
            apiPath: ShopeeApiPaths.OrderGetDetail,
            shopId: shopId,
            extraQuery: new()
            {
                ["order_sn_list"] = orderSn,
                ["request_order_status_pending"] = "true",
                ["response_optional_fields"] = ShopeeOptionalFields.Detail
            },
            ct: ct);

        var res = await http.GetAsync(url, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            _log.LogWarning("Shopee get_order_detail failed: {Status} {Body}", res.StatusCode, body);
            throw new HttpRequestException($"Shopee get_order_detail failed: {(int)res.StatusCode}");
        }
        return body;
    }

    // list
    public async Task<string> GetOrderListRawAsync(
        long shopId,
        string timeRangeField,
        long timeFrom,
        long timeTo,
        int pageSize = 50,
        string? cursor = null,
        string? orderStatus = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(timeRangeField))
            throw new ArgumentException("timeRangeField is required", nameof(timeRangeField));

        var query = new Dictionary<string, string?>
        {
            ["time_range_field"] = timeRangeField,
            ["time_from"] = timeFrom.ToString(),
            ["time_to"] = timeTo.ToString(),
            ["page_size"] = pageSize.ToString()
            // ปลอดภัยสุด: ไม่ส่ง response_optional_fields สำหรับ list
            // ถ้าจำเป็นจริงๆ ให้ใช้เฉพาะที่รองรับ เช่น:
            // ["response_optional_fields"] = ShopeeOptionalFields.List
        };
        if (!string.IsNullOrWhiteSpace(cursor)) query["cursor"] = cursor;
        if (!string.IsNullOrWhiteSpace(orderStatus)) query["order_status"] = orderStatus;

        var (url, http) = await BuildSignedGetAsync(
            apiPath: ShopeeApiPaths.OrderGetList,
            shopId: shopId,
            extraQuery: query,
            ct: ct);

        var res = await http.GetAsync(url, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            _log.LogWarning("Shopee get_order_list failed: {Status} {Body}", res.StatusCode, body);
            throw new HttpRequestException($"Shopee get_order_list failed: {(int)res.StatusCode}");
        }
        return body;
    }

    // ====== Internal: signed URL ======

    private async Task<(string url, HttpClient http)> BuildSignedGetAsync(
        string apiPath,
        long shopId,
        Dictionary<string, string?>? extraQuery,
        CancellationToken ct)
    {
        var (partnersId, accountIdBig, _) = await _shopRepo.GetShopBindingAsync(shopId, ct);
        if (accountIdBig is null || accountIdBig.Value != shopId)
            _log.LogDebug("Shop binding accountIdBig mismatch. input={Input} bound={Bound}", shopId, accountIdBig);

        var cfg = await _partnerRepo.GetConfigByPartnersIdAsync(partnersId, ct)
                  ?? throw new InvalidOperationException($"Partners config not found: {partnersId}");
        if (cfg.PartnerId is null || string.IsNullOrWhiteSpace(cfg.PartnerKey))
            throw new InvalidOperationException("Shopee PartnerId/PartnerKey is required");

        var (accessToken, tokenEnv, _, _) = await _resolver.GetAccessTokenAsync(
            channel: "shopee",
            environment: cfg.Environment ?? "prod",
            partnerId: cfg.PartnerId,
            appKey: null,
            accountIdBig: shopId,
            accountIdStr: null,
            ct: ct);

        var host = _resolver.HostFor("shopee", tokenEnv);
        var ts = UnixTime.NowSeconds();

        var sign = ShopeeSign.BuildShopSign(
            partnerId: cfg.PartnerId.Value,
            partnerKey: cfg.PartnerKey!,
            apiPath: apiPath,
            timestamp: ts,
            accessToken: accessToken,
            shopId: shopId,
            mode: ShopeeKeyMode.RawString
        );

        var baseQuery = new Dictionary<string, string?>
        {
            ["partner_id"] = cfg.PartnerId.Value.ToString(),
            ["timestamp"] = ts.ToString(),
            ["sign"] = sign,
            ["access_token"] = accessToken,
            ["shop_id"] = shopId.ToString()
        };

        if (extraQuery is not null)
            foreach (var kv in extraQuery) baseQuery[kv.Key] = kv.Value;

        var url = QueryHelpers.AddQueryString($"{host}{apiPath}", baseQuery);

        var http = _http.CreateClient(ClientName);
        return (url, http);
    }

    private async Task<(string url, HttpClient http)> BuildSignedPostAsync(
        string apiPath,
        long shopId,
        Dictionary<string, string?>? extraQuery,
        CancellationToken ct)
    {
        var (partnersId, accountIdBig, _) = await _shopRepo.GetShopBindingAsync(shopId, ct);
        if (accountIdBig is null || accountIdBig.Value != shopId)
            _log.LogDebug("Shop binding accountIdBig mismatch. input={Input} bound={Bound}", shopId, accountIdBig);

        var cfg = await _partnerRepo.GetConfigByPartnersIdAsync(partnersId, ct)
                  ?? throw new InvalidOperationException($"Partners config not found: {partnersId}");
        if (cfg.PartnerId is null || string.IsNullOrWhiteSpace(cfg.PartnerKey))
            throw new InvalidOperationException("Shopee PartnerId/PartnerKey is required");

        var (accessToken, tokenEnv, _, _) = await _resolver.GetAccessTokenAsync(
            channel: "shopee",
            environment: cfg.Environment ?? "prod",
            partnerId: cfg.PartnerId,
            appKey: null,
            accountIdBig: shopId,
            accountIdStr: null,
            ct: ct);

        var host = _resolver.HostFor("shopee", tokenEnv);
        var ts = UnixTime.NowSeconds();

        var sign = ShopeeSign.BuildShopSign(
            partnerId: cfg.PartnerId.Value,
            partnerKey: cfg.PartnerKey!,
            apiPath: apiPath,
            timestamp: ts,
            accessToken: accessToken,
            shopId: shopId,
            mode: ShopeeKeyMode.RawString
        );

        var baseQuery = new Dictionary<string, string?>
        {
            ["partner_id"] = cfg.PartnerId.Value.ToString(),
            ["timestamp"] = ts.ToString(),
            ["sign"] = sign,
            ["access_token"] = accessToken,
            ["shop_id"] = shopId.ToString()
        };

        if (extraQuery is not null)
            foreach (var kv in extraQuery) baseQuery[kv.Key] = kv.Value;

        var url = QueryHelpers.AddQueryString($"{host}{apiPath}", baseQuery);

        var http = _http.CreateClient(ClientName);
        return (url, http);
    }

    // ====== Order Actions (POST) ======

    /// <summary>
    /// POST /api/v2/order/cancel_order
    /// ยกเลิก order ก่อนจัดส่ง
    /// </summary>
    public async Task<string> CancelOrderAsync(long shopId, string orderSn, string cancelReason, object? itemList = null, CancellationToken ct = default)
    {
        var (url, http) = await BuildSignedPostAsync(
            apiPath: ShopeeApiPaths.OrderCancelOrder,
            shopId: shopId,
            extraQuery: null,
            ct: ct);

        var body = itemList is not null
            ? new { order_sn = orderSn, cancel_reason = cancelReason, item_list = itemList }
            : (object)new { order_sn = orderSn, cancel_reason = cancelReason };

        var res = await http.PostJsonAsync(url, body, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            _log.LogWarning("Shopee cancel_order failed: {Status} {Body}", res.StatusCode, text);
            throw new HttpRequestException($"Shopee cancel_order failed: {(int)res.StatusCode}");
        }
        return text;
    }

    /// <summary>
    /// POST /api/v2/order/handle_buyer_cancellation
    /// ACCEPT or REJECT buyer cancellation request
    /// </summary>
    public async Task<string> HandleBuyerCancellationAsync(long shopId, string orderSn, string operation, CancellationToken ct = default)
    {
        var (url, http) = await BuildSignedPostAsync(
            apiPath: ShopeeApiPaths.OrderHandleBuyerCancellation,
            shopId: shopId,
            extraQuery: null,
            ct: ct);

        var body = new { order_sn = orderSn, operation };

        var res = await http.PostJsonAsync(url, body, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            _log.LogWarning("Shopee handle_buyer_cancellation failed: {Status} {Body}", res.StatusCode, text);
            throw new HttpRequestException($"Shopee handle_buyer_cancellation failed: {(int)res.StatusCode}");
        }
        return text;
    }

    /// <summary>
    /// POST /api/v2/order/set_note
    /// ตั้ง note ให้ order
    /// </summary>
    public async Task<string> SetNoteAsync(long shopId, string orderSn, string note, CancellationToken ct = default)
    {
        var (url, http) = await BuildSignedPostAsync(
            apiPath: ShopeeApiPaths.OrderSetNote,
            shopId: shopId,
            extraQuery: null,
            ct: ct);

        var body = new { order_sn = orderSn, note };

        var res = await http.PostJsonAsync(url, body, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            _log.LogWarning("Shopee set_note failed: {Status} {Body}", res.StatusCode, text);
            throw new HttpRequestException($"Shopee set_note failed: {(int)res.StatusCode}");
        }
        return text;
    }
}

