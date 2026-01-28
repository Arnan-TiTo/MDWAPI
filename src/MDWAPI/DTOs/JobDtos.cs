using System.Text.Json.Serialization;

namespace MDWAPI.DTOs;

public class JobConfigDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string? Schedule { get; set; } // Value1
    public string? Url { get; set; } // Value2
    public string? Query { get; set; } // Value3
    public string? Behavior { get; set; } // Value4
    public string? Watermark { get; set; } // Value5
    public DateTime? UpdatedAt { get; set; }
}

public class JobLogDto
{
    public Guid RunId { get; set; }
    public string Category { get; set; } = "";
    public string Phase { get; set; } = "";
    public string? Step { get; set; }
    public string Level { get; set; } = "INFO";
    public string Message { get; set; } = "";
    public long? JobId { get; set; }
    public string? JobName { get; set; }
    public int? HttpStatus { get; set; }
    public long? DurationMs { get; set; }
    public string? MetaJson { get; set; }
}

public class JobStateUpdateDto
{
    public string? Watermark { get; set; } // Value5
    public DateTime UpdatedAt { get; set; }
}
