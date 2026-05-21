using MDWAPI.Helpers;
using MDWAPI.Repos;
using Microsoft.AspNetCore.WebUtilities;

namespace MDWAPI.Services;

public class ShopeeAuthLinkService
{
    private readonly IPartnerRepo _partnerRepo;
    private readonly IShopRepo _shopRepo;
    private readonly ILogger<ShopeeAuthLinkService> _log;

    public ShopeeAuthLinkService(IPartnerRepo partnerRepo, IShopRepo shopRepo, ILogger<ShopeeAuthLinkService> log)
    {
        _partnerRepo = partnerRepo;
        _shopRepo = shopRepo;
        _log = log;
    }

    // สร้าง auth URL สำหรับให้ร้านไปยินยอมบน Shopee แล้ว Shopee จะ redirect กลับ callbackUrl
    public async Task<string> BuildAuthUrlAsync(long shopId, string callbackUrl, CancellationToken ct)
    {
        var (partnersId, accountIdBig, _) = await _shopRepo.GetShopBindingAsync(shopId, ct);
        var cfg = await _partnerRepo.GetConfigByPartnersIdAsync(partnersId, ct)
                  ?? throw new InvalidOperationException($"Partners config not found: {partnersId}");

        if (cfg.PartnerId is null || string.IsNullOrWhiteSpace(cfg.PartnerKey))
            throw new InvalidOperationException("Shopee PartnerId/PartnerKey is required");

        var host = cfg.Environment?.ToLowerInvariant() == "sandbox"
            ? "https://openplatform.sandbox.test-stable.shopee.sg"
            : "https://partner.shopeemobile.com";

        var apiPath = "/api/v2/shop/auth_partner";
        var ts = UnixTime.NowSeconds();

        // base string = partner_id + api_path + timestamp
        var sign = ShopeeSign.ComputeHexHmac($"{cfg.PartnerId}{apiPath}{ts}", cfg.PartnerKey!,ShopeeKeyMode.RawString);

        // แนบ state เพื่อผูก context
        var state = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{shopId}|{ts}"));

        var url = QueryHelpers.AddQueryString($"{host}{apiPath}", new Dictionary<string, string?>
        {
            ["partner_id"] = cfg.PartnerId!.Value.ToString(),
            ["timestamp"] = ts.ToString(),
            ["sign"] = sign,
            ["redirect"] = callbackUrl,
            ["state"] = state
        });

        return url;
    }
}
