using MDWAPI.Data;
using MDWAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/adw/order-items")]
    public class AdwOrderItemsController : ControllerBase
    {
        private readonly AppDbContext _db;
        public AdwOrderItemsController(AppDbContext db) => _db = db;

        // GET /api/adw/order-items?channel=Shopee&shopId=225987929&page=1&pageSize=100&orderId=123
        [HttpGet]
        public async Task<ActionResult<PagedResult<VwOrderMergedItem>>> GetItems(
            [FromQuery] string channel,
            [FromQuery] long shopId,
            [FromQuery] long? orderId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 100)
        {
            if (string.IsNullOrWhiteSpace(channel)) return BadRequest("channel is required.");
            if (shopId == 0) return BadRequest("shopId is required.");
            if (page <= 0) page = 1;
            if (pageSize <= 0 || pageSize > 1000) pageSize = 100;

            var q = _db.VwOrderMergedItems.AsNoTracking()
                     .Where(x => x.Channel == channel && x.ShopId == shopId);

            if (orderId.HasValue)
                q = q.Where(x => x.UnifiedOrderId == orderId.Value);

            var total = await q.CountAsync();
            var items = await q.OrderBy(x => x.UnifiedOrderItemId)
                               .Skip((page - 1) * pageSize)
                               .Take(pageSize)
                               .ToListAsync();

            return Ok(new PagedResult<VwOrderMergedItem>
            {
                Page = page,
                PageSize = pageSize,
                TotalItems = total,
                Items = items
            });
        }
    }
}
