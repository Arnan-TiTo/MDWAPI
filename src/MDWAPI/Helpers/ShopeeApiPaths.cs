public static class ShopeeApiPaths
{
    // ===== Auth =====
    public const string AuthGetAccessToken = "/api/v2/auth/token/get";
    public const string AuthRefreshAccessToken = "/api/v2/auth/access_token/get";

    // ===== Shop / Utility (ไม่ต้องมีออเดอร์ก็ทดสอบได้) =====
    public const string ShopGetProfile = "/api/v2/shop/get_profile";
    public const string LogisticsGetChannelList = "/api/v2/logistics/get_channel_list";

    // ===== Order =====
    public const string OrderGetList = "/api/v2/order/get_order_list";
    public const string OrderGetDetail = "/api/v2/order/get_order_detail";

    // ===== Order Actions =====
    public const string OrderCancelOrder = "/api/v2/order/cancel_order";
    public const string OrderHandleBuyerCancellation = "/api/v2/order/handle_buyer_cancellation";
    public const string OrderSetNote = "/api/v2/order/set_note";

    // ===== Logistics =====
    public const string LogiGetShippingParam = "/api/v2/logistics/get_shipping_parameter";
    public const string LogiGetTrackingNumber = "/api/v2/logistics/get_tracking_number";
    public const string LogiShipOrder = "/api/v2/logistics/ship_order";
    public const string LogiGetAddressList = "/api/v2/logistics/get_address_list";
    public const string LogiGetShippingDocParam = "/api/v2/logistics/get_shipping_document_parameter";
    public const string LogiCreateShippingDoc = "/api/v2/logistics/create_shipping_document";
    public const string LogiGetDocResult = "/api/v2/logistics/get_shipping_document_result";
    public const string LogiDownloadDoc = "/api/v2/logistics/download_shipping_document";
}
