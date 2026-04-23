-- ============================================================
-- MBW - Seed Master Data (Real Data: Vibe and Chic)
-- Database: VCINDW
-- ============================================================

SET NOCOUNT ON;

-- ============================================================
-- 1. TierMasters  (ข้อมูลจริงจาก 06_update_tier_criteria.sql)
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM mbw.TierMasters)
BEGIN
    SET IDENTITY_INSERT mbw.TierMasters ON;
    INSERT INTO mbw.TierMasters
        (TierId, TierCode, TierName, MinPoints, MaxPoints, MinSpendAmount, MaxSpendAmount, TierColor, SortOrder, IsActive, CreatedAt)
    VALUES
        (1, 'VIBE',         'VIBE MEMBER',         0,     100,    0,      2500,   '#3498DB', 1, 1, SYSUTCDATETIME()),
        (2, 'SAND',         'SAND MEMBER',          101,   200,    2501,   20000,  '#D35400', 2, 1, SYSUTCDATETIME()),
        (3, 'ICE_GRAY',     'ICE GRAY MEMBER',      201,   2000,   20001,  50000,  '#BDC3C7', 3, 1, SYSUTCDATETIME()),
        (4, 'SPEED_YELLOW', 'SPEEDYELLOW MEMBER',   2001,  3600,   50001,  90000,  '#F1C40F', 4, 1, SYSUTCDATETIME()),
        (5, 'ULTRA_VIOLET', 'ULTRAVIOLET MEMBER',   3601,  NULL,   90001,  NULL,   '#9B59B6', 5, 1, SYSUTCDATETIME());
    SET IDENTITY_INSERT mbw.TierMasters OFF;
    PRINT 'TierMasters: inserted 5 rows';
END
ELSE
    PRINT 'TierMasters: skipped (already has data)';
GO

-- ============================================================
-- 2. MemberChannels  (ข้อมูลจริงจาก 03_seed_master_data.sql)
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM mbw.MemberChannels)
BEGIN
    SET IDENTITY_INSERT mbw.MemberChannels ON;
    INSERT INTO mbw.MemberChannels
        (ChannelId, ChannelCode, ChannelName, Description, IsActive, SortOrder, CreatedAt)
    VALUES
        (1, 'LINE_OA',  'Line Official Account', N'สมัครสมาชิกผ่าน LINE OA',   1, 1, SYSUTCDATETIME()),
        (2, 'SHOPEE',   'Shopee',                N'สมัครสมาชิกที่ Shopee',      1, 2, SYSUTCDATETIME()),
        (3, 'TIKTOK',   'TikTok',                N'สมัครสมาชิกที่ TikTok',      1, 3, SYSUTCDATETIME());
    SET IDENTITY_INSERT mbw.MemberChannels OFF;
    PRINT 'MemberChannels: inserted 3 rows';
END
ELSE
    PRINT 'MemberChannels: skipped (already has data)';
GO

-- ============================================================
-- 3. RegistrationProductOptions  (ข้อมูลจริงจาก 03_seed_master_data.sql)
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM mbw.RegistrationProductOptions)
BEGIN
    SET IDENTITY_INSERT mbw.RegistrationProductOptions ON;
    INSERT INTO mbw.RegistrationProductOptions
        (OptionId, OptionCode, OptionName, IsAllowOtherText, IsActive, SortOrder, CreatedAt)
    VALUES
        (1, 'SN24_SUNSCREEN',   N'SN24 Multi UV Protection Hybrid Sunscreen SPF50+ PA++++', 0, 1, 1, SYSUTCDATETIME()),
        (2, 'VIBELLA_MIST',     N'VIBELLA: Amino Essence Mist Spray',                        0, 1, 2, SYSUTCDATETIME()),
        (3, 'OTHER',            N'อื่นๆ',                                                     1, 1, 3, SYSUTCDATETIME());
    SET IDENTITY_INSERT mbw.RegistrationProductOptions OFF;
    PRINT 'RegistrationProductOptions: inserted 3 rows';
END
ELSE
    PRINT 'RegistrationProductOptions: skipped (already has data)';
GO

-- ============================================================
-- 4. PointPolicies  (1 point ทุก 50 บาท ตามข้อกำหนดจริง)
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM mbw.PointPolicies)
BEGIN
    SET IDENTITY_INSERT mbw.PointPolicies ON;
    INSERT INTO mbw.PointPolicies
        (PolicyId, PolicyName, PlatformType, EarnFormula, EarnRate, MinOrderAmount, EligibleStatuses, ExpiryDays, EffectiveFrom, EffectiveTo, IsActive, CreatedBy, CreatedAt)
    VALUES
        (1, N'นโยบายสะสมพอยท์มาตรฐาน (1 พอยท์ / 50 บาท)', 'ALL',    'AMOUNT_DIV_100', 2.0, 50.00, 'shipped,completed', 365, '2024-11-11', NULL, 1, 'system', SYSUTCDATETIME()),
        (2, N'นโยบาย Shopee (1 พอยท์ / 50 บาท)',            'SHOPEE', 'AMOUNT_DIV_100', 2.0, 50.00, 'shipped,completed', 365, '2024-11-11', NULL, 1, 'system', SYSUTCDATETIME()),
        (3, N'นโยบาย TikTok Shop (1 พอยท์ / 50 บาท)',       'TIKTOK', 'AMOUNT_DIV_100', 2.0, 50.00, 'completed',         365, '2024-11-11', NULL, 1, 'system', SYSUTCDATETIME());
    SET IDENTITY_INSERT mbw.PointPolicies OFF;
    PRINT 'PointPolicies: inserted 3 rows';
END
ELSE
    PRINT 'PointPolicies: skipped (already has data)';
GO

-- ============================================================
-- 5. ContentDocuments  (เนื้อหาจริงจาก 07_seed_content_documents.sql)
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM mbw.ContentDocuments WHERE DocumentType = 'TERMS' AND LanguageCode = 'th')
BEGIN
    INSERT INTO mbw.ContentDocuments
        (DocumentType, VersionNo, LanguageCode, Title, ContentHtml, IsActive, EffectiveFrom, PublishedAt, CreatedAt)
    VALUES ('TERMS', '1', 'th', N'ข้อกำหนดการใช้บริการ Vibe and Chic',
    N'<div style="line-height: 1.8; color: #333; font-size: 15px;">
        <p>* สมาชิกสามารถรับพอยท์จากการใช้จ่ายผ่านร้านค้าภายใต้ Vibe and Chic Innovations Co., Ltd. โดยมีสิทธิ์รับ 1 พอยท์ต่อการใช้จ่ายครบทุก ๆ 50 บาท</p>
        <p>* 1 พอยท์ สามารถใช้แทนเงินสดได้ 1 บาท โดยสมาชิกสามารถใช้เป็นส่วนลดในการซื้อสินค้าที่เข้าร่วมแคมเปญ หรือลุ้นรับของรางวัลสุดพิเศษ โดยสามารถติดตามรายละเอียดได้ที่ Line Official</p>
        <p>* สมาชิกสามารถสะสมและใช้งานพอยท์ได้ตั้งแต่ 11 พฤศจิกายน 2567 เป็นต้นไป หรือจนกว่าจะมีการเปลี่ยนแปลง เงื่อนไขให้เป็นไปตามที่บริษัทกำหนด</p>
        <p style="margin-top: 20px;">และสามารถศึกษาข้อกำหนดการใช้บริการเพิ่มเติมได้ที่: <a href="https://bit.ly/3Cl1fzN" style="color: #007AFF; text-decoration: none;">https://bit.ly/3Cl1fzN</a></p>
    </div>',
    1, '2024-11-11', '2024-11-11', SYSUTCDATETIME());
    PRINT 'ContentDocuments: inserted TERMS (th)';
END

IF NOT EXISTS (SELECT 1 FROM mbw.ContentDocuments WHERE DocumentType = 'TERMS' AND LanguageCode = 'en')
BEGIN
    INSERT INTO mbw.ContentDocuments
        (DocumentType, VersionNo, LanguageCode, Title, ContentHtml, IsActive, EffectiveFrom, PublishedAt, CreatedAt)
    VALUES ('TERMS', '1', 'en', N'Vibe and Chic Terms of Service',
    N'<div style="line-height: 1.8; color: #333; font-size: 15px;">
        <p>* Members can earn points from spending at stores under Vibe and Chic Innovations Co., Ltd., earning 1 point for every 50 Baht spent.</p>
        <p>* 1 point can be used as 1 Baht cash equivalent. Members can use points as discounts for participating products or for a chance to win special rewards. Follow details at Line Official.</p>
        <p>* Members can accumulate and use points from November 11, 2024 onwards, or until terms are changed as determined by the company.</p>
        <p style="margin-top: 20px;">For more information, visit: <a href="https://bit.ly/3Cl1fzN" style="color: #007AFF; text-decoration: none;">https://bit.ly/3Cl1fzN</a></p>
    </div>',
    1, '2024-11-11', '2024-11-11', SYSUTCDATETIME());
    PRINT 'ContentDocuments: inserted TERMS (en)';
END

IF NOT EXISTS (SELECT 1 FROM mbw.ContentDocuments WHERE DocumentType = 'PRIVACY' AND LanguageCode = 'th')
BEGIN
    INSERT INTO mbw.ContentDocuments
        (DocumentType, VersionNo, LanguageCode, Title, ContentHtml, IsActive, EffectiveFrom, PublishedAt, CreatedAt)
    VALUES ('PRIVACY', '1', 'th', N'นโยบายคุ้มครองข้อมูลส่วนบุคคล',
    N'<div style="line-height: 1.8; color: #333; font-size: 15px;">
        <p>สามารถศึกษานโยบายความเป็นส่วนตัว เพิ่มเติมได้ที่: <a href="https://bit.ly/3Cl1fzN" style="color: #007AFF; text-decoration: none;">https://bit.ly/3Cl1fzN</a></p>
    </div>',
    1, '2024-11-11', '2024-11-11', SYSUTCDATETIME());
    PRINT 'ContentDocuments: inserted PRIVACY (th)';
END

IF NOT EXISTS (SELECT 1 FROM mbw.ContentDocuments WHERE DocumentType = 'PRIVACY' AND LanguageCode = 'en')
BEGIN
    INSERT INTO mbw.ContentDocuments
        (DocumentType, VersionNo, LanguageCode, Title, ContentHtml, IsActive, EffectiveFrom, PublishedAt, CreatedAt)
    VALUES ('PRIVACY', '1', 'en', N'Privacy Policy',
    N'<div style="line-height: 1.8; color: #333; font-size: 15px;">
        <p>You can study more about our Privacy Policy at: <a href="https://bit.ly/3Cl1fzN" style="color: #007AFF; text-decoration: none;">https://bit.ly/3Cl1fzN</a></p>
    </div>',
    1, '2024-11-11', '2024-11-11', SYSUTCDATETIME());
    PRINT 'ContentDocuments: inserted PRIVACY (en)';
END
GO

-- ============================================================
-- 6. LineOaConfigs  (Vibe and Chic - กรอก Channel ID/Secret จริงก่อน deploy)
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM mbw.LineOaConfigs)
BEGIN
    SET IDENTITY_INSERT mbw.LineOaConfigs ON;
    INSERT INTO mbw.LineOaConfigs
        (LineOaConfigId, CompanysId, LineOaName, LoginChannelId, LoginChannelSecret, LoginCallbackUrl, MsgChannelSecret, MsgChannelToken, LiffId, IsActive, CreatedAt)
    VALUES
        (1, 1, 'Vibe and Chic LINE OA',
         '{{LINE_LOGIN_CHANNEL_ID}}',
         '{{LINE_LOGIN_CHANNEL_SECRET}}',
         'https://chicspheremember.vibeandchic.com/line/callback',
         '{{LINE_MSG_CHANNEL_SECRET}}',
         '{{LINE_MSG_CHANNEL_TOKEN}}',
         '{{LIFF_ID}}',
         1, SYSUTCDATETIME());
    SET IDENTITY_INSERT mbw.LineOaConfigs OFF;
    PRINT 'LineOaConfigs: inserted 1 row (replace {{}} placeholders with real values)';
END
ELSE
    PRINT 'LineOaConfigs: skipped (already has data)';
GO


PRINT '=== Seed master data completed ===';
