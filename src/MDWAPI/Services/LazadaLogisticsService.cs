using Microsoft.AspNetCore.WebUtilities;
using MDWAPI.Helpers;

namespace MDWAPI.Services;

public class LazadaLogisticsService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _cfg;
    private readonly ChannelTokenResolver _resolver;
    private readonly LazadaOrderService _orderService;
    private readonly ILogger<LazadaLogisticsService> _log;

    // ตัวสร้างบริการ (inject HttpClientFactory, Config, Resolver, Logger)
    public LazadaLogisticsService(
        IHttpClientFactory httpFactory,
        IConfiguration cfg,
        ChannelTokenResolver resolver,
        LazadaOrderService orderService,
        ILogger<LazadaLogisticsService> log)
    {
        _httpFactory = httpFactory;
        _cfg = cfg;
        _resolver = resolver;
        _orderService = orderService;
        _log = log;
    }

    public async Task<byte[]> DownloadLabelAsync(long shopId, MDWAPI.Models.LabelDownloadRequest req, CancellationToken ct)
    {
        var orderIds = new List<string>();

        if (!string.IsNullOrEmpty(req.OrderSn))
        {
            orderIds.Add(req.OrderSn);
        }
        else if (req.FromDate.HasValue && req.ToDate.HasValue)
        {
            // Fetch orders by date range
            var fromIso = req.FromDate.Value.ToString("yyyy-MM-ddTHH:mm:ssK");
            var toIso = req.ToDate.Value.ToString("yyyy-MM-ddTHH:mm:ssK");

            var listJson = await _orderService.GetOrderListRawAsync(shopId, fromIso, toIso, ct: ct);
            using var doc = JsonDocument.Parse(listJson);
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("orders", out var orders))
            {
                foreach (var order in orders.EnumerateArray())
                {
                    if (order.TryGetProperty("order_id", out var id))
                    {
                        orderIds.Add(id.GetRawText().Trim('"'));
                    }
                }
            }
        }

        if (orderIds.Count == 0)
        {
            throw new ArgumentException("No orders found for the given criteria.");
        }

        // Lazada print waybill takes order_ids as a comma-separated string or multiple values
        // Usually it's in the query parameters for Lazada GET waybill
        var parameters = new Dictionary<string, string?>
        {
            ["order_ids"] = $"[{string.Join(",", orderIds)}]"
        };

        return await PrintWaybillAsync(shopId.ToString(), parameters, ct);
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

        var http = _httpFactory.CreateClient("Lazada");
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
