namespace MDWAPI.Helpers
{
    public static class TiktokApiPaths
    {
        // Logistics 
        public const string LogisticsGetShippingInfo = "/api/logistics/shipping_info/query";
        public const string LogisticsConfirmShip = "/api/logistics/shipping/confirm";
        public const string LogisticsDocumentCreate = "/api/logistics/shipping_document/create";
        public const string LogisticsDocumentQuery = "/api/logistics/shipping_document/query";
        public const string LogisticsDocumentDownload = "/api/logistics/shipping_document/download";

        // Orders (versioned 202309)
        public const string OrdersSearch202309 = "/order/202309/orders/search";
        public const string OrderDetail202309 = "/order/202309/orders/detail";
    }
}
