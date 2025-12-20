using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using MDWAPI.Data;
using MDWAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Services;

public class IngestionAuditService : IIngestionAuditService
{
    private readonly AppDbContext _db;

    public IngestionAuditService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<long> BeginAsync(UnifiedOrderTrans trans, CancellationToken ct)
    {
        // 1) Insert ด้วย EF (ไม่มี trigger แล้ว → OUTPUT OK)
        _db.Add(trans);
        await _db.SaveChangesAsync(ct); // ได้ TransId

        // 2) เรียก SP ตั้ง BatchNo (ถ้า trans.BatchNo ยังว่าง)
        if (string.IsNullOrWhiteSpace(trans.BatchNo))
        {
            var conn = _db.Database.GetDbConnection();
            await EnsureOpenAsync(conn, ct);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "mdw.UnifiedOrderTrans_AssignBatchNo";
            cmd.CommandType = CommandType.StoredProcedure;

            AddIn(cmd, "@TransId", SqlDbType.BigInt, null, trans.TransId);
            AddIn(cmd, "@Force", SqlDbType.Bit, null, 0);
            AddIn(cmd, "@PadLen", SqlDbType.Int, null, 8);
            var outBatch = AddOut(cmd, "@OutBatchNo", SqlDbType.NVarChar, 100);

            await cmd.ExecuteNonQueryAsync(ct);
            trans.BatchNo = Convert.ToString(outBatch.Value);
        }

        return trans.TransId;
    }

    public async Task AddItemAsync(long transId, UnifiedOrderTransItem item, CancellationToken ct)
    {
        item.TransId = transId;
        _db.Add(item);
        await _db.SaveChangesAsync(ct);
    }

    public async Task CompleteAsync(long transId, Action<UnifiedOrderTrans> mutator, CancellationToken ct)
    {
        var entity = await _db.UnifiedOrderTrans.FirstOrDefaultAsync(x => x.TransId == transId, ct);
        if (entity is null) return;

        mutator(entity);
        entity.CompletedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    // helpers
    private static async Task EnsureOpenAsync(DbConnection conn, CancellationToken ct)
    {
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);
    }

    private static SqlParameter AddIn(DbCommand cmd, string name, SqlDbType type, int? size, object? value)
    {
        var p = new SqlParameter(name, type);
        if (size.HasValue) p.Size = size.Value;
        p.Direction = ParameterDirection.Input;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
        return p;
    }

    private static SqlParameter AddOut(DbCommand cmd, string name, SqlDbType type, int? size = null)
    {
        var p = new SqlParameter(name, type);
        if (size.HasValue) p.Size = size.Value;
        p.Direction = ParameterDirection.Output;
        cmd.Parameters.Add(p);
        return p;
    }
}
