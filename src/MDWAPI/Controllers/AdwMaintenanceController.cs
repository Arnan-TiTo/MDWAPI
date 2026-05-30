using MDWAPI.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/adw/maintenance")]
    public class AdwMaintenanceController : ControllerBase
    {
        private readonly AppDbContext _db;
        public AdwMaintenanceController(AppDbContext db) => _db = db;

        // POST /api/adw/maintenance/update-addresses?channel=Shopee&shopId=225987929&dryRun=false&overwrite=false
        [HttpPost("update-addresses")]
        public async Task<ActionResult> UpdateAddresses(
            [FromQuery] string channel,
            [FromQuery] long? shopId,
            [FromQuery] bool dryRun = true,
            [FromQuery] bool overwrite = false)
        {
            var p1 = new SqlParameter("@Channel", (object?)channel ?? DBNull.Value);
            var p2 = new SqlParameter("@ShopId", (object?)shopId ?? DBNull.Value);
            var p3 = new SqlParameter("@DryRun", dryRun ? 1 : 0);
            var p4 = new SqlParameter("@Overwrite", overwrite ? 1 : 0);

            await _db.Database.ExecuteSqlRawAsync(
                "EXEC adw.usp_UpdateAddressesFromIDW @Channel,@ShopId,@DryRun,@Overwrite",
                p1, p2, p3, p4);

            return Ok(new { ok = true });
        }
    }
}
