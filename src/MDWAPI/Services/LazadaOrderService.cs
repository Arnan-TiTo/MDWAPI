using Microsoft.AspNetCore.WebUtilities;
using MDWAPI.Repos;

namespace MDWAPI.Services;

public class LazadaOrderService
{
    // ชื่อ client สำหรับ IHttpClientFactory
    private const string ClientName = "Lazada";

    // พาธสำหรับดึงรายการคำสั่งซื้อ
    private const string ApiOrdersGet = "/rest/orders/get";

    // พาธสำหรับดึงรายละเอียดคำสั่งซื้อ
    private const string ApiOrderGet = "/rest/order/get";

    // พาธสำหรับดึงรายการสินค้าในคำสั่งซื้อ
    private const string ApiOrderItemsGet = "/rest/order/items/get";

    // ฟิลด์ฉีดพึ่งพา
    private readonly IHttpClientFactory _http;
    private readonly IShopRepo _shopRepo;
    private readonly IPartnerRepo _partnerRepo;
    private readonly ChannelTokenResolver _resolver;
    private readonly ILogger<LazadaOrderService> _log;

    // ตัวสร้าง
    public LazadaOrderService(
        IHttpClientFactory http,
        IShopRepo shopRepo,
        IPartnerRepo partnerRepo,
        ChannelTokenResolver resolver,
        ILogger<LazadaOrderService> log)
    {
        _http = http;
        _shopRepo = shopRepo;
        _partnerRepo = partnerRepo;
        _resolver = resolver;
        _log = log;
    }

    // ดึงรายละเอียดคำสั่งซื้อแบบ raw JSON
    public async Task<string> GetOrderDetailRawAsync(
        long shopId,
        string orderId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            throw new ArgumentException("orderId is required", nameof(orderId));

        var query = new Dictionary<string, string?>
        {
            ["order_id"] = orderId
        };

        var (url, http) = await BuildSignedGetAsync(shopId, ApiOrderGet, query, ct);

        var res = await http.GetAsync(url, ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
        {
            _log.LogWarning("Lazada order/get failed: {Status} {Body}", res.StatusCode, body);
            throw new HttpRequestException($"Lazada order/get failed: {(int)res.StatusCode}");
        }

        return body;
    }

    // ดึงรายการคำสั่งซื้อแบบ raw JSON
    // created_after/created_before ใช้ ISO-8601 ตามสเปก Lazada (เช่น 2025-09-30T00:00:00+07:00)
    public async Task<string> GetOrderListRawAsync(
        long shopId,
        string createdAfterIso,
        string createdBeforeIso,
        int offset = 0,
        int limit = 50,
        string? status = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(createdAfterIso) || string.IsNullOrWhiteSpace(createdBeforeIso))
            throw new ArgumentException("createdAfterIso and createdBeforeIso are required");

        var query = new Dictionary<string, string?>
        {
            ["created_after"] = createdAfterIso,
            ["created_before"] = createdBeforeIso,
            ["offset"] = offset.ToString(),
            ["limit"] = limit.ToString()
        };

        if (!string.IsNullOrWhiteSpace(status))
            query["status"] = status;

        var (url, http) = await BuildSignedGetAsync(shopId, ApiOrdersGet, query, ct);

        var res = await http.GetAsync(url, ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
        {
            _log.LogWarning("Lazada orders/get failed: {Status} {Body}", res.StatusCode, body);
            throw new HttpRequestException($"Lazada orders/get failed: {(int)res.StatusCode}");
        }

        return body;
    }

    // ดึงรายการสินค้าในคำสั่งซื้อแบบ raw JSON
    public async Task<string> GetOrderItemsRawAsync(
        long shopId,
        string orderId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            throw new ArgumentException("orderId is required", nameof(orderId));

        var query = new Dictionary<string, string?>
        {
            ["order_id"] = orderId
        };

        var (url, http) = await BuildSignedGetAsync(shopId, ApiOrderItemsGet, query, ct);

        var res = await http.GetAsync(url, ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
        {
            _log.LogWarning("Lazada order/items/get failed: {Status} {Body}", res.StatusCode, body);
            throw new HttpRequestException($"Lazada order/items/get failed: {(int)res.StatusCode}");
        }

        return body;
    }

    // สร้าง URL GET ที่เติม access_token อัตโนมัติ และคืน HttpClient พร้อม BaseAddress
    private async Task<(string url, HttpClient http)> BuildSignedGetAsync(
        long inputShopId,
        string apiPath,
        Dictionary<string, string?> query,
        CancellationToken ct)
    {
        var (partnersId, _, accountIdStr) = await _shopRepo.GetShopBindingAsync(inputShopId, ct);
        var cfg = await _partnerRepo.GetConfigByPartnersIdAsync(partnersId, ct)
                  ?? throw new InvalidOperationException($"Partners config not found: {partnersId}");

        var sellerId = accountIdStr ?? inputShopId.ToString();

        var (accessToken, tokenEnv, _, _) = await _resolver.GetAccessTokenAsync(
            channel: "lazada",
            environment: cfg.Environment ?? "prod",
            partnerId: null,
            appKey: null,
            accountIdBig: null,
            accountIdStr: sellerId,
            ct: ct);

        var host = _resolver.HostFor("lazada", tokenEnv);

        var qs = query is null ? new Dictionary<string, string?>() : new Dictionary<string, string?>(query);
        qs["access_token"] = accessToken;

        var url = QueryHelpers.AddQueryString($"{host}{apiPath}", qs);
        var http = _http.CreateClient(ClientName);
        http.BaseAddress = new Uri(host);
        return (url, http);
    }
}
