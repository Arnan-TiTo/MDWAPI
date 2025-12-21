using System.Web;
using MDWAPI.Repos;
using Microsoft.Extensions.Logging;

namespace MDWAPI.Services
{
    public class LazadaAuthLinkService
    {
        private readonly IPartnerRepo _partnerRepo;
        private readonly ILogger<LazadaAuthLinkService> _log;

        public LazadaAuthLinkService(IPartnerRepo partnerRepo, ILogger<LazadaAuthLinkService> log)
        {
            _partnerRepo = partnerRepo;
            _log = log;
        }

        /// <summary>
        /// สร้างลิงก์ OAuth สำหรับ Lazada
        /// </summary>
        /// <param name="partnersId">แถวใน mdw.Partners ที่เก็บ AppKey/Environment</param>
        /// <param name="accountIdStr">อาจเป็น seller_name/identifier (optional ใช้เก็บใน state)</param>
        /// <param name="callbackUrl">URL ของระบบคุณที่ Lazada จะ redirect กลับมา</param>
        public async Task<string> BuildAuthUrlAsync(int partnersId, string? accountIdStr, string callbackUrl, CancellationToken ct = default)
        {
            var cfg = await _partnerRepo.GetConfigByPartnersIdAsync(partnersId, ct)
                      ?? throw new InvalidOperationException($"Partners config not found: {partnersId}");

            var appKey = cfg.AppKey;
            if (string.IsNullOrWhiteSpace(appKey))
                throw new InvalidOperationException("Lazada AppKey not found in Partners");

            // Lazada ใช้โดเมน authorize เดียว (region/ประเทศจะยึดจากบัญชี)
            // เอกสารทั่วไป: https://auth.lazada.com/oauth/authorize
            var authHost = "https://auth.lazada.com/oauth/authorize";

            // ทำ state ไว้อ้างอิงย้อนกลับ (ปรับได้ตามต้องการ)
            var state = $"pid={partnersId}|acct={accountIdStr ?? "-"}";

            var q = HttpUtility.ParseQueryString(string.Empty);
            q["response_type"] = "code";
            q["force_auth"] = "true";
            q["client_id"] = appKey;
            q["redirect_uri"] = callbackUrl;
            q["state"] = state;

            var url = $"{authHost}?{q}";
            _log.LogInformation("Lazada auth URL built: {url}", url);
            return url;
        }
    }
}
