-- ============================================
-- Point Expiry: Add ExpiryDays to PointPolicies
-- ============================================

-- 1. เพิ่ม ExpiryDays ที่ PointPolicies
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('mbw.PointPolicies') AND name = 'ExpiryDays')
BEGIN
    ALTER TABLE mbw.PointPolicies ADD ExpiryDays INT NULL;
    PRINT 'Added ExpiryDays to mbw.PointPolicies';
END

-- 2. สร้าง table PointExpirations (ถ้ายังไม่มี)
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('mbw.PointExpirations'))
BEGIN
    CREATE TABLE mbw.PointExpirations (
        ExpirationId    BIGINT IDENTITY(1,1) PRIMARY KEY,
        MemberId        BIGINT NOT NULL,
        SourceLedgerId  BIGINT NOT NULL,
        OriginalPoints  INT NOT NULL,
        RemainingPoints INT NOT NULL,
        ExpiresAt       DATETIME2 NOT NULL,
        ExpiredAt       DATETIME2 NULL,
        Status          NVARCHAR(20) NOT NULL DEFAULT 'Active',

        CONSTRAINT FK_PointExpirations_Member FOREIGN KEY (MemberId)
            REFERENCES mbw.Members(MemberId),
        CONSTRAINT FK_PointExpirations_Ledger FOREIGN KEY (SourceLedgerId)
            REFERENCES mbw.PointLedger(LedgerId)
    );

    CREATE INDEX IX_PointExpirations_Member_Status
        ON mbw.PointExpirations(MemberId, Status)
        INCLUDE (RemainingPoints, ExpiresAt);

    CREATE INDEX IX_PointExpirations_ExpiresAt
        ON mbw.PointExpirations(ExpiresAt)
        WHERE Status = 'Active' AND RemainingPoints > 0;

    PRINT 'Created mbw.PointExpirations';
END

-- 3. ตัวอย่าง: update existing policy ให้มี ExpiryDays
-- UPDATE mbw.PointPolicies SET ExpiryDays = 365 WHERE PolicyId = 1;
