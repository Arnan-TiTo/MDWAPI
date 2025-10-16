using MDWAPI.Models;
using MDWAPI.Repos;
using Microsoft.Extensions.Logging;
using System.Web;

namespace MDWAPI.Services
{
    /// <summary>
    /// สร้างลิงก์ OAuth สำหรับ TikTok Shop (TTS)
    /// หมายเหตุ: ฟิลด์ที่ต้องใช้จริงในโปรดักชันอาจมีเพิ่มเติม เช่น scope / merchant_type ฯลฯ
    /// </summary>
    public class TiktokAuthLinkService
    {
        private readonly IPartnerRepo _partnerRepo;
        private readonly IShopRepo _shopRepo;
        private readonly ILogger<TiktokAuthLinkService> _log;

        public TiktokAuthLinkService(IPartnerRepo partnerRepo, IShopRepo shopRepo, ILogger<TiktokAuthLinkService> log)
        {
            _partnerRepo = partnerRepo;
            _shopRepo = shopRepo;
            _log = log;
        }

        /// <summary>
        /// partnersId: ชี้ไปที่ mdw.Partners เพื่อดึง AppKey/Environment
        /// accountIdStr:ส่ง shop_id (string) เพื่อให้ TikTok รู้ว่าจะเชื่อมร้านไหน
        /// callbackUrl: TikTok จะ redirect กลับมาพร้อม code
        /// </summary>
        public async Task<string> BuildAuthUrlAsync(long shopId, string callbackUrl, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(shopId.ToString()))
                throw new ArgumentException("accountIdStr (shop_id) is required for TikTok", nameof(shopId));

            var (partnersId, accountIdStr, _) = await _shopRepo.GetShopBindingAsync(shopId, ct);
            var cfg = await _partnerRepo.GetConfigByPartnersIdAsync(partnersId, ct)
                      ?? throw new InvalidOperationException($"Partners config not found: {partnersId}");

            if (cfg.PartnerId is null || string.IsNullOrWhiteSpace(cfg.PartnerKey))
                throw new InvalidOperationException("Shopee PartnerId/PartnerKey is required");

            var appKey = cfg.AppKey;
            if (string.IsNullOrWhiteSpace(appKey))
                throw new InvalidOperationException("TikTok AppKey (client_key) not found in Partners");

            // โดเมน authorize ของ TikTok Shop
            // เอกสารทั่วไป: https://auth.tiktok-shops.com/oauth/authorize
            // (ถ้าต้องใช้ sandbox จะเป็นโดเมนเดียวกัน แต่อิงสภาพแวดล้อมจากแอคเคานท์)
            var authHost = "https://auth.tiktok-shops.com/oauth/authorize";

            // แนบข้อมูลอ้างอิงกลับไปใน state ตามต้องการ
            var state = $"pid={partnersId}|shop_id={accountIdStr}";

            var q = HttpUtility.ParseQueryString(string.Empty);
            q["app_key"] = appKey;
            q["redirect_uri"] = callbackUrl;
            q["state"] = state;

            // บางสภาพแวดล้อมต้องการส่ง shop_id ไปด้วย
            q["shop_id"] = accountIdStr.ToString();

            var url = $"{authHost}?{q}";
            _log.LogInformation("TikTok auth URL built: {url}", url);
            return url;
        }
    }
}
