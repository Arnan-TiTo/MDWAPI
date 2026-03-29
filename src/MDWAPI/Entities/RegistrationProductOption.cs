using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

[Table("RegistrationProductOptions", Schema = "mbw")]
public class RegistrationProductOption
{
    [Key] public int OptionId { get; set; }

    [Required, MaxLength(50)] public string OptionCode { get; set; } = default!;
    [Required, MaxLength(200)] public string OptionName { get; set; } = default!;
    public bool IsAllowOtherText { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
