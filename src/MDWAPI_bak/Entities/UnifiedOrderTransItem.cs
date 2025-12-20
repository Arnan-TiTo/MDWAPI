namespace MDWAPI.Entities;

public class UnifiedOrderTransItem
{
    public long ItemId { get; set; }
    public long TransId { get; set; }
    public UnifiedOrderTrans Trans { get; set; } = default!;

    public string? OrderRef { get; set; }
    public string? ExternalOrderId { get; set; }
    public byte[]? RawHash { get; set; }
    public string Result { get; set; } = default!; // Created/Updated/Unchanged/Failed/Skipped
    public long? UnifiedOrderId { get; set; }
    public string? ErrorMessage { get; set; }
}
