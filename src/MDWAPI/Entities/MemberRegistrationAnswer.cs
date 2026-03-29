using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

[Table("MemberRegistrationAnswers", Schema = "mbw")]
public class MemberRegistrationAnswer
{
    [Key] public long AnswerId { get; set; }

    public long MemberId { get; set; }
    public int OptionId { get; set; }
    [MaxLength(500)] public string? OtherText { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(MemberId))]
    public Member Member { get; set; } = default!;

    [ForeignKey(nameof(OptionId))]
    public RegistrationProductOption Option { get; set; } = default!;
}
