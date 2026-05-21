// MDWAPI/Repos/ShopRepo.cs
using MDWAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Repos
{
    public class ShopRepo : IShopRepo
    {
        private readonly AppDbContext _db;
        public ShopRepo(AppDbContext db) => _db = db;

        public async Task<(int partnersId, long? accountIdBig, string? accountIdStr)> GetShopBindingAsync(
            long inputShopId, CancellationToken ct)
        {
            // ดึงข้อมูล shop + platform
            var row = await _db.Shops
                .AsNoTracking()
                .Where(s => s.ShopId == inputShopId)
                .Select(s => new { s.PartnerId, s.Platform, s.ShopId })
                .FirstOrDefaultAsync(ct);

            if (row is null)
                throw new InvalidOperationException($"Shop not found by ShopId={inputShopId}");

            // Shopee ใช้ AccountIdBig, TikTok ใช้ AccountIdStr
            if (string.Equals(row.Platform, "tiktok", StringComparison.OrdinalIgnoreCase))
            {
                return (row.PartnerId, null, row.ShopId.ToString());
            }

            // ค่าเดิมสำหรับ Shopee/Lazada
            return (row.PartnerId, row.ShopId, null);
        }
    }
}
