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

    // ใช้ได้ทั้ง get_order_detail และ get_order_list
    private static readonly string ResponseOptionalFields = string.Join(",",
        "buyer_username", "recipient_address", "payment_method", "buyer_user_id", "cod",
        "estimated_shipping_fee", "days_to_ship", "item_list", "package_list", "note",
        "note_update_time", "message_to_seller", "region", "reverse_shipping_fee",
        "actual_shipping_fee_confirmed", "invoice_data", "order_status", "pickup_done_time",
        "pay_time", "shipping_carrier", "total_amount", "create_time", "update_time",
        "dropshipper", "dropshipper_phone", "fulfillment_flag"
    );

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

    // 1) ไม่ต้องมีออเดอร์: ใช้เทสสิทธิ์/ลายเซ็นได้
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

    // 2) ไม่ต้องมีออเดอร์ก็เรียกได้ (จะได้ลิสต์ว่าง)
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

    // 3) ต้องมี order_sn จริง
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
                ["response_optional_fields"] = ResponseOptionalFields
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

    // 4) ดึงลิสต์ออเดอร์ (จะว่างถ้าไม่มี)
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

    // ====== Internal: สร้าง URL เซ็น + HttpClient ======

    private async Task<(string url, HttpClient http)> BuildSignedGetAsync(
        string apiPath,
        long shopId,
        Dictionary<string, string?>? extraQuery,
        CancellationToken ct)
    {
        // 1) map FE shopId -> partnersId/accountIdBig
        var (partnersId, accountIdBig, _) = await _shopRepo.GetShopBindingAsync(shopId, ct);
        if (accountIdBig is null || accountIdBig.Value != shopId)
            _log.LogDebug("Shop binding accountIdBig mismatch. input={Input} bound={Bound}", shopId, accountIdBig);

        // 2) โหลด partner config
        var cfg = await _partnerRepo.GetConfigByPartnersIdAsync(partnersId, ct)
                  ?? throw new InvalidOperationException($"Partners config not found: {partnersId}");
        if (cfg.PartnerId is null || string.IsNullOrWhiteSpace(cfg.PartnerKey))
            throw new InvalidOperationException("Shopee PartnerId/PartnerKey is required");

        // 3) ดึง access_token ปัจจุบัน + env + host
        var (accessToken, tokenEnv, _, _) = await _resolver.GetAccessTokenAsync(
            channel: "shopee",
            environment: cfg.Environment,
            partnerId: cfg.PartnerId,
            appKey: null,
            accountIdBig: shopId,
            accountIdStr: null,
            ct: ct);

        var host = _resolver.HostFor("shopee", tokenEnv);
        var ts = UnixTime.NowSeconds();

        // 4) เซ็นแบบ shop-level
        var sign = ShopeeSign.BuildShopSign(
            partnerId: cfg.PartnerId.Value,
            partnerKey: cfg.PartnerKey!,
            apiPath: apiPath,
            timestamp: ts,
            accessToken: accessToken,
            shopId: shopId,
            mode: ShopeeKeyMode.RawString   // ระบุชื่อพารามิเตอร์ให้ชัด ป้องกันสลับลำดับ
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

        // HttpClient ตามชื่อ client เดิม
        var http = _http.CreateClient(ClientName);
        // ไม่จำเป็นต้องตั้ง BaseAddress ก็ได้ เพราะเรา call ด้วย absolute URL
        return (url, http);
    }
}
