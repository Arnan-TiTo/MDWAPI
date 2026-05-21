// MDWAPI/Services/MarketplaceAuthService.cs
using MDWAPI.Common;
using MDWAPI.Repos;

namespace MDWAPI.Services
{
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

        // ===== EXCHANGE (แบบ shopId เดิม) =====
        public async Task<object> ExchangeCodeByShopAsync(Platform platform, long shopId, string code, CancellationToken ct)
        {
            var (partnersId, accountIdBig, accountIdStr) = await _shopRepo.GetShopBindingAsync(shopId, ct);

            return platform switch
            {
                Platform.Shopee => await _shopee.ExchangeCodeAsync(platform, partnersId, accountIdBig, accountIdStr, code, ct),
                Platform.Lazada => await _lazada.ExchangeCodeAsync(platform, partnersId, accountIdBig, accountIdStr, code, ct),
                Platform.TikTok => await _tiktok.ExchangeCodeAsync(platform, partnersId, accountIdBig, accountIdStr, code, ct),
                _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported platform")
            };
        }

        // ===== REFRESH (แบบ shopId เดิม — ไม่รับ refreshToken) =====
        public async Task<object> RefreshByShopAsync(Platform platform, long shopId, CancellationToken ct)
        {
            var (partnersId, accountIdBig, accountIdStr) = await _shopRepo.GetShopBindingAsync(shopId, ct);

            return platform switch
            {
                // Shopee/Lazada: ถ้าคุณมี service อื่นดูแล refresh อัตโนมัติ ก็เรียกตาม flow เดิมได้
                Platform.Shopee => throw new NotSupportedException("Use ShopeeTokenRefreshService (controller เดิม)"),
                Platform.Lazada => throw new NotSupportedException("Implement Lazada auto-refresh if needed"),
                // ✅ TikTok: ใช้ RefreshByAccountAsync เพื่อไปงัด refresh_token จาก DB เอง
                Platform.TikTok => await _tiktok.RefreshByAccountAsync(platform, partnersId, accountIdBig, accountIdStr, ct),
                _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported platform")
            };
        }

        // ===== (ยังคงมี) EXCHANGE/REFRESH แบบส่ง partnersId+account ตรง =====
        public Task<object> ExchangeCodeAsync(Platform platform, int partnersId, long? accountIdBig, string? accountIdStr, string code, CancellationToken ct)
        {
            return platform switch
            {
                Platform.Shopee => _shopee.ExchangeCodeAsync(platform, partnersId, accountIdBig, accountIdStr, code, ct),
                Platform.Lazada => _lazada.ExchangeCodeAsync(platform, partnersId, accountIdBig, accountIdStr, code, ct),
                Platform.TikTok => _tiktok.ExchangeCodeAsync(platform, partnersId, accountIdBig, accountIdStr, code, ct),
                _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported platform")
            };
        }

        public Task<object> RefreshAsync(Platform platform, int partnersId, long? accountIdBig, string? accountIdStr, string refreshToken, CancellationToken ct)
        {
            return platform switch
            {
                Platform.Shopee => _shopee.RefreshAsync(platform, partnersId, accountIdBig, accountIdStr, refreshToken, ct),
                Platform.Lazada => _lazada.RefreshAsync(platform, partnersId, accountIdBig, accountIdStr, refreshToken, ct),
                Platform.TikTok => _tiktok.RefreshAsync(platform, partnersId, accountIdBig, accountIdStr, refreshToken, ct),
                _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported platform")
            };
        }

        public Task<object> RefreshByAccountAsync(
            Platform platform,
            int partnersId,
            long? accountIdBig,
            string? accountIdStr,
            CancellationToken ct)
        {
            switch (platform)
            {
                case Platform.TikTok:
                    return _tiktok.RefreshByAccountAsync(platform, partnersId, accountIdBig, accountIdStr, ct);

                default:
                    throw new NotSupportedException("RefreshByAccountAsync currently supports only TikTok.");
            }
        }
    }
}
