# DDL SQL Server สำหรับตารางที่ยังขาดของ LIFF Member System

ระบบ: Vibe and Chic Member Loyalty

วันที่จัดทำ: 29/03/2026

## 1. วัตถุประสงค์

เอกสารนี้สรุปเฉพาะตารางที่ยังขาด เพื่อให้หน้า LIFF รองรับ UI ตามรูปได้ครบมากขึ้น โดยออกแบบให้เข้ากับ schema `VCINDW.mbw` และรูปแบบการตั้งชื่อเดิมของระบบ

## 2. ขอบเขตตารางที่แนะนำให้เพิ่ม

| Table | Priority | Purpose |
|---|---|---|
| `MemberChannels` | สูง | ช่องทางสมัคร/สาขาใน LIFF |
| `RegistrationProductOptions` | สูง | master ตัวเลือกสินค้าที่ใช้ตอบตอนสมัคร |
| `MemberRegistrationAnswers` | สูง | คำตอบจริงแบบ multi-select ของสมาชิก |
| `ContentDocuments` | สูง | Terms / Privacy / Member Policy แบบ versioned |
| `MemberConsentLogs` | สูง | audit consent ต่อ document version |
| `TierMasters` | สูง | master ระดับสมาชิก |
| `MemberTierHistories` | สูง | ประวัติเปลี่ยน tier |
| `MemberNotifications` | สูง | inbox แจ้งเตือนย้อนหลัง 14 วัน |
| `RewardRedemptions` | สูง | รายการแลกรางวัลของสมาชิก |
| `RewardFulfillments` | กลาง-สูง | สถานะดำเนินการ/จัดส่ง |
| `RewardRedemptionHistories` | กลาง-สูง | audit การเปลี่ยนสถานะรางวัล |

## 3. หลักการออกแบบ

- ตารางใหม่ถูกออกแบบให้เพิ่มเข้าไปได้โดยไม่ต้องรื้อ Members เดิมทันที
- Branch, MembershipTier และ HowYouKnowMe ยังใช้เป็น snapshot/text เดิมได้ในช่วงเปลี่ยนผ่าน
- หากภายหลังต้อง normalize เพิ่ม สามารถใช้ optional ALTER TABLE ในภาคผนวกได้
- DDL นี้ใช้แนวทางตั้งชื่อแบบเดียวกับ schema เดิม: PK_, FK_, UQ_, IX_ และใช้ COLLATE SQL_Latin1_General_CP1_CI_AS กับคอลัมน์ข้อความ

## 4. DDL SQL Server

### VCINDW.mbw.MemberChannels

- **วัตถุประสงค์:** Master ของช่องทางสมัคร/สาขาที่ใช้ในหน้า LIFF เช่น Line Official
- **เหตุผลที่ต้องมี:** ใช้แทนการเก็บค่า Branch เป็นข้อความล้วน และช่วยให้จัดการรายการช่องทางจากหลังบ้านได้

```sql
CREATE TABLE VCINDW.mbw.MemberChannels (
    ChannelId int IDENTITY(1,1) NOT NULL,
    ChannelCode nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    ChannelName nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    Description nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    IsActive bit DEFAULT 1 NOT NULL,
    SortOrder int DEFAULT 0 NOT NULL,
    CreatedAt datetime2 DEFAULT sysutcdatetime() NOT NULL,
    UpdatedAt datetime2 NULL,
    CONSTRAINT PK_MemberChannels PRIMARY KEY (ChannelId),
    CONSTRAINT UQ_MemberChannels_ChannelCode UNIQUE (ChannelCode)
);
GO
CREATE NONCLUSTERED INDEX IX_MemberChannels_IsActive_SortOrder
ON VCINDW.mbw.MemberChannels (IsActive, SortOrder);
GO
```

### VCINDW.mbw.RegistrationProductOptions

- **วัตถุประสงค์:** Master ของตัวเลือก checkbox ในหน้าสมัครสมาชิก ว่ารู้จักสมาชิกจากสินค้าตัวใด
- **เหตุผลที่ต้องมี:** แทนการเก็บ HowYouKnowMe เป็นข้อความเดียว รองรับหลายตัวเลือกและจัดลำดับการแสดงผลได้

```sql
CREATE TABLE VCINDW.mbw.RegistrationProductOptions (
    OptionId int IDENTITY(1,1) NOT NULL,
    OptionCode nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    OptionName nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    IsAllowOtherText bit DEFAULT 0 NOT NULL,
    IsActive bit DEFAULT 1 NOT NULL,
    SortOrder int DEFAULT 0 NOT NULL,
    CreatedAt datetime2 DEFAULT sysutcdatetime() NOT NULL,
    UpdatedAt datetime2 NULL,
    CONSTRAINT PK_RegistrationProductOptions PRIMARY KEY (OptionId),
    CONSTRAINT UQ_RegistrationProductOptions_OptionCode UNIQUE (OptionCode)
);
GO
CREATE NONCLUSTERED INDEX IX_RegistrationProductOptions_IsActive_SortOrder
ON VCINDW.mbw.RegistrationProductOptions (IsActive, SortOrder);
GO
```

### VCINDW.mbw.MemberRegistrationAnswers

- **วัตถุประสงค์:** คำตอบจริงของสมาชิกจาก RegistrationProductOptions
- **เหตุผลที่ต้องมี:** รองรับ multi-select และกรณีเลือก “อื่นๆ” พร้อมข้อความเพิ่มเติม

```sql
CREATE TABLE VCINDW.mbw.MemberRegistrationAnswers (
    AnswerId bigint IDENTITY(1,1) NOT NULL,
    MemberId bigint NOT NULL,
    OptionId int NOT NULL,
    OtherText nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    CreatedAt datetime2 DEFAULT sysutcdatetime() NOT NULL,
    CONSTRAINT PK_MemberRegistrationAnswers PRIMARY KEY (AnswerId),
    CONSTRAINT UQ_MemberRegistrationAnswers_MemberId_OptionId UNIQUE (MemberId, OptionId),
    CONSTRAINT FK_MemberRegistrationAnswers_Members FOREIGN KEY (MemberId)
        REFERENCES VCINDW.mbw.Members(MemberId),
    CONSTRAINT FK_MemberRegistrationAnswers_RegistrationProductOptions FOREIGN KEY (OptionId)
        REFERENCES VCINDW.mbw.RegistrationProductOptions(OptionId)
);
GO
CREATE NONCLUSTERED INDEX IX_MemberRegistrationAnswers_MemberId
ON VCINDW.mbw.MemberRegistrationAnswers (MemberId);
GO
```

### VCINDW.mbw.ContentDocuments

- **วัตถุประสงค์:** เก็บ Terms / Privacy / Member Policy แบบ versioned
- **เหตุผลที่ต้องมี:** ใช้แสดงหน้าเอกสารใน LIFF และใช้เป็นต้นทางสำหรับ audit consent

```sql
CREATE TABLE VCINDW.mbw.ContentDocuments (
    DocumentId bigint IDENTITY(1,1) NOT NULL,
    DocumentType nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    VersionNo nvarchar(30) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    LanguageCode nvarchar(10) COLLATE SQL_Latin1_General_CP1_CI_AS DEFAULT 'th' NOT NULL,
    Title nvarchar(300) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    ContentHtml nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    ContentText nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    IsActive bit DEFAULT 1 NOT NULL,
    EffectiveFrom datetime2 DEFAULT sysutcdatetime() NOT NULL,
    EffectiveTo datetime2 NULL,
    PublishedAt datetime2 DEFAULT sysutcdatetime() NOT NULL,
    CreatedAt datetime2 DEFAULT sysutcdatetime() NOT NULL,
    UpdatedAt datetime2 NULL,
    CONSTRAINT PK_ContentDocuments PRIMARY KEY (DocumentId),
    CONSTRAINT UQ_ContentDocuments_DocumentType_VersionNo_LanguageCode
        UNIQUE (DocumentType, VersionNo, LanguageCode)
);
GO
CREATE NONCLUSTERED INDEX IX_ContentDocuments_DocumentType_IsActive_PublishedAt
ON VCINDW.mbw.ContentDocuments (DocumentType, IsActive, PublishedAt);
GO
```

### VCINDW.mbw.MemberConsentLogs

- **วัตถุประสงค์:** เก็บประวัติการยอมรับเอกสารของสมาชิก
- **เหตุผลที่ต้องมี:** แม้ Members จะมี ConsentAccepted/ConsentedAt แล้ว แต่ยังไม่พอสำหรับ version history และ audit

```sql
CREATE TABLE VCINDW.mbw.MemberConsentLogs (
    ConsentLogId bigint IDENTITY(1,1) NOT NULL,
    MemberId bigint NOT NULL,
    DocumentId bigint NOT NULL,
    AcceptedFlag bit NOT NULL,
    AcceptedAt datetime2 DEFAULT sysutcdatetime() NOT NULL,
    AcceptedFromChannel nvarchar(30) COLLATE SQL_Latin1_General_CP1_CI_AS DEFAULT 'LIFF' NOT NULL,
    AcceptedIp nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    AcceptedUserAgent nvarchar(1000) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    CreatedAt datetime2 DEFAULT sysutcdatetime() NOT NULL,
    CONSTRAINT PK_MemberConsentLogs PRIMARY KEY (ConsentLogId),
    CONSTRAINT FK_MemberConsentLogs_Members FOREIGN KEY (MemberId)
        REFERENCES VCINDW.mbw.Members(MemberId),
    CONSTRAINT FK_MemberConsentLogs_ContentDocuments FOREIGN KEY (DocumentId)
        REFERENCES VCINDW.mbw.ContentDocuments(DocumentId)
);
GO
CREATE NONCLUSTERED INDEX IX_MemberConsentLogs_MemberId_AcceptedAt
ON VCINDW.mbw.MemberConsentLogs (MemberId, AcceptedAt DESC);
GO
CREATE NONCLUSTERED INDEX IX_MemberConsentLogs_DocumentId
ON VCINDW.mbw.MemberConsentLogs (DocumentId);
GO
```

### VCINDW.mbw.TierMasters

- **วัตถุประสงค์:** Master ของระดับสมาชิกที่แสดงในหน้า Member Tier
- **เหตุผลที่ต้องมี:** ช่วยให้จัดการ rule การแสดงผลของ tier ได้โดยไม่ต้อง hardcode ใน frontend

```sql
CREATE TABLE VCINDW.mbw.TierMasters (
    TierId int IDENTITY(1,1) NOT NULL,
    TierCode nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    TierName nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    MinPoints decimal(18,2) DEFAULT 0 NOT NULL,
    MaxPoints decimal(18,2) NULL,
    MinSpendAmount decimal(18,2) DEFAULT 0 NOT NULL,
    MaxSpendAmount decimal(18,2) NULL,
    TierColor nvarchar(20) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    IconUrl nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    Description nvarchar(1000) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    SortOrder int DEFAULT 0 NOT NULL,
    IsActive bit DEFAULT 1 NOT NULL,
    CreatedAt datetime2 DEFAULT sysutcdatetime() NOT NULL,
    UpdatedAt datetime2 NULL,
    CONSTRAINT PK_TierMasters PRIMARY KEY (TierId),
    CONSTRAINT UQ_TierMasters_TierCode UNIQUE (TierCode)
);
GO
CREATE NONCLUSTERED INDEX IX_TierMasters_IsActive_SortOrder
ON VCINDW.mbw.TierMasters (IsActive, SortOrder);
GO
```

### VCINDW.mbw.MemberTierHistories

- **วัตถุประสงค์:** ประวัติการคำนวณ/เปลี่ยนระดับสมาชิก
- **เหตุผลที่ต้องมี:** รองรับการตรวจสอบย้อนหลังว่าทำไมสมาชิกอยู่ tier ไหน ณ ช่วงเวลาใด

```sql
CREATE TABLE VCINDW.mbw.MemberTierHistories (
    MemberTierHistoryId bigint IDENTITY(1,1) NOT NULL,
    MemberId bigint NOT NULL,
    TierId int NOT NULL,
    PreviousTierId int NULL,
    TierPoints decimal(18,2) DEFAULT 0 NOT NULL,
    SpendAmount decimal(18,2) DEFAULT 0 NOT NULL,
    WindowStartDate date NULL,
    WindowEndDate date NULL,
    CalculatedAt datetime2 DEFAULT sysutcdatetime() NOT NULL,
    Reason nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    CreatedAt datetime2 DEFAULT sysutcdatetime() NOT NULL,
    CONSTRAINT PK_MemberTierHistories PRIMARY KEY (MemberTierHistoryId),
    CONSTRAINT FK_MemberTierHistories_Members FOREIGN KEY (MemberId)
        REFERENCES VCINDW.mbw.Members(MemberId),
    CONSTRAINT FK_MemberTierHistories_TierMasters FOREIGN KEY (TierId)
        REFERENCES VCINDW.mbw.TierMasters(TierId),
    CONSTRAINT FK_MemberTierHistories_PreviousTierMasters FOREIGN KEY (PreviousTierId)
        REFERENCES VCINDW.mbw.TierMasters(TierId)
);
GO
CREATE NONCLUSTERED INDEX IX_MemberTierHistories_MemberId_CalculatedAt
ON VCINDW.mbw.MemberTierHistories (MemberId, CalculatedAt DESC);
GO
```

### VCINDW.mbw.MemberNotifications

- **วัตถุประสงค์:** กล่องข้อความแจ้งเตือนในหน้า LIFF
- **เหตุผลที่ต้องมี:** แยกจาก OutboxMessages ซึ่งทำหน้าที่เป็นคิวส่งข้อความ ไม่ใช่ notification inbox ของสมาชิก

```sql
CREATE TABLE VCINDW.mbw.MemberNotifications (
    NotificationId bigint IDENTITY(1,1) NOT NULL,
    MemberId bigint NOT NULL,
    NotificationType nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    Title nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    Message nvarchar(1000) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    RefType nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    RefId nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    IsRead bit DEFAULT 0 NOT NULL,
    ReadAt datetime2 NULL,
    DisplayUntil datetime2 NULL,
    CreatedAt datetime2 DEFAULT sysutcdatetime() NOT NULL,
    CONSTRAINT PK_MemberNotifications PRIMARY KEY (NotificationId),
    CONSTRAINT FK_MemberNotifications_Members FOREIGN KEY (MemberId)
        REFERENCES VCINDW.mbw.Members(MemberId)
);
GO
CREATE NONCLUSTERED INDEX IX_MemberNotifications_MemberId_CreatedAt
ON VCINDW.mbw.MemberNotifications (MemberId, CreatedAt DESC);
GO
CREATE NONCLUSTERED INDEX IX_MemberNotifications_MemberId_IsRead
ON VCINDW.mbw.MemberNotifications (MemberId, IsRead);
GO
```

### VCINDW.mbw.RewardRedemptions

- **วัตถุประสงค์:** รายการแลกรางวัลของสมาชิก
- **เหตุผลที่ต้องมี:** ใช้ทำหน้า “รางวัลของฉัน” และรองรับสถานะ ใช้งานได้ / ใช้งานแล้ว / หมดอายุ / กำลังดำเนินการ / จัดส่งแล้ว

```sql
CREATE TABLE VCINDW.mbw.RewardRedemptions (
    RedemptionId bigint IDENTITY(1,1) NOT NULL,
    RedemptionCode nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    MemberId bigint NOT NULL,
    RewardId int NOT NULL,
    RewardCodeId bigint NULL,
    RewardNameSnapshot nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    RewardTypeSnapshot nvarchar(30) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    Status nvarchar(30) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    PointsSpent int NOT NULL,
    CouponCode nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    QrPayload nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    ExpiresAt datetime2 NULL,
    UsedAt datetime2 NULL,
    CancelledAt datetime2 NULL,
    RedeemedAt datetime2 DEFAULT sysutcdatetime() NOT NULL,
    CreatedAt datetime2 DEFAULT sysutcdatetime() NOT NULL,
    UpdatedAt datetime2 NULL,
    CONSTRAINT PK_RewardRedemptions PRIMARY KEY (RedemptionId),
    CONSTRAINT UQ_RewardRedemptions_RedemptionCode UNIQUE (RedemptionCode),
    CONSTRAINT FK_RewardRedemptions_Members FOREIGN KEY (MemberId)
        REFERENCES VCINDW.mbw.Members(MemberId)
);
GO
CREATE NONCLUSTERED INDEX IX_RewardRedemptions_MemberId_Status
ON VCINDW.mbw.RewardRedemptions (MemberId, Status);
GO
CREATE NONCLUSTERED INDEX IX_RewardRedemptions_MemberId_RedeemedAt
ON VCINDW.mbw.RewardRedemptions (MemberId, RedeemedAt DESC);
GO
```

### VCINDW.mbw.RewardFulfillments

- **วัตถุประสงค์:** สถานะการดำเนินการ/จัดส่งของรางวัล
- **เหตุผลที่ต้องมี:** จำเป็นเมื่อมีรางวัลที่ต้องอนุมัติ จัดของ หรือจัดส่งจริง

```sql
CREATE TABLE VCINDW.mbw.RewardFulfillments (
    FulfillmentId bigint IDENTITY(1,1) NOT NULL,
    RedemptionId bigint NOT NULL,
    FulfillmentType nvarchar(30) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    FulfillmentStatus nvarchar(30) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    RecipientName nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    Phone nvarchar(30) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    AddressSnapshot nvarchar(1000) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    CarrierName nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    TrackingNo nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    ShippedAt datetime2 NULL,
    DeliveredAt datetime2 NULL,
    CreatedAt datetime2 DEFAULT sysutcdatetime() NOT NULL,
    UpdatedAt datetime2 NULL,
    CONSTRAINT PK_RewardFulfillments PRIMARY KEY (FulfillmentId),
    CONSTRAINT UQ_RewardFulfillments_RedemptionId UNIQUE (RedemptionId),
    CONSTRAINT FK_RewardFulfillments_RewardRedemptions FOREIGN KEY (RedemptionId)
        REFERENCES VCINDW.mbw.RewardRedemptions(RedemptionId)
);
GO
CREATE NONCLUSTERED INDEX IX_RewardFulfillments_FulfillmentStatus
ON VCINDW.mbw.RewardFulfillments (FulfillmentStatus);
GO
CREATE NONCLUSTERED INDEX IX_RewardFulfillments_TrackingNo
ON VCINDW.mbw.RewardFulfillments (TrackingNo);
GO
```

### VCINDW.mbw.RewardRedemptionHistories

- **วัตถุประสงค์:** ประวัติการเปลี่ยนสถานะของการแลกรางวัล
- **เหตุผลที่ต้องมี:** ช่วย audit และใช้ support ตรวจย้อนหลังได้ง่าย

```sql
CREATE TABLE VCINDW.mbw.RewardRedemptionHistories (
    RedemptionHistoryId bigint IDENTITY(1,1) NOT NULL,
    RedemptionId bigint NOT NULL,
    OldStatus nvarchar(30) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    NewStatus nvarchar(30) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    ChangedAt datetime2 DEFAULT sysutcdatetime() NOT NULL,
    ChangedBy nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    Remark nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    CONSTRAINT PK_RewardRedemptionHistories PRIMARY KEY (RedemptionHistoryId),
    CONSTRAINT FK_RewardRedemptionHistories_RewardRedemptions FOREIGN KEY (RedemptionId)
        REFERENCES VCINDW.mbw.RewardRedemptions(RedemptionId)
);
GO
CREATE NONCLUSTERED INDEX IX_RewardRedemptionHistories_RedemptionId_ChangedAt
ON VCINDW.mbw.RewardRedemptionHistories (RedemptionId, ChangedAt DESC);
GO
```

## 5. Optional ALTER TABLE ที่แนะนำ

ส่วนนี้ไม่ใช่ตารางใหม่ แต่แนะนำไว้สำหรับรอบถัดไป หากต้องการ normalize ข้อมูลใน `Members` เพิ่มขึ้น

```sql
-- Optional enhancement 1: เก็บภาษาที่สมาชิกเลือกจากหน้าเมนู
ALTER TABLE VCINDW.mbw.Members
ADD PreferredLanguage nvarchar(10) COLLATE SQL_Latin1_General_CP1_CI_AS NULL;
GO

-- Optional enhancement 2: เก็บ country code ของเบอร์โทร
ALTER TABLE VCINDW.mbw.Members
ADD PhoneCountryCode nvarchar(10) COLLATE SQL_Latin1_General_CP1_CI_AS NULL;
GO

-- Optional enhancement 3: ผูก channel/tier แบบ normalize เพิ่มในอนาคต
ALTER TABLE VCINDW.mbw.Members
ADD RegisterChannelId int NULL,
    CurrentTierId int NULL;
GO

ALTER TABLE VCINDW.mbw.Members
ADD CONSTRAINT FK_Members_MemberChannels FOREIGN KEY (RegisterChannelId)
    REFERENCES VCINDW.mbw.MemberChannels(ChannelId);
GO

ALTER TABLE VCINDW.mbw.Members
ADD CONSTRAINT FK_Members_TierMasters FOREIGN KEY (CurrentTierId)
    REFERENCES VCINDW.mbw.TierMasters(TierId);
GO
```

## 6. แผนดำเนินการทีละขั้นตอน

### Phase 0: วิเคราะห์ผลกระทบ

1. ทวน schema ปัจจุบันของ RewardCatalog และ RewardCodes เพื่อยืนยันชื่อ PK/ชนิดข้อมูลก่อน deploy
1. ยืนยัน mapping สถานะ reward ที่ต้องใช้ใน UI: AVAILABLE, USED, EXPIRED, PROCESSING, SHIPPED
1. ยืนยันว่าหน้า LIFF จะดึง Branch/Channel จาก master table ใหม่หรือยังคงใช้ข้อความเดิมระยะสั้น

### Phase 1: สร้างตารางใหม่ในฐานข้อมูล

1. รัน DDL ในเอกสารนี้บน environment DEV ก่อน
1. ตรวจสอบการสร้าง PK, FK, UNIQUE และ INDEX ทุกตัว
1. สร้าง migration script แยกตามกลุ่ม: registration, consent, tier, notification, reward

### Phase 2: seed master data

1. seed MemberChannels เช่น LINE_OFFICIAL
1. seed RegistrationProductOptions ให้ตรงกับ checkbox ในหน้าสมัคร
1. seed TierMasters ให้ตรงกับเกณฑ์ VIBE / SAND / ICE GRAY / SPEEDYELLOW / ULTRAVIOLET
1. publish ContentDocuments สำหรับ TERMS และ PRIVACY อย่างน้อยภาษาไทย

### Phase 3: ปรับ backend API / service

1. สมัครสมาชิก: บันทึก MemberRegistrationAnswers และ MemberConsentLogs เพิ่มจาก flow เดิม
1. เมนูเอกสาร: อ่าน ContentDocuments ล่าสุดตาม DocumentType
1. notification inbox: เขียน MemberNotifications ทุกครั้งที่มี signup success, earn, adjust, redeem
1. reward redeem: เขียน RewardRedemptions ทุกครั้งที่แลกสำเร็จ และถ้ามีการจัดส่งให้สร้าง RewardFulfillments
1. tier job: หลังคำนวณ tier ให้เขียน MemberTierHistories

### Phase 4: ปรับ LIFF frontend

1. หน้าสมัคร: เปลี่ยน checkbox source product ให้ดึงจาก RegistrationProductOptions
1. หน้า terms/privacy: อ่านเอกสารจาก ContentDocuments
1. หน้าระดับสมาชิก: อ่าน tier list จาก TierMasters และ current tier ของสมาชิก
1. หน้าการแจ้งเตือน: อ่านจาก MemberNotifications แทน outbox
1. หน้ารางวัลของฉัน: แยกแท็บตาม RewardRedemptions.Status และ RewardFulfillments.FulfillmentStatus

### Phase 5: backfill / migration data

1. สมาชิกเก่า: map Members.HowYouKnowMe เป็น MemberRegistrationAnswers ถ้าพอแยกได้
1. สมาชิกเก่า: set consent log ย้อนหลังจาก ConsentAccepted + ConsentedAt โดยอ้างอิง document version ปัจจุบัน
1. สมาชิกเก่า: สร้าง RewardRedemptions ย้อนหลังจาก reward code history เดิมถ้ามีข้อมูลเพียงพอ
1. เติม MemberNotifications ย้อนหลังเฉพาะรายการสำคัญถ้าต้องการให้ผู้ใช้เห็นประวัติเดิม

### Phase 6: UAT และ cutover

1. ทดสอบสมัครใหม่ end-to-end ผ่าน LIFF
1. ทดสอบ login สมาชิกเก่าแล้วดูว่าหน้าเมนู/แต้ม/รางวัล/แจ้งเตือนยังทำงานครบ
1. ทดสอบแลกรางวัลแบบ digital coupon และแบบ physical reward
1. ตรวจสอบ index และ execution plan ของหน้า notification / my rewards / tier history
1. deploy PROD พร้อม script rollback และ backup ก่อน cutover

## 7. ข้อแนะนำในการ deploy

1. รันบน DEV ก่อนเสมอ แล้วทดสอบผ่าน LIFF จริง
2. สำรองฐานข้อมูลก่อน deploy PROD
3. แยก script เป็น batch ย่อยเพื่อ rollback ง่าย
4. หลัง deploy ให้ seed master data ก่อนเปิด UI ฝั่งใหม่
5. ใช้ feature flag หรือ config เพื่อเปิดใช้หน้าใหม่เป็นลำดับ

## 8. หมายเหตุสำคัญ

- ใน DDL ของ `RewardRedemptions` ตั้ง `RewardId` เป็น `int` ตามแนวทางเอกสารเดิม หาก PK จริงของ `RewardCatalog` ต่างจากนี้ ให้ปรับชนิดข้อมูลและ FK ก่อนใช้งาน
- ถ้าระบบปัจจุบันยังไม่มี flow physical reward สามารถ deploy `RewardRedemptions` ก่อน และเลื่อน `RewardFulfillments` / `RewardRedemptionHistories` ไป phase ถัดไปได้
- ถ้าต้องการ persist ภาษาที่ผู้ใช้เลือกจากหน้าเมนู แนะนำให้ใช้ `PreferredLanguage` ใน `Members` ตาม optional ALTER