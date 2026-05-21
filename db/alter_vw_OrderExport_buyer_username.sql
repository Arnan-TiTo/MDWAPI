-- =============================================
-- Migration: Use BuyerUsername for buyer_userid
-- Date: 2026-02-26
-- Description:
--   1. Refresh adw.vw_OrderMerged to pick up new BuyerUsername column from mdw.UnifiedOrders
--   2. ALTER adw.vw_OrderExport to use ISNULL(BuyerUsername, BuyerUserId) as buyer_userid
-- =============================================

-- Step 1: Refresh vw_OrderMerged (it uses SELECT u.* and needs to see the new column)
EXEC sp_refreshview 'adw.vw_OrderMerged';
GO

-- Step 2: ALTER vw_OrderExport
ALTER VIEW adw.vw_OrderExport
AS
WITH base AS (
    SELECT
        -- keys & meta
        o.UnifiedOrderId,
        o.channel,
        concat(concat(o.Channel,'-'),COALESCE(o.sendername,'VIBE AND CHIC OFFICIAL STORE'))               COLLATE Thai_CI_AS AS SaleChannel,
        o.ShopId,
        CONVERT(nvarchar(50), o.ExternalOrderNo) COLLATE Thai_CI_AS AS ExternalOrderNo,
        CONVERT(nvarchar(50), o.ExternalOrderId) COLLATE Thai_CI_AS AS ExternalOrderId,
        o.BuyerName             COLLATE Thai_CI_AS AS BuyerName,
        CONVERT(nvarchar(100), o.BuyerUserId) COLLATE Thai_CI_AS AS BuyerUserId,
        o.BuyerUsername         COLLATE Thai_CI_AS AS BuyerUsername,   -- << NEW
        CONVERT(nvarchar(50),  o.BuyerPhone)  COLLATE Thai_CI_AS AS BuyerPhone,
        o.BuyerEmail            COLLATE Thai_CI_AS AS BuyerEmail,
        o.CreatedTimeUtc,
        o.UpdatedTimeUtc,
        o.PaidTimeUtc,
        o.paymentMethod,
        o.OrderStatus           COLLATE Thai_CI_AS AS OrderStatus,
        o.FulfillmentStatus     COLLATE Thai_CI_AS AS FulfillmentStatus,
        o.PaymentStatus         COLLATE Thai_CI_AS AS PaymentStatus,
        o.ShipmentProvider      COLLATE Thai_CI_AS AS ShipmentProvider,
        o.ShipmentServiceCode   COLLATE Thai_CI_AS AS ShipmentServiceCode,
        o.ShippingFeeAmount,

        -- ship-to
        o.ShipTo_ReceiverName   COLLATE Thai_CI_AS AS ShipTo_Name,
        CONVERT(nvarchar(50), o.ShipTo_Phone)      COLLATE Thai_CI_AS AS ShipTo_Phone,
        o.ShipTo_Email          COLLATE Thai_CI_AS AS ShipTo_Email,
        o.ShipTo_Address1       COLLATE Thai_CI_AS AS ShipTo_Address1,
        o.ShipTo_Address2       COLLATE Thai_CI_AS AS ShipTo_Address2,
        o.ShipTo_District       COLLATE Thai_CI_AS AS ShipTo_District,
        o.ShipTo_City           COLLATE Thai_CI_AS AS ShipTo_City,
        o.ShipTo_State          COLLATE Thai_CI_AS AS ShipTo_State,
        o.ShipTo_PostalCode     COLLATE Thai_CI_AS AS ShipTo_PostalCode,
        o.Full_Address          COLLATE Thai_CI_AS AS ShipTo_FullAddress,

        -- idw tracking
        o.trackNo               COLLATE Thai_CI_AS AS trackNo,

        -- items
        i.UnifiedOrderItemId,
        i.ProductName           COLLATE Thai_CI_AS AS ProductName,
        i.VariationName         COLLATE Thai_CI_AS AS VariationName,
        i.SellerSku             COLLATE Thai_CI_AS AS SellerSku,
        i.PlatformSku           COLLATE Thai_CI_AS AS PlatformSku,
        i.QtyOrdered,
        i.UnitPrice,
        i.OriginalPrice,
        i.LineTotal,
        i.itemName              COLLATE Thai_CI_AS AS itemName,
        i.itemVariantUnit       COLLATE Thai_CI_AS AS itemVariantUnit,
        i.itemSkd               COLLATE Thai_CI_AS AS itemSkd,
        i.qtyTotal
    FROM adw.vw_OrderMerged       AS o
    JOIN adw.vw_OrderMergedItems  AS i
      ON i.UnifiedOrderId = o.UnifiedOrderId
)
, r1 AS (
    SELECT
        -- keys
        b.UnifiedOrderId AS order_id,
        ISNULL(
            COALESCE(b.ExternalOrderNo, b.ExternalOrderId),
            N'' COLLATE Thai_CI_AS
        ) COLLATE Thai_CI_AS                                        AS order_no,
        ISNULL(b.Channel, N'' COLLATE Thai_CI_AS)                   AS channel,        
        ISNULL(b.SaleChannel, N'' COLLATE Thai_CI_AS)               AS sale_channel,
        ISNULL(CAST(b.ShopId AS bigint), 0)                         AS shop_id,

        -- times (datetime2)
        b.CreatedTimeUtc                                            AS created_at_utc,
        b.UpdatedTimeUtc                                            AS updated_at_utc,
        CAST(NULL AS datetime2)                                     AS confirmed_at_utc,
        b.PaidTimeUtc                                               AS paid_at_utc,

        -- buyer / receiver  <<< CHANGED: use BuyerUsername with fallback to BuyerUserId
        ISNULL(b.BuyerUsername, b.BuyerUserId) AS buyer_UserId,
        ISNULL(
            COALESCE(b.BuyerName, b.BuyerUserId),
            N'' COLLATE Thai_CI_AS
        ) COLLATE Thai_CI_AS                                       AS buyer_name,
        ISNULL(b.ShipTo_Name,   N'' COLLATE Thai_CI_AS)             AS shipto_name,
        ISNULL(b.ShipTo_Phone,  N'' COLLATE Thai_CI_AS)             AS shipto_phone,
        ISNULL(b.ShipTo_Email,  N'' COLLATE Thai_CI_AS)             AS shipto_email,

        -- address
        ISNULL(
          LTRIM(RTRIM(
            COALESCE(b.ShipTo_Address1, N'' COLLATE Thai_CI_AS)
            + CASE WHEN ISNULL(b.ShipTo_Address2, N'' COLLATE Thai_CI_AS) <> N'' COLLATE Thai_CI_AS
                   THEN N' ' COLLATE Thai_CI_AS + b.ShipTo_Address2
                   ELSE N'' COLLATE Thai_CI_AS END
          )),
          N'' COLLATE Thai_CI_AS
        ) COLLATE Thai_CI_AS                                         AS shipto_address,
        ISNULL(b.ShipTo_District,    N'' COLLATE Thai_CI_AS)         AS shipto_district,
        ISNULL(b.ShipTo_City,        N'' COLLATE Thai_CI_AS)         AS shipto_city,
        ISNULL(b.ShipTo_State,       N'' COLLATE Thai_CI_AS)         AS shipto_state,
        ISNULL(b.ShipTo_PostalCode,  N'' COLLATE Thai_CI_AS)         AS shipto_postcode,
        ISNULL(b.ShipTo_FullAddress, N'' COLLATE Thai_CI_AS)         AS shipto_fulladdress,
        

        -- shipment
        ISNULL(b.ShipmentProvider,     N'' COLLATE Thai_CI_AS)      AS shipment_provider,
        ISNULL(b.ShipmentServiceCode,  N'' COLLATE Thai_CI_AS)      AS shipment_service,
        ISNULL(CAST(NULL AS bit), 0)                                AS auto_gen_tracking,
        ISNULL(b.ShippingFeeAmount, 0)        AS shipping_fee,

        -- payment & status       
        ISNULL(b.paymentMethod,     N'' COLLATE Thai_CI_AS)         AS payment_method,
        ISNULL(b.PaymentStatus,     N'' COLLATE Thai_CI_AS)         AS payment_status,
        ISNULL(b.FulfillmentStatus, N'' COLLATE Thai_CI_AS)         AS fulfillment_status,
        ISNULL(b.OrderStatus,       N'' COLLATE Thai_CI_AS)         AS order_status,
        CAST(NULL AS datetime2)                                     AS canceled_at_utc,
        ISNULL(CAST(NULL AS nvarchar(200))  COLLATE Thai_CI_AS, N'' COLLATE Thai_CI_AS)   AS cancel_reason,
        ISNULL(CAST(NULL AS nvarchar(1000)) COLLATE Thai_CI_AS, N'' COLLATE Thai_CI_AS)   AS seller_note,
        ISNULL(CAST(NULL AS nvarchar(1000)) COLLATE Thai_CI_AS, N'' COLLATE Thai_CI_AS)   AS buyer_note,

        -- tracking
        ISNULL(b.trackNo, N'' COLLATE Thai_CI_AS)                   AS tracking_no,
        ISNULL(CAST(NULL AS nvarchar(400)) COLLATE Thai_CI_AS, N'' COLLATE Thai_CI_AS)    AS tracking_url,

        -- product & pricing
        ISNULL(CAST(NULL AS nvarchar(100)) COLLATE Thai_CI_AS, N'' COLLATE Thai_CI_AS)    AS product_code,
        ISNULL(b.SellerSku, N'' COLLATE Thai_CI_AS)                 AS sku,
        ISNULL(CAST(NULL AS nvarchar(100)) COLLATE Thai_CI_AS, N'' COLLATE Thai_CI_AS)    AS barcode,
        ISNULL(CAST(NULL AS nvarchar(200)) COLLATE Thai_CI_AS, N'' COLLATE Thai_CI_AS)    AS brand,
        ISNULL(CAST(NULL AS nvarchar(200)) COLLATE Thai_CI_AS, N'' COLLATE Thai_CI_AS)    AS category_main,
        ISNULL(CAST(NULL AS nvarchar(200)) COLLATE Thai_CI_AS, N'' COLLATE Thai_CI_AS)    AS category_sub,

        ISNULL(COALESCE(b.itemName, b.ProductName), N'' COLLATE Thai_CI_AS)
            COLLATE Thai_CI_AS                                      AS product_name,
        ISNULL(b.itemVariantUnit, N'' COLLATE Thai_CI_AS)           AS option1,
        ISNULL(b.VariationName,  N'' COLLATE Thai_CI_AS)            AS option2,

        CAST(1 AS int)                                              AS qty_sold,
        ISNULL(b.LineTotal, 0)                                      AS sale_price,
        ISNULL(COALESCE(b.OriginalPrice, b.UnitPrice), 0)           AS list_price,

        -- discounts & misc
        ISNULL(CAST(NULL AS decimal(18,2)), 0)                      AS bill_discount,
        ISNULL(CAST(NULL AS decimal(18,2)), 0)                    AS linepay_coupon,
        ISNULL(CAST(NULL AS decimal(18,2)), 0)                      AS line_points,
        ISNULL(CAST(NULL AS nvarchar(200))  COLLATE Thai_CI_AS, N'' COLLATE Thai_CI_AS)   AS coupon_product_code,
        ISNULL(CAST(NULL AS decimal(18,2)), 0)                      AS coupon_product_amount,
        ISNULL(CAST(NULL AS nvarchar(200))  COLLATE Thai_CI_AS, N'' COLLATE Thai_CI_AS)   AS coupon_line_code,
        ISNULL(CAST(NULL AS decimal(18,2)), 0)                      AS coupon_line_amount,
        ISNULL(CAST(NULL AS nvarchar(200))  COLLATE Thai_CI_AS, N'' COLLATE Thai_CI_AS)   AS coupon_shipping_code,
        ISNULL(CAST(NULL AS decimal(18,2)), 0)                      AS coupon_shipping_amount,

        ISNULL(CAST(NULL AS bit), 0)                                AS is_gift,
        ISNULL(CAST(NULL AS bit), 0)                                AS is_preorder,
        ISNULL(CAST(NULL AS nvarchar(50)) COLLATE Thai_CI_AS, N'' COLLATE Thai_CI_AS)     AS transfer_status,
        CAST(NULL AS datetime2)                                     AS expected_payout_date,
        CAST(NULL AS datetime2)                                     AS payout_date,
        ISNULL(CAST(NULL AS decimal(18,2)), 0)                      AS amount_processing,
        ISNULL(CAST(NULL AS decimal(18,2)), 0)                      AS amount_pending,
        ISNULL(CAST(NULL AS decimal(18,2)), 0)                      AS amount_received,
        ISNULL(CAST(NULL AS decimal(18,2)), 0)                      AS fee_payment,
        ISNULL(CAST(NULL AS decimal(18,2)), 0)                      AS fee_service,
        ISNULL(CAST(NULL AS decimal(18,2)), 0)                      AS vat_amount,
        ISNULL(CAST(NULL AS decimal(18,2)), 0)                      AS wht_coupon_only,
        ISNULL(CAST(NULL AS decimal(18,2)), 0)                      AS share_and_earn,
        ISNULL(CAST(NULL AS decimal(18,2)), 0)                      AS points_paid
    FROM base b
)
, r2 AS (
    SELECT
        r1.order_id,
        r1.order_no,
        r1.channel,
        r1.sale_channel,
        r1.shop_id,

        r1.created_at_utc,
        r1.updated_at_utc,
        r1.confirmed_at_utc,
        r1.paid_at_utc,

        r1.buyer_userid,
        r1.buyer_name,
        r1.shipto_name,
        r1.shipto_phone,
        r1.shipto_email,

        r1.shipto_address,
        r1.shipto_district,
        r1.shipto_city,
        r1.shipto_state,
        r1.shipto_postcode,
        r1.ShipTo_FullAddress,
        
        r1.shipment_provider,
        r1.shipment_service,
        r1.auto_gen_tracking,
        r1.shipping_fee,

        r1.payment_method,
        r1.payment_status,
        r1.fulfillment_status,
        r1.order_status,
        r1.canceled_at_utc,
        r1.cancel_reason,
        r1.seller_note,
        r1.buyer_note,

        r1.tracking_no,
        r1.tracking_url,

        r1.product_code,
        r1.sku,
        r1.barcode,
        r1.brand,
        r1.category_main,
        r1.category_sub,

        r1.product_name,
        r1.option1,
        r1.option2,

        ISNULL(CAST(b.qtyTotal - 1 AS int), 0)                      AS qty_sold,
        CAST(0 AS decimal(18,2))                                    AS sale_price,
        CAST(0 AS decimal(18,2))                                    AS list_price,

        r1.bill_discount,
        r1.linepay_coupon,
        r1.line_points,
        r1.coupon_product_code,
        r1.coupon_product_amount,
        r1.coupon_line_code,
        r1.coupon_line_amount,
        r1.coupon_shipping_code,
        r1.coupon_shipping_amount,

        r1.is_gift,
        r1.is_preorder,
        r1.transfer_status,
        r1.expected_payout_date,
        r1.payout_date,
        r1.amount_processing,
        r1.amount_pending,
        r1.amount_received,
        r1.fee_payment,
        r1.fee_service,
        r1.vat_amount,
        r1.wht_coupon_only,
        r1.share_and_earn,
        r1.points_paid
    FROM r1
    JOIN base b ON b.UnifiedOrderId = r1.order_id
    WHERE b.qtyTotal IS NOT NULL AND b.qtyTotal > 1
)
SELECT  * FROM r1
UNION ALL
SELECT  * FROM r2;
GO
