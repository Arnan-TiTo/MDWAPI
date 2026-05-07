using MDWAPI.DTOs;
using MDWAPI.Models;

namespace MDWAPI.Services;

public interface IUnifiedOrderWriter
{
    Task<long> InsertRawAsync(string channel, long? shopId, string? sellerId, string externalOrderId, string rawJson, string? batchNo, CancellationToken ct);
    Task<long> UpsertAsync(UnifiedOrderDto dto, CancellationToken ct);

    Task<NormalizeResult> UpsertFromShopeeRawAsync(long? shopId, string? sellerId, string rawJson, string? batchNo, CancellationToken ct);
    Task<NormalizeResult> UpsertFromTiktokRawAsync(long? shopId, string? sellerId, string rawJson, string? batchNo, CancellationToken ct);
    Task<NormalizeResult> UpsertFromLazadaRawAsync(long? shopId, string? sellerId, string rawJson, string? batchNo, CancellationToken ct);
    Task UpsertShopeeEscrowAsync(string orderSn, string escrowJson, CancellationToken ct);
}
