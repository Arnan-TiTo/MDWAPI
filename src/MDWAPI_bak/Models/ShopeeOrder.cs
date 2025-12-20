namespace MDWAPI.Models;

public class ShopeeOrder
{
    public int Id { get; set; }
    public long ShopId { get; set; }
    public string OrderSn { get; set; } = default!;
    public string? OrderStatus { get; set; }
    public string? Currency { get; set; }
    public decimal? TotalAmount { get; set; }
    public string? PaymentMethod { get; set; }
    public bool? Cod { get; set; }
    public long? BuyerUserId { get; set; }
    public string? BuyerUsername { get; set; }
    public DateTime? CreateTime { get; set; }
    public DateTime? UpdateTime { get; set; }
    public DateTime? PayTime { get; set; }
    public DateTime? PickupDoneTime { get; set; }
    public DateTime? ShipByDate { get; set; }
    public string? Recv_Name { get; set; }
    public string? Recv_Phone { get; set; }
    public string? Recv_Town { get; set; }
    public string? Recv_District { get; set; }
    public string? Recv_City { get; set; }
    public string? Recv_State { get; set; }
    public string? Recv_Region { get; set; }
    public string? Recv_Zipcode { get; set; }
    public string? Recv_FullAddress { get; set; }
    public string? RawJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ShopeeOrderItem
{
    public int Id { get; set; }
    public string OrderSn { get; set; } = default!;
    public long OrderItemId { get; set; }
    public long? ItemId { get; set; }
    public string? ItemName { get; set; }
    public string? ItemSku { get; set; }
    public long? ModelId { get; set; }
    public string? ModelName { get; set; }
    public string? ModelSku { get; set; }
    public int? ModelQty { get; set; }
    public decimal? ModelOriginalPrice { get; set; }
    public decimal? ModelDiscountedPrice { get; set; }
    public decimal? WeightKg { get; set; }
    public string? ImageUrl { get; set; }
    public string? PackageNumber { get; set; }
    public string? ShippingCarrier { get; set; }
    public string? LogisticsStatus { get; set; }
    public long? LogisticsChannelId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
