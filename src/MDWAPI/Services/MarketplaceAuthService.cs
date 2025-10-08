using MDWAPI.Common;
using MDWAPI.Repos;

namespace MDWAPI.Services;

public class MarketplaceAuthService
{
    private readonly IShopRepo _shopRepo;
    private readonly ShopeeAuthProvider _shopee;
    private readonly LazadaAuthProvider _lazada;
    private readonly TiktokAuthProvider _tiktok;
    private readonly ILogger<MarketplaceAuthService> _log;

    public MarketplaceAuthService(
        IShopRepo shopRepo,
        ShopeeAuthProvider shopee,
        LazadaAuthProvider lazada,
        TiktokAuthProvider tiktok,
        ILogger<MarketplaceAuthService> log)
    {
        _shopRepo = shopRepo;
        _shopee = shopee;
        _lazada = lazada;
        _tiktok = tiktok;
        _log = log;
    }

    // แลก code ตามแพลตฟอร์ม
    public async Task<object> ExchangeCodeAsync(Platform platform, long shopId, string code, CancellationToken ct)
    {
        var (partnersId, accountIdBig, accountIdStr) = await _shopRepo.GetShopBindingAsync(shopId, ct);

        return platform switch
        {
            Platform.Shopee => await _shopee.ExchangeCodeAsync(platform, partnersId, accountIdBig, accountIdStr, code, ct),
            Platform.Lazada => await _lazada.ExchangeCodeAsync(platform, partnersId, accountIdBig, accountIdStr, code, ct),
            Platform.TikTok => await _tiktok.ExchangeCodeAsync(platform, partnersId, accountIdBig, accountIdStr, code, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, null)
        };
    }

    // รีเฟรช token ตามแพลตฟอร์ม
    public async Task<object> RefreshAsync(Platform platform, long shopId, string refreshToken, CancellationToken ct)
    {
        var (partnersId, accountIdBig, accountIdStr) = await _shopRepo.GetShopBindingAsync(shopId, ct);

        return platform switch
        {
            Platform.Shopee => await _shopee.RefreshAsync(platform, partnersId, accountIdBig, accountIdStr, refreshToken, ct),
            Platform.Lazada => await _lazada.RefreshAsync(platform, partnersId, accountIdBig, accountIdStr, refreshToken, ct),
            Platform.TikTok => await _tiktok.RefreshAsync(platform, partnersId, accountIdBig, accountIdStr, refreshToken, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, null)
        };
    }
}
