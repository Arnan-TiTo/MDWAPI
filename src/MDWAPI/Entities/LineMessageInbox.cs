using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

// ─── 19. LineMessageInbox ─────────────────────────
[Table("LineMessageInbox", Schema = "mbw")]
public class LineMessageInboxEntry
{
    [Key] public long MessageEventId { get; set; }

    public long? MemberId { get; set; }
    [Required, MaxLength(200)] public string LineUserId { get; set; } = default!;
    [Required, MaxLength(20)] public string MessageType { get; set; } = default!;
    public string? RawPayload { get; set; }
    [Required, MaxLength(20)] public string ProcessStatus { get; set; } = "New";
    public string? ExtractedDataJson { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(MemberId))]
    public Member? Member { get; set; }
}
