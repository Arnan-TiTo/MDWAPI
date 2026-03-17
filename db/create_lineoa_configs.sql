-- =============================================
-- Multi-Company LINE OA Config
-- ใช้ dbo.Companys ที่มีอยู่แล้ว
-- เพิ่ม: mbw.LineOaConfigs + Members.CompanyId
-- =============================================

-- 1. สร้าง mbw.LineOaConfigs (เก็บ LINE OA credentials per company)
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id 
               WHERE s.name = 'mbw' AND t.name = 'LineOaConfigs')
BEGIN
    CREATE TABLE [mbw].[LineOaConfigs] (
        LineOaConfigId      INT IDENTITY(1,1) PRIMARY KEY,
        CompanysId          INT            NOT NULL,        -- FK → dbo.Companys.Id
        LineOaName          NVARCHAR(200)  NOT NULL,        -- ชื่อ LINE OA
        LoginChannelId      NVARCHAR(50)   NULL,            -- LINE Login Channel ID
        LoginChannelSecret  NVARCHAR(100)  NULL,            -- LINE Login Channel Secret
        LoginCallbackUrl    NVARCHAR(500)  NULL,            -- LINE Login Callback URL
        MsgChannelSecret    NVARCHAR(100)  NULL,            -- LINE Messaging Channel Secret (webhook verify)
        MsgChannelToken     NVARCHAR(500)  NOT NULL,        -- LINE Messaging Channel Access Token (push msg)
        LiffId              NVARCHAR(50)   NULL,            -- LIFF App ID
        IsActive            BIT            NOT NULL DEFAULT 1,
        CreatedAt           DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedAt           DATETIME2      NULL,

        CONSTRAINT FK_LineOaConfigs_Companys FOREIGN KEY (CompanysId) REFERENCES [dbo].[Companys](Id)
    );

    -- 1 active LINE OA per company
    CREATE UNIQUE INDEX IX_LineOaConfigs_CompanyActive
    ON [mbw].[LineOaConfigs](CompanysId) WHERE IsActive = 1;

    PRINT 'Created mbw.LineOaConfigs';
END
GO

-- 2. เพิ่ม CompanysId ที่ Members (member สังกัดบริษัทไหน)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('mbw.Members') AND name = 'CompanysId')
BEGIN
    ALTER TABLE [mbw].[Members] ADD CompanysId INT NULL;
    
    ALTER TABLE [mbw].[Members]
    ADD CONSTRAINT FK_Members_Companys FOREIGN KEY (CompanysId) REFERENCES [dbo].[Companys](Id);

    PRINT 'Added CompanysId to mbw.Members';
END
GO

-- 3. เพิ่ม CompanysId ที่ MemberIdentities (LINE userId ผูกกับ LINE OA ของ company ไหน)  
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('mbw.MemberIdentities') AND name = 'CompanysId')
BEGIN
    ALTER TABLE [mbw].[MemberIdentities] ADD CompanysId INT NULL;

    ALTER TABLE [mbw].[MemberIdentities]
    ADD CONSTRAINT FK_MemberIdentities_Companys FOREIGN KEY (CompanysId) REFERENCES [dbo].[Companys](Id);

    PRINT 'Added CompanysId to mbw.MemberIdentities';
END
GO

-- 4. Insert ค่าเริ่มต้น (ย้ายจาก appsettings.json)
-- *** แก้ CompanysId ให้ตรงกับ Company ของคุณ ***
/*
INSERT INTO [mbw].[LineOaConfigs] 
    (CompanysId, LineOaName, LoginChannelId, LoginChannelSecret, LoginCallbackUrl, MsgChannelSecret, MsgChannelToken, LiffId)
VALUES (
    1,                                          -- CompanysId → ใส่ Id ของ Company จาก dbo.Companys
    N'Vibe & Chic Official',                    -- ชื่อ LINE OA
    '2009472836',                               -- Login Channel ID
    '3ca7a86f2db93e06ec226319aee9d911',         -- Login Channel Secret
    'https://yoursite.com/api/line/callback',    -- Callback URL
    '5fd35017b35f808ab6c74b52de7d3f92',         -- Messaging Channel Secret
    'G281LePrRqqjP+...ilFU=',                   -- Messaging Channel Access Token
    '2009472836-ju6Buk3K'                        -- LIFF ID
);
*/

-- 5. Verify
SELECT 'LineOaConfigs' AS [Table], COUNT(*) AS [Count] FROM [mbw].[LineOaConfigs]
UNION ALL
SELECT 'Members with CompanysId', COUNT(*) FROM [mbw].[Members] WHERE CompanysId IS NOT NULL;
GO
