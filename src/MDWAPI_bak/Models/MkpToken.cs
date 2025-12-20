namespace MDWAPI.Models
{
    public class MkpToken
    {
        public int Id { get; set; }
        public long ShopId { get; set; }
        public string AccessToken { get; set; } = string.Empty;
        public string? RefreshToken { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public string? Country { get; set; }
        public string ShopFrom { get; set; } = "Shopee";
    }
}
