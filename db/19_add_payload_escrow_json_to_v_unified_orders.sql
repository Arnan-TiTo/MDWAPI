-- ============================================================
-- 19_add_payload_escrow_json_to_v_unified_orders.sql
-- Expose PayloadEscrowJson in mdw.v_UnifiedOrders
-- ============================================================

CREATE OR ALTER VIEW mdw.v_UnifiedOrders
AS
SELECT
    o.UnifiedOrderId,
    o.ExternalOrderId,
    o.ExternalOrderNo,
    o.Channel,
    o.ShopId,
    o.SellerId,
    o.OrderStatus,
    o.CreatedTimeUtc,
    o.UpdatedTimeUtc,
    o.BuyerUserId,
    o.BuyerUsername,
    o.SubtotalAmount,
    o.DiscountSellerAmount,
    o.DiscountPlatformAmount,
    o.VoucherAmount,
    o.ShippingFeeAmount,
    o.TotalAmount,
    o.PaidAmount,

    (
        SELECT i.*
        FROM mdw.UnifiedOrderItems AS i
        WHERE i.UnifiedOrderId = o.UnifiedOrderId
        FOR JSON PATH
    ) AS ItemsJson,

    (
        SELECT p.*
        FROM mdw.UnifiedOrderPayments AS p
        WHERE p.UnifiedOrderId = o.UnifiedOrderId
        FOR JSON PATH
    ) AS PaymentsJson,

    (
        SELECT s.*
        FROM mdw.UnifiedOrderShipments AS s
        WHERE s.UnifiedOrderId = o.UnifiedOrderId
        FOR JSON PATH
    ) AS ShipmentsJson,

    (
        SELECT a.*
        FROM mdw.UnifiedOrderAddresses AS a
        WHERE a.UnifiedOrderAddressId = o.ShipToAddressId
        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
    ) AS ShipToJson,

    o.EscrowAmount,
    o.BuyerPaidShippingFee,
    o.ActualShippingFee,
    o.PlatformShippingRebate,
    o.CommissionFee,
    o.ServiceFee,
    o.PlatformFee,
    o.PaymentTransactionFee,
    o.AmsCommissionFee,
    o.SellerVoucherCode,
    o.PayloadEscrowJson
FROM mdw.UnifiedOrders AS o;
GO

EXEC sp_refreshview 'mdw.v_UnifiedOrders';
GO
EXEC sp_refreshview 'adw.vw_OrderExportCashSaleFormatTH';
GO
