using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using MDWAPI.Dtos;
using MDWAPI.Helpers;
using MDWAPI.Repos;

namespace MDWAPI.Services;

public class ShopeeTokenRefreshService
{
    private const string ClientName = "Shopee";
    private readonly IHttpClientFactory _http;
    private readonly IShopRepo _shopRepo;
    private readonly IPartnerRepo _partnerRepo;
    private readonly IChannelTokenRepo _chanRepo;
    private readonly ILogger<ShopeeTokenRefreshService> _log;

    public ShopeeTokenRefreshService(
        IHttpClientFactory http,
        IShopRepo shopRepo,
        IPartnerRepo partnerRepo,
        IChannelTokenRepo chanRepo,
        ILogger<ShopeeTokenRefreshService> log)
    {
        _http = http;
        _shopRepo = shopRepo;
        _partnerRepo = partnerRepo;
        _chanRepo = chanRepo;
        _log = log;
    }

    private static string HostByEnv(string? env)
        => (env ?? "prod").Equals("sandbox", StringComparison.OrdinalIgnoreCase)
            ? "https://openplatform.sandbox.test-stable.shopee.sg"
            : "https://partner.shopeemobile.com";

    public async Task<object> RefreshByShopIdAsync(long shopId, CancellationToken ct = default)
    {
        // 1) map shop → partnersId/accountIdBig
        var (partnersId, accountIdBig, _) = await _shopRepo.GetShopBindingAsync(shopId, ct);
        if (accountIdBig is null)
            throw new InvalidOperationException("accountIdBig is required for Shopee refresh.");

        // 2) partner config
        var cfg = await _partnerRepo.GetConfigByPartnersIdAsync(partnersId, ct)
                  ?? throw new InvalidOperationException($"Partners config not found: {partnersId}");
        if (cfg.PartnerId is null || string.IsNullOrWhiteSpace(cfg.PartnerKey))
            throw new InvalidOperationException("Shopee PartnerId/PartnerKey is required");

        // 3) หา refresh token ล่าสุด (เฉพาะที่มี RefreshToken)
        var current = await _chanRepo.GetLatestForRefreshAsync(
            channel: "shopee",
            environment: cfg.Environment ?? "prod",
            partnerId: cfg.PartnerId,
            accountIdBig: accountIdBig.Value,
            ct: ct);

        if (current is null || string.IsNullOrWhiteSpace(current.RefreshToken))
            throw new InvalidOperationException("No refresh_token found for this shop.");

        // 4) call Shopee /api/v2/auth/access_token/get
        var host = HostByEnv(cfg.Environment);
        var api = "/api/v2/auth/access_token/get";
        var ts = UnixTime.NowSeconds();

        var sign = ShopeeSign.BuildPartnerAuthSign(
            cfg.PartnerId.Value, cfg.PartnerKey!, api, ts, ShopeeKeyMode.RawString);

        var url = QueryHelpers.AddQueryString($"{host}{api}", new Dictionary<string, string?>
        {
            ["partner_id"] = cfg.PartnerId.Value.ToString(),
            ["timestamp"] = ts.ToString(),
            ["sign"] = sign
        });

        var http = _http.CreateClient(ClientName);
        http.Timeout = TimeSpan.FromSeconds(30);

        var body = new
        {
            refresh_token = current.RefreshToken!,
            shop_id = accountIdBig.Value,
            partner_id = cfg.PartnerId.Value
        };

        using var res = await http.PostAsJsonAsync(url, body, ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            _log.LogWarning("Shopee access_token/get failed: {Status} {Body}", res.StatusCode, json);
            throw new HttpRequestException($"Shopee access_token/get failed: {(int)res.StatusCode}. Body: {json}");
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var accessTokenNew = root.GetProperty("access_token").GetString()!;
        var refreshTokenNew = root.GetProperty("refresh_token").GetString()!;
        var expireIn = root.GetProperty("expire_in").GetInt64();
        var scope = root.TryGetProperty("scope", out var sc) ? sc.GetString() : null;
        long? refreshExpIn = root.TryGetProperty("refresh_token_expire_in", out var rti) ? rti.GetInt64() : (long?)null;

        // ✅ ใช้ object initializer สำหรับ ChannelTokenDtos
        var dto = new ChannelTokenDtos
        {
            Channel = "shopee",
            Environment = cfg.Environment ?? "prod",
            AuthType = "shop",
            PartnerId = cfg.PartnerId,
            AppKey = null,
            AccountIdBig = accountIdBig,
            AccountIdStr = null,
            AccessToken = accessTokenNew,
            RefreshToken = refreshTokenNew,
            AccessTokenExpAt = DateTime.UtcNow.AddSeconds(expireIn),
            RefreshTokenExpAt = refreshExpIn.HasValue ? DateTime.UtcNow.AddSeconds(refreshExpIn.Value) : null,
            Scope = scope,
            Country = "TH",
            Region = "TH",
            CompanysId = null,
            PartnersId = cfg.Id,
            TokenPayloadJson = json,
            ExtraJson = null,
            isActive = true
        };

        await _chanRepo.UpsertAsync(dto, ct);

        // คืนผลเป็น object (คลายจาก JsonDocument แล้ว)
        return System.Text.Json.JsonSerializer.Deserialize<object>(json)!;
    }
}
