using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

// ─── 4. MemberMappingRequests ─────────────────────
[Table("MemberMappingRequests", Schema = "mbw")]
public class MemberMappingRequest
{
    [Key] public long RequestId { get; set; }

    public long MemberId { get; set; }
    [Required, MaxLength(20)] public string PlatformType { get; set; } = default!;
    public int? ShopId { get; set; }
    [Required, MaxLength(200)] public string PlatformAccountKey { get; set; } = default!;
    [MaxLength(200)] public string? PlatformAccountName { get; set; }
    [Required, MaxLength(30)] public string SourceType { get; set; } = "ADMIN";
    [Required, MaxLength(20)] public string RequestStatus { get; set; } = "Pending";
    public decimal? ConfidenceScore { get; set; }
    [MaxLength(100)] public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    [MaxLength(1000)] public string? ReviewNote { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(MemberId))]
    public Member Member { get; set; } = default!;
    public List<MemberMappingEvidence> Evidences { get; set; } = new();
}

// ─── 5. MemberMappingEvidence ─────────────────────
[Table("MemberMappingEvidence", Schema = "mbw")]
public class MemberMappingEvidence
{
    [Key] public long EvidenceId { get; set; }

    public long RequestId { get; set; }
    [Required, MaxLength(30)] public string EvidenceType { get; set; } = default!;
    public string? EvidenceValue { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(RequestId))]
    public MemberMappingRequest Request { get; set; } = default!;
}
