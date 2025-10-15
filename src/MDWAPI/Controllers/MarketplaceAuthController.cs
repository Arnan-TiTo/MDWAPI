using MDWAPI.Common;
using MDWAPI.Services;
using Microsoft.AspNetCore.Authorization;
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
        private readonly ShopeeTokenRefreshService _shopeeRefresh; // ✅ เรียกใช้ตรงสำหรับ Shopee
        private readonly ILogger<MarketplaceAuthController> _log;

        public MarketplaceAuthController(
            MarketplaceAuthService svc,
            ShopeeAuthLinkService shopeeLink,
            LazadaAuthLinkService lazadaLink,
            TiktokAuthLinkService tiktokLink,
            ShopeeTokenRefreshService shopeeRefresh,
            ILogger<MarketplaceAuthController> log)
        {
            _svc = svc;
            _shopeeLink = shopeeLink;
            _lazadaLink = lazadaLink;
            _tiktokLink = tiktokLink;
            _shopeeRefresh = shopeeRefresh;
            _log = log;
        }

        // =========================
        // Create Link (ทุกแพลตฟอร์ม)
        // =========================
        // GET /api/market/auth/link?platform=Shopee&shopId=225987929&callbackUrl=...
        // GET /api/market/auth/link?platform=Lazada&partnersId=1013&accountIdStr=SELLER_ABC&callbackUrl=...
        // GET /api/market/auth/link?platform=TikTok&partnersId=1013&accountIdStr=<shop_id>&callbackUrl=...
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
                        if (!partnersId.HasValue)
                            return BadRequest(new { success = false, error = "partnersId_required_for_tiktok" });
                        if (string.IsNullOrWhiteSpace(accountIdStr))
                            return BadRequest(new { success = false, error = "accountIdStr_required_for_tiktok", hint = "use TikTok shop_id as accountIdStr" });
                        url = await _tiktokLink.BuildAuthUrlAsync(partnersId.Value, accountIdStr!, callbackUrl, ct);
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

        // =========================
        // Exchange (ทุกแพลตฟอร์ม) — ใช้ shopId + code
        // =========================
        // POST /api/market/auth/exchange?platform=Shopee&shopId=225987929&code=...
        // POST /api/market/auth/exchange?platform=Lazada&shopId=<binding_shopId>&code=...
        // POST /api/market/auth/exchange?platform=TikTok&shopId=<shop_id>&code=ROW_...
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
        // POST /api/market/auth/refresh?platform=TikTok&shopId=<shop_id>
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh(
            [FromQuery] Platform platform,
            [FromQuery] long shopId,
            CancellationToken ct)
        {
            if (shopId <= 0)
                return BadRequest(new { success = false, error = "shopId_required" });

            try
            {
                object result;

                if (platform == Platform.Shopee)
                {
                    // ✅ Shopee ใช้ service เดิม (อ่าน refresh token เองจาก DB)
                    result = await _shopeeRefresh.RefreshByShopIdAsync(shopId, ct);
                }
                else
                {
                    // ✅ TikTok/Lazada -> ให้ MarketplaceAuthService ไปตัดสินใจ (TikTok = RefreshByAccountAsync)
                    result = await _svc.RefreshByShopAsync(platform, shopId, ct);
                }

                return Ok(new { success = true, result });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No refresh_token", StringComparison.OrdinalIgnoreCase))
            {
                return NotFound(new { success = false, error = "no_refresh_token", message = "No refresh_token found. Please reconnect the shop." });
            }
            catch (NotSupportedException ex)
            {
                // เผื่อ Lazada ยังไม่ทำ auto-refresh
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

        // =========================
        // (ออปชัน) Shopee callback เดิม — คงไว้เพื่อความเข้ากันได้
        // =========================
        [AllowAnonymous]
        [HttpGet("shopee/callback")]
        public async Task<IActionResult> ShopeeCallback(
            [FromQuery] string code,
            [FromQuery(Name = "shop_id")] long shopIdFromShopee,
            [FromQuery] string? state,
            [FromQuery] string? main_account_id,
            [FromQuery] string? next,
            CancellationToken ct = default)
        {
            try
            {
                var result = await _svc.ExchangeCodeByShopAsync(Platform.Shopee, shopIdFromShopee, code, ct);

                if (!string.IsNullOrWhiteSpace(next))
                {
                    var uri = new UriBuilder(next);
                    var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
                    q["success"] = "true";
                    uri.Query = q.ToString();
                    return Redirect(uri.ToString());
                }

                return Ok(new { success = true, shopId = shopIdFromShopee, exchanged = result });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Shopee OAuth callback failed: {Msg}", ex.Message);
                if (!string.IsNullOrWhiteSpace(next))
                {
                    var uri = new UriBuilder(next);
                    var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
                    q["success"] = "false";
                    q["error"] = "callback_exchange_failed";
                    uri.Query = q.ToString();
                    return Redirect(uri.ToString());
                }
                return BadRequest(new { success = false, error = ex.Message });
            }
        }
    }
}
