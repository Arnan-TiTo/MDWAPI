-- ============================================================
-- 10_add_escrow_fields.sql
-- เพิ่ม escrow / income breakdown fields
-- Source: Shopee /api/v2/payment/get_escrow_detail → order_income
-- ============================================================

-- ─── 1. mdw.ShopeeOrders (legacy, optional) ─────────────────
-- ShopeeOrders is no longer part of the active UnifiedOrders flow.
-- Keep this section guarded so older DBs can still be patched without
-- breaking new DBs that never created mdw.ShopeeOrders.
IF OBJECT_ID('mdw.ShopeeOrders', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('mdw.ShopeeOrders', 'EscrowAmount') IS NULL
        ALTER TABLE mdw.ShopeeOrders ADD EscrowAmount DECIMAL(18,4) NULL;
    IF COL_LENGTH('mdw.ShopeeOrders', 'BuyerPaidShippingFee') IS NULL
        ALTER TABLE mdw.ShopeeOrders ADD BuyerPaidShippingFee DECIMAL(18,4) NULL;
    IF COL_LENGTH('mdw.ShopeeOrders', 'ActualShippingFee') IS NULL
        ALTER TABLE mdw.ShopeeOrders ADD ActualShippingFee DECIMAL(18,4) NULL;
    IF COL_LENGTH('mdw.ShopeeOrders', 'PlatformShippingRebate') IS NULL
        ALTER TABLE mdw.ShopeeOrders ADD PlatformShippingRebate DECIMAL(18,4) NULL;
    IF COL_LENGTH('mdw.ShopeeOrders', 'CommissionFee') IS NULL
        ALTER TABLE mdw.ShopeeOrders ADD CommissionFee DECIMAL(18,4) NULL;
    IF COL_LENGTH('mdw.ShopeeOrders', 'ServiceFee') IS NULL
        ALTER TABLE mdw.ShopeeOrders ADD ServiceFee DECIMAL(18,4) NULL;
    IF COL_LENGTH('mdw.ShopeeOrders', 'PlatformFee') IS NULL
        ALTER TABLE mdw.ShopeeOrders ADD PlatformFee DECIMAL(18,4) NULL;
    IF COL_LENGTH('mdw.ShopeeOrders', 'PaymentTransactionFee') IS NULL
        ALTER TABLE mdw.ShopeeOrders ADD PaymentTransactionFee DECIMAL(18,4) NULL;
    IF COL_LENGTH('mdw.ShopeeOrders', 'AmsCommissionFee') IS NULL
        ALTER TABLE mdw.ShopeeOrders ADD AmsCommissionFee DECIMAL(18,4) NULL;
    IF COL_LENGTH('mdw.ShopeeOrders', 'SellerVoucherCode') IS NULL
        ALTER TABLE mdw.ShopeeOrders ADD SellerVoucherCode NVARCHAR(500) NULL;
    IF COL_LENGTH('mdw.ShopeeOrders', 'EscrowUpdatedAt') IS NULL
        ALTER TABLE mdw.ShopeeOrders ADD EscrowUpdatedAt DATETIME2 NULL;
END
GO

-- ─── 2. mdw.UnifiedOrders ───────────────────────────────────
ALTER TABLE mdw.UnifiedOrders ADD
    EscrowAmount           DECIMAL(18,4) NULL,
    BuyerPaidShippingFee   DECIMAL(18,4) NULL,
    ActualShippingFee      DECIMAL(18,4) NULL,
    PlatformShippingRebate DECIMAL(18,4) NULL,
    CommissionFee          DECIMAL(18,4) NULL,
    ServiceFee             DECIMAL(18,4) NULL,
    PlatformFee            DECIMAL(18,4) NULL,
    PaymentTransactionFee  DECIMAL(18,4) NULL,
    AmsCommissionFee       DECIMAL(18,4) NULL,
    SellerVoucherCode      NVARCHAR(500) NULL;

-- ─── 3. อัปเดต view mdw.v_UnifiedOrders ────────────────────
-- (DROP + CREATE เพราะ SQL Server ไม่รองรับ ALTER VIEW column append แบบง่าย)
-- ดูโครงสร้าง view เดิมก่อนรัน — เพิ่ม escrow fields ต่อท้าย SELECT

-- NOTE: ให้ replace view body ด้านล่างนี้ตาม view เดิมของคุณ
--       โดยเพิ่ม escrow columns จาก mdw.UnifiedOrders ต่อท้าย
/*
ALTER VIEW mdw.v_UnifiedOrders AS
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
    -- ... existing columns ...
    -- Escrow fields
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
    -- ... existing JSON aggregations ...
FROM mdw.UnifiedOrders o
...
*/
