using System.Net.Http.Headers;
using Microsoft.AspNetCore.WebUtilities;
using MDWAPI.Repos;

namespace MDWAPI.Services;

public class TiktokOrderService
{
    // ชื่อ client สำหรับ IHttpClientFactory
    private const string ClientName = "TikTok";

    // พาธค้นหารายการคำสั่งซื้อ
    private const string ApiOrdersSearch = "/api/orders/search";

    // พาธรายละเอียดคำสั่งซื้อ
    private const string ApiOrderDetailQuery = "/api/orders/detail/query";

    // ฟิลด์ฉีดพึ่งพา
    private readonly IHttpClientFactory _http;
    private readonly IShopRepo _shopRepo;
    private readonly IPartnerRepo _partnerRepo;
    private readonly ChannelTokenResolver _resolver;
    private readonly ILogger<TiktokOrderService> _log;

    // ตัวสร้าง
    public TiktokOrderService(
        IHttpClientFactory http,
        IShopRepo shopRepo,
        IPartnerRepo partnerRepo,
        ChannelTokenResolver resolver,
        ILogger<TiktokOrderService> log)
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

        var (http, url) = await CreateAuthedRequestAsync(
            shopId,
            ApiOrderDetailQuery,
            new Dictionary<string, string?> { ["order_id"] = orderId },
            ct);

        var res = await http.GetAsync(url, ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
        {
            _log.LogWarning("TikTok orders/detail/query failed: {Status} {Body}", res.StatusCode, body);
            throw new HttpRequestException($"TikTok orders/detail/query failed: {(int)res.StatusCode}");
        }

        return body;
    }

    // ดึงรายการคำสั่งซื้อแบบ raw JSON
    // time_from/time_to เป็น Unix seconds หรือรูปแบบที่บัญชีคุณรองรับ (ปรับพารามิเตอร์ตามสเปกจริงได้)
    public async Task<string> GetOrderListRawAsync(
        long shopId,
        long timeFrom,
        long timeTo,
        int pageSize = 50,
        string? cursor = null,
        string? status = null,
        CancellationToken ct = default)
    {
        var qs = new Dictionary<string, string?>
        {
            ["time_from"] = timeFrom.ToString(),
            ["time_to"] = timeTo.ToString(),
            ["page_size"] = pageSize.ToString()
        };

        if (!string.IsNullOrWhiteSpace(cursor))
            qs["cursor"] = cursor;

        if (!string.IsNullOrWhiteSpace(status))
            qs["status"] = status;

        var (http, url) = await CreateAuthedRequestAsync(shopId, ApiOrdersSearch, qs, ct);

        var res = await http.GetAsync(url, ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
        {
            _log.LogWarning("TikTok orders/search failed: {Status} {Body}", res.StatusCode, body);
            throw new HttpRequestException($"TikTok orders/search failed: {(int)res.StatusCode}");
        }

        return body;
    }

    // สร้าง HttpClient ที่ใส่ Authorization: Bearer {access_token} และสร้าง URL พร้อม query
    private async Task<(HttpClient http, string url)> CreateAuthedRequestAsync(
        long inputShopId,
        string apiPath,
        Dictionary<string, string?> query,
        CancellationToken ct)
    {
        var (partnersId, _, accountIdStr) = await _shopRepo.GetShopBindingAsync(inputShopId, ct);
        var cfg = await _partnerRepo.GetConfigByPartnersIdAsync(partnersId, ct)
                  ?? throw new InvalidOperationException($"Partners config not found: {partnersId}");

        var accountKey = accountIdStr ?? inputShopId.ToString();

        var (accessToken, tokenEnv, _, _) = await _resolver.GetAccessTokenAsync(
            channel: "tiktok",
            environment: cfg.Environment,
            partnerId: null,
            appKey: null,
            accountIdBig: null,
            accountIdStr: accountKey,
            ct: ct);

        var host = _resolver.HostFor("tiktok", tokenEnv);

        var url = QueryHelpers.AddQueryString($"{host}{apiPath}", query);

        var http = _http.CreateClient(ClientName);
        http.BaseAddress = new Uri(host);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        http.Timeout = TimeSpan.FromSeconds(30);

        return (http, url);
    }
}
