namespace MDWAPI.DTOs;

public record UnifiedOrderDto
{
    public string Channel { get; init; } = default!;
    public long? ShopId { get; init; }
    public string? SellerId { get; init; }
    public string ExternalOrderId { get; init; } = default!;
    public string? ExternalOrderNo { get; init; } = null!;

    public string? OrderStatus { get; init; }
    public string? FulfillmentStatus { get; init; }
    public string? PaymentStatus { get; init; }
    public string? Currency { get; init; }

    public decimal? SubtotalAmount { get; init; }
    public decimal? DiscountSellerAmount { get; init; }
    public decimal? DiscountPlatformAmount { get; init; }
    public decimal? VoucherAmount { get; init; }
    public decimal? ShippingFeeAmount { get; init; }
    public decimal? TaxAmount { get; init; }
    public decimal? OtherFeeAmount { get; init; }
    public decimal? TotalAmount { get; init; }
    public decimal? PaidAmount { get; init; }
    public decimal? RefundAmount { get; init; }

    // Escrow / income breakdown
    public decimal? EscrowAmount { get; init; }
    public decimal? BuyerPaidShippingFee { get; init; }
    public decimal? ActualShippingFee { get; init; }
    public decimal? PlatformShippingRebate { get; init; }
    public decimal? CommissionFee { get; init; }
    public decimal? ServiceFee { get; init; }
    public decimal? PlatformFee { get; init; }
    public decimal? PaymentTransactionFee { get; init; }
    public decimal? AmsCommissionFee { get; init; }
    public string? SellerVoucherCode { get; init; }

    public string? PaymentMethod { get; init; }
    public string? ShipmentProvider { get; init; }
    public string? ShipmentServiceCode { get; init; }
    public string? TrackingNo { get; init; }
    public string? WarehouseCode { get; init; }

    public string? BuyerUserId { get; init; }
    public string? BuyerUsername { get; init; }
    public string? BuyerName { get; init; }
    public string? BuyerPhone { get; init; }
    public string? BuyerEmail { get; init; }

    public UnifiedAddress? ShipTo { get; init; }
    public UnifiedAddress? BillTo { get; init; }

    public DateTimeOffset? CreatedTimeUtc { get; init; }
    public DateTimeOffset? UpdatedTimeUtc { get; init; }
    public DateTimeOffset? PaidTimeUtc { get; init; }
    public DateTimeOffset? CancelTimeUtc { get; init; }
    public DateTimeOffset? ShippedTimeUtc { get; init; }
    public DateTimeOffset? DeliveredTimeUtc { get; init; }
    public DateTimeOffset? CompletedTimeUtc { get; init; }

    public string? NoteBuyer { get; init; }
    public string? NoteSeller { get; init; }

    public List<UnifiedOrderItem> Items { get; init; } = new();
    public List<UnifiedPayment> Payments { get; init; } = new();
    public List<UnifiedShipment> Shipments { get; init; } = new();

    public long? SourceRawId { get; init; }
    public byte[]? SourcePayloadHash { get; init; }
    public string? IngestBatchNo { get; init; }
}

public record UnifiedOrderItem
{
    public string? ExternalItemId { get; init; }
    public string ProductName { get; init; } = default!;
    public string? VariationName { get; init; }
    public string? SellerSku { get; init; }
    public string? PlatformSku { get; init; }
    public int QtyOrdered { get; init; }
    public int? QtyCanceled { get; init; }
    public int? QtyShipped { get; init; }
    public decimal? UnitPrice { get; init; }
    public decimal? OriginalPrice { get; init; }
    public decimal? DiscountSeller { get; init; }
    public decimal? DiscountPlatform { get; init; }
    public decimal? TaxAmount { get; init; }
    public decimal? ShippingAlloc { get; init; }
    public decimal? LineTotal { get; init; }
    public Dictionary<string, string?>? Attributes { get; init; }
}

public record UnifiedAddress
{
    public string? Name { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Country { get; init; }
    public string? State { get; init; }
    public string? City { get; init; }
    public string? District { get; init; }
    public string? PostalCode { get; init; }
    public string? Address1 { get; init; }
    public string? Address2 { get; init; }
    public string? FullAddress { get; init; }
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }
}

public record UnifiedPayment
{
    public string? Method { get; init; }
    public string? ChannelTxnId { get; init; }
    public decimal? PaidAmount { get; init; }
    public string? Currency { get; init; }
    public DateTimeOffset? PaidTimeUtc { get; init; }
    public decimal? FeeAmount { get; init; }
    public Dictionary<string, object?>? FeeDetails { get; init; }
    public bool? IsCOD { get; init; }
}

public record UnifiedShipment
{
    public string? Provider { get; init; }
    public string? ServiceCode { get; init; }
    public string? TrackingNo { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? PickupTimeUtc { get; init; }
    public DateTimeOffset? ShippedTimeUtc { get; init; }
    public DateTimeOffset? DeliveredTimeUtc { get; init; }
    public string? FirstMileCarrier { get; init; }
    public string? LastMileCarrier { get; init; }
    public object? Raw { get; init; }
}
