using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

[Table("MemberConsentLogs", Schema = "mbw")]
public class MemberConsentLog
{
    [Key] public long ConsentLogId { get; set; }

    public long MemberId { get; set; }
    public long DocumentId { get; set; }
    public bool AcceptedFlag { get; set; }
    public DateTime AcceptedAt { get; set; }
    [Required, MaxLength(30)] public string AcceptedFromChannel { get; set; } = "LIFF";
    [MaxLength(50)] public string? AcceptedIp { get; set; }
    [MaxLength(1000)] public string? AcceptedUserAgent { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(MemberId))]
    public Member Member { get; set; } = default!;

    [ForeignKey(nameof(DocumentId))]
    public ContentDocument Document { get; set; } = default!;
}
