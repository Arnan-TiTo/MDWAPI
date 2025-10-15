using System.Web;
using MDWAPI.Repos;
using Microsoft.Extensions.Logging;

namespace MDWAPI.Services
{
    /// <summary>
    /// สร้างลิงก์ OAuth สำหรับ TikTok Shop (TTS)
    /// หมายเหตุ: ฟิลด์ที่ต้องใช้จริงในโปรดักชันอาจมีเพิ่มเติม เช่น scope / merchant_type ฯลฯ
    /// </summary>
    public class TiktokAuthLinkService
    {
        private readonly IPartnerRepo _partnerRepo;
        private readonly ILogger<TiktokAuthLinkService> _log;

        public TiktokAuthLinkService(IPartnerRepo partnerRepo, ILogger<TiktokAuthLinkService> log)
        {
            _partnerRepo = partnerRepo;
            _log = log;
        }

        /// <summary>
        /// partnersId: ชี้ไปที่ mdw.Partners เพื่อดึง AppKey/Environment
        /// accountIdStr: แนะนำให้ส่ง shop_id (string) ถ้ามี เพื่อให้ TikTok รู้ว่าจะเชื่อมร้านไหน
        /// callbackUrl: ระบบคุณที่ TikTok จะ redirect กลับมาพร้อม code
        /// </summary>
        public async Task<string> BuildAuthUrlAsync(int partnersId, string accountIdStr, string callbackUrl, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(accountIdStr))
                throw new ArgumentException("accountIdStr (shop_id) is required for TikTok", nameof(accountIdStr));

            var cfg = await _partnerRepo.GetConfigByPartnersIdAsync(partnersId, ct)
                      ?? throw new InvalidOperationException($"Partners config not found: {partnersId}");

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
            q["shop_id"] = accountIdStr;

            var url = $"{authHost}?{q}";
            _log.LogInformation("TikTok auth URL built: {url}", url);
            return url;
        }
    }
}
