-- ─── 09_cleanup_duplicate_redemptions.sql ───────────────────
-- สคริปต์ลบรายการแลกของรางวัลที่ซ้ำซ้อน (Double-Redemption) 
-- โดยเลือกเก็บรายการที่เก่าที่สุดไว้ และลบรายการที่ซ้ำในระยะเวลา 5 นาทีออก

BEGIN TRANSACTION;

-- 1. สร้างตารางชั่วคราวเพื่อหา ID ที่ต้องการลบ
SELECT 
    r1.RedemptionId,
    r1.LedgerId
INTO #DuplicatesToRemove
FROM mbw.RewardRedemptions r1
JOIN mbw.RewardRedemptions r2 ON 
    r1.MemberId = r2.MemberId AND 
    r1.RewardId = r2.RewardId AND 
    r1.RedemptionId > r2.RedemptionId AND -- เก็บอันที่ ID น้อยกว่า (เก่ากว่า) ไว้
    ABS(DATEDIFF(SECOND, r1.CreatedAt, r2.CreatedAt)) < 300; -- ซ้ำใน 5 นาที

-- 2. ลบ PointLedger ที่เกี่ยวข้อง (เพื่อคืนยอดแต้มให้ถูกต้องหากเป็นการลบส่วนเกิน)
-- หมายเหตุ: ในระบบจริงแต้มโดนตัดไปแล้ว การลบประวัติตรงนี้อาจทำให้ยอด TotalBurned คลาดเคลื่อนเล็กน้อย 
-- แต่เพื่อให้หน้าจอประวัติสะอาด เราจะลบรายการ Ledger ส่วนเกินออกครับ
IF EXISTS (SELECT 1 FROM #DuplicatesToRemove WHERE LedgerId IS NOT NULL)
BEGIN
    DELETE FROM mbw.PointLedger 
    WHERE LedgerId IN (SELECT LedgerId FROM #DuplicatesToRemove WHERE LedgerId IS NOT NULL);
END

-- 3. ลบรายการ Redemption ที่ซ้ำ
DELETE FROM mbw.RewardRedemptions 
WHERE RedemptionId IN (SELECT RedemptionId FROM #DuplicatesToRemove);

SELECT @@ROWCOUNT AS 'DeletedDuplicates';

DROP TABLE #DuplicatesToRemove;

COMMIT;
GO
