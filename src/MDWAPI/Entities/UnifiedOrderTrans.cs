namespace MDWAPI.Entities;

public class UnifiedOrderTrans
{
    public long TransId { get; set; }
    public string Platform { get; set; } = default!;
    public long? ShopId { get; set; }
    public string? SellerId { get; set; }
    public string? BatchNo { get; set; }
    public string? Env { get; set; }
    public string Mode { get; set; } = default!;
    public string? TimeRangeField { get; set; }
    public long? TimeFromEpoch { get; set; }
    public long? TimeToEpoch { get; set; }
    public DateTime RequestAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }

    public int TotalRefs { get; set; }
    public int Attempted { get; set; }
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int UnchangedCount { get; set; }
    public int FailedCount { get; set; }
    public string? Notes { get; set; }

    public List<UnifiedOrderTransItem> Items { get; set; } = new();
}
