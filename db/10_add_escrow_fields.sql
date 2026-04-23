-- ============================================================
-- 10_add_escrow_fields.sql
-- เพิ่ม escrow / income breakdown fields
-- Source: Shopee /api/v2/payment/get_escrow_detail → order_income
-- ============================================================

-- ─── 1. mdw.ShopeeOrders ────────────────────────────────────
ALTER TABLE mdw.ShopeeOrders ADD
    EscrowAmount           DECIMAL(18,4) NULL,  -- escrow_amount (รายรับจากคำสั่งซื้อ)
    BuyerPaidShippingFee   DECIMAL(18,4) NULL,  -- buyer_paid_shipping_fee
    ActualShippingFee      DECIMAL(18,4) NULL,  -- actual_shipping_fee
    PlatformShippingRebate DECIMAL(18,4) NULL,  -- shopee_shipping_rebate
    CommissionFee          DECIMAL(18,4) NULL,  -- commission_fee (ค่าคอมมิชชั่น)
    ServiceFee             DECIMAL(18,4) NULL,  -- service_fee (ค่าบริการ)
    PlatformFee            DECIMAL(18,4) NULL,  -- platform_fee (ค่าธรรมเนียมโครงสร้างพื้นฐาน)
    PaymentTransactionFee  DECIMAL(18,4) NULL,  -- seller_transaction_fee (ค่าธุรกรรม)
    AmsCommissionFee       DECIMAL(18,4) NULL,  -- ams_commission_fee
    SellerVoucherCode      NVARCHAR(500) NULL,  -- seller_voucher_code (อาจมีหลาย code คั่นด้วย ,)
    EscrowUpdatedAt        DATETIME2 NULL;      -- เวลาที่ดึงข้อมูล escrow ล่าสุด

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
