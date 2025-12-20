using MDWAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MDWAPI.Controllers
{

    [ApiController]
    [Authorize]
    [Route("api/market/shopee")]
    public class MarketIngestController : ControllerBase
    {
        private readonly ShopeeOrderService _api;         // ตัวที่คุณใช้เรียก Shopee
        private readonly ShopeeOrderIngestService _ingest;

        public MarketIngestController(ShopeeOrderService api, ShopeeOrderIngestService ingest)
        {
            _api = api; _ingest = ingest;
        }

        // GET /api/market/shopee/ingest/list?shopId=225987929&timeRangeField=create_time&timeFrom=...&timeTo=...
        [HttpGet("ingest/list")]
        public async Task<IActionResult> IngestByList(
            long shopId, string timeRangeField, long timeFrom, long timeTo, int pageSize = 50, string? cursor = null, string? status = null, CancellationToken ct = default)
        {
            var json = await _api.GetOrderListRawAsync(shopId, timeRangeField, timeFrom, timeTo, pageSize, cursor, status, ct);
            var inserted = await _ingest.IngestFromOrderListJsonAsync(shopId, json, ct);
            return Ok(new { inserted });
        }
    }

}
