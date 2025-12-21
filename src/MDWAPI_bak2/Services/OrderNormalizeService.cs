using MDWAPI.Data;
using MDWAPI.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Threading.Tasks;

namespace MDWAPI.Services
{
    /// <summary>
    /// Facade สำหรับ normalize orders จาก RAW ของแต่ละแพลตฟอร์ม
    /// รองรับทั้งเวอร์ชันคืน long (UnifiedOrderId) และคืน NormalizeResult
    /// </summary>
    public class OrderNormalizeService
    {
        private readonly AppDbContext _db;
        private readonly IUnifiedOrderWriter _writer;
        private readonly ILogger<OrderNormalizeService> _log;

        public OrderNormalizeService(AppDbContext db, IUnifiedOrderWriter writer, ILogger<OrderNormalizeService> log)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _log = log ?? throw new ArgumentNullException(nameof(log));
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

        public async Task<NormalizeResult> NormalizeTiktokWithResultAsync(
            long? shopId, string? sellerId, string rawOrderJson, string? batchNo, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(rawOrderJson))
                throw new ArgumentException("rawOrderJson is empty.", nameof(rawOrderJson));

            // ใช้คอนเนกชันจาก EF โดยตรง
            var conn = (SqlConnection)_db.Database.GetDbConnection();
            var needClose = conn.State != ConnectionState.Open;

            try
            {
                if (needClose) await conn.OpenAsync(ct).ConfigureAwait(false);

                using var cmd = new SqlCommand("mdw.usp_Normalize_TikTok_Order", conn)
                { CommandType = CommandType.StoredProcedure };

                // ถ้ามีทรานแซกชันของ EF อยู่ ให้ผูก SP เข้าทรานแซกชันเดียวกัน
                var dbTx = _db.Database.CurrentTransaction;
                if (dbTx != null)
                {
                    var sqlTx = dbTx.GetDbTransaction() as SqlTransaction;
                    if (sqlTx != null) cmd.Transaction = sqlTx;
                }

                cmd.Parameters.AddWithValue("@ShopId", (object?)shopId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@SellerId", (object?)sellerId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@BatchNo", (object?)batchNo ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Env", DBNull.Value); // เผื่อใช้ต่อภายหลัง
                cmd.Parameters.Add("@RawOrder", SqlDbType.NVarChar, -1).Value = rawOrderJson;

                var pUnifiedOrderId = cmd.Parameters.Add("@UnifiedOrderId", SqlDbType.BigInt);
                var pExternalOrderId = cmd.Parameters.Add("@ExternalOrderId", SqlDbType.NVarChar, 100);
                var pOutcome = cmd.Parameters.Add("@Outcome", SqlDbType.NVarChar, 20);
                var pRawHash = cmd.Parameters.Add("@RawHash512", SqlDbType.VarBinary, 64);

                pUnifiedOrderId.Direction = ParameterDirection.Output;
                pExternalOrderId.Direction = ParameterDirection.Output;
                pOutcome.Direction = ParameterDirection.Output;
                pRawHash.Direction = ParameterDirection.Output;

                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

                return new NormalizeResult
                {
                    UnifiedOrderId = (long)(pUnifiedOrderId.Value ?? 0L),
                    ExternalOrderId = pExternalOrderId.Value?.ToString() ?? string.Empty,
                    Outcome = Enum.TryParse<NormalizeOutcome>(pOutcome.Value?.ToString(), out var oc) ? oc : NormalizeOutcome.Updated,
                    RawHash = pRawHash.Value as byte[] ?? Array.Empty<byte>()
                };
            }
            catch (SqlException ex)
            {
                // ถ้าสตอร์ฯ ใช้ไม่ได้ ให้ fallback ไปเส้น writer เพื่อให้งานไม่ล้ม
                _log.LogError(ex, "Normalize TikTok via SP failed. Fallback to writer. ShopId={ShopId}, SellerId={SellerId}, BatchNo={BatchNo}",
                              shopId, sellerId, batchNo);
                return await _writer.UpsertFromTiktokRawAsync(shopId, sellerId, rawOrderJson, batchNo, ct)
                                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Normalize TikTok unexpected error. ShopId={ShopId}, SellerId={SellerId}, BatchNo={BatchNo}",
                              shopId, sellerId, batchNo);
                throw; // ให้ Controller บันทึก failed ลง audit ตาม flow เดิม
            }
            finally
            {
                if (needClose && conn.State == ConnectionState.Open)
                    await conn.CloseAsync().ConfigureAwait(false);
            }
        }

        public Task<NormalizeResult> NormalizeLazadaWithResultAsync(long? shopId, string? sellerId, string rawJson, string? batchNo, CancellationToken ct)
            => _writer.UpsertFromLazadaRawAsync(shopId, sellerId, rawJson, batchNo, ct);
    }
}
