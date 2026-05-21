using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

/// <summary>
/// Log สำหรับงาน background job (interval/tick, job start/step/finish)
/// เก็บเป็นแถวต่อเหตุการณ์ เพื่อ trace ได้ละเอียด และผูกด้วย RunId
/// </summary>
[Table("JobLogs", Schema = "dbo")]
public class JobLog
{
    [Key]
    public long Id { get; set; }

    /// <summary>
    /// Correlation id ของการ run ครั้งนี้ (ผูก job start/step/finish ไว้ด้วยกัน)
    /// </summary>
    public Guid RunId { get; set; }

    /// <summary>
    /// กลุ่มของ log เช่น MarketJob, Tick, ...
    /// </summary>
    [MaxLength(50)]
    public string Category { get; set; } = "MarketJob";

    /// <summary>Id ของ job ในตาราง dbo.Misc (ถ้ามี)</summary>
    public long? JobId { get; set; }

    /// <summary>ชื่อ job ในตาราง dbo.Misc (ถ้ามี)</summary>
    [MaxLength(200)]
    public string? JobName { get; set; }

    /// <summary>start | step | finish | error | skip</summary>
    [MaxLength(20)]
    public string Phase { get; set; } = "step";

    /// <summary>ชื่อขั้นตอน เช่น IsDue, BuildWindows, PostWindow, SaveWatermark</summary>
    [MaxLength(100)]
    public string? Step { get; set; }

    /// <summary>INFO | WARN | ERROR</summary>
    [MaxLength(10)]
    public string Level { get; set; } = "INFO";

    /// <summary>ข้อความ log</summary>
    [MaxLength(4000)]
    public string Message { get; set; } = string.Empty;

    public int? HttpStatus { get; set; }
    public long? DurationMs { get; set; }

    /// <summary>ข้อมูลเสริม (json string) เช่น window from/to, qs, etc.</summary>
    [MaxLength(4000)]
    public string? MetaJson { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
