using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MDWAPI.Data;

namespace MDWAPI.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/dashboard")]
    public class DashboardController : ControllerBase
    {
        private readonly AppDbContext _db;
        public DashboardController(AppDbContext db) => _db = db;

        [HttpGet]
        public async Task<ActionResult> GetDashboard()
        {
            var tokenList = await _db.ChannelTokens
                .Select(t => new TokenStatusDto
                {
                    Id = t.Id,
                    Channel = t.Channel ?? "",
                    Environment = t.Environment ?? "",
                    AccountIdStr = t.AccountIdStr ?? "",
                    AccountIdBig = t.AccountIdBig,
                    AccessTokenExpAt = t.AccessTokenExpAt,
                    RefreshTokenExpAt = t.RefreshTokenExpAt,
                    IsActive = t.isActive,
                    TokenStatus =
                        t.isActive == false ? "inactive" :
                        (t.AccessTokenExpAt < DateTime.UtcNow ? "expired" : "active")
                })
                .OrderBy(t => t.Channel)
                .ThenBy(t => t.Environment)
                .ToListAsync();

            var jobs = await _db.UnifiedOrderTrans
                .OrderByDescending(x => x.TransId)
                .Take(20)
                .Select(x => new UnifiedOrderTransDto
                {
                    TransId = x.TransId,
                    Platform = x.Platform ?? "",
                    ShopId = x.ShopId,
                    Mode = x.Mode ?? "",
                    RequestAtUtc = x.RequestAtUtc,
                    CompletedAtUtc = x.CompletedAtUtc,
                    TotalRefs = x.TotalRefs,
                    Attempted = x.Attempted,
                    CreatedCount = x.CreatedCount,
                    UpdatedCount = x.UpdatedCount,
                    UnchangedCount = x.UnchangedCount,
                    FailedCount = x.FailedCount,
                    Notes = x.Notes ?? ""
                })
                .ToListAsync();

            return Ok(new
            {
                tokens = tokenList,
                jobs = jobs
            });
        }
    }

    // DTOs ใช้ nullable ถ้าค่าอาจเป็น null
    public class TokenStatusDto
    {
        public int Id { get; set; }
        public string? Channel { get; set; }
        public string? Environment { get; set; }
        public string? AccountIdStr { get; set; }
        public long? AccountIdBig { get; set; }
        public DateTime AccessTokenExpAt { get; set; }
        public DateTime? RefreshTokenExpAt { get; set; }
        public bool IsActive { get; set; }
        public string? TokenStatus { get; set; }
    }

    public class UnifiedOrderTransDto
    {
        public long TransId { get; set; }
        public string? Platform { get; set; }
        public long? ShopId { get; set; }
        public string? Mode { get; set; }
        public DateTime RequestAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public int TotalRefs { get; set; }
        public int Attempted { get; set; }
        public int CreatedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int UnchangedCount { get; set; }
        public int FailedCount { get; set; }
        public string? Notes { get; set; }
    }
}