using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MDWAPI.Entities;

[Table("UnifiedOrderAddresses", Schema = "mdw")]
public class UnifiedOrderAddresses
{
    [Key] public long UnifiedOrderAddressId { get; set; }
    [Required, MaxLength(20)] public string Type { get; set; } = default!;
    [MaxLength(200)] public string? Name { get; set; }
    [MaxLength(60)] public string? Phone { get; set; }
    [MaxLength(200)] public string? Email { get; set; }
    [MaxLength(80)] public string? Country { get; set; }
    [MaxLength(120)] public string? State { get; set; }
    [MaxLength(120)] public string? City { get; set; }
    [MaxLength(120)] public string? District { get; set; }
    [MaxLength(20)] public string? PostalCode { get; set; }
    [MaxLength(300)] public string? Address1 { get; set; }
    [MaxLength(300)] public string? Address2 { get; set; }
    [MaxLength(1000)] public string? FullAddress { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
}
