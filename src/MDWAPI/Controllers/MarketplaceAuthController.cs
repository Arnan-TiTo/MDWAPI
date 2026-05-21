using MDWAPI.Common;
using MDWAPI.Dtos;
using MDWAPI.Repos;
using MDWAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace MDWAPI.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/market/auth")]
    public class MarketplaceAuthController : ControllerBase
    {
        private readonly MarketplaceAuthService _svc;
        private readonly ShopeeAuthLinkService _shopeeLink;
        private readonly LazadaAuthLinkService _lazadaLink;
        private readonly TiktokAuthLinkService _tiktokLink;
        private readonly ShopeeTokenRefreshService _shopeeRefresh;
        private readonly IChannelTokenRepo _chanTokens;
        private readonly ILogger<MarketplaceAuthController> _log;

        public MarketplaceAuthController(
            MarketplaceAuthService svc,
            ShopeeAuthLinkService shopeeLink,
            LazadaAuthLinkService lazadaLink,
            TiktokAuthLinkService tiktokLink,
            ShopeeTokenRefreshService shopeeRefresh,
            IChannelTokenRepo chanTokens,
            ILogger<MarketplaceAuthController> log)
        {
            _svc = svc;
            _shopeeLink = shopeeLink;
            _lazadaLink = lazadaLink;
            _tiktokLink = tiktokLink;
            _shopeeRefresh = shopeeRefresh;
            _chanTokens = chanTokens;
            _log = log;
        }

        // =========================
        // Create Link (ทุกแพลตฟอร์ม)
        // =========================
        [HttpGet("link")]
        public async Task<IActionResult> GetAuthLink(
            [FromQuery] Platform platform,
            [FromQuery] long? shopId,
            [FromQuery] int? partnersId,
            [FromQuery] string? accountIdStr,
            [FromQuery] string callbackUrl,
            CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(callbackUrl))
                    return BadRequest(new { success = false, error = "callbackUrl_required" });

                string url;
                switch (platform)
                {
                    case Platform.Shopee:
                        if (!shopId.HasValue || shopId.Value <= 0)
                            return BadRequest(new { success = false, error = "shopId_required_for_shopee" });
                        url = await _shopeeLink.BuildAuthUrlAsync(shopId.Value, callbackUrl, ct);
                        break;

                    case Platform.Lazada:
                        if (!partnersId.HasValue)
                            return BadRequest(new { success = false, error = "partnersId_required_for_lazada" });
                        url = await _lazadaLink.BuildAuthUrlAsync(partnersId.Value, accountIdStr, callbackUrl, ct);
                        break;

                    case Platform.TikTok:
                        //if (!partnersId.HasValue)
                            //return BadRequest(new { success = false, error = "partnersId_required_for_tiktok" });
                        if (string.IsNullOrWhiteSpace(accountIdStr))
                            return BadRequest(new { success = false, error = "accountIdStr_required_for_tiktok", hint = "use TikTok shop_id as accountIdStr" });
                        url = await _tiktokLink.BuildAuthUrlAsync(Convert.ToInt64(accountIdStr!), callbackUrl, ct);
                        break;

                    default:
                        return BadRequest(new { success = false, error = "unsupported_platform" });
                }

                return Ok(new { success = true, authUrl = url });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, error = ex.Message });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "GetAuthLink failed");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, error = ex.Message });
            }
        }

        public record ExchangeCbDto(string Platform, long ShopId, string Code, string State);

        [HttpPost("exchange-cb")]
        [AllowAnonymous]                     // อนุญาตไม่ต้องล็อกอิน
        [EnableCors("CallbackCors")]         // policy CORS เฉพาะ action
        [IgnoreAntiforgeryToken]             // กันกรณีมี AutoValidateAntiForgeryToken ทั่วระบบ
        public async Task<IActionResult> ExchangeFromCallback([FromBody] ExchangeCbDto dto, CancellationToken ct)
        {
            // TODO: แนะนำตรวจความถูกต้องของ state (เช่น HMAC + หมดอายุ) ก่อนแลกจริง
            if (string.IsNullOrWhiteSpace(dto.Platform) || dto.ShopId <= 0 || string.IsNullOrWhiteSpace(dto.Code))
                return BadRequest(new { success = false, error = "invalid_payload" });

            var platform = Enum.Parse<Platform>(dto.Platform, ignoreCase: true);
            var result = await _svc.ExchangeCodeByShopAsync(platform, dto.ShopId, dto.Code, ct);
            return Ok(new { success = true, result });
        }

        // =========================
        // Exchange (ทุกแพลตฟอร์ม) — ใช้ shopId + code
        // =========================
        [HttpPost("exchange")]
        public async Task<IActionResult> Exchange(
            [FromQuery] Platform platform,
            [FromQuery] long shopId,
            [FromQuery] string code,
            CancellationToken ct)
        {
            if (shopId <= 0 || string.IsNullOrWhiteSpace(code))
                return BadRequest(new { success = false, error = "shopId_and_code_are_required" });

            try
            {
                var result = await _svc.ExchangeCodeByShopAsync(platform, shopId, code, ct);
                return Ok(new { success = true, result });
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { success = false, error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, error = ex.Message });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Exchange failed");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, error = ex.Message });
            }
        }

        // =========================
        // Refresh (ทุกแพลตฟอร์ม) — ใช้ shopId อย่างเดียว
        // =========================
        // POST /api/market/auth/refresh?platform=Shopee&shopId=225987929
        // POST /api/market/auth/refresh?platform=Lazada&shopId=<binding_shopId>
        // POST /api/market/auth/refresh?platform=TikTok&shopId=<shop_id>[&partnersId=1013]
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh(
            [FromQuery] Platform platform,
            [FromQuery] long shopId,
            [FromQuery] int? partnersId, // optional; ถ้าไม่ส่งมาจะ auto-detect จาก ChannelTokens
            CancellationToken ct)
        {
            if (shopId <= 0)
                return BadRequest(new { success = false, error = "shopId_required" });

            try
            {
                object result;

                if (platform == Platform.Shopee)
                {
                    result = await _shopeeRefresh.RefreshByShopIdAsync(shopId, ct);
                    return Ok(new { success = true, result });
                }

                if (platform == Platform.Lazada)
                {
                    return BadRequest(new { success = false, error = "not_supported_for_lazada" });
                }

                // ===== TikTok =====
                int? pid = partnersId;

                if (!pid.HasValue)
                {
                    pid = await AutoDetectPartnersIdForTikTokAsync(shopId, ct);
                    if (!pid.HasValue)
                    {
                        return BadRequest(new
                        {
                            success = false,
                            error = "partnersId_required",
                            message = "Cannot auto-detect partnersId for this TikTok shop. Please supply ?partnersId=xxxx."
                        });
                    }
                }

                result = await _svc.RefreshByAccountAsync(
                    platform: Platform.TikTok,
                    partnersId: pid.Value,
                    accountIdBig: null,
                    accountIdStr: shopId.ToString(),
                    ct: ct);

                return Ok(new { success = true, result });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No refresh_token", StringComparison.OrdinalIgnoreCase))
            {
                return NotFound(new { success = false, error = "no_refresh_token", message = "No refresh_token found. Please reconnect the shop." });
            }
            catch (NotSupportedException ex)
            {
                return BadRequest(new { success = false, error = ex.Message });
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { success = false, error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, error = ex.Message });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Refresh failed");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// เดา partnersId สำหรับ TikTok จาก ChannelTokens เท่านั้น:
        /// - ลอง environment=prod ก่อน, ถ้าไม่เจอค่อยลอง sandbox
        /// </summary>
        private async Task<int?> AutoDetectPartnersIdForTikTokAsync(long shopId, CancellationToken ct)
        {
            var accountIdStr = shopId.ToString();

            ChannelTokenDtos? row = null;

            //ลองไล่ลำดับ env 
            if (row == null)
            {
                foreach (var e in new[] { "prod", "sandbox" })
                {
                    row = await _chanTokens.GetLatestForRefreshByStrAsync(
                        channel: "tiktok",
                        environment: e,
                        appKey: null,
                        accountIdStr: accountIdStr,
                        ct: ct
                    );
                    if (row?.PartnersId is > 0) break;
                }
            }

            // final fallback: (ถ้ามี) เมธอดที่ผ่อนเงื่อนไขด้าน account id
            if (row == null)
            {
                row = await _chanTokens.GetLatestForTikTokShopAsync(accountIdStr, ct);
            }

            return (row?.PartnersId is > 0) ? row!.PartnersId : null;
        }

    }
}
