using MDWAPI.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/fe/orders")]
    public class MarketplaceFeOrdersController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ILogger<MarketplaceFeOrdersController> _log;

        public MarketplaceFeOrdersController(AppDbContext db, ILogger<MarketplaceFeOrdersController> log)
        {
            _db = db; _log = log;
        }

        // ====== DTOs ======
        public sealed record UnifiedOrderListItemDto(
            long UnifiedOrderId,
            string? ExternalOrderNo,
            string? Channel,
            long ShopId,
            string? SellerId,
            string? OrderStatus,
            DateTime CreatedTimeUtc,
            DateTime? UpdatedTimeUtc,
            string? ItemsJson,
            string? PaymentsJson,
            string? ShipmentsJson,
            string? ShipToJson
        );

        public sealed record PagedResult<T>(
            int Total,
            int Page,
            int PageSize,
            IEnumerable<T> Data
        );

        // ====== OPTIONS (กัน 405 จาก preflight CORS) ======
        [HttpOptions]
        [AllowAnonymous]
        public IActionResult Options() => Ok();

        // ====== LIST (filter + paging) ======
        // GET /api/fe/orders?channel=TikTok&shopId=123&q=ABC&status=Paid&fromUtc=2025-10-01&toUtc=2025-10-22&page=1&pageSize=50&sort=createdDesc
        [HttpGet]
        public async Task<ActionResult<PagedResult<UnifiedOrderListItemDto>>> List(
            [FromQuery] string? channel,
            [FromQuery] long? shopId,
            [FromQuery] string? q,           // ค้นหา ExternalOrderNo / SellerId แบบ contains
            [FromQuery] string? status,
            [FromQuery] DateTime? fromUtc,
            [FromQuery] DateTime? toUtc,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? sort = "createdDesc",
            CancellationToken ct = default)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0 || pageSize > 200) pageSize = 50;

            var qy = _db.VUnifiedOrders.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(channel))
                qy = qy.Where(o => o.Channel == channel);

            if (shopId is > 0)
                qy = qy.Where(o => o.ShopId == shopId.Value);

            if (!string.IsNullOrWhiteSpace(status))
                qy = qy.Where(o => o.OrderStatus == status);

            if (!string.IsNullOrWhiteSpace(q))
            {
                var kw = q.Trim();
                qy = qy.Where(o =>
                    (o.ExternalOrderNo ?? "").Contains(kw) ||
                    (o.SellerId ?? "").Contains(kw)
                );
            }

            if (fromUtc.HasValue)
                qy = qy.Where(o => o.CreatedTimeUtc >= fromUtc.Value);
            if (toUtc.HasValue)
                qy = qy.Where(o => o.CreatedTimeUtc < toUtc.Value);

            // sort
            qy = sort?.ToLowerInvariant() switch
            {
                "createdasc" => qy.OrderBy(o => o.CreatedTimeUtc).ThenBy(o => o.UnifiedOrderId),
                _ => qy.OrderByDescending(o => o.CreatedTimeUtc).ThenByDescending(o => o.UnifiedOrderId),
            };

            var total = await qy.CountAsync(ct);

            var data = await qy
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(o => new UnifiedOrderListItemDto(
                    o.UnifiedOrderId,
                    o.ExternalOrderNo,
                    o.Channel,
                    o.ShopId,
                    o.SellerId,
                    o.OrderStatus,
                    o.CreatedTimeUtc,
                    o.UpdatedTimeUtc,
                    o.ItemsJson,
                    o.PaymentsJson,
                    o.ShipmentsJson,
                    o.ShipToJson
                ))
                .ToListAsync(ct);

            return Ok(new PagedResult<UnifiedOrderListItemDto>(total, page, pageSize, data));
        }

        // ====== GET by UnifiedOrderId ======
        // GET /api/fe/orders/20014
        [HttpGet("{id:long}")]
        public async Task<ActionResult<UnifiedOrderListItemDto>> GetById(long id, CancellationToken ct)
        {
            var o = await _db.VUnifiedOrders.AsNoTracking()
                .Where(x => x.UnifiedOrderId == id)
                .Select(x => new UnifiedOrderListItemDto(
                    x.UnifiedOrderId,
                    x.ExternalOrderNo,
                    x.Channel,
                    x.ShopId,
                    x.SellerId,
                    x.OrderStatus,
                    x.CreatedTimeUtc,
                    x.UpdatedTimeUtc,
                    x.ItemsJson,
                    x.PaymentsJson,
                    x.ShipmentsJson,
                    x.ShipToJson
                ))
                .SingleOrDefaultAsync(ct);

            if (o is null) return NotFound();
            return Ok(o);
        }

        // ====== GET by (Channel + ExternalOrderNo) ======
        // GET /api/fe/orders/by-external/TikTok/251009MP2J3XNQ
        [HttpGet("by-external/{channel}/{externalOrderNo}")]
        public async Task<ActionResult<UnifiedOrderListItemDto>> GetByExternal(
            string channel, string externalOrderNo, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(externalOrderNo))
                return BadRequest("channel & externalOrderNo are required.");

            var o = await _db.VUnifiedOrders.AsNoTracking()
                .Where(x => x.Channel == channel && x.ExternalOrderNo == externalOrderNo)
                .Select(x => new UnifiedOrderListItemDto(
                    x.UnifiedOrderId,
                    x.ExternalOrderNo,
                    x.Channel,
                    x.ShopId,
                    x.SellerId,
                    x.OrderStatus,
                    x.CreatedTimeUtc,
                    x.UpdatedTimeUtc,
                    x.ItemsJson,
                    x.PaymentsJson,
                    x.ShipmentsJson,
                    x.ShipToJson
                ))
                .SingleOrDefaultAsync(ct);

            if (o is null) return NotFound();
            return Ok(o);
        }
    }
}
