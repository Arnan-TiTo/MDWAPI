using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using MDWAPI.Common;
using MDWAPI.Dtos;
using MDWAPI.Helpers;
using MDWAPI.Repos;

namespace MDWAPI.Services;

public class ShopeeAuthProvider : IPlatformAuthProvider
{
    private const string ClientName = "Shopee";

    private readonly IHttpClientFactory _http;
    private readonly IChannelTokenRepo _chanRepo;
    private readonly IPartnerRepo _partnerRepo;
    private readonly ILogger<ShopeeAuthProvider> _log;

    public ShopeeAuthProvider(
        IHttpClientFactory httpClientFactory,
        IChannelTokenRepo chanRepo,
        IPartnerRepo partnerRepo,
        ILogger<ShopeeAuthProvider> log)
    {
        _http = httpClientFactory;
        _chanRepo = chanRepo;
        _partnerRepo = partnerRepo;
        _log = log;
    }

    private static string HostByEnv(string? env)
        => (env ?? "prod").ToLowerInvariant() == "sandbox"
            ? "https://openplatform.sandbox.test-stable.shopee.sg"
            : "https://partner.shopeemobile.com";

    public async Task<object> ExchangeCodeAsync(
        Platform platform,
        int partnersId,
        long? accountIdBig,
        string? accountIdStr,
        string code,
        CancellationToken ct)
    {
        if (platform != Platform.Shopee) throw new ArgumentException("Invalid platform for ShopeeAuthProvider");
        if (accountIdBig is null) throw new ArgumentException("Shopee needs numeric shop_id (AccountIdBig)");

        var cfg = await _partnerRepo.GetConfigByPartnersIdAsync(partnersId, ct)
                  ?? throw new InvalidOperationException($"Partners config not found: {partnersId}");
        if (cfg.PartnerId is null || string.IsNullOrWhiteSpace(cfg.PartnerKey))
            throw new InvalidOperationException("Shopee PartnerId/PartnerKey is required");

        var host = HostByEnv(cfg.Environment);
        var api = "/api/v2/auth/token/get";

        var modes = new[] {
            ShopeeKeyMode.StripHexToBytes,
            ShopeeKeyMode.StripPrefixAscii,
            ShopeeKeyMode.RawString
        };

        foreach (var mode in modes)
        {
            var ts = UnixTime.NowSeconds();
            var sign = ShopeeSign.BuildPartnerAuthSign(cfg.PartnerId.Value, cfg.PartnerKey!, api, ts, mode);

            var url = QueryHelpers.AddQueryString($"{host}{api}", new Dictionary<string, string?>
            {
                ["partner_id"] = cfg.PartnerId.Value.ToString(),
                ["timestamp"] = ts.ToString(),
                ["sign"] = sign
            });

            var http = _http.CreateClient(ClientName);
            http.BaseAddress = new Uri(host);
            http.Timeout = TimeSpan.FromSeconds(30);

            var body = new { code, shop_id = accountIdBig.Value, partner_id = cfg.PartnerId.Value };

            using var res = await http.PostAsJsonAsync(url, body, ct);
            var json = await res.Content.ReadAsStringAsync(ct);

            if (res.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var accessToken = root.GetProperty("access_token").GetString()!;
                var refreshToken = root.GetProperty("refresh_token").GetString()!;
                var expireIn = root.GetProperty("expire_in").GetInt64();
                var scope = root.TryGetProperty("scope", out var sc) ? sc.GetString() : null;
                long? refreshExpIn = root.TryGetProperty("refresh_token_expire_in", out var rti) ? rti.GetInt64() : (long?)null;

                // ✅ ใช้ object initializer (ไม่มีพารามิเตอร์ ctor)
                var dto = new ChannelTokenDtos
                {
                    Channel = "shopee",
                    Environment = cfg.Environment ?? "prod",
                    AuthType = "shop",
                    PartnerId = cfg.PartnerId,
                    AppKey = null,
                    AccountIdBig = accountIdBig,
                    AccountIdStr = null,
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    AccessTokenExpAt = DateTime.UtcNow.AddSeconds(expireIn),
                    RefreshTokenExpAt = refreshExpIn.HasValue ? DateTime.UtcNow.AddSeconds(refreshExpIn.Value) : null,
                    Scope = scope,
                    Country = "TH",
                    Region = "TH",
                    CompanysId = null,
                    PartnersId = cfg.Id,
                    TokenPayloadJson = json,
                    ExtraJson = $"{{\"sign_mode\":\"{mode}\"}}",
                    isActive = true
                };

                await _chanRepo.UpsertAsync(dto, ct);
                _log.LogInformation("Shopee token/get success with sign_mode={Mode}", mode);
                return JsonSerializer.Deserialize<object>(json)!;
            }

            if ((int)res.StatusCode == 403 && json.Contains("error_sign", StringComparison.OrdinalIgnoreCase))
            {
                _log.LogWarning("Shopee token/get wrong sign with mode={Mode}. Body={Body}", mode, json);
                continue;
            }

            _log.LogWarning("Shopee token/get failed: {Status} {Body}", res.StatusCode, json);
            throw new HttpRequestException($"Shopee token/get failed: {(int)res.StatusCode}. Body: {json}");
        }

        throw new HttpRequestException("Shopee token/get failed: wrong sign with all key modes.");
    }

    public async Task<object> RefreshAsync(
        Platform platform,
        int partnersId,
        long? accountIdBig,
        string? accountIdStr,
        string refreshToken,
        CancellationToken ct)
    {
        if (platform != Platform.Shopee) throw new ArgumentException("Invalid platform for ShopeeAuthProvider");
        if (accountIdBig is null) throw new ArgumentException("Shopee needs numeric shop_id (AccountIdBig)");

        var cfg = await _partnerRepo.GetConfigByPartnersIdAsync(partnersId, ct)
                  ?? throw new InvalidOperationException($"Partners config not found: {partnersId}");
        if (cfg.PartnerId is null || string.IsNullOrWhiteSpace(cfg.PartnerKey))
            throw new InvalidOperationException("Shopee PartnerId/PartnerKey is required");

        var host = HostByEnv(cfg.Environment);
        var api = "/api/v2/auth/access_token/get";

        var modes = new[] {
            ShopeeKeyMode.StripHexToBytes,
            ShopeeKeyMode.StripPrefixAscii,
            ShopeeKeyMode.RawString
        };

        foreach (var mode in modes)
        {
            var ts = UnixTime.NowSeconds();
            var sign = ShopeeSign.BuildPartnerAuthSign(cfg.PartnerId.Value, cfg.PartnerKey!, api, ts, mode);

            var url = QueryHelpers.AddQueryString($"{host}{api}", new Dictionary<string, string?>
            {
                ["partner_id"] = cfg.PartnerId.Value.ToString(),
                ["timestamp"] = ts.ToString(),
                ["sign"] = sign
            });

            var http = _http.CreateClient(ClientName);
            http.BaseAddress = new Uri(host);
            http.Timeout = TimeSpan.FromSeconds(30);

            var body = new { refresh_token = refreshToken, shop_id = accountIdBig.Value, partner_id = cfg.PartnerId.Value };

            using var res = await http.PostAsJsonAsync(url, body, ct);
            var json = await res.Content.ReadAsStringAsync(ct);

            if (res.IsSuccessStatusCode)
            {
                // (ถ้าต้อง upsert token หลัง refresh ด้วย ให้ parse แล้ว new ChannelTokenDtos แบบ object initializer คล้ายข้างบน)
                _log.LogInformation("Shopee access_token/get success with sign_mode={Mode}", mode);
                return JsonSerializer.Deserialize<object>(json)!;
            }

            if ((int)res.StatusCode == 403 && json.Contains("error_sign", StringComparison.OrdinalIgnoreCase))
            {
                _log.LogWarning("Shopee access_token/get wrong sign with mode={Mode}. Body={Body}", mode, json);
                continue;
            }

            _log.LogWarning("Shopee access_token/get failed: {Status} {Body}", res.StatusCode, json);
            throw new HttpRequestException($"Shopee access_token/get failed: {(int)res.StatusCode}. Body: {json}");
        }

        throw new HttpRequestException("Shopee access_token/get failed: wrong sign with all key modes.");
    }
}
