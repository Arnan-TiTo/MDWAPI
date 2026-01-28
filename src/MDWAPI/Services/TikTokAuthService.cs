using System.Net.Http;
using System.Text.Json;
using System.Web;

namespace MDWAPI.Helpers
{
    // เรียก TikTok AUTH จริง (เวอร์ชัน v2) ด้วย GET + querystring
    //   GET https://auth.tiktok-shops.com/api/v2/token/get?app_key=...&app_secret=...&grant_type=authorized_code&auth_code=...&shop_id=...
    //   GET https://auth.tiktok-shops.com/api/v2/token/refresh?app_key=...&app_secret=...&grant_type=refresh_token&refresh_token=...&shop_id=...
    public class TikTokAuthService
    {
        private readonly HttpClient _http;

        // host หลัก + สำรอง (บาง region/document เก่าอาจใช้ globalshop)
        private static readonly string[] Hosts = new[]
        {
            "https://auth.tiktok-shops.com",
            "https://auth.tiktokglobalshop.com"
        };

        public TikTokAuthService(HttpClient http)
        {
            _http = http;
            _http.Timeout = TimeSpan.FromSeconds(30);
        }

        public record TikTokTokenPayload(
            string? access_token,
            string? refresh_token,
            int? expires_in
        );

        public record TikTokTokenResponse(
            int code,
            TikTokTokenPayload? data,
            string? message,
            string? request_id
        );

        public async Task<TikTokTokenResponse> ExchangeTokenAsync(
            string appKey,
            string appSecret,
            string authCode,
            string shopId,
            CancellationToken ct)
        {
            var qs = HttpUtility.ParseQueryString(string.Empty);
            qs["app_key"] = appKey;
            qs["app_secret"] = appSecret;          // <- สำคัญ
            qs["grant_type"] = "authorized_code";
            qs["auth_code"] = authCode;
            qs["shop_id"] = shopId;

            var path = "/api/v2/token/get";
            return await CallAuthAsync(path, qs, ct);
        }

        public async Task<TikTokTokenResponse> RefreshTokenAsync(
            string appKey,
            string appSecret,
            string refreshToken,
            string shopId,
            CancellationToken ct)
        {
            var qs = HttpUtility.ParseQueryString(string.Empty);
            qs["app_key"] = appKey;
            qs["app_secret"] = appSecret;        // <- สำคัญ
            qs["grant_type"] = "refresh_token";
            qs["refresh_token"] = refreshToken;
            qs["shop_id"] = shopId;

            var path = "/api/v2/token/refresh";
            return await CallAuthAsync(path, qs, ct);
        }

        private async Task<TikTokTokenResponse> CallAuthAsync(string path, System.Collections.Specialized.NameValueCollection qs, CancellationToken ct)
        {
            // ลองทีละ host (กันกรณีบาง environment ใช้ domain อื่น)
            foreach (var host in Hosts)
            {
                var url = $"{host}{path}?{qs}";

                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Accept.ParseAdd("application/json");

                using var resp = await _http.SendAsync(req, ct);
                var text = await resp.Content.ReadAsStringAsync(ct);

                // พยายาม parse JSON เสมอ ถ้าไม่ใช่ → โยน error พร้อม body (ช่วยดีบั๊ก)
                TikTokTokenResponse? dto = null;
                try { dto = JsonSerializer.Deserialize<TikTokTokenResponse>(text); }
                catch { /* ignore, จะเช็คต่อด้านล่าง */ }

                var ok = resp.IsSuccessStatusCode && dto is not null && dto.code == 0 && dto.data is not null;
                if (ok) return dto!;

                // ถ้า 404/400 จาก host นี้ ลอง host ถัดไป (ถ้ามี)
                var last = host == Hosts[^1];
                //var bodyForLog = (text?.Length ?? 0) > 300 ? text[..300] + "..." : text;
                var bodyForLog = (text is not null && text.Length > 300) ? text[..300] + "..." : text;

                if (!last)
                    continue;

                // สุดท้ายแล้วยัง fail → โยนเหตุผลชัด ๆ ออกไป
                if (dto is not null)
                    throw new HttpRequestException($"{path} error code={dto.code} message={dto.message} | body={bodyForLog}");

                throw new HttpRequestException($"{path} invalid response ({(int)resp.StatusCode}) | {bodyForLog}");
            }

            // จะไม่ถึงบรรทัดนี้
            throw new HttpRequestException("Auth call failed (no host responded).");
        }
    }
}
