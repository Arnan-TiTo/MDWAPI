namespace MDWAPI.Models;

public class Shops
{
    public int Id { get; set; }
    public long ShopId { get; set; }
    public int PartnerId { get; set; }
    public string? Name { get; set; }
    public string? Country { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Password { get; set; }
    public string? Platform { get; set; }

    // Navigation
    public Partners Partners { get; set; } = null!;
}