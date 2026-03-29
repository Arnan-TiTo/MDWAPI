-- =============================================
-- Seed Master Data for LIFF Member System
-- =============================================

-- 1. Seed MemberChannels
INSERT INTO VCINDW.mbw.MemberChannels (ChannelCode, ChannelName, Description, SortOrder)
VALUES 
('LINE_OA', 'Line Official Account', 'สมัครสมาชิกผ่าน LINE OA', 1),
('SHOPEE', 'Shopee', 'สมัครสมาชิกที่ Shopee', 2),
('TIKTOK', 'TikTok', 'สมัครสมาชิกที่ TikTok', 3);


-- 2. Seed RegistrationProductOptions
INSERT INTO VCINDW.mbw.RegistrationProductOptions (OptionCode, OptionName, IsAllowOtherText, SortOrder)
VALUES 
('SN24_MULTI_UV_PROTECTION_HYBRID_SUNSCREEN_SPF50_PA', 'SN24 Multi UV Protection Hybrid Sunscreen SPF50+ PA++++', 0, 1),
('VIBELLA: Amino Essence Mist Spray', 'VIBELLA: Amino Essence Mist Spray', 0, 2),
('OTHER', 'อื่นๆ', 1, 3);


-- 3. Seed TierMasters
INSERT INTO VCINDW.mbw.TierMasters (TierCode, TierName, MinPoints, MinSpendAmount, TierColor, SortOrder)
VALUES 
('VIBE', 'VIBE', 0, 0, '#C0C0C0', 1),
('SAND', 'SAND', 1000, 5000, '#F4A460', 2),
('ICE_GRAY', 'ICE GRAY', 5000, 20000, '#D3D3D3', 3),
('SPEED_YELLOW', 'SPEEDYELLOW', 15000, 60000, '#FFFF00', 4),
('ULTRA_VIOLET', 'ULTRAVIOLET', 30000, 120000, '#EE82EE', 5);


-- 4. Seed Initial ContentDocuments (Terms & Privacy)
INSERT INTO VCINDW.mbw.ContentDocuments (DocumentType, VersionNo, LanguageCode, Title, ContentHtml, PublishedAt)
VALUES 
('TERMS', '1.0', 'th', 'ข้อกำหนดและเงื่อนไขการใช้งาน', '<h1>ข้อกำหนดและเงื่อนไข</h1><p>เนื้อหาข้อกำหนดและเงื่อนไขการเป็นสมาชิก...</p>', GETUTCDATE()),
('PRIVACY', '1.0', 'th', 'นโยบายความเป็นส่วนตัว', '<h1>นโยบายความเป็นส่วนตัว</h1><p>เราให้ความสำคัญกับความเป็นส่วนตัวของคุณ...</p>', GETUTCDATE());

