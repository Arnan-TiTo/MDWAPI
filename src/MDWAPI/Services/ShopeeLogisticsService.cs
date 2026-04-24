using MDWAPI.Helpers;
using MDWAPI.Repos;
using Microsoft.AspNetCore.WebUtilities;
using System.Text.Json;

namespace MDWAPI.Services;

public class ShopeeLogisticsService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IShopRepo _shopRepo;
    private readonly IPartnerRepo _partnerRepo;
    private readonly ChannelTokenResolver _resolver;
    private readonly ShopeeOrderService _orderService;
    private readonly IUnifiedOrderLabelDocumentRepo _labelDocRepo;
    private readonly ILogger<ShopeeLogisticsService> _log;

    public ShopeeLogisticsService(
        IHttpClientFactory httpFactory,
        IShopRepo shopRepo,
        IPartnerRepo partnerRepo,
        ChannelTokenResolver resolver,
        ShopeeOrderService orderService,
        IUnifiedOrderLabelDocumentRepo labelDocRepo,
        ILogger<ShopeeLogisticsService> log)
    {
        _httpFactory = httpFactory;
        _shopRepo = shopRepo;
        _partnerRepo = partnerRepo;
        _resolver = resolver;
        _orderService = orderService;
        _labelDocRepo = labelDocRepo;
        _log = log;
    }

    public async Task<byte[]> DownloadLabelAsync(long shopId, MDWAPI.Models.LabelDownloadRequest req, CancellationToken ct)
    {
        var orderSns = new List<string>();

        if (!string.IsNullOrEmpty(req.OrderSn))
        {
            orderSns.Add(req.OrderSn);
        }
        else if (req.FromDate.HasValue && req.ToDate.HasValue)
        {
            // Fetch orders by date range
            var timeFrom = new DateTimeOffset(req.FromDate.Value).ToUnixTimeSeconds();
            var timeTo = new DateTimeOffset(req.ToDate.Value).ToUnixTimeSeconds();

            var listJson = await _orderService.GetOrderListRawAsync(shopId, "create_time", timeFrom, timeTo, 50, null, null, ct);
            using var doc = JsonDocument.Parse(listJson);
            if (doc.RootElement.TryGetProperty("response", out var response) &&
                response.TryGetProperty("order_list", out var orderList))
            {
                foreach (var order in orderList.EnumerateArray())
                {
                    if (order.TryGetProperty("order_sn", out var sn))
                    {
                        orderSns.Add(sn.GetString()!);
                    }
                }
            }
        }

        if (orderSns.Count == 0)
        {
            if (req.RawBody.HasValue)
                return await DownloadShippingDocumentAsync(shopId, req.RawBody.Value, ct);
            
            throw new ArgumentException("No orders found for the given criteria.");
        }

        // For Shopee, we can request label for multiple order_sn in one go
        var body = new { order_list = orderSns.Select(sn => new { order_sn = sn }).ToList() };
        return await DownloadShippingDocumentAsync(shopId, body, ct);
    }

    // -------------------- Core helpers (DB-based partner resolution) --------------------

    private async Task<(long partnerId, string partnerKey, string environment)> GetPartnerFromDbAsync(long shopId, CancellationToken ct)
    {
        var (partnersId, accountIdBig, _) = await _shopRepo.GetShopBindingAsync(shopId, ct);

        var cfg = await _partnerRepo.GetConfigByPartnersIdAsync(partnersId, ct)
                  ?? throw new InvalidOperationException($"Partners config not found: {partnersId}");
        if (cfg.PartnerId is null || string.IsNullOrWhiteSpace(cfg.PartnerKey))
            throw new InvalidOperationException("Shopee PartnerId/PartnerKey is required");

        return (cfg.PartnerId.Value, cfg.PartnerKey!, cfg.Environment ?? "prod");
    }

    private async Task<(string url, HttpClient http)> BuildSignedGetAsync(
        string apiPath,
        long shopId,
        Dictionary<string, string?> extraQuery,
        CancellationToken ct)
    {
        var (partnerId, partnerKey, envCfg) = await GetPartnerFromDbAsync(shopId, ct);

        var (accessToken, environment, _, _) = await _resolver.GetAccessTokenAsync(
            channel: "shopee",
            environment: envCfg,
            partnerId: partnerId,
            appKey: null,
            accountIdBig: shopId,
            accountIdStr: null,
            ct);

        var host = _resolver.HostFor("shopee", environment);
        var ts = UnixTime.NowSeconds();

        var sign = ShopeeSign.BuildShopSign(
            partnerId: (int)partnerId,
            partnerKey: partnerKey,
            apiPath: apiPath,
            timestamp: ts,
            accessToken: accessToken,
            shopId: shopId,
            ShopeeKeyMode.RawString
        );

        var baseQuery = new Dictionary<string, string?>
        {
            ["partner_id"] = partnerId.ToString(),
            ["timestamp"] = ts.ToString(),
            ["sign"] = sign,
            ["access_token"] = accessToken,
            ["shop_id"] = shopId.ToString()
        };

        if (extraQuery is not null)
            foreach (var kv in extraQuery) baseQuery[kv.Key] = kv.Value;

        var url = QueryHelpers.AddQueryString($"{host}{apiPath}", baseQuery);
        var http = _httpFactory.CreateClient("Shopee");
        http.BaseAddress = new Uri(host);
        return (url, http);
    }

    private async Task<(string url, HttpClient http)> BuildSignedPostAsync(
        string apiPath,
        long shopId,
        Dictionary<string, string?>? extraQuery,
        CancellationToken ct)
    {
        var (partnerId, partnerKey, envCfg) = await GetPartnerFromDbAsync(shopId, ct);

        var (accessToken, environment, _, _) = await _resolver.GetAccessTokenAsync(
            channel: "shopee",
            environment: envCfg,
            partnerId: partnerId,
            appKey: null,
            accountIdBig: shopId,
            accountIdStr: null,
            ct);

        var host = _resolver.HostFor("shopee", environment);
        var ts = UnixTime.NowSeconds();

        var sign = ShopeeSign.BuildShopSign(
            partnerId: (int)partnerId,
            partnerKey: partnerKey,
            apiPath: apiPath,
            timestamp: ts,
            accessToken: accessToken,
            shopId: shopId,
            ShopeeKeyMode.RawString
        );

        var baseQuery = new Dictionary<string, string?>
        {
            ["partner_id"] = partnerId.ToString(),
            ["timestamp"] = ts.ToString(),
            ["sign"] = sign,
            ["access_token"] = accessToken,
            ["shop_id"] = shopId.ToString()
        };

        if (extraQuery is not null)
            foreach (var kv in extraQuery) baseQuery[kv.Key] = kv.Value;

        var url = QueryHelpers.AddQueryString($"{host}{apiPath}", baseQuery);
        var http = _httpFactory.CreateClient("Shopee");
        http.BaseAddress = new Uri(host);
        return (url, http);
    }

    // -------------------- Logistics endpoints (ready-to-use) --------------------

    /// <summary>
    /// logistics/get_shipping_parameter (GET)
    /// </summary>
    public async Task<string> GetShippingParameterAsync(long shopId, string orderSn, CancellationToken ct = default)
    {
        var (url, http) = await BuildSignedGetAsync(
            apiPath: ShopeeApiPaths.LogiGetShippingParam,
            shopId: shopId,
            extraQuery: new Dictionary<string, string?> { ["order_sn"] = orderSn },
            ct);

        var res = await http.GetAsync(url, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        res.EnsureSuccessStatusCode();
        return body;
    }

    /// <summary>
    /// logistics/get_address_list (GET) — fallback เมื่อ get_shipping_parameter return address_list ว่าง
    /// </summary>
    public async Task<string> GetAddressListAsync(long shopId, CancellationToken ct = default)
    {
        var (url, http) = await BuildSignedGetAsync(
            apiPath: ShopeeApiPaths.LogiGetAddressList,
            shopId: shopId,
            extraQuery: new Dictionary<string, string?>(),
            ct);

        var res = await http.GetAsync(url, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        res.EnsureSuccessStatusCode();
        return body;
    }

    /// <summary>
    /// logistics/get_tracking_number (GET)
    /// </summary>
    public async Task<string> GetTrackingNumberAsync(long shopId, string orderSn, CancellationToken ct = default)
    {
        var (url, http) = await BuildSignedGetAsync(
            apiPath: ShopeeApiPaths.LogiGetTrackingNumber,
            shopId: shopId,
            extraQuery: new Dictionary<string, string?> { ["order_sn"] = orderSn },
            ct);

        var res = await http.GetAsync(url, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        res.EnsureSuccessStatusCode();
        return body;
    }

    /// <summary>
    /// logistics/ship_order (POST)
    /// </summary>
    public async Task<string> ShipOrderAsync(long shopId, object shipBody, CancellationToken ct = default)
    {
        var (url, http) = await BuildSignedPostAsync(
            apiPath: ShopeeApiPaths.LogiShipOrder,
            shopId: shopId,
            extraQuery: null,
            ct);

        var res = await http.PostJsonAsync(url, shipBody, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        res.EnsureSuccessStatusCode();
        return body;
    }

    /// <summary>
    /// logistics/get_shipping_document_result (POST)
    /// ใช้ตรวจสอบสถานะเอกสาร (หลังจาก create document แล้ว)
    /// </summary>
    public async Task<string> GetShippingDocumentResultAsync(long shopId, object requestBody, CancellationToken ct = default)
    {
        var (url, http) = await BuildSignedPostAsync(
            apiPath: ShopeeApiPaths.LogiGetDocResult,
            shopId: shopId,
            extraQuery: null,
            ct);

        var res = await http.PostJsonAsync(url, requestBody, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        res.EnsureSuccessStatusCode();
        return body;
    }

    /// <summary>
    /// logistics/download_shipping_document (POST) => byte[] (เช่น PDF label)
    /// </summary>
    public async Task<byte[]> DownloadShippingDocumentAsync(long shopId, object requestBody, CancellationToken ct = default)
    {
        var (url, http) = await BuildSignedPostAsync(
            apiPath: ShopeeApiPaths.LogiDownloadDoc,
            shopId: shopId,
            extraQuery: null,
            ct);

        var res = await http.PostJsonAsync(url, requestBody, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>
    /// logistics/get_shipping_document_parameter (POST)
    /// ดึงประเภท AWB ที่เลือกได้สำหรับ order
    /// </summary>
    public async Task<string> GetShippingDocumentParameterAsync(long shopId, object requestBody, CancellationToken ct = default)
    {
        var (url, http) = await BuildSignedPostAsync(
            apiPath: ShopeeApiPaths.LogiGetShippingDocParam,
            shopId: shopId,
            extraQuery: null,
            ct);

        var res = await http.PostJsonAsync(url, requestBody, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        res.EnsureSuccessStatusCode();
        return body;
    }

    /// <summary>
    /// logistics/get_shipping_document_data_info (GET)
    /// Fetch จาก Shopee → UPSERT ลง mdw.UnifiedOrderLabelDocument → return result_list
    /// Required: order_sn — Optional: tracking_number
    /// </summary>
    public async Task<JsonElement> GetShippingDocumentDataInfoAsync(long shopId, string orderSn, string? trackingNumber = null, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?> { ["order_sn"] = orderSn };
        if (!string.IsNullOrEmpty(trackingNumber))
            query["tracking_number"] = trackingNumber;

        var (url, http) = await BuildSignedGetAsync(
            apiPath: ShopeeApiPaths.LogiGetShippingDocDataInfo,
            shopId: shopId,
            extraQuery: query,
            ct);

        var res = await http.GetAsync(url, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"Shopee {(int)res.StatusCode}: {body}", null, res.StatusCode);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement.Clone();

        // UPSERT: response contains shipping_document_info + recipient_address_info
        if (root.TryGetProperty("response", out var resp))
        {
            try { await _labelDocRepo.UpsertAsync("Shopee", shopId, orderSn, null, resp, ct); }
            catch (Exception ex) { _log.LogWarning(ex, "UpsertLabelDocument failed for {OrderSn}", orderSn); }
        }

        return root;
    }

    /// <summary>
    /// logistics/create_shipping_document (POST)
    /// สร้าง shipping document task (ก่อน download ได้)
    /// </summary>
    public async Task<string> CreateShippingDocumentAsync(long shopId, object requestBody, CancellationToken ct = default)
    {
        var (url, http) = await BuildSignedPostAsync(
            apiPath: ShopeeApiPaths.LogiCreateShippingDoc,
            shopId: shopId,
            extraQuery: null,
            ct);

        var res = await http.PostJsonAsync(url, requestBody, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        res.EnsureSuccessStatusCode();
        return body;
    }

    // -------------------- Generic helpers (GET/POST) --------------------

    /// <summary>
    /// Generic GET สำหรับ /api/v2/logistics/* อื่น ๆ
    /// ใส่ apiPath จาก ShopeeApiPaths หรือ string literal ได้
    /// </summary>
    public async Task<string> GetAsync(long shopId, string apiPath, Dictionary<string, string?> query, CancellationToken ct = default)
    {
        var (url, http) = await BuildSignedGetAsync(apiPath, shopId, query, ct);
        var res = await http.GetAsync(url, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        res.EnsureSuccessStatusCode();
        return body;
    }

    /// <summary>
    /// Generic POST สำหรับ /api/v2/logistics/* อื่น ๆ
    /// </summary>
    public async Task<string> PostAsync(long shopId, string apiPath, object body, CancellationToken ct = default)
    {
        var (url, http) = await BuildSignedPostAsync(apiPath, shopId, null, ct);
        var res = await http.PostJsonAsync(url, body, ct);
        var resp = await res.Content.ReadAsStringAsync(ct);
        res.EnsureSuccessStatusCode();
        return resp;
    }
}
