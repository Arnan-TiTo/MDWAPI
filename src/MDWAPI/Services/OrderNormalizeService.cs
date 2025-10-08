using System.Threading;
using System.Threading.Tasks;
using MDWAPI.Models;

namespace MDWAPI.Services
{
    /// <summary>
    /// Facade สำหรับเรียก writer ให้ upsert จาก RAW ของแต่ละแพลตฟอร์ม
    /// มีทั้งเวอร์ชันคืน long (UnifiedOrderId) และเวอร์ชันคืน NormalizeResult
    /// </summary>
    public class OrderNormalizeService
    {
        private readonly IUnifiedOrderWriter _writer;

        public OrderNormalizeService(IUnifiedOrderWriter writer)
        {
            _writer = writer;
        }

        // ====== เวอร์ชันเดิม: คืน UnifiedOrderId (long) ======

        public Task<long> NormalizeShopeeAsync(long? shopId, string? sellerId, string rawJson, string? batchNo, CancellationToken ct)
            => UpsertReturnId(_writer.UpsertFromShopeeRawAsync(shopId, sellerId, rawJson, batchNo, ct));

        public Task<long> NormalizeTiktokAsync(long? shopId, string? sellerId, string rawJson, string? batchNo, CancellationToken ct)
            => UpsertReturnId(_writer.UpsertFromTiktokRawAsync(shopId, sellerId, rawJson, batchNo, ct));

        public Task<long> NormalizeLazadaAsync(long? shopId, string? sellerId, string rawJson, string? batchNo, CancellationToken ct)
            => UpsertReturnId(_writer.UpsertFromLazadaRawAsync(shopId, sellerId, rawJson, batchNo, ct));

        private static async Task<long> UpsertReturnId(Task<NormalizeResult> task)
        {
            var r = await task.ConfigureAwait(false);
            return r.UnifiedOrderId;
        }

        // ====== เวอร์ชันใหม่: ต้องการรายละเอียดผลลัพธ์ (Created/Updated/Unchanged, RawHash) ======

        public Task<NormalizeResult> NormalizeShopeeWithResultAsync(long? shopId, string? sellerId, string rawJson, string? batchNo, CancellationToken ct)
            => _writer.UpsertFromShopeeRawAsync(shopId, sellerId, rawJson, batchNo, ct);

        public Task<NormalizeResult> NormalizeTiktokWithResultAsync(long? shopId, string? sellerId, string rawJson, string? batchNo, CancellationToken ct)
            => _writer.UpsertFromTiktokRawAsync(shopId, sellerId, rawJson, batchNo, ct);

        public Task<NormalizeResult> NormalizeLazadaWithResultAsync(long? shopId, string? sellerId, string rawJson, string? batchNo, CancellationToken ct)
            => _writer.UpsertFromLazadaRawAsync(shopId, sellerId, rawJson, batchNo, ct);
    }
}
