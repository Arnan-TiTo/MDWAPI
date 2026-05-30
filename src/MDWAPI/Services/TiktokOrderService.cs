using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;

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

            var (accessToken, env, _, appKey) = await _resolver.GetAccessTokenAsync(
                channel: channel,
                environment: defaultEnv,
                partnerId: null,
                appKey: null,
                accountIdBig: null,
                accountIdStr: shopId.ToString(),
                ct: ct);

            if (string.IsNullOrWhiteSpace(accessToken))
                throw new InvalidOperationException("TikTok accessToken missing for this shop.");
            if (string.IsNullOrWhiteSpace(appKey))
                throw new InvalidOperationException("TikTok appKey missing for this shop.");

            var appSecret = await _resolver.ResolveAppSecretAsync(
                channel: channel,
                environment: env,
                partnersId: 0,
                appKey: appKey!,
                accountIdStr: shopId.ToString(),
                ct: ct);

            if (string.IsNullOrWhiteSpace(shopCipher))
            {
                shopCipher = await EnsureShopCipherAsync(
                    shopId: shopId,
                    env: env,
                    appKey: appKey!,
                    appSecret: appSecret,
                    accessToken: accessToken,
                    ct: ct);
            }

            var host = _resolver.HostFor(channel, env);
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            var q = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["app_key"] = appKey,
                ["sign_method"] = "sha256",
                ["timestamp"] = ts,
                ["shop_cipher"] = shopCipher,
                ["ids"] = idsCsv,

                // ใส่ access_token ใน query ได้ (และถูกตัดออกจาก sign โดย BuildSignDocSpec)
                ["access_token"] = accessToken
            };

            q["sign"] = BuildSignDocSpec(appSecret, path, q, bodyUtf8: null);

            var url = QueryHelpers.AddQueryString($"{host}{path}", q);

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("x-tts-access-token", accessToken);
            req.Headers.Accept.ParseAdd("application/json");

            _log.LogInformation("TikTok GET {Url}", url);

            using var http = _http.CreateClient("TikTok");
            using var resp = await http.SendAsync(req, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"TikTok {path} failed: {(int)resp.StatusCode} | {text}");

            return text;
        }

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
            long timeFrom,
            long timeTo,
            int pageSize,
            string? cursor,
            string? status,
            string? shopCipher,
            CancellationToken ct)
        {
            const string channel = "tiktok";
            const string defaultEnv = "prod";
            const string ver = "202309";
            var path = $"/order/{ver}/orders/search";

            var (accessToken, env, _, appKey) = await _resolver.GetAccessTokenAsync(
                channel: channel,
                environment: defaultEnv,
                partnerId: null,
                appKey: null,
                accountIdBig: null,
                accountIdStr: shopId.ToString(),
                ct: ct);

            if (string.IsNullOrWhiteSpace(accessToken))
                throw new InvalidOperationException("TikTok accessToken missing for this shop.");
            if (string.IsNullOrWhiteSpace(appKey))
                throw new InvalidOperationException("TikTok appKey missing for this shop.");

            var appSecret = await _resolver.ResolveAppSecretAsync(
                channel: channel,
                environment: env,
                partnersId: 0,
                appKey: appKey!,
                accountIdStr: shopId.ToString(),
                ct: ct);

            if (string.IsNullOrWhiteSpace(shopCipher))
            {
                shopCipher = await EnsureShopCipherAsync(
                    shopId: shopId,
                    env: env,
                    appKey: appKey!,
                    appSecret: appSecret,
                    accessToken: accessToken,
                    ct: ct);
            }

            var host = _resolver.HostFor(channel, env);
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            var q = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["app_key"] = appKey,
                ["sign_method"] = "sha256",
                ["timestamp"] = ts,
                ["shop_cipher"] = shopCipher,
                ["page_size"] = pageSize.ToString(),
                ["sort_field"] = "create_time",
                ["sort_order"] = "ASC",

                // ใส่ access_token ใน query ได้
                ["access_token"] = accessToken
            };

            if (!string.IsNullOrWhiteSpace(cursor))
                q["page_token"] = cursor;

            var bodyObj = new
            {
                order_status = string.IsNullOrWhiteSpace(status) ? null : status,
                create_time_ge = timeFrom,
                create_time_lt = timeTo
            };

            var bodyJson = JsonSerializer.Serialize(
                bodyObj,
                new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });

            var bodyBytes = Encoding.UTF8.GetBytes(bodyJson);

            q["sign"] = BuildSignDocSpec(appSecret, path, q, bodyBytes);

            var url = QueryHelpers.AddQueryString($"{host}{path}", q);

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
            };
            req.Headers.TryAddWithoutValidation("x-tts-access-token", accessToken);
            req.Headers.Accept.ParseAdd("application/json");

            _log.LogInformation("TikTok POST {Url} | body={Body}", url, bodyJson);

            using var http = _http.CreateClient("TikTok");
            using var resp = await http.SendAsync(req, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"TikTok {path} failed: {(int)resp.StatusCode} | {text}");

            return text;
        }

        // =========================================================
        // Cipher: DB -> shops API -> throw (พร้อมรายละเอียดจริง)
        // =========================================================
        private async Task<string> EnsureShopCipherAsync(
            long shopId,
            string env,
            string appKey,
            string appSecret,
            string accessToken,
            CancellationToken ct)
        {
            var shopIdStr = shopId.ToString();

            // 1) จาก DB ก่อน
            var cipher = await _resolver.ResolveShopCipherAsync("tiktok", env, appKey, shopIdStr, ct);
            if (!string.IsNullOrWhiteSpace(cipher))
                return cipher;

            // 2) จาก shops API พร้อม diagnostic
            var diag = await FetchShopCipherFromApiVerboseAsync(env, appKey, appSecret, accessToken, shopIdStr, ct);

            if (!string.IsNullOrWhiteSpace(diag.cipher))
            {
                await _resolver.UpsertShopCipherAsync("tiktok", env, appKey, shopIdStr, diag.cipher!, ct);
                return diag.cipher!;
            }

            // ✅ ตรงนี้คุณจะได้ “เหตุผลจริง” ไปเลย
            throw new InvalidOperationException(
                "TikTok shop_cipher not found.\n" +
                $"shopId={shopIdStr} env={env} appKey={appKey}\n" +
                $"shopsApiStatus={(diag.httpStatus?.ToString() ?? "n/a")} code={(diag.apiCode?.ToString() ?? "n/a")} message={diag.apiMessage ?? "n/a"}\n" +
                $"url={diag.url}\n" +
                $"gotShopIds=[{string.Join(",", diag.gotShopIds ?? Array.Empty<string>())}]\n" +
                $"body={diag.body}"
            );
        }

        private async Task<(string? cipher,
                            int? httpStatus,
                            int? apiCode,
                            string? apiMessage,
                            string? url,
                            string? body,
                            string[]? gotShopIds)> FetchShopCipherFromApiVerboseAsync(
            string env,
            string appKey,
            string appSecret,
            string accessToken,
            string shopIdStr,
            CancellationToken ct)
        {
            const string ver = "202309";
            var host = _resolver.HostFor("tiktok", env);
            var path = $"/authorization/{ver}/shops";
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            var q = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["app_key"] = appKey,
                ["sign_method"] = "sha256",
                ["timestamp"] = ts,
                ["access_token"] = accessToken
            };

            q["sign"] = BuildSignDocSpec(appSecret, path, q, bodyUtf8: null);

            var url = QueryHelpers.AddQueryString($"{host}{path}", q);

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Accept.ParseAdd("application/json");
            req.Headers.TryAddWithoutValidation("x-tts-access-token", accessToken);

            using var http = _http.CreateClient("TikTok");
            using var resp = await http.SendAsync(req, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("TikTok shops list HTTP failed: {Status} url={Url} body={Body}",
                    (int)resp.StatusCode, url, text);

                return (null, (int)resp.StatusCode, null, null, url, text, null);
            }

            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;

                int? apiCode = null;
                string? apiMessage = null;

                if (root.TryGetProperty("code", out var cEl) && cEl.ValueKind == JsonValueKind.Number)
                    apiCode = cEl.GetInt32();

                if (root.TryGetProperty("message", out var mEl) && mEl.ValueKind == JsonValueKind.String)
                    apiMessage = mEl.GetString();

                // code != 0 -> ส่งกลับไว้ให้ throw เห็นเหตุผล
                if (apiCode.HasValue && apiCode.Value != 0)
                {
                    _log.LogWarning("TikTok shops list returned code={Code} message={Message} url={Url} body={Body}",
                        apiCode, apiMessage, url, text);

                    return (null, (int)resp.StatusCode, apiCode, apiMessage, url, text, null);
                }

                if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                    return (null, (int)resp.StatusCode, apiCode, apiMessage, url, text, null);

                if (!data.TryGetProperty("shops", out var shops) || shops.ValueKind != JsonValueKind.Array)
                    return (null, (int)resp.StatusCode, apiCode, apiMessage, url, text, null);

                // หา shop ที่ id ตรง
                foreach (var s in shops.EnumerateArray())
                {
                    if (s.ValueKind != JsonValueKind.Object) continue;

                    var id = ReadShopIdAsString(s);
                    if (!string.Equals(id, shopIdStr, StringComparison.Ordinal))
                        continue;

                    var cipher = ReadCipher(s);
                    if (!string.IsNullOrWhiteSpace(cipher))
                        return (cipher, (int)resp.StatusCode, apiCode, apiMessage, url, text, null);
                }

                // code==0 แต่ไม่ match -> เก็บ list ของ ids เพื่อ debug
                var gotIds = shops.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.Object)
                    .Select(ReadShopIdAsString)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!)
                    .Distinct()
                    .ToArray();

                _log.LogWarning("TikTok shops list OK but shopId not matched. want={Want} got=[{Got}] url={Url}",
                    shopIdStr, string.Join(",", gotIds), url);

                return (null, (int)resp.StatusCode, apiCode, apiMessage, url, text, gotIds);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "TikTok shops list parse failed. url={Url} body={Body}", url, text);
                return (null, (int)resp.StatusCode, null, null, url, text, null);
            }
        }

        // -----------------------------
        // JSON helpers (รองรับ id เป็น number/string)
        // -----------------------------
        private static string? ReadShopIdAsString(JsonElement shopObj)
        {
            if (shopObj.TryGetProperty("id", out var idEl))
            {
                if (idEl.ValueKind == JsonValueKind.String) return idEl.GetString();
                if (idEl.ValueKind == JsonValueKind.Number) return idEl.TryGetInt64(out var v) ? v.ToString() : null;
            }

            if (shopObj.TryGetProperty("shop_id", out var sid))
            {
                if (sid.ValueKind == JsonValueKind.String) return sid.GetString();
                if (sid.ValueKind == JsonValueKind.Number) return sid.TryGetInt64(out var v) ? v.ToString() : null;
            }

            if (shopObj.TryGetProperty("shopId", out var sid2))
            {
                if (sid2.ValueKind == JsonValueKind.String) return sid2.GetString();
                if (sid2.ValueKind == JsonValueKind.Number) return sid2.TryGetInt64(out var v) ? v.ToString() : null;
            }

            if (shopObj.TryGetProperty("account_id", out var aid))
            {
                if (aid.ValueKind == JsonValueKind.String) return aid.GetString();
                if (aid.ValueKind == JsonValueKind.Number) return aid.TryGetInt64(out var v) ? v.ToString() : null;
            }

            if (shopObj.TryGetProperty("accountId", out var aid2))
            {
                if (aid2.ValueKind == JsonValueKind.String) return aid2.GetString();
                if (aid2.ValueKind == JsonValueKind.Number) return aid2.TryGetInt64(out var v) ? v.ToString() : null;
            }

            return null;
        }

        private static string? ReadCipher(JsonElement shopObj)
        {
            if (shopObj.TryGetProperty("cipher", out var c1) && c1.ValueKind == JsonValueKind.String) return c1.GetString();
            if (shopObj.TryGetProperty("shop_cipher", out var c2) && c2.ValueKind == JsonValueKind.String) return c2.GetString();
            if (shopObj.TryGetProperty("shopCipher", out var c3) && c3.ValueKind == JsonValueKind.String) return c3.GetString();
            return null;
        }

        // -----------------------------
        // Signing
        // -----------------------------
        /// <summary>
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

        // ====== Order Actions ======

        /// <summary>
        /// POST /order/202309/orders/cancel
        /// ยกเลิก order (ก่อนจัดส่ง)
        /// </summary>
        public async Task<string> CancelOrderAsync(
            long shopId,
            string orderId,
            string cancelReason,
            string? shopCipher = null,
            CancellationToken ct = default)
        {
            const string channel = "tiktok";
            const string defaultEnv = "prod";
            var path = MDWAPI.Helpers.TiktokApiPaths.OrderCancel202309;

            var (accessToken, env, _, appKey) = await _resolver.GetAccessTokenAsync(
                channel: channel,
                environment: defaultEnv,
                partnerId: null,
                appKey: null,
                accountIdBig: null,
                accountIdStr: shopId.ToString(),
                ct: ct);

            if (string.IsNullOrWhiteSpace(accessToken))
                throw new InvalidOperationException("TikTok accessToken missing for this shop.");
            if (string.IsNullOrWhiteSpace(appKey))
                throw new InvalidOperationException("TikTok appKey missing for this shop.");

            var appSecret = await _resolver.ResolveAppSecretAsync(
                channel: channel,
                environment: env,
                partnersId: 0,
                appKey: appKey!,
                accountIdStr: shopId.ToString(),
                ct: ct);

            if (string.IsNullOrWhiteSpace(shopCipher))
            {
                shopCipher = await EnsureShopCipherAsync(
                    shopId: shopId,
                    env: env,
                    appKey: appKey!,
                    appSecret: appSecret,
                    accessToken: accessToken,
                    ct: ct);
            }

            var host = _resolver.HostFor(channel, env);
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            var q = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["app_key"] = appKey,
                ["sign_method"] = "sha256",
                ["timestamp"] = ts,
                ["shop_cipher"] = shopCipher,
                ["access_token"] = accessToken
            };

            var bodyObj = new
            {
                order_id = orderId,
                cancel_reason = cancelReason
            };

            var bodyJson = JsonSerializer.Serialize(bodyObj);
            var bodyBytes = Encoding.UTF8.GetBytes(bodyJson);

            q["sign"] = BuildSignDocSpec(appSecret, path, q, bodyBytes);

            var url = QueryHelpers.AddQueryString($"{host}{path}", q);

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
            };
            req.Headers.TryAddWithoutValidation("x-tts-access-token", accessToken);
            req.Headers.Accept.ParseAdd("application/json");

            _log.LogInformation("TikTok POST cancel {Url} | body={Body}", url, bodyJson);

            using var http = _http.CreateClient("TikTok");
            using var resp = await http.SendAsync(req, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"TikTok cancel order failed: {(int)resp.StatusCode} | {text}");

            return text;
        }

        public async Task<string> GetOrderEscrowRawAsync(
            long shopId,
            string orderId,
            string? shopCipher,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(orderId))
                throw new ArgumentException("orderId is required.", nameof(orderId));

            const string channel = "tiktok";
            const string defaultEnv = "prod";
            var path = $"/finance/202501/orders/{orderId}/statement_transactions";

            var (accessToken, env, _, appKey) = await _resolver.GetAccessTokenAsync(
                channel: channel,
                environment: defaultEnv,
                partnerId: null,
                appKey: null,
                accountIdBig: null,
                accountIdStr: shopId.ToString(),
                ct: ct);

            if (string.IsNullOrWhiteSpace(accessToken))
                throw new InvalidOperationException("TikTok accessToken missing for this shop.");
            if (string.IsNullOrWhiteSpace(appKey))
                throw new InvalidOperationException("TikTok appKey missing for this shop.");

            var appSecret = await _resolver.ResolveAppSecretAsync(
                channel: channel,
                environment: env,
                partnersId: 0,
                appKey: appKey!,
                accountIdStr: shopId.ToString(),
                ct: ct);

            if (string.IsNullOrWhiteSpace(shopCipher))
            {
                shopCipher = await EnsureShopCipherAsync(
                    shopId: shopId,
                    env: env,
                    appKey: appKey!,
                    appSecret: appSecret,
                    accessToken: accessToken,
                    ct: ct);
            }

            var host = _resolver.HostFor(channel, env);
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            var q = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["app_key"] = appKey,
                ["sign_method"] = "sha256",
                ["timestamp"] = ts,
                ["shop_cipher"] = shopCipher,
                ["access_token"] = accessToken
            };

            q["sign"] = BuildSignDocSpec(appSecret, path, q, bodyUtf8: null);

            var url = QueryHelpers.AddQueryString($"{host}{path}", q);

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("x-tts-access-token", accessToken);
            req.Headers.Accept.ParseAdd("application/json");

            _log.LogInformation("TikTok GET escrow {Url}", url);

            using var http = _http.CreateClient("TikTok");
            using var resp = await http.SendAsync(req, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"TikTok {path} failed: {(int)resp.StatusCode} | {text}");

            return text;
        }
    }
}
