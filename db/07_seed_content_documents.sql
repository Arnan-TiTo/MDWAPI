-- Seed Content Documents for TERMS and PRIVACY with REAL CONTENT from images
-- Type: TERMS, PRIVACY
-- Language: th, en

-- 1. TERMS (Thai)
IF NOT EXISTS (SELECT 1 FROM mbw.ContentDocuments WHERE DocumentType = 'TERMS' AND LanguageCode = 'th')
BEGIN
    INSERT INTO mbw.ContentDocuments (DocumentType, VersionNo, LanguageCode, Title, ContentHtml, IsActive, CreatedAt)
    VALUES ('TERMS', 1, 'th', N'ข้อกำหนดการใช้บริการ Vibe and Chic', 
    N'<div style="line-height: 1.8; color: #333; font-size: 15px;">
        <p>* สมาชิกสามารถรับพอยท์จากการใช้จ่ายผ่านร้านค้าภายใต้ Vibe and Chic Innovations Co., Ltd. โดยมีสิทธิ์รับ 1 พอยท์ต่อการใช้จ่ายครบทุก ๆ 50 บาท</p>
        <p>* 1 พอยท์ สามารถใช้แทนเงินสดได้ 1 บาท โดยสมาชิกสามารถใช้เป็นส่วนลดในการซื้อสินค้าที่เข้าร่วมแคมเปญ หรือลุ้นรับของรางวัลสุดพิเศษ โดยสามารถติดตามรายละเอียดได้ที่ Line Official</p>
        <p>* สมาชิกสามารถสะสมและใช้งานพอยท์ได้ตั้งแต่ 11 พฤศจิกายน 2567 เป็นต้นไป หรือจนกว่าจะมีการเปลี่ยนแปลง เงื่อนไขให้เป็นไปตามที่บริษัทกำหนด</p>
        <p style="margin-top: 20px;">และสามารถศึกษาข้อกำหนดการใช้บริการเพิ่มเติมได้ที่: <a href="https://bit.ly/3Cl1fzN" style="color: #007AFF; text-decoration: none;">https://bit.ly/3Cl1fzN</a></p>
    </div>', 1, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE mbw.ContentDocuments 
    SET Title = N'ข้อกำหนดการใช้บริการ Vibe and Chic',
        ContentHtml = N'<div style="line-height: 1.8; color: #333; font-size: 15px;">
        <p>* สมาชิกสามารถรับพอยท์จากการใช้จ่ายผ่านร้านค้าภายใต้ Vibe and Chic Innovations Co., Ltd. โดยมีสิทธิ์รับ 1 พอยท์ต่อการใช้จ่ายครบทุก ๆ 50 บาท</p>
        <p>* 1 พอยท์ สามารถใช้แทนเงินสดได้ 1 บาท โดยสมาชิกสามารถใช้เป็นส่วนลดในการซื้อสินค้าที่เข้าร่วมแคมเปญ หรือลุ้นรับของรางวัลสุดพิเศษ โดยสามารถติดตามรายละเอียดได้ที่ Line Official</p>
        <p>* สมาชิกสามารถสะสมและใช้งานพอยท์ได้ตั้งแต่ 11 พฤศจิกายน 2567 เป็นต้นไป หรือจนกว่าจะมีการเปลี่ยนแปลง เงื่อนไขให้เป็นไปตามที่บริษัทกำหนด</p>
        <p style="margin-top: 20px;">และสามารถศึกษาข้อกำหนดการใช้บริการเพิ่มเติมได้ที่: <a href="https://bit.ly/3Cl1fzN" style="color: #007AFF; text-decoration: none;">https://bit.ly/3Cl1fzN</a></p>
    </div>'
    WHERE DocumentType = 'TERMS' AND LanguageCode = 'th';
END

-- 2. TERMS (English - Translated from real content)
IF NOT EXISTS (SELECT 1 FROM mbw.ContentDocuments WHERE DocumentType = 'TERMS' AND LanguageCode = 'en')
BEGIN
    INSERT INTO mbw.ContentDocuments (DocumentType, VersionNo, LanguageCode, Title, ContentHtml, IsActive, CreatedAt)
    VALUES ('TERMS', 1, 'en', N'Vibe and Chic Terms of Service', 
    N'<div style="line-height: 1.8; color: #333; font-size: 15px;">
        <p>* Members can earn points from spending at stores under Vibe and Chic Innovations Co., Ltd., earning 1 point for every 50 Baht spent.</p>
        <p>* 1 point can be used as 1 Baht cash equivalent. Members can use points as discounts for participating products or for a chance to win special rewards. Follow details at Line Official.</p>
        <p>* Members can accumulate and use points from November 11, 2024 onwards, or until terms are changed as determined by the company.</p>
        <p style="margin-top: 20px;">For more information, visit: <a href="https://bit.ly/3Cl1fzN" style="color: #007AFF; text-decoration: none;">https://bit.ly/3Cl1fzN</a></p>
    </div>', 1, GETUTCDATE());
END

-- 3. PRIVACY (Thai)
IF NOT EXISTS (SELECT 1 FROM mbw.ContentDocuments WHERE DocumentType = 'PRIVACY' AND LanguageCode = 'th')
BEGIN
    INSERT INTO mbw.ContentDocuments (DocumentType, VersionNo, LanguageCode, Title, ContentHtml, IsActive, CreatedAt)
    VALUES ('PRIVACY', 1, 'th', N'นโยบายคุ้มครองข้อมูลส่วนบุคคล', 
    N'<div style="line-height: 1.8; color: #333; font-size: 15px;">
        <p>สามารถศึกษานโยบายความเป็นส่วนตัว เพิ่มเติมได้ที่: <a href="https://bit.ly/3Cl1fzN" style="color: #007AFF; text-decoration: none;">https://bit.ly/3Cl1fzN</a></p>
    </div>', 1, GETUTCDATE());
END
ELSE
BEGIN
    UPDATE mbw.ContentDocuments 
    SET Title = N'นโยบายคุ้มครองข้อมูลส่วนบุคคล',
        ContentHtml = N'<div style="line-height: 1.8; color: #333; font-size: 15px;">
        <p>สามารถศึกษานโยบายความเป็นส่วนตัว เพิ่มเติมได้ที่: <a href="https://bit.ly/3Cl1fzN" style="color: #007AFF; text-decoration: none;">https://bit.ly/3Cl1fzN</a></p>
    </div>'
    WHERE DocumentType = 'PRIVACY' AND LanguageCode = 'th';
END

-- 4. PRIVACY (English)
IF NOT EXISTS (SELECT 1 FROM mbw.ContentDocuments WHERE DocumentType = 'PRIVACY' AND LanguageCode = 'en')
BEGIN
    INSERT INTO mbw.ContentDocuments (DocumentType, VersionNo, LanguageCode, Title, ContentHtml, IsActive, CreatedAt)
    VALUES ('PRIVACY', 1, 'en', N'Privacy Policy', 
    N'<div style="line-height: 1.8; color: #333; font-size: 15px;">
        <p>You can study more about our Privacy Policy at: <a href="https://bit.ly/3Cl1fzN" style="color: #007AFF; text-decoration: none;">https://bit.ly/3Cl1fzN</a></p>
    </div>', 1, GETUTCDATE());
END
