using System.Net.Http.Headers;
using Microsoft.AspNetCore.WebUtilities;
using MDWAPI.Helpers;

namespace MDWAPI.Services;

/// <summary>
/// TikTok Shop Logistics (ใช้ access_token (Bearer) จาก ChannelTokens + ChannelTokenResolver)
/// </summary>
public class TiktokLogisticsService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _cfg;
    private readonly ChannelTokenResolver _resolver;
    private readonly ILogger<TiktokLogisticsService> _log;

    public TiktokLogisticsService(
        IHttpClientFactory httpFactory,
        IConfiguration cfg,
        ChannelTokenResolver resolver,
        ILogger<TiktokLogisticsService> log)
    {
        _httpFactory = httpFactory;
        _cfg = cfg;
        _resolver = resolver;
        _log = log;
    }

    private string GetEnv() => _cfg.GetValue<string>("TikTok:Environment") ?? "prod";

    /// <summary>
    /// สร้าง HttpClient ที่แนบ Authorization: Bearer {access_token} และ baseAddress ของ TikTok ตาม env
    /// </summary>
    private async Task<(HttpClient http, string host)> CreateAuthedClientAsync(
        long? accountIdBig, string? accountIdStr, CancellationToken ct)
    {
        // สำหรับ TikTok มักอิง appKey (client_key) เก็บใน ChannelTokens 
        var env = GetEnv();

        // ดึงโทเค็นจาก ChannelTokens (channel='tiktok')
        var (accessToken, environment, _, appKey) =
            await _resolver.GetAccessTokenAsync(
                channel: "tiktok",
                environment: env,
                partnerId: null,             // ไม่จำเป็นสำหรับ TikTok
                appKey: null,                // ปล่อย null เก็บ token ตามบัญชี
                accountIdBig: accountIdBig,
                accountIdStr: accountIdStr,
                ct: ct);

        var host = _resolver.HostFor("tiktok", environment);

        var http = _httpFactory.CreateClient("Shopee");
        http.BaseAddress = new Uri(host);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        http.Timeout = TimeSpan.FromSeconds(30);

        return (http, host);
    }

    // ---------------------- Ready-to-use endpoints ----------------------

    /// <summary>
    /// GET logistics/shipping_info/query
    /// </summary>
    public async Task<string> GetShippingInfoAsync(long? accountIdBig, string? accountIdStr, string orderId, CancellationToken ct = default)
    {
        var (http, host) = await CreateAuthedClientAsync(accountIdBig, accountIdStr, ct);

        var url = QueryHelpers.AddQueryString($"{host}{TiktokApiPaths.LogisticsGetShippingInfo}",
            new Dictionary<string, string?> { ["order_id"] = orderId });

        var res = await http.GetAsync(url, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        res.EnsureSuccessStatusCode();
        return body;
    }

    /// <summary>
    /// POST logistics/shipping/confirm
    /// </summary>
    public async Task<string> ConfirmShipAsync(long? accountIdBig, string? accountIdStr, object body, CancellationToken ct = default)
    {
        var (http, host) = await CreateAuthedClientAsync(accountIdBig, accountIdStr, ct);
        var url = $"{host}{TiktokApiPaths.LogisticsConfirmShip}";

        var res = await http.PostJsonAsync(url, body, ct);
        var data = await res.Content.ReadAsStringAsync(ct);
        res.EnsureSuccessStatusCode();
        return data;
    }

    /// <summary>
    /// POST logistics/shipping_document/create
    /// </summary>
    public async Task<string> CreateShippingDocumentAsync(long? accountIdBig, string? accountIdStr, object body, CancellationToken ct = default)
    {
        var (http, host) = await CreateAuthedClientAsync(accountIdBig, accountIdStr, ct);
        var url = $"{host}{TiktokApiPaths.LogisticsDocumentCreate}";

        var res = await http.PostJsonAsync(url, body, ct);
        var data = await res.Content.ReadAsStringAsync(ct);
        res.EnsureSuccessStatusCode();
        return data;
    }

    /// <summary>
    /// POST logistics/shipping_document/query
    /// </summary>
    public async Task<string> QueryShippingDocumentAsync(long? accountIdBig, string? accountIdStr, object body, CancellationToken ct = default)
    {
        var (http, host) = await CreateAuthedClientAsync(accountIdBig, accountIdStr, ct);
        var url = $"{host}{TiktokApiPaths.LogisticsDocumentQuery}";

        var res = await http.PostJsonAsync(url, body, ct);
        var data = await res.Content.ReadAsStringAsync(ct);
        res.EnsureSuccessStatusCode();
        return data;
    }

    /// <summary>
    /// POST logistics/shipping_document/download  (คืนไฟล์ byte[])
    /// </summary>
    public async Task<byte[]> DownloadShippingDocumentAsync(long? accountIdBig, string? accountIdStr, object body, CancellationToken ct = default)
    {
        var (http, host) = await CreateAuthedClientAsync(accountIdBig, accountIdStr, ct);
        var url = $"{host}{TiktokApiPaths.LogisticsDocumentDownload}";

        var res = await http.PostJsonAsync(url, body, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsByteArrayAsync(ct);
    }

    // ---------------------- Generic helpers ----------------------

    public async Task<string> GetAsync(long? accountIdBig, string? accountIdStr, string apiPath, Dictionary<string, string?> query, CancellationToken ct = default)
    {
        var (http, host) = await CreateAuthedClientAsync(accountIdBig, accountIdStr, ct);
        var url = QueryHelpers.AddQueryString($"{host}{apiPath}", query);
        var res = await http.GetAsync(url, ct);
        var data = await res.Content.ReadAsStringAsync(ct);
        res.EnsureSuccessStatusCode();
        return data;
    }

    public async Task<string> PostAsync(long? accountIdBig, string? accountIdStr, string apiPath, object body, CancellationToken ct = default)
    {
        var (http, host) = await CreateAuthedClientAsync(accountIdBig, accountIdStr, ct);
        var url = $"{host}{apiPath}";

        var res = await http.PostJsonAsync(url, body, ct);
        var data = await res.Content.ReadAsStringAsync(ct);
        res.EnsureSuccessStatusCode();
        return data;
    }
}
