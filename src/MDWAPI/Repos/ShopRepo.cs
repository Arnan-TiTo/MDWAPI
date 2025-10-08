using MDWAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Repos
{
    public class ShopRepo : IShopRepo
    {
        private readonly AppDbContext _db;
        public ShopRepo(AppDbContext db) => _db = db;

        /// <summary>
        /// Map จาก numeric ShopId (เช่น Shopee shop_id) -> (PartnersId, AccountIdBig, AccountIdStr)
        /// - Shopee: ใช้ AccountIdBig = ShopId
        /// - แพลตฟอร์มอื่นที่ต้องใช้ string id: ตอนนี้คืน null ไปก่อน
        /// </summary>
        public async Task<(int partnersId, long? accountIdBig, string? accountIdStr)> GetShopBindingAsync(
            long inputShopId, CancellationToken ct)
        {
            var row = await _db.Shops
                .AsNoTracking()
                .Where(s => s.ShopId == inputShopId)
                .Select(s => new
                {
                    s.PartnerId,
                    AccountIdBig = (long?)s.ShopId,
                    AccountIdStr = (string?)null
                })
                .FirstOrDefaultAsync(ct);

            if (row is null)
                throw new InvalidOperationException($"Shop not found by ShopId={inputShopId}");

            return (row.PartnerId, row.AccountIdBig, row.AccountIdStr);
        }
    }
}
