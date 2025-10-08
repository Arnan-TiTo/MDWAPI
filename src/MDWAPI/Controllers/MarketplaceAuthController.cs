using MDWAPI.Common;
using MDWAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MDWAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/market/auth")]
public class MarketplaceAuthController : ControllerBase
{
    private readonly MarketplaceAuthService _svc;
    private readonly ShopeeAuthLinkService _shopeeLink;
    private readonly ShopeeTokenRefreshService _refreshSvc;
    private readonly ILogger<MarketplaceAuthController> _log;

    public MarketplaceAuthController(
        MarketplaceAuthService svc, 
        ShopeeAuthLinkService shopeeLink, 
        ShopeeTokenRefreshService refreshSvc,
        ILogger<MarketplaceAuthController> log)
    {
        _svc = svc;
        _shopeeLink = shopeeLink;
       _refreshSvc = refreshSvc;
        _log = log;
    }

    // ========== exchange code ==========
    [HttpPost("exchange")]
    public async Task<IActionResult> Exchange(
        [FromQuery] Platform platform,
        [FromQuery] long shopId,
        [FromQuery] string code,
        CancellationToken ct)
    {
        try
        {
            var result = await _svc.ExchangeCodeAsync(platform, shopId, code, ct);
            return Ok(new { success = true, result });
        }
        catch (HttpRequestException ex)
        {
            // error from provider
            return StatusCode(StatusCodes.Status502BadGateway, new { success = false, error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, error = ex.Message });
        }
    }


    //[HttpPost("refresh")]
    //public async Task<IActionResult> Refresh([FromQuery] Platform platform, [FromQuery] long shopId, [FromQuery] string refreshToken, CancellationToken ct)
    //{
    //    var result = await _svc.RefreshAsync(platform, shopId, refreshToken, ct);
    //    return Ok(result);
    //}


    /// <summary>
    /// POST /api/market/auth/refresh?platform=Shopee&shopId=225987929
    /// </summary>
    [Authorize]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(string platform, long shopId, CancellationToken ct)
    {
        try
        {
            var res = await _refreshSvc.RefreshByShopIdAsync(shopId, ct);
            return Ok(res);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No refresh_token"))
        {
            return NotFound(new { error = "no_refresh_token", message = "No refresh_token found. Please reconnect the shop." });
        }
    }

    // ========== Shopee: create link ==========
    // FE call to endpoint for get Shopee login link
    [HttpGet("shopee/link")]
    public async Task<IActionResult> GetShopeeAuthLink(
        [FromQuery] long shopId,
        [FromQuery] string callbackUrl,
        CancellationToken ct)
    {
        var url = await _shopeeLink.BuildAuthUrlAsync(shopId, callbackUrl, ct);
        return Ok(new { authUrl = url });
    }

    // ========== Shopee: callback (auto-exchange) ==========
    // route for shopee redirect after accept
    // https://vibeandchic.com/api/market/auth/shopee/callback
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
            // แลก token อัตโนมัติและบันทึกลง ChannelTokens
            var result = await _svc.ExchangeCodeAsync(Platform.Shopee, shopIdFromShopee, code, ct);

            // ถ้ามี next ให้ redirect กลับ FE ได้
            if (!string.IsNullOrWhiteSpace(next))
            {
                // return to FE เช่น success=true
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
