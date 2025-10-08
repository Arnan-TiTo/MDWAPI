using System.Text.Json;
using MDWAPI.Repos;

namespace MDWAPI.Services;

public class ShopeeOrderIngestService
{
    private readonly IShopeeOrderIngestRepo _repo;
    private readonly ILogger<ShopeeOrderIngestService> _log;
    public ShopeeOrderIngestService(IShopeeOrderIngestRepo repo, ILogger<ShopeeOrderIngestService> log)
    {
        _repo = repo; _log = log;
    }

    public async Task<int> IngestFromOrderListJsonAsync(long shopId, string listJson, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(listJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("response", out var resp)) return 0;
        if (!resp.TryGetProperty("order_list", out var arr) || arr.ValueKind != JsonValueKind.Array) return 0;

        var count = 0;
        foreach (var order in arr.EnumerateArray())
        {
            await _repo.UpsertOrderWithItemsAsync(shopId, order, ct);
            count++;
        }
        _log.LogInformation("Ingested {Count} orders (shopId={ShopId})", count, shopId);
        return count;
    }
}
