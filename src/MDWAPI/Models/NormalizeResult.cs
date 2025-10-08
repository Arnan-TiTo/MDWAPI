namespace MDWAPI.Models;

public enum NormalizeOutcome { Created, Updated, Unchanged }

public class NormalizeResult
{
    public NormalizeOutcome Outcome { get; set; }
    public long UnifiedOrderId { get; set; }
    public string ExternalOrderId { get; set; } = default!;
    public byte[] RawHash { get; set; } = default!;
}
