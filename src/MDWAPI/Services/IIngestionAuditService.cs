using MDWAPI.Entities;

namespace MDWAPI.Services;

public interface IIngestionAuditService
{
    Task<long> BeginAsync(UnifiedOrderTrans trans, CancellationToken ct);
    Task AddItemAsync(long transId, UnifiedOrderTransItem item, CancellationToken ct);
    Task CompleteAsync(long transId, Action<UnifiedOrderTrans> update, CancellationToken ct);
}
