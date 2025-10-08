using MDWAPI.Common;
using MDWAPI.Dtos;
using MDWAPI.Repos;

namespace MDWAPI.Services;

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

    // แลก code สำหรับ TikTok Shop (ตัวอย่าง mock — เติมของจริงได้ภายหลัง)
    public async Task<object> ExchangeCodeAsync(
        Platform platform,
        int partnersId,
        long? accountIdBig,
        string? accountIdStr,
        string code,
        CancellationToken ct)
    {
        if (platform != Platform.TikTok) throw new ArgumentException("Invalid platform for TiktokAuthProvider");
        if (string.IsNullOrWhiteSpace(accountIdStr)) throw new ArgumentException("TikTok needs accountIdStr (shop_id/seller_id)");

        var cfg = await _partnerRepo.GetConfigByPartnersIdAsync(partnersId, ct)
                  ?? throw new InvalidOperationException($"Partners config not found: {partnersId}");

        // 👉 สมมติว่าแลกสำเร็จ (mock)
        var accessToken = $"TTK_ACCESS_{Guid.NewGuid():N}";
        var refreshToken = $"TTK_REFRESH_{Guid.NewGuid():N}";
        var expireIn = 4 * 3600;

        var dto = new ChannelTokenDtos
        {
            Channel = platform.ToChannelString(), // "tiktok"
            Environment = cfg.Environment ?? "prod",
            AuthType = "shop",
            PartnerId = null,
            AppKey = cfg.AppKey,
            AccountIdBig = null,
            AccountIdStr = accountIdStr,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpAt = DateTime.UtcNow.AddSeconds(expireIn),
            RefreshTokenExpAt = DateTime.UtcNow.AddDays(30),
            Scope = null,
            Country = "TH",
            Region = "TH",
            CompanysId = null,
            PartnersId = cfg.Id,
            TokenPayloadJson = null,
            ExtraJson = null,
            isActive = true
        };

        await _chanRepo.UpsertAsync(dto, ct);

        return new { access_token = accessToken, refresh_token = refreshToken, expire_in = expireIn };
    }

    // รีเฟรช token สำหรับ TikTok Shop (ตัวอย่าง mock)
    public async Task<object> RefreshAsync(
        Platform platform,
        int partnersId,
        long? accountIdBig,
        string? accountIdStr,
        string refreshToken,
        CancellationToken ct)
    {
        if (platform != Platform.TikTok) throw new ArgumentException("Invalid platform for TiktokAuthProvider");
        if (string.IsNullOrWhiteSpace(accountIdStr)) throw new ArgumentException("TikTok needs accountIdStr");

        var cfg = await _partnerRepo.GetConfigByPartnersIdAsync(partnersId, ct)
                  ?? throw new InvalidOperationException($"Partners config not found: {partnersId}");

        var newAccess = $"TTK_ACCESS_{Guid.NewGuid():N}";
        var expireIn = 4 * 3600;

        var dto = new ChannelTokenDtos
        {
            Channel = platform.ToChannelString(),
            Environment = cfg.Environment ?? "prod",
            AuthType = "shop",
            PartnerId = null,
            AppKey = cfg.AppKey,
            AccountIdBig = null,
            AccountIdStr = accountIdStr,
            AccessToken = newAccess,
            RefreshToken = refreshToken,             // refresh เดิม
            AccessTokenExpAt = DateTime.UtcNow.AddSeconds(expireIn),
            RefreshTokenExpAt = DateTime.UtcNow.AddDays(30),
            Scope = null,
            Country = "TH",
            Region = "TH",
            CompanysId = null,
            PartnersId = cfg.Id,
            TokenPayloadJson = null,
            ExtraJson = null,
            isActive = true
        };

        await _chanRepo.UpsertAsync(dto, ct);

        return new { access_token = newAccess, refresh_token = refreshToken, expire_in = expireIn };
    }
}
