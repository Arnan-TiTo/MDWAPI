using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MDWAPI.Services
{
    public class TiktokOrderService
    {
        private readonly IHttpClientFactory _http;
        private readonly ChannelTokenResolver _resolver;
        private readonly ILogger<TiktokOrderService> _log;

        public TiktokOrderService(
            IHttpClientFactory httpClientFactory,
            ChannelTokenResolver resolver,
            ILogger<TiktokOrderService> log)
        {
            _http = httpClientFactory;
            _resolver = resolver;
            _log = log;
        }

        /// <summary>
        /// เวอร์ชัน 202309: ดึงคำสั่งซื้อด้วย ids (คอมม่า)
        /// GET /order/202309/orders?app_key=...&sign_method=sha256&timestamp=...&shop_cipher=...&ids=id1,id2
        /// Header: x-tts-access-token
        /// </summary>
        public async Task<string> GetOrderDetailByIdsRawAsync(
            long shopId,
            IEnumerable<string> orderIds,
            string? shopCipher,
            CancellationToken ct)
        {
            if (orderIds is null) throw new ArgumentNullException(nameof(orderIds));
            var idsCsv = string.Join(",", orderIds.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (string.IsNullOrWhiteSpace(idsCsv))
                throw new ArgumentException("orderIds is empty.", nameof(orderIds));

            const string channel = "tiktok";
            const string defaultEnv = "prod";
            const string ver = "202309";
            var path = $"/order/{ver}/orders";

            // 1) access_token / appKey / env
            var (accessToken, env, _, appKey) = await _resolver.GetAccessTokenAsync(
                channel: channel,
                environment: defaultEnv,
                partnerId: null,
                appKey: null,
                accountIdBig: null,
                accountIdStr: shopId.ToString(),
                ct: ct);

            if (string.IsNullOrWhiteSpace(appKey))
                throw new InvalidOperationException("TikTok appKey missing for this shop.");

            // 2) app_secret (อ่านจาก ExtraJson ของ token แถวเดียวกันก่อน)
            var appSecret = await _resolver.ResolveAppSecretAsync(
                channel: channel,
                environment: env,
                partnersId: 0,                  // ใช้ 0 เพื่อบังคับอ่านจาก ChannelTokens.ExtraJson ก่อน
                appKey: appKey!,
                accountIdStr: shopId.ToString(),
                ct: ct);

            // 3) shop_cipher (ถ้า controller ไม่ส่งมา ลองโหลดจากตาราง)
            if (string.IsNullOrWhiteSpace(shopCipher))
            {
                shopCipher = await _resolver.ResolveShopCipherAsync(
                                 channel: channel,
                                 environment: env,
                                 appKey: appKey!,
                                 accountIdStr: shopId.ToString(),
                                 ct: ct)
                             ?? throw new InvalidOperationException("TikTok shop_cipher not found. Please refresh auth first.");
            }

            var host = _resolver.HostFor(channel, env);
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            // === build query ===
            var q = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["app_key"] = appKey,
                ["sign_method"] = "sha256",
                ["timestamp"] = ts,
                ["shop_cipher"] = shopCipher,
                ["ids"] = idsCsv
            };

            // เซ็นตามสเปค (GET ไม่มี body)
            var sign = BuildSignDocSpec(appSecret, path, q, bodyUtf8: null);
            q["sign"] = sign;

            var url = QueryHelpers.AddQueryString($"{host}{path}", q);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("x-tts-access-token", accessToken);
            req.Headers.Accept.ParseAdd("application/json");

            _log.LogInformation("TikTok GET {Url}", url);

            using var http = _http.CreateClient("Shopee"); // ใช้ชื่อ client เดิมของคุณ
            using var resp = await http.SendAsync(req, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"TikTok /order/{ver}/orders failed: {(int)resp.StatusCode} | {text}");

            return text;
        }

        /// <summary>
        /// wrapper: id เดียว (เรียก /order/202309/orders ด้วย ids=เดี่ยว)
        /// </summary>
        public Task<string> GetOrderDetailRawAsync(
            long shopId,
            string orderRef,
            string? shopCipher,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(orderRef))
                throw new ArgumentException("orderRef is required.", nameof(orderRef));

            return GetOrderDetailByIdsRawAsync(
                shopId: shopId,
                orderIds: new[] { orderRef },
                shopCipher: shopCipher,
                ct: ct);
        }

        public async Task<string> GetOrderListRawAsync(
            long shopId,
            long timeFrom,                // Unix seconds
            long timeTo,                  // Unix seconds
            int pageSize,                 // e.g. 20
            string? cursor,               // page_token
            string? status,               // order_status (เช่น "UNPAID", "TO_SHIP", ...)
            string? shopCipher,           // null ได้ เดี๋ยวไป resolve ให้
            CancellationToken ct)
        {
            const string channel = "tiktok";
            const string defaultEnv = "prod";
            const string ver = "202309";
            var path = $"/order/{ver}/orders/search";

            // 1) ดึง access_token / appKey / env จาก resolver
            var (accessToken, env, _, appKey) = await _resolver.GetAccessTokenAsync(
                channel: channel,
                environment: defaultEnv,
                partnerId: null,
                appKey: null,
                accountIdBig: null,
                accountIdStr: shopId.ToString(),
                ct: ct);

            if (string.IsNullOrWhiteSpace(appKey))
                throw new InvalidOperationException("TikTok appKey missing for this shop.");

            // 2) app_secret
            var appSecret = await _resolver.ResolveAppSecretAsync(
                channel: channel,
                environment: env,
                partnersId: 0,                      // บังคับอ่านจาก ChannelTokens.ExtraJson ก่อน
                appKey: appKey!,
                accountIdStr: shopId.ToString(),
                ct: ct);

            // 3) shop_cipher (resolve ถ้า controller ไม่ส่งมา)
            if (string.IsNullOrWhiteSpace(shopCipher))
            {
                shopCipher = await _resolver.ResolveShopCipherAsync(
                                 channel: channel,
                                 environment: env,
                                 appKey: appKey!,
                                 accountIdStr: shopId.ToString(),
                                 ct: ct)
                             ?? throw new InvalidOperationException("TikTok shop_cipher not found. Please refresh auth first.");
            }

            var host = _resolver.HostFor(channel, env);
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            // 4) Query params (สำหรับ URL + ใช้ในการ sign)
            var q = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["app_key"] = appKey,
                ["sign_method"] = "sha256",
                ["timestamp"] = ts,
                ["shop_cipher"] = shopCipher,
                ["page_size"] = pageSize.ToString(),
                // เอกสารมีตัวเลือก sort_field / sort_order; ตั้งค่า default ให้
                ["sort_field"] = "create_time",
                ["sort_order"] = "ASC"
            };
            if (!string.IsNullOrWhiteSpace(cursor))
                q["page_token"] = cursor;

            // 5) Body (JSON) — จะถูกนับรวมใน stringToSign (เพราะ content-type เป็น application/json)
            var bodyObj = new
            {
                order_status = string.IsNullOrWhiteSpace(status) ? null : status,   // ex: "UNPAID"
                create_time_ge = timeFrom,
                create_time_lt = timeTo,
                // เลือกเพิ่ม filter อื่น ๆ ได้ เช่น update_time_ge/lt, shipping_type, buyer_user_id, warehouse_ids ฯลฯ
            };

            var bodyJson = JsonSerializer.Serialize(
                bodyObj,
                new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
            var bodyBytes = Encoding.UTF8.GetBytes(bodyJson);

            // 6) สร้างลายเซ็นตามสเปค (รวม body)
            var sign = BuildSignDocSpec(appSecret, path, q, bodyBytes);
            q["sign"] = sign;

            // 7) สร้าง URL + ส่งคำขอ
            var url = QueryHelpers.AddQueryString($"{host}{path}", q);
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
            };
            req.Headers.TryAddWithoutValidation("x-tts-access-token", accessToken);
            req.Headers.Accept.ParseAdd("application/json");

            _log.LogInformation("TikTok POST {Url} | body={Body}", url, bodyJson);

            using var http = _http.CreateClient("Shopee");
            using var resp = await http.SendAsync(req, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"TikTok /order/{ver}/orders/search failed: {(int)resp.StatusCode} | {text}");

            return text;
        }

        /// <summary>
        /// สร้างลายเซ็นตามหน้า “Sign your API request”
        /// HMAC_SHA256(secret, secret + path + sortedQuery(excl. sign/access_token) + body(if any) + secret)
        /// </summary>
        private static string BuildSignDocSpec(
            string appSecret,
            string path,
            IDictionary<string, string?> query,
            byte[]? bodyUtf8)
        {
            var filtered = query
                .Where(kv => !kv.Key.Equals("sign", StringComparison.OrdinalIgnoreCase)
                          && !kv.Key.Equals("access_token", StringComparison.OrdinalIgnoreCase))
                .OrderBy(kv => kv.Key, StringComparer.Ordinal);

            var sb = new StringBuilder();
            sb.Append(path);
            foreach (var kv in filtered)
                sb.Append(kv.Key).Append(kv.Value ?? string.Empty);

            if (bodyUtf8 is { Length: > 0 })
                sb.Append(Encoding.UTF8.GetString(bodyUtf8));

            var toSign = appSecret + sb + appSecret;
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(toSign));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
