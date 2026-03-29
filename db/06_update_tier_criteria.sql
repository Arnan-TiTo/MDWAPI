-- =============================================
-- Update Tier Criteria based on UI Screenshots
-- Date: 2026-03-30
-- =============================================

PRINT '--- Updating TierMasters Criteria ---';

-- 1. VIBE MEMBER (0 - 100 points, 0+ spend)
UPDATE VCINDW.mbw.TierMasters
SET TierName = 'VIBE MEMBER',
    MinPoints = 0,
    MaxPoints = 100,
    MinSpendAmount = 0,
    MaxSpendAmount = 2500,
    TierColor = '#3498DB', -- Blue as seen in screenshot icon
    SortOrder = 1
WHERE TierCode = 'VIBE';

-- 2. SAND MEMBER (101 - 200 points, 2,501+ spend)
UPDATE VCINDW.mbw.TierMasters
SET TierName = 'SAND MEMBER',
    MinPoints = 101,
    MaxPoints = 200,
    MinSpendAmount = 2501,
    MaxSpendAmount = 20000,
    TierColor = '#D35400', -- Orange as seen in screenshot icon
    SortOrder = 2
WHERE TierCode = 'SAND';

-- 3. ICE GRAY MEMBER (201 - 2000 points, 20,001+ spend)
UPDATE VCINDW.mbw.TierMasters
SET TierName = 'ICE GRAY MEMBER',
    MinPoints = 201,
    MaxPoints = 2000,
    MinSpendAmount = 20001,
    MaxSpendAmount = 50000,
    TierColor = '#BDC3C7', -- Silver/Gray as seen in screenshot icon
    SortOrder = 3
WHERE TierCode = 'ICE_GRAY';

-- 4. SPEEDYELLOW MEMBER (2001 - 3600 points, 50,001+ spend)
UPDATE VCINDW.mbw.TierMasters
SET TierName = 'SPEEDYELLOW MEMBER',
    MinPoints = 2001,
    MaxPoints = 3600,
    MinSpendAmount = 50001,
    MaxSpendAmount = 90000,
    TierColor = '#F1C40F', -- Yellow as seen in screenshot icon
    SortOrder = 4
WHERE TierCode = 'SPEED_YELLOW';

-- 5. ULTRAVIOLET MEMBER (3601+ points, 90,001+ spend)
UPDATE VCINDW.mbw.TierMasters
SET TierName = 'ULTRAVIOLET MEMBER',
    MinPoints = 3601,
    MaxPoints = NULL,
    MinSpendAmount = 90001,
    MaxSpendAmount = NULL,
    TierColor = '#9B59B6', -- Purple as seen in screenshot icon
    SortOrder = 5
WHERE TierCode = 'ULTRA_VIOLET';

