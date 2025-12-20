using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Linq;
using MDWAPI.Common;
using MDWAPI.Dtos;
using MDWAPI.Repos;
using Microsoft.AspNetCore.WebUtilities;

namespace MDWAPI.Services
{
    /// <summary>
    /// ดูแล token/get, token/refresh และดึง cipher (shop list) หลัง refresh
    /// - token host: https://auth.tiktok-shops.com  (ลายเซ็นแบบเดิม: path + k+v แล้ว HMAC)
    /// - open host : https://open-api.tiktokglobalshop.com (ลายเซ็นแบบใหม่ doc-spec: secret + path + sorted(kv except sign/access_token) + body + secret)
    /// </summary>
    public class TiktokAuthProvider : IPlatformAuthProvider
    {
        private readonly IHttpClientFactory _http;
        private readonly IChannelTokenRepo _chanRepo;
        private readonly IPartnerRepo _partnerRepo;
        private readonly ILogger<TiktokAuthProvider> _log;

        public TiktokAuthProvider(
            IHttpClientFactory httpClientFactory,
            IChannelTokenRepo chanRepo,
            IPartnerRepo partnerRepo,
            ILogger<TiktokAuthProvider> log)
        {
            _http = httpClientFactory;
            _chanRepo = chanRepo;
            _partnerRepo = partnerRepo;
            _log = log;
        }

        // ---------------- Models ----------------
        private record TikTokCommonResp<T>(int code, string? message, T? data);
        private record TokenRespData(string access_token, string refresh_token, int? expires_in);
        private record ShopsRespDataShop(string id, string cipher, string? name, string? region, string? code, string? seller_type);
        private record ShopsRespData(IReadOnlyList<ShopsRespDataShop> shops);

        // ---------------- Public: Exchange (เก็บไว้เผื่อใช้) ----------------
        public async Task<object> ExchangeCodeAsync(
            Platform platform,
            int partnersId,
            long? accountIdBig,
            string? accountIdStr,
            string code,
            CancellationToken ct)
        {
            if (platform != Platform.TikTok) throw new ArgumentException("Invalid platform for TiktokAuthProvider");
            if (string.IsNullOrWhiteSpace(accountIdStr)) throw new ArgumentException("TikTok needs accountIdStr (shop_id)");

            var (appKey, appSecret, env, partnerRowId) = await LoadPartnerAsync(partnersId, ct);

            var http = _http.CreateClient("Tiktok");
            var tokenResp = await CallTokenGetAsync(http, appKey, appSecret, code, accountIdStr!, env, ct);

            if (tokenResp is null || tokenResp.data is null || tokenResp.code != 0)
                throw new HttpRequestException($"token/get error code={tokenResp?.code} message={tokenResp?.message}");

            var accessToken = tokenResp.data.access_token;
            var refreshToken = tokenResp.data.refresh_token;
            var expireIn = tokenResp.data.expires_in ?? 4 * 3600;

            // ลองดึง cipher ทันที (ถ้า fail ไม่ล้ม process)
            string? cipher = null;
            try
            {
                cipher = await FetchCipherAsync(http, appKey, appSecret, accessToken, env, accountIdStr!, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Fetch cipher after exchange failed; will try again on refresh.");
            }

            var extraJson = MergeExtraJson(
                existingJson: null,
                new Dictionary<string, string?>
                {
                    ["app_secret"] = appSecret,
                    ["shop_cipher"] = cipher,
                    ["cipher"] = cipher
                });

            var dto = new ChannelTokenDtos
            {
                Channel = platform.ToChannelString(),   // "tiktok"
                Environment = env,
                AuthType = "shop",
                PartnerId = null,
                AppKey = appKey,
                AccountIdBig = null,
                AccountIdStr = accountIdStr,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                AccessTokenExpAt = DateTime.UtcNow.AddSeconds(expireIn),
                RefreshTokenExpAt = DateTime.UtcNow.AddDays(30),
                Country = "TH",
                Region = "TH",
                PartnersId = partnerRowId,
                ExtraJson = extraJson,
                isActive = true
            };

            await _chanRepo.UpsertAsync(dto, ct);

            return new
            {
                access_token = accessToken,
                refresh_token = refreshToken,
                expire_in = expireIn,
                shop_cipher = cipher
            };
        }

        // ---------------- Public: Refresh (ใช้จริง) ----------------
        /// <summary>
        /// refresh โดยอ้าง shopId (accountIdStr) จาก DB แล้วอัปเดต access_token + (ถ้ายังไม่มี) ดึง cipher เก็บ
        /// </summary>
        public async Task<object> RefreshByAccountAsync(
            Platform platform,
            int partnersId,
            long? accountIdBig,
            string? accountIdStr,
            CancellationToken ct)
        {
            if (platform != Platform.TikTok) throw new ArgumentException("Invalid platform for TiktokAuthProvider");
            if (string.IsNullOrWhiteSpace(accountIdStr)) throw new ArgumentException("TikTok needs accountIdStr");

            var (appKey, appSecret, env, partnerRowId) = await LoadPartnerAsync(partnersId, ct);

            // หา refresh ล่าสุดของบัญชีนี้
            var row = await _chanRepo.GetValidAsync("tiktok", env, null, appKey, null, accountIdStr, ct);

            if (row is null)
                row = await _chanRepo.GetValidAsync("tiktok", env, null, null, null, accountIdStr, ct);

            if (row is null)
            {
                var otherEnv = string.Equals(env, "prod", StringComparison.OrdinalIgnoreCase) ? "sandbox" : "prod";
                row = await _chanRepo.GetValidAsync("tiktok", otherEnv, null, null, null, accountIdStr, ct);
            }

            if (row is null)
                row = await _chanRepo.GetLatestForTikTokShopAsync(accountIdStr, ct);

            if (row is null || string.IsNullOrWhiteSpace(row.RefreshToken))
                throw new InvalidOperationException("No refresh_token found for this TikTok account. Please reconnect the shop.");

            // ✅ FIX: ต้องเป็น Tiktok client ไม่ใช่ Shopee
            var http = _http.CreateClient("Tiktok");

            var refreshResp = await CallTokenRefreshAsync(http, appKey, appSecret, row.RefreshToken!, accountIdStr!, env, ct);

            if (refreshResp is null || refreshResp.data is null || refreshResp.code != 0)
                throw new HttpRequestException($"token/refresh error code={refreshResp?.code} message={refreshResp?.message}");

            var newAccess = refreshResp.data.access_token;
            var expireIn = refreshResp.data.expires_in ?? 4 * 3600;

            // ถ้ายังไม่มี cipher ให้ลองดึงตอนนี้ (ใช้ spec เซ็นใหม่)
            var extraJson = row.ExtraJson;
            var hasCipher = !string.IsNullOrWhiteSpace(TryRead(extraJson, "shop_cipher"))
                         || !string.IsNullOrWhiteSpace(TryRead(extraJson, "cipher"));
            string? fetchedCipher = null;

            if (!hasCipher)
            {
                try
                {
                    fetchedCipher = await FetchCipherAsync(http, appKey, appSecret, newAccess, env, accountIdStr!, ct);
                    if (!string.IsNullOrWhiteSpace(fetchedCipher))
                    {
                        extraJson = MergeExtraJson(extraJson, new Dictionary<string, string?>
                        {
                            ["shop_cipher"] = fetchedCipher,
                            ["cipher"] = fetchedCipher
                        });
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Fetch cipher during refresh failed; continue without it.");
                }
            }

            // Ensure app_secret present in ExtraJson
            extraJson = MergeExtraJson(extraJson, new Dictionary<string, string?> { ["app_secret"] = appSecret });

            var dto = new ChannelTokenDtos
            {
                Channel = platform.ToChannelString(),
                Environment = env,
                AuthType = "shop",
                PartnerId = null,
                AppKey = appKey,
                AccountIdBig = null,
                AccountIdStr = accountIdStr,
                AccessToken = newAccess,
                RefreshToken = row.RefreshToken!, // คง refresh เดิม
                AccessTokenExpAt = DateTime.UtcNow.AddSeconds(expireIn),
                RefreshTokenExpAt = row.RefreshTokenExpAt ?? DateTime.UtcNow.AddDays(30),
                Country = "TH",
                Region = "TH",
                PartnersId = partnerRowId,
                ExtraJson = extraJson,
                isActive = true
            };

            await _chanRepo.UpsertAsync(dto, ct);

            return new
            {
                access_token = newAccess,
                refresh_token = row.RefreshToken,
                expire_in = expireIn,
                shop_cipher = TryRead(dto.ExtraJson, "shop_cipher") ?? TryRead(dto.ExtraJson, "cipher") ?? fetchedCipher
            };
        }

        // ให้คอมไพล์ผ่านตามสัญญา interface
        public Task<object> RefreshAsync(Platform platform, int partnersId, long? accountIdBig, string? accountIdStr, string refreshToken, CancellationToken ct)
            => throw new NotSupportedException("Use RefreshByAccountAsync for TikTok.");

        // ---------------- Internal helpers ----------------

        private async Task<(string appKey, string appSecret, string environment, int partnersId)> LoadPartnerAsync(int partnersId, CancellationToken ct)
        {
            var cfg = await _partnerRepo.GetConfigByPartnersIdAsync(partnersId, ct)
                      ?? throw new InvalidOperationException($"Partners config not found: {partnersId}");

            if (string.IsNullOrWhiteSpace(cfg.AppKey))
                throw new InvalidOperationException("TikTok AppKey (client_key) not found in Partners.");
            if (string.IsNullOrWhiteSpace(cfg.PartnerKey))
                throw new InvalidOperationException("TikTok AppSecret (PartnerKey) not found in Partners.");

            return (cfg.AppKey!, cfg.PartnerKey!, cfg.Environment ?? "prod", cfg.Id);
        }

        private static string HostFor(string environment)
            => string.Equals(environment, "sandbox", StringComparison.OrdinalIgnoreCase)
                ? "https://sandbox-open-api.tiktokglobalshop.com"
                : "https://open-api.tiktokglobalshop.com";

        private static string AuthHostFor(string environment)
            => "https://auth.tiktok-shops.com";

        // ====== (A) ลายเซ็นแบบเดิม – ใช้กับ token/get, token/refresh ======
        private static string HmacHex(string key, string raw)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        }

        /// <summary>
        /// Legacy sign: HMAC_SHA256(app_secret, path + concat(sorted(k+v)) )
        /// ✅ IMPORTANT: exclude "sign" and "app_secret" from string-to-sign
        /// </summary>
        private static string BuildSign_Legacy(string appSecret, string path, IDictionary<string, string?> query)
        {
            var sb = new StringBuilder(path);

            foreach (var kv in query
                     .Where(x =>
                         !string.Equals(x.Key, "sign", StringComparison.OrdinalIgnoreCase) &&
                         !string.Equals(x.Key, "app_secret", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                sb.Append(kv.Key);
                sb.Append(kv.Value ?? "");
            }

            return HmacHex(appSecret, sb.ToString());
        }

        // ====== (B) ลายเซ็นแบบเอกสารใหม่ – ใช้กับ open-api เช่น /authorization/202309/shops ======
        private static string BuildSign_DocSpec(
            string appSecret,
            string path,
            IDictionary<string, string?> query,  // ต้องมี sign_method=sha256 ด้วย
            byte[]? bodyUtf8 // GET = null
        )
        {
            // 1) ตัด sign, access_token ออก
            var filtered = query
                .Where(kv => !kv.Key.Equals("sign", StringComparison.OrdinalIgnoreCase)
                          && !kv.Key.Equals("access_token", StringComparison.OrdinalIgnoreCase))
                .OrderBy(kv => kv.Key, StringComparer.Ordinal);

            // 2) path + concat(k+v)
            var sb = new StringBuilder();
            sb.Append(path);
            foreach (var kv in filtered)
                sb.Append(kv.Key).Append(kv.Value ?? string.Empty);

            if (bodyUtf8 is { Length: > 0 })
                sb.Append(Encoding.UTF8.GetString(bodyUtf8));

            // 3) wrap secret + ... + secret แล้ว HMAC
            var toSign = appSecret + sb + appSecret;
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(toSign));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string? TryRead(string? json, string key)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                    return v.GetString();
            }
            catch { }
            return null;
        }

        private static string MergeExtraJson(string? existingJson, IDictionary<string, string?> patch)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(existingJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(existingJson);
                    foreach (var p in doc.RootElement.EnumerateObject())
                        dict[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString();
                }
                catch { /* ignore */ }
            }

            foreach (var kv in patch)
                if (!string.IsNullOrWhiteSpace(kv.Value))
                    dict[kv.Key] = kv.Value;

            return JsonSerializer.Serialize(dict);
        }

        // --------- TikTok auth calls ----------
        private async Task<TikTokCommonResp<TokenRespData>> CallTokenGetAsync(
            HttpClient http, string appKey, string appSecret, string code, string shopId, string env, CancellationToken ct)
        {
            var baseUri = AuthHostFor(env);
            var path = "/api/v2/token/get";
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            // ใช้ dict สำหรับ sign (ไม่มี app_secret)
            var qSign = new Dictionary<string, string?>
            {
                ["app_key"] = appKey,
                ["grant_type"] = "authorized_code",
                ["auth_code"] = code,
                ["shop_id"] = shopId,
                ["timestamp"] = ts
            };

            var sign = BuildSign_Legacy(appSecret, path, qSign);

            // dict สำหรับ request จริง (มี app_secret)
            var q = new Dictionary<string, string?>(qSign)
            {
                ["sign"] = sign,
                ["app_secret"] = appSecret
            };

            var url = QueryHelpers.AddQueryString($"{baseUri}{path}", q);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"token/get invalid http {(int)resp.StatusCode}: {body}");

            return JsonSerializer.Deserialize<TikTokCommonResp<TokenRespData>>(body)
                   ?? throw new HttpRequestException("token/get invalid json");
        }

        private async Task<TikTokCommonResp<TokenRespData>> CallTokenRefreshAsync(
          HttpClient http,
          string appKey,
          string appSecret,
          string refreshToken,
          string shopId,
          string env,
          CancellationToken ct)
        {
            var baseUri = AuthHostFor(env);
            var path = "/api/v2/token/refresh";
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            // ใช้ dict สำหรับ sign (ไม่มี app_secret / sign)
            var qSign = new Dictionary<string, string?>
            {
                ["app_key"] = appKey,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["shop_id"] = shopId,
                ["timestamp"] = ts
            };

            var sign = BuildSign_Legacy(appSecret, path, qSign);

            // dict สำหรับ request จริง (มี app_secret)
            var q = new Dictionary<string, string?>(qSign)
            {
                ["sign"] = sign,
                ["app_secret"] = appSecret
            };

            var url = QueryHelpers.AddQueryString($"{baseUri}{path}", q);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"token/refresh invalid http {(int)resp.StatusCode}: {body}");

            return JsonSerializer.Deserialize<TikTokCommonResp<TokenRespData>>(body)
                   ?? throw new HttpRequestException("token/refresh invalid json");
        }


        // --------- Get shop list (cipher) ----------
        /// <summary>
        /// ดึงรายชื่อร้านที่บัญชีนี้ authorize ไว้ แล้วเลือก cipher ตรงกับ shop_id (accountIdStr)
        /// Endpoint: GET /authorization/202309/shops
        /// Header: x-tts-access-token
        /// Query: app_key, sign_method=sha256, timestamp, sign (doc-spec)
        /// </summary>
        private async Task<string?> FetchCipherAsync(
            HttpClient http, string appKey, string appSecret, string accessToken, string env, string accountIdStr, CancellationToken ct)
        {
            var baseUri = HostFor(env);
            var path = "/authorization/202309/shops";
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            var q = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["app_key"] = appKey,
                ["sign_method"] = "sha256",
                ["timestamp"] = ts
            };
            q["sign"] = BuildSign_DocSpec(appSecret, path, q, bodyUtf8: null);

            var url = QueryHelpers.AddQueryString($"{baseUri}{path}", q);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Accept.ParseAdd("application/json");
            req.Headers.TryAddWithoutValidation("x-tts-access-token", accessToken);

            using var resp = await http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _log.LogInformation("FetchShopCipher GET {Url} => {Status}. Body={Body}", url, (int)resp.StatusCode, body);
                return null;
            }

            var parsed = JsonSerializer.Deserialize<TikTokCommonResp<ShopsRespData>>(body);
            var shops = parsed?.data?.shops;
            if (shops == null || shops.Count == 0) return null;

            var hit = shops.FirstOrDefault(s => string.Equals(s.id, accountIdStr, StringComparison.Ordinal));
            return hit?.cipher;
        }
    }
}
