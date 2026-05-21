using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MDWAPI.Services;

namespace MDWAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ShopeeController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ShopeeController> _logger;
    private readonly IConfiguration _config;
    private readonly ShopeeOrderService _svc;

    public ShopeeController(IHttpClientFactory httpClientFactory, ILogger<ShopeeController> logger, IConfiguration config, ShopeeOrderService svc)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _config = config;
        _svc = svc;
    }

    // Simple sanity endpoint showing protected call and config usage.
    [HttpGet("ping")]
    public async Task<IActionResult> Ping()
    {
        var client = _httpClientFactory.CreateClient("Shopee");
        // NOTE: Replace with real Shopee Partner API call + signature later.
        var baseUrl = client.BaseAddress?.ToString() ?? "(no base url)";
        return Ok(new { message = "Shopee client ready", baseUrl });
    }

    // GET /api/shopee/order-detail?shopId=123&orderSn=220101ABCDE
    [HttpGet("order-detail")]
    public async Task<IActionResult> GetOrderDetail(
        [FromQuery] long shopId,
        [FromQuery] string orderSn,
        [FromQuery] string? responseOptionalFields,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orderSn)) return BadRequest("orderSn is required");
        var result = await _svc.GetOrderDetailRawAsync(shopId, orderSn, ct, responseOptionalFields);
        return Ok(result);

    }
}
