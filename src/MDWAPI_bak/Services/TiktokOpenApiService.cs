using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MDWAPI.Services
{
    /// <summary>
    /// Thin client สำหรับ TikTok Shop Open API:
    /// - เซ็นคำขอแบบ doc spec: HMACSHA256(secret, secret + path + sortedQueryNoSign + bodyIfAny + secret)
    /// - รองรับ GET/POST (JSON)
    /// - Helper: /authorization/{ver}/shops (ดึง shop list + cipher), /order/{ver}/orders/detail/query
    /// หมายเหตุ: ใช้ HttpClientFactory "Shopee" ตามโค้ดเดิมที่คุณมีอยู่แล้ว
    /// </summary>
    public class TiktokOpenApiService
    {
        private readonly IHttpClientFactory _http;
        private readonly ChannelTokenResolver _resolver;
        private readonly ILogger<TiktokOpenApiService> _log;

        public TiktokOpenApiService(
            IHttpClientFactory httpClientFactory,
            ChannelTokenResolver resolver,
            ILogger<TiktokOpenApiService> log)
        {
            _http = httpClientFactory;
            _resolver = resolver;
            _log = log;
        }

        // -----------------------------
        // Public helpers (high level)
        // -----------------------------
        public async Task<string?> GetShopCipherAsync(
            long shopId,
            string? version = "202309",
            string channel = "tiktok",
            string defaultEnv = "prod",
            CancellationToken ct = default)
        {
            // 1) resolve token/appKey/env
            var tokenInfo = await _resolver.GetAccessTokenAsync(
                channel: channel,
                environment: defaultEnv,
                partnerId: null,
                appKey: null,
                accountIdBig: null,
                accountIdStr: shopId.ToString(),
                ct: ct);

            var accessToken = tokenInfo.accessToken;
            var env = tokenInfo.environment ?? defaultEnv;
            var appKey = tokenInfo.appKey ?? throw new InvalidOperationException("TikTok appKey missing.");

            // 2) resolve app_secret จาก ExtraJson/Partners
            var appSecret = await _resolver.ResolveAppSecretAsync(
                channel: channel,
                environment: env,
                partnersId: 0,
                appKey: appKey,
                accountIdStr: shopId.ToString(),
                ct: ct);

            // 3) call /authorization/{ver}/shops
            var host = _resolver.HostFor(channel, env);
            var path = $"/authorization/{version}/shops";

            var ts = NowEpoch();
            var q = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["app_key"] = appKey,
                ["sign_method"] = "sha256",
                ["timestamp"] = ts
            };

            var sign = BuildSignDocSpec(appSecret, path, q, bodyUtf8: null);
            q["sign"] = sign;

            var url = QueryHelpers.AddQueryString($"{host}{path}", q);

            var text = await SendAsync(HttpMethod.Get, url, accessToken, bodyUtf8: null, ct);
            // รูปแบบ response ตามตัวอย่างล่าสุดที่คุณยิงได้ (data.shops[].cipher|id)
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.GetInt32() == 0 &&
                doc.RootElement.TryGetProperty("data", out var dataEl) &&
                dataEl.TryGetProperty("shops", out var shopsEl) &&
                shopsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in shopsEl.EnumerateArray())
                {
                    var id = s.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    if (id == shopId.ToString())
                    {
                        if (s.TryGetProperty("cipher", out var cEl) && cEl.ValueKind == JsonValueKind.String)
                            return cEl.GetString();
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// เรียก /order/{ver}/orders/detail/query (GET) ด้วย shop_cipher + order_id_list
        /// คืน raw JSON string
        /// </summary>
        public async Task<string> OrdersDetailQueryAsync(
            long shopId,
            string orderIdListCsv,
            string? shopCipher = null,
            string? version = "202309",
            string channel = "tiktok",
            string defaultEnv = "prod",
            CancellationToken ct = default)
        {
            // 1) resolve token/appKey/env
            var tokenInfo = await _resolver.GetAccessTokenAsync(
                channel: channel,
                environment: defaultEnv,
                partnerId: null,
                appKey: null,
                accountIdBig: null,
                accountIdStr: shopId.ToString(),
                ct: ct);

            var accessToken = tokenInfo.accessToken;
            var env = tokenInfo.environment ?? defaultEnv;
            var appKey = tokenInfo.appKey ?? throw new InvalidOperationException("TikTok appKey missing.");

            // 2) app_secret
            var appSecret = await _resolver.ResolveAppSecretAsync(
                channel: channel,
                environment: env,
                partnersId: 0,
                appKey: appKey,
                accountIdStr: shopId.ToString(),
                ct: ct);

            // 3) shop cipher (ถ้าไม่ส่งมาก็อ่านจาก DB)
            if (string.IsNullOrWhiteSpace(shopCipher))
            {
                shopCipher = await _resolver.ResolveShopCipherAsync(
                                 channel: channel,
                                 environment: env,
                                 appKey: appKey,
                                 accountIdStr: shopId.ToString(),
                                 ct: ct)
                             ?? throw new InvalidOperationException("TikTok shop_cipher not found. Please refresh auth first.");
            }

            // 4) build request
            var host = _resolver.HostFor(channel, env);
            var path = $"/order/{version}/orders/detail/query";

            var ts = NowEpoch();
            var q = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["app_key"] = appKey,
                ["sign_method"] = "sha256",
                ["timestamp"] = ts,
                ["shop_cipher"] = shopCipher,
                ["order_id_list"] = orderIdListCsv
            };

            var sign = BuildSignDocSpec(appSecret, path, q, bodyUtf8: null);
            q["sign"] = sign;

            var url = QueryHelpers.AddQueryString($"{host}{path}", q);
            _log.LogInformation("TikTok GET {Url}", url);

            var text = await SendAsync(HttpMethod.Get, url, accessToken, bodyUtf8: null, ct);
            return text;
        }

        // -----------------------------
        // Core HTTP helpers
        // -----------------------------
        private async Task<string> SendAsync(
            HttpMethod method,
            string url,
            string accessToken,
            byte[]? bodyUtf8,
            CancellationToken ct)
        {
            using var req = new HttpRequestMessage(method, url);
            req.Headers.TryAddWithoutValidation("x-tts-access-token", accessToken);
            req.Headers.Accept.ParseAdd("application/json");

            if (method != HttpMethod.Get && bodyUtf8 is { Length: > 0 })
                req.Content = new ByteArrayContent(bodyUtf8);

            using var http = _http.CreateClient("Shopee"); // reuse client profile เดิม
            using var resp = await http.SendAsync(req, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _log.LogInformation("TikTok {Method} {Url} => {Status}. Body={Body}",
                    method, url, (int)resp.StatusCode, text);
                throw new HttpRequestException($"TikTok openapi failed: {(int)resp.StatusCode} | {text}");
            }

            return text;
        }

        // -----------------------------
        // Signing
        // -----------------------------
        /// <summary>
        /// ตามหน้า doc "Sign your API request":
        /// sign = HMACSHA256(secret,  secret + path + concat(sorted(k=v without sign/access_token)) + body(if any & !multipart) + secret )
        /// หมายเหตุ:
        /// - เราใช้ GET เป็นหลัก (bodyUtf8=null)
        /// - query ต้องใส่ sign_method=sha256 ด้วย เพื่อให้ server รับ sign แบบ HMAC-SHA256
        /// </summary>
        private static string BuildSignDocSpec(
            string appSecret,
            string path,
            IDictionary<string, string?> query,
            byte[]? bodyUtf8)
        {
            // 1) ตัด sign & access_token ออกจากชุดที่จะ concat
            var filtered = query
                .Where(kv => !kv.Key.Equals("sign", StringComparison.OrdinalIgnoreCase)
                          && !kv.Key.Equals("access_token", StringComparison.OrdinalIgnoreCase))
                .OrderBy(kv => kv.Key, StringComparer.Ordinal);

            // 2) สร้างสตริง: path + {k}{v} สำหรับทุกคู่ที่เรียงแล้ว
            var sb = new StringBuilder();
            sb.Append(path);
            foreach (var kv in filtered)
                sb.Append(kv.Key).Append(kv.Value ?? string.Empty);

            // 3) ต่อ body (ถ้ามี และ content-type ไม่ใช่ multipart) — ในคลาสนี้เราให้ผู้เรียกแปลง body เป็น UTF8 มาก่อน
            if (bodyUtf8 is { Length: > 0 })
                sb.Append(Encoding.UTF8.GetString(bodyUtf8));

            // 4) ครอบด้วย secret หน้า-หลัง แล้ว HMAC-SHA256
            var toSign = appSecret + sb + appSecret;
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(toSign));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string NowEpoch() => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
    }
}
