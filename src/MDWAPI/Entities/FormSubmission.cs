using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

// ─── 6. FormSubmissions ───────────────────────────
[Table("FormSubmissions", Schema = "mbw")]
public class FormSubmission
{
    [Key] public long SubmissionId { get; set; }

    public long MemberId { get; set; }
    [Required, MaxLength(30)] public string FormType { get; set; } = default!;
    public string? FormDataJson { get; set; }
    [Required, MaxLength(20)] public string ProcessStatus { get; set; } = "Pending";
    public DateTime? ProcessedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(MemberId))]
    public Member Member { get; set; } = default!;
}
