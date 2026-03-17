using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

// ─── 20. AdminRoles ───────────────────────────────
[Table("AdminRoles", Schema = "mbw")]
public class AdminRole
{
    [Key] public int RoleId { get; set; }

    [Required, MaxLength(50)] public string RoleName { get; set; } = default!;
    public string? Permissions { get; set; } // JSON
    public DateTime CreatedAt { get; set; }
}

// ─── 21. AdminAuditLogs ───────────────────────────
[Table("AdminAuditLogs", Schema = "mbw")]
public class AdminAuditLog
{
    [Key] public long AuditId { get; set; }

    public int UserId { get; set; }                    // FK → dbo.Users
    [Required, MaxLength(50)] public string ActionType { get; set; } = default!;
    [MaxLength(50)] public string? EntityType { get; set; }
    [MaxLength(100)] public string? EntityId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    [MaxLength(50)] public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation — cross-schema FK → dbo.Users
    [ForeignKey(nameof(UserId))]
    public Models.User User { get; set; } = default!;
}

// ─── 22. WebhookInbox ─────────────────────────────
[Table("WebhookInbox", Schema = "mbw")]
public class WebhookInboxEntry
{
    [Key] public long InboxId { get; set; }

    [Required, MaxLength(20)] public string Source { get; set; } = default!;
    [Required, MaxLength(50)] public string EventType { get; set; } = default!;
    [Required, MaxLength(200)] public string EventKey { get; set; } = default!;
    public string? RawPayload { get; set; }
    [Required, MaxLength(20)] public string ProcessStatus { get; set; } = "New";
    public DateTime? ProcessedAt { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ─── 23. ApiCallLogs ──────────────────────────────
[Table("ApiCallLogs", Schema = "mbw")]
public class ApiCallLog
{
    [Key] public long ApiLogId { get; set; }

    [Required, MaxLength(100)] public string ApiName { get; set; } = default!;
    [Required, MaxLength(10)] public string RequestMethod { get; set; } = default!;
    [Required, MaxLength(500)] public string RequestUrl { get; set; } = default!;
    [MaxLength(200)] public string? RequestRef { get; set; }
    public string? RequestPayload { get; set; }
    public int? ResponseStatus { get; set; }
    public string? ResponsePayload { get; set; }
    public int? DurationMs { get; set; }
    [MaxLength(1000)] public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
}
