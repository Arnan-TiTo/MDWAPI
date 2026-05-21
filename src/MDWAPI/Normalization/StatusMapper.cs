namespace MDWAPI.Normalization;

public static class StatusMapper
{
    public static string? Order(string channel, string? raw)
    {
        if (raw is null) return null;
        channel = channel.ToLowerInvariant();

        return channel switch
        {
            "shopee" => raw switch
            {
                "UNPAID" => "CREATED",
                "READY_TO_SHIP" or "PROCESSED" => "PAID",
                "SHIPPED" => "SHIPPED",
                "TO_RETURN" or "TO_REFUND" => "RETURNED",
                "CANCELLED" => "CANCELLED",
                "COMPLETED" => "COMPLETED",
                _ => raw
            },
            "tiktok" => raw switch
            {
                "WAIT_SELLER_SEND_GOODS" => "PAID",
                "IN_TRANSIT" => "SHIPPED",
                "DELIVERED" => "DELIVERED",
                "CANCELLED" => "CANCELLED",
                "COMPLETED" => "COMPLETED",
                _ => raw
            },
            "lazada" => raw switch
            {
                "pending" => "CREATED",
                "packed" or "ready_to_ship" => "PAID",
                "shipped" => "SHIPPED",
                "delivered" => "DELIVERED",
                "canceled" => "CANCELLED",
                "returned" or "failed_delivery" => "RETURNED",
                "completed" => "COMPLETED",
                _ => raw
            },
            _ => raw
        };
    }
}
