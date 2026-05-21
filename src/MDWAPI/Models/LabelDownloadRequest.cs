using System.Text.Json;

namespace MDWAPI.Models;

public class LabelDownloadRequest
{
    public string? OrderSn { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public JsonElement? RawBody { get; set; }
}
