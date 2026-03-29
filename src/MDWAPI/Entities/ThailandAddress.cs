using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

[Table("ThailandAddress", Schema = "mbw")]
public class ThailandAddress
{
    [Key]
    public int Id { get; set; }
    
    [MaxLength(200)]
    public string tambonID { get; set; } = string.Empty;
    
    [MaxLength(200)]
    public string subDistrict { get; set; } = string.Empty;
    
    [MaxLength(200)]
    public string district { get; set; } = string.Empty;
    
    [MaxLength(200)]
    public string province { get; set; } = string.Empty;
    
    [MaxLength(20)]
    public string postcode { get; set; } = string.Empty;
}
