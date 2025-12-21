using Microsoft.AspNetCore.WebUtilities;
using MDWAPI.Helpers;

namespace MDWAPI.Services;

public class LazadaLogisticsService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _cfg;
    private readonly ChannelTokenResolver _resolver;
    private readonly ILogger<LazadaLogisticsService> _log;

    // ตัวสร้างบริการ (inject HttpClientFactory, Config, Resolver, Logger)
    public LazadaLogisticsService(
        IHttpClientFactory httpFactory,
        IConfiguration cfg,
        ChannelTokenResolver resolver,
        ILogger<LazadaLogisticsService> log)
    {
        _httpFactory = httpFactory;
        _cfg = cfg;
        _resolver = resolver;
        _log = log;
    }

    // อ่านค่า environment จาก appsettings ("prod" by default)
    private string GetEnv()
        => _cfg.GetValue<string>("Lazada:Environment") ?? "prod";

    // สร้าง HttpClient + คืน host + access_token จาก ChannelTokens
    private async Task<(HttpClient http, string host, string accessToken)> CreateClientWithTokenAsync(
        long? accountIdBig,
        string? accountIdStr,
        CancellationToken ct)
    {
        var env = GetEnv();

        var (accessToken, environment, _, appKey) =
            await _resolver.GetAccessTokenAsync(
                channel: "lazada",
                environment: env,
                partnerId: null,
                appKey: null,
                accountIdBig: accountIdBig,
                accountIdStr: accountIdStr,
                ct: ct);

        var host = _resolver.HostFor("lazada", environment);

        var http = _httpFactory.CreateClient("Shopee");
        http.BaseAddress = new Uri(host);
        http.Timeout = TimeSpan.FromSeconds(30);

        return (http, host, accessToken);
    }

    // เรียกดูสถานะ/เลขติดตาม (GET) — เติม access_token ใน query ให้อัตโนมัติ
    public async Task<string> GetTrackingAsync(
        string sellerId,
        Dictionary<string, string?> parameters,
        CancellationToken ct = default)
    {
        var (http, host, token) = await CreateClientWithTokenAsync(null, sellerId, ct);

        var query = parameters is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?>(parameters);

        query["access_token"] = token;

        var url = QueryHelpers.AddQueryString($"{host}{LazadaApiPaths.LogisticsGetTracking}", query);

        var res = await http.GetAsync(url, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        res.EnsureSuccessStatusCode();
        return body;
    }

    // ยืนยันการจัดส่ง (POST) — ส่ง body เป็น JSON และเติม access_token ใน query
    public async Task<string> ShipOrderAsync(
        string sellerId,
        object body,
        CancellationToken ct = default)
    {
        var (http, host, token) = await CreateClientWithTokenAsync(null, sellerId, ct);

        var url = QueryHelpers.AddQueryString(
            $"{host}{LazadaApiPaths.LogisticsShipOrder}",
            new Dictionary<string, string?> { ["access_token"] = token });

        var res = await http.PostJsonAsync(url, body, ct);
        var data = await res.Content.ReadAsStringAsync(ct);
        res.EnsureSuccessStatusCode();
        return data;
    }

    // ขอสร้าง/ดึงเอกสารจัดส่ง (GET) — บาง region ส่งพารามิเตอร์เป็น query
    public async Task<string> GetShipmentDocumentAsync(
        string sellerId,
        Dictionary<string, string?> parameters,
        CancellationToken ct = default)
    {
        var (http, host, token) = await CreateClientWithTokenAsync(null, sellerId, ct);

        var query = parameters is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?>(parameters);

        query["access_token"] = token;

        var url = QueryHelpers.AddQueryString($"{host}{LazadaApiPaths.LogisticsGetShipmentDoc}", query);

        var res = await http.GetAsync(url, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        res.EnsureSuccessStatusCode();
        return body;
    }

    // ดาวน์โหลด Waybill/Label (GET) → ไฟล์ byte[]
    public async Task<byte[]> PrintWaybillAsync(
        string sellerId,
        Dictionary<string, string?> parameters,
        CancellationToken ct = default)
    {
        var (http, host, token) = await CreateClientWithTokenAsync(null, sellerId, ct);

        var query = parameters is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?>(parameters);

        query["access_token"] = token;

        var url = QueryHelpers.AddQueryString($"{host}{LazadaApiPaths.LogisticsPrintWaybill}", query);

        var res = await http.GetAsync(url, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsByteArrayAsync(ct);
    }

    // เรียก GET แบบ generic (เติม access_token ให้อัตโนมัติ)
    public async Task<string> GetAsync(
        string sellerId,
        string apiPath,
        Dictionary<string, string?> parameters,
        CancellationToken ct = default)
    {
        var (http, host, token) = await CreateClientWithTokenAsync(null, sellerId, ct);

        var query = parameters is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?>(parameters);

        query["access_token"] = token;

        var url = QueryHelpers.AddQueryString($"{host}{apiPath}", query);

        var res = await http.GetAsync(url, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        res.EnsureSuccessStatusCode();
        return body;
    }

    // เรียก POST แบบ generic (เติม access_token ให้อัตโนมัติ)
    public async Task<string> PostAsync(
        string sellerId,
        string apiPath,
        object body,
        CancellationToken ct = default)
    {
        var (http, host, token) = await CreateClientWithTokenAsync(null, sellerId, ct);

        var url = QueryHelpers.AddQueryString(
            $"{host}{apiPath}",
            new Dictionary<string, string?> { ["access_token"] = token });

        var res = await http.PostJsonAsync(url, body, ct);
        var data = await res.Content.ReadAsStringAsync(ct);
        res.EnsureSuccessStatusCode();
        return data;
    }
}
