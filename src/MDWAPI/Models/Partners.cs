namespace MDWAPI.Models;

public class Partners
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public int? CompanysId { get; set; }
    public int PartnerId { get; set; }
    public string PartnerKey { get; set; } = null!;
    public string? Environment { get; set; }
    public DateTime CreatedAt { get; set; }

    // Link to Shops
    public ICollection<Shops> Shops { get; set; } = new List<Shops>();
}
