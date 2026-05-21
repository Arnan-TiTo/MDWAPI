using MDWAPI.Data;
using MDWAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/adw/orders")]
    public class AdwOrdersController : ControllerBase
    {
        private readonly AppDbContext _db;
        public AdwOrdersController(AppDbContext db) => _db = db;

        // GET /api/adw/orders?channel=Shopee&shopId=225987929&page=1&pageSize=50&status=PAID
        [HttpGet]
        public async Task<ActionResult<PagedResult<VwOrderMerged>>> GetOrders(
            [FromQuery] string channel,
            [FromQuery] long shopId,
            [FromQuery] string? status,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            if (string.IsNullOrWhiteSpace(channel)) return BadRequest("channel is required.");
            if (shopId == 0) return BadRequest("shopId is required.");
            if (page <= 0) page = 1;
            if (pageSize <= 0 || pageSize > 500) pageSize = 50;

            var q = _db.VwOrderMerged.AsNoTracking()
                     .Where(x => x.Channel == channel && x.ShopId == shopId);

            if (!string.IsNullOrEmpty(status))
                q = q.Where(x => x.OrderStatus == status);

            var total = await q.CountAsync();
            var items = await q.OrderByDescending(x => x.CreatedTimeUtc)
                               .Skip((page - 1) * pageSize)
                               .Take(pageSize)
                               .ToListAsync();

            return Ok(new PagedResult<VwOrderMerged>
            {
                Page = page,
                PageSize = pageSize,
                TotalItems = total,
                Items = items
            });
        }

        // GET /api/adw/orders/{unifiedOrderId}
        [HttpGet("{unifiedOrderId:long}")]
        public async Task<ActionResult<VwOrderMerged>> GetById(long unifiedOrderId)
        {
            var item = await _db.VwOrderMerged.AsNoTracking()
                          .FirstOrDefaultAsync(x => x.UnifiedOrderId == unifiedOrderId);
            return item is null ? NotFound() : Ok(item);
        }
    }
}
