-- ============================================================
-- 13_fix_escrow_columns_for_fe_orders.sql
-- Production-safe fix for MDW View Orders after escrow fields were added
-- to the API model.
--
-- Fixes errors like:
--   Invalid column name 'EscrowAmount'
--   Invalid column name 'BuyerPaidShippingFee'
--   ...
-- ============================================================

IF OBJECT_ID('mdw.ShopeeOrders', 'U') IS NOT NULL AND COL_LENGTH('mdw.ShopeeOrders', 'EscrowAmount') IS NULL
    ALTER TABLE mdw.ShopeeOrders ADD EscrowAmount DECIMAL(18,4) NULL;
GO

IF OBJECT_ID('mdw.ShopeeOrders', 'U') IS NOT NULL AND COL_LENGTH('mdw.ShopeeOrders', 'BuyerPaidShippingFee') IS NULL
    ALTER TABLE mdw.ShopeeOrders ADD BuyerPaidShippingFee DECIMAL(18,4) NULL;
GO

IF OBJECT_ID('mdw.ShopeeOrders', 'U') IS NOT NULL AND COL_LENGTH('mdw.ShopeeOrders', 'ActualShippingFee') IS NULL
    ALTER TABLE mdw.ShopeeOrders ADD ActualShippingFee DECIMAL(18,4) NULL;
GO

IF OBJECT_ID('mdw.ShopeeOrders', 'U') IS NOT NULL AND COL_LENGTH('mdw.ShopeeOrders', 'PlatformShippingRebate') IS NULL
    ALTER TABLE mdw.ShopeeOrders ADD PlatformShippingRebate DECIMAL(18,4) NULL;
GO

IF OBJECT_ID('mdw.ShopeeOrders', 'U') IS NOT NULL AND COL_LENGTH('mdw.ShopeeOrders', 'CommissionFee') IS NULL
    ALTER TABLE mdw.ShopeeOrders ADD CommissionFee DECIMAL(18,4) NULL;
GO

IF OBJECT_ID('mdw.ShopeeOrders', 'U') IS NOT NULL AND COL_LENGTH('mdw.ShopeeOrders', 'ServiceFee') IS NULL
    ALTER TABLE mdw.ShopeeOrders ADD ServiceFee DECIMAL(18,4) NULL;
GO

IF OBJECT_ID('mdw.ShopeeOrders', 'U') IS NOT NULL AND COL_LENGTH('mdw.ShopeeOrders', 'PlatformFee') IS NULL
    ALTER TABLE mdw.ShopeeOrders ADD PlatformFee DECIMAL(18,4) NULL;
GO

IF OBJECT_ID('mdw.ShopeeOrders', 'U') IS NOT NULL AND COL_LENGTH('mdw.ShopeeOrders', 'PaymentTransactionFee') IS NULL
    ALTER TABLE mdw.ShopeeOrders ADD PaymentTransactionFee DECIMAL(18,4) NULL;
GO

IF OBJECT_ID('mdw.ShopeeOrders', 'U') IS NOT NULL AND COL_LENGTH('mdw.ShopeeOrders', 'AmsCommissionFee') IS NULL
    ALTER TABLE mdw.ShopeeOrders ADD AmsCommissionFee DECIMAL(18,4) NULL;
GO

IF OBJECT_ID('mdw.ShopeeOrders', 'U') IS NOT NULL AND COL_LENGTH('mdw.ShopeeOrders', 'SellerVoucherCode') IS NULL
    ALTER TABLE mdw.ShopeeOrders ADD SellerVoucherCode NVARCHAR(500) NULL;
GO

IF OBJECT_ID('mdw.ShopeeOrders', 'U') IS NOT NULL AND COL_LENGTH('mdw.ShopeeOrders', 'EscrowUpdatedAt') IS NULL
    ALTER TABLE mdw.ShopeeOrders ADD EscrowUpdatedAt DATETIME2 NULL;
GO

IF COL_LENGTH('mdw.UnifiedOrders', 'EscrowAmount') IS NULL
    ALTER TABLE mdw.UnifiedOrders ADD EscrowAmount DECIMAL(18,4) NULL;
GO

IF COL_LENGTH('mdw.UnifiedOrders', 'BuyerPaidShippingFee') IS NULL
    ALTER TABLE mdw.UnifiedOrders ADD BuyerPaidShippingFee DECIMAL(18,4) NULL;
GO

IF COL_LENGTH('mdw.UnifiedOrders', 'ActualShippingFee') IS NULL
    ALTER TABLE mdw.UnifiedOrders ADD ActualShippingFee DECIMAL(18,4) NULL;
GO

IF COL_LENGTH('mdw.UnifiedOrders', 'PlatformShippingRebate') IS NULL
    ALTER TABLE mdw.UnifiedOrders ADD PlatformShippingRebate DECIMAL(18,4) NULL;
GO

IF COL_LENGTH('mdw.UnifiedOrders', 'CommissionFee') IS NULL
    ALTER TABLE mdw.UnifiedOrders ADD CommissionFee DECIMAL(18,4) NULL;
GO

IF COL_LENGTH('mdw.UnifiedOrders', 'ServiceFee') IS NULL
    ALTER TABLE mdw.UnifiedOrders ADD ServiceFee DECIMAL(18,4) NULL;
GO

IF COL_LENGTH('mdw.UnifiedOrders', 'PlatformFee') IS NULL
    ALTER TABLE mdw.UnifiedOrders ADD PlatformFee DECIMAL(18,4) NULL;
GO

IF COL_LENGTH('mdw.UnifiedOrders', 'PaymentTransactionFee') IS NULL
    ALTER TABLE mdw.UnifiedOrders ADD PaymentTransactionFee DECIMAL(18,4) NULL;
GO

IF COL_LENGTH('mdw.UnifiedOrders', 'AmsCommissionFee') IS NULL
    ALTER TABLE mdw.UnifiedOrders ADD AmsCommissionFee DECIMAL(18,4) NULL;
GO

IF COL_LENGTH('mdw.UnifiedOrders', 'SellerVoucherCode') IS NULL
    ALTER TABLE mdw.UnifiedOrders ADD SellerVoucherCode NVARCHAR(500) NULL;
GO

CREATE OR ALTER VIEW mdw.v_UnifiedOrders
AS
SELECT
    o.UnifiedOrderId,
    o.ExternalOrderNo,
    o.Channel,
    o.ShopId,
    o.SellerId,
    o.OrderStatus,
    o.CreatedTimeUtc,
    o.UpdatedTimeUtc,
    o.BuyerUserId,
    o.BuyerUsername,

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
    o.SellerVoucherCode
FROM mdw.UnifiedOrders AS o;
GO

EXEC sp_refreshview 'mdw.v_UnifiedOrders';
GO

EXEC sp_refreshview 'adw.vw_OrderMerged';
GO

EXEC sp_refreshview 'adw.vw_OrderMergedItems';
GO

EXEC sp_refreshview 'adw.vw_OrderExport';
GO

EXEC sp_refreshview 'adw.vw_OrderExportFormatTH';
GO
