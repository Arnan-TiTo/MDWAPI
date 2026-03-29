using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

[Table("ContentDocuments", Schema = "mbw")]
public class ContentDocument
{
    [Key] public long DocumentId { get; set; }

    [Required, MaxLength(50)] public string DocumentType { get; set; } = default!; // TERMS, PRIVACY, POLICY
    [Required, MaxLength(30)] public string VersionNo { get; set; } = default!;
    [Required, MaxLength(10)] public string LanguageCode { get; set; } = "th";
    [Required, MaxLength(300)] public string Title { get; set; } = default!;
    [Required] public string ContentHtml { get; set; } = default!;
    public string? ContentText { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public DateTime PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
