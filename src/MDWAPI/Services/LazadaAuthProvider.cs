using System.Net.Http.Json;
using MDWAPI.Common;
using MDWAPI.Dtos;
using MDWAPI.Repos;

namespace MDWAPI.Services;

public class LazadaAuthProvider : IPlatformAuthProvider
{
    private readonly IHttpClientFactory _http;
    private readonly IChannelTokenRepo _chanRepo;
    private readonly IPartnerRepo _partnerRepo;
    private readonly ILogger<LazadaAuthProvider> _log;

    public LazadaAuthProvider(
        IHttpClientFactory httpClientFactory,
        IChannelTokenRepo chanRepo,
        IPartnerRepo partnerRepo,
        ILogger<LazadaAuthProvider> log)
    {
        _http = httpClientFactory;
        _chanRepo = chanRepo;
        _partnerRepo = partnerRepo;
        _log = log;
    }

    // แลก code สำหรับ Lazada (สาธิต: mock token – เติมการเรียก OAuth จริงภายหลัง)
    public async Task<object> ExchangeCodeAsync(
        Platform platform,
        int partnersId,
        long? accountIdBig,
        string? accountIdStr,
        string code,
        CancellationToken ct)
    {
        if (platform != Platform.Lazada) throw new ArgumentException("Invalid platform for LazadaAuthProvider");
        if (string.IsNullOrWhiteSpace(accountIdStr)) throw new ArgumentException("Lazada needs seller_id as AccountIdStr");

        var cfg = await _partnerRepo.GetConfigByPartnersIdAsync(partnersId, ct)
                  ?? throw new InvalidOperationException($"Partners config not found: {partnersId}");

        // TODO: เรียก OAuth จริงของ Lazada แทนที่ mock ด้านล่าง
        var accessToken = $"LAZADA_ACCESS_{Guid.NewGuid():N}";
        var refreshToken = $"LAZADA_REFRESH_{Guid.NewGuid():N}";
        var expireIn = 4 * 3600; // 4 ชั่วโมง (ตัวอย่าง)

        // ✅ ใช้ object initializer
        var dto = new ChannelTokenDtos
        {
            Channel = platform.ToChannelString(),   // "lazada"
            Environment = cfg.Environment ?? "prod",
            AuthType = "seller",
            PartnerId = null,                         // Lazada ฝั่งนี้ใช้ AppKey มากกว่า
            AppKey = cfg.AppKey,
            AccountIdBig = null,
            AccountIdStr = accountIdStr,                 // seller_id (string)
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpAt = DateTime.UtcNow.AddSeconds(expireIn),
            RefreshTokenExpAt = DateTime.UtcNow.AddDays(30),  // ตัวอย่าง อายุ refresh
            Scope = null,
            Country = "TH",
            Region = "TH",
            CompanysId = null,
            PartnersId = cfg.Id,
            TokenPayloadJson = null,                         // เก็บ payload จริงถ้ามี
            ExtraJson = null,
            isActive = true
        };

        await _chanRepo.UpsertAsync(dto, ct);
        return new { access_token = accessToken, refresh_token = refreshToken, expire_in = expireIn };
    }

    // รีเฟรช token สำหรับ Lazada (สาธิต: mock – เติมเรียกจริงภายหลัง)
    public async Task<object> RefreshAsync(
        Platform platform,
        int partnersId,
        long? accountIdBig,
        string? accountIdStr,
        string refreshToken,
        CancellationToken ct)
    {
        if (platform != Platform.Lazada) throw new ArgumentException("Invalid platform for LazadaAuthProvider");
        if (string.IsNullOrWhiteSpace(accountIdStr)) throw new ArgumentException("Lazada needs seller_id as AccountIdStr");

        var cfg = await _partnerRepo.GetConfigByPartnersIdAsync(partnersId, ct)
                  ?? throw new InvalidOperationException($"Partners config not found: {partnersId}");

        // TODO: เรียก refresh token ของ Lazada จริงแทน mock
        var newAccess = $"LAZADA_ACCESS_{Guid.NewGuid():N}";
        var expireIn = 4 * 3600;

        // ✅ ใช้ object initializer
        var dto = new ChannelTokenDtos
        {
            Channel = platform.ToChannelString(),
            Environment = cfg.Environment ?? "prod",
            AuthType = "seller",
            PartnerId = null,
            AppKey = cfg.AppKey,
            AccountIdBig = null,
            AccountIdStr = accountIdStr,
            AccessToken = newAccess,
            RefreshToken = refreshToken,                 // ส่วนใหญ่ refresh คงเดิม (แล้วแต่ผู้ให้บริการ)
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
