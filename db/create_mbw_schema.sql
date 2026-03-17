/*******************************************************************************
 *  Member Loyalty System — Schema: [mbw]
 *  Database : VCINDW  (SQL Server)
 *  Version  : 1.0
 *  Date     : 2026-03-16
 *
 *  23 tables สำหรับระบบสมาชิกสะสมแต้ม LINE × Shopee × TikTok
 *  แยก schema [mbw] ออกจาก [mdw] (marketplace) และ [dbo] (system)
 *
 *  Cross-schema FK:
 *    mbw.MemberPlatformAccounts.ShopId      → mdw.Shops.Id
 *    mbw.OrderMemberLinks.UnifiedOrderId    → mdw.UnifiedOrders.UnifiedOrderId
 *    mbw.OrderClaims.UnifiedOrderId         → mdw.UnifiedOrders.UnifiedOrderId
 *    mbw.OrderStatusHistory.UnifiedOrderId  → mdw.UnifiedOrders.UnifiedOrderId
 *    mbw.AdminAuditLogs.UserId              → dbo.Users.Id
 ******************************************************************************/

-- ============================================================
-- 0. สร้าง Schema [mbw]
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'mbw')
    EXEC('CREATE SCHEMA [mbw]');
GO

-- ============================================================
-- กลุ่ม A: สมาชิกและ Mapping (6 ตาราง)
-- ============================================================

-- 1. Members – ตาราง master สมาชิก
CREATE TABLE [mbw].[Members] (
    MemberId          BIGINT         IDENTITY(1,1) NOT NULL,
    MemberCode        NVARCHAR(50)   NOT NULL,        -- รหัสสมาชิกที่แสดง (เช่น MBW-000001)
    DisplayName       NVARCHAR(200)  NULL,
    Phone             NVARCHAR(30)   NULL,
    Email             NVARCHAR(200)  NULL,
    [Status]          NVARCHAR(20)   NOT NULL DEFAULT 'Active',  -- Active / Inactive / Suspended
    ConsentAccepted   BIT            NOT NULL DEFAULT 0,
    ConsentedAt       DATETIME2      NULL,
    RegisteredAt      DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedAt         DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt         DATETIME2      NULL,

    CONSTRAINT PK_Members PRIMARY KEY (MemberId),
    CONSTRAINT UQ_Members_MemberCode UNIQUE (MemberCode)
);
GO

CREATE INDEX IX_Members_Phone ON [mbw].[Members] (Phone) WHERE Phone IS NOT NULL;
CREATE INDEX IX_Members_Email ON [mbw].[Members] (Email) WHERE Email IS NOT NULL;
CREATE INDEX IX_Members_Status ON [mbw].[Members] ([Status]);
GO

-- 2. MemberIdentities – LINE identity ของสมาชิก
CREATE TABLE [mbw].[MemberIdentities] (
    MemberIdentityId  BIGINT         IDENTITY(1,1) NOT NULL,
    MemberId          BIGINT         NOT NULL,
    ProviderType      NVARCHAR(30)   NOT NULL,        -- LINE_LOGIN / LINE_OA
    ProviderUserKey   NVARCHAR(200)  NOT NULL,        -- LINE userId
    DisplayName       NVARCHAR(200)  NULL,
    PictureUrl        NVARCHAR(500)  NULL,
    LinkedAt          DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    IsActive          BIT            NOT NULL DEFAULT 1,

    CONSTRAINT PK_MemberIdentities PRIMARY KEY (MemberIdentityId),
    CONSTRAINT FK_MemberIdentities_Members FOREIGN KEY (MemberId) REFERENCES [mbw].[Members](MemberId),
    CONSTRAINT UQ_MemberIdentities_Provider UNIQUE (ProviderType, ProviderUserKey)
);
GO

CREATE INDEX IX_MemberIdentities_MemberId ON [mbw].[MemberIdentities] (MemberId);
GO

-- 3. MemberPlatformAccounts – account Shopee/TikTok ที่ผูกกับ member
CREATE TABLE [mbw].[MemberPlatformAccounts] (
    MemberPlatformAccountId BIGINT      IDENTITY(1,1) NOT NULL,
    MemberId                BIGINT      NOT NULL,
    PlatformType            NVARCHAR(20)  NOT NULL,    -- SHOPEE / TIKTOK
    ShopId                  INT          NOT NULL,     -- FK → mdw.Shops.Id
    PlatformAccountKey      NVARCHAR(200) NOT NULL,    -- buyer username หรือ userId บน platform
    PlatformAccountName     NVARCHAR(200) NULL,
    VerifiedStatus          NVARCHAR(20)  NOT NULL DEFAULT 'Pending', -- Pending / Verified / Rejected
    VerifiedAt              DATETIME2    NULL,
    VerifiedBy              NVARCHAR(100) NULL,
    LinkMethod              NVARCHAR(20)  NOT NULL DEFAULT 'MANUAL',  -- MANUAL / FORM / AUTO
    ConfidenceScore         DECIMAL(5,2) NULL,
    EffectiveFrom           DATETIME2    NULL,
    EffectiveTo             DATETIME2    NULL,
    IsPrimary               BIT          NOT NULL DEFAULT 0,
    CreatedAt               DATETIME2    NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_MemberPlatformAccounts PRIMARY KEY (MemberPlatformAccountId),
    CONSTRAINT FK_MemberPlatformAccounts_Members FOREIGN KEY (MemberId) REFERENCES [mbw].[Members](MemberId),
    CONSTRAINT FK_MemberPlatformAccounts_Shops FOREIGN KEY (ShopId) REFERENCES [mdw].[Shops](Id),
    CONSTRAINT UQ_MemberPlatformAccounts_PlatShopKey UNIQUE (PlatformType, ShopId, PlatformAccountKey)
);
GO

CREATE INDEX IX_MemberPlatformAccounts_MemberId ON [mbw].[MemberPlatformAccounts] (MemberId);
CREATE INDEX IX_MemberPlatformAccounts_PlatformKey ON [mbw].[MemberPlatformAccounts] (PlatformType, PlatformAccountKey);
GO

-- 4. MemberMappingRequests – คำขอ mapping (staging ก่อนอนุมัติ)
CREATE TABLE [mbw].[MemberMappingRequests] (
    RequestId             BIGINT         IDENTITY(1,1) NOT NULL,
    MemberId              BIGINT         NOT NULL,
    PlatformType          NVARCHAR(20)   NOT NULL,
    ShopId                INT            NULL,
    PlatformAccountKey    NVARCHAR(200)  NOT NULL,
    PlatformAccountName   NVARCHAR(200)  NULL,
    SourceType            NVARCHAR(30)   NOT NULL DEFAULT 'ADMIN', -- ADMIN / MEMBER_FORM / LINE_MESSAGE / IMPORT
    RequestStatus         NVARCHAR(20)   NOT NULL DEFAULT 'Pending', -- Pending / Approved / Rejected
    ConfidenceScore       DECIMAL(5,2)   NULL,
    ReviewedBy            NVARCHAR(100)  NULL,
    ReviewedAt            DATETIME2      NULL,
    ReviewNote            NVARCHAR(1000) NULL,
    CreatedAt             DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_MemberMappingRequests PRIMARY KEY (RequestId),
    CONSTRAINT FK_MemberMappingRequests_Members FOREIGN KEY (MemberId) REFERENCES [mbw].[Members](MemberId)
);
GO

CREATE INDEX IX_MemberMappingRequests_MemberId ON [mbw].[MemberMappingRequests] (MemberId);
CREATE INDEX IX_MemberMappingRequests_Status ON [mbw].[MemberMappingRequests] (RequestStatus, CreatedAt);
GO

-- 5. MemberMappingEvidence – หลักฐานประกอบการ mapping
CREATE TABLE [mbw].[MemberMappingEvidence] (
    EvidenceId       BIGINT         IDENTITY(1,1) NOT NULL,
    RequestId        BIGINT         NOT NULL,
    EvidenceType     NVARCHAR(30)   NOT NULL,          -- SCREENSHOT / ORDER_NO / RAW_MESSAGE / FORM_DATA
    EvidenceValue    NVARCHAR(MAX)  NULL,              -- text / URL / JSON
    CreatedAt        DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_MemberMappingEvidence PRIMARY KEY (EvidenceId),
    CONSTRAINT FK_MemberMappingEvidence_Requests FOREIGN KEY (RequestId) REFERENCES [mbw].[MemberMappingRequests](RequestId)
);
GO

CREATE INDEX IX_MemberMappingEvidence_RequestId ON [mbw].[MemberMappingEvidence] (RequestId);
GO

-- 6. FormSubmissions – แบบฟอร์มที่ member กรอก
CREATE TABLE [mbw].[FormSubmissions] (
    SubmissionId     BIGINT         IDENTITY(1,1) NOT NULL,
    MemberId         BIGINT         NOT NULL,
    FormType         NVARCHAR(30)   NOT NULL,          -- REGISTRATION / MAPPING / CLAIM
    FormDataJson     NVARCHAR(MAX)  NULL,
    ProcessStatus    NVARCHAR(20)   NOT NULL DEFAULT 'Pending', -- Pending / Processed / Failed
    ProcessedAt      DATETIME2      NULL,
    CreatedAt        DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_FormSubmissions PRIMARY KEY (SubmissionId),
    CONSTRAINT FK_FormSubmissions_Members FOREIGN KEY (MemberId) REFERENCES [mbw].[Members](MemberId)
);
GO

CREATE INDEX IX_FormSubmissions_MemberId ON [mbw].[FormSubmissions] (MemberId);
CREATE INDEX IX_FormSubmissions_Status ON [mbw].[FormSubmissions] (ProcessStatus, CreatedAt);
GO


-- ============================================================
-- กลุ่ม B: Order-Member Linking (3 ตาราง)
-- ============================================================

-- 7. OrderMemberLinks – ผูก order กับ member
CREATE TABLE [mbw].[OrderMemberLinks] (
    OrderMemberLinkId         BIGINT         IDENTITY(1,1) NOT NULL,
    UnifiedOrderId            BIGINT         NOT NULL,     -- FK → mdw.UnifiedOrders
    MemberId                  BIGINT         NOT NULL,
    MemberPlatformAccountId   BIGINT         NULL,         -- FK → mbw.MemberPlatformAccounts
    LinkMethod                NVARCHAR(30)   NOT NULL,     -- VERIFIED_ACCOUNT / CLAIM / COUPON
    LinkedAt                  DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    LinkedBy                  NVARCHAR(100)  NULL,

    CONSTRAINT PK_OrderMemberLinks PRIMARY KEY (OrderMemberLinkId),
    CONSTRAINT FK_OrderMemberLinks_UnifiedOrders FOREIGN KEY (UnifiedOrderId) REFERENCES [mdw].[UnifiedOrders](UnifiedOrderId),
    CONSTRAINT FK_OrderMemberLinks_Members FOREIGN KEY (MemberId) REFERENCES [mbw].[Members](MemberId),
    CONSTRAINT FK_OrderMemberLinks_PlatAccounts FOREIGN KEY (MemberPlatformAccountId) REFERENCES [mbw].[MemberPlatformAccounts](MemberPlatformAccountId)
);
GO

CREATE INDEX IX_OrderMemberLinks_UnifiedOrderId ON [mbw].[OrderMemberLinks] (UnifiedOrderId);
CREATE INDEX IX_OrderMemberLinks_MemberId ON [mbw].[OrderMemberLinks] (MemberId);
GO

-- 8. OrderClaims – claim order ย้อนหลัง (กรณีซื้อก่อนสมัคร)
CREATE TABLE [mbw].[OrderClaims] (
    ClaimId          BIGINT         IDENTITY(1,1) NOT NULL,
    MemberId         BIGINT         NOT NULL,
    UnifiedOrderId   BIGINT         NOT NULL,          -- FK → mdw.UnifiedOrders
    ClaimStatus      NVARCHAR(20)   NOT NULL DEFAULT 'Pending', -- Pending / Approved / Rejected
    EvidenceJson     NVARCHAR(MAX)  NULL,
    ReviewedBy       NVARCHAR(100)  NULL,
    ReviewedAt       DATETIME2      NULL,
    CreatedAt        DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_OrderClaims PRIMARY KEY (ClaimId),
    CONSTRAINT FK_OrderClaims_Members FOREIGN KEY (MemberId) REFERENCES [mbw].[Members](MemberId),
    CONSTRAINT FK_OrderClaims_UnifiedOrders FOREIGN KEY (UnifiedOrderId) REFERENCES [mdw].[UnifiedOrders](UnifiedOrderId)
);
GO

CREATE INDEX IX_OrderClaims_MemberId ON [mbw].[OrderClaims] (MemberId);
CREATE INDEX IX_OrderClaims_UnifiedOrderId ON [mbw].[OrderClaims] (UnifiedOrderId);
CREATE INDEX IX_OrderClaims_Status ON [mbw].[OrderClaims] (ClaimStatus, CreatedAt);
GO

-- 9. OrderStatusHistory – ประวัติเปลี่ยนสถานะ order
CREATE TABLE [mbw].[OrderStatusHistory] (
    StatusHistoryId  BIGINT         IDENTITY(1,1) NOT NULL,
    UnifiedOrderId   BIGINT         NOT NULL,          -- FK → mdw.UnifiedOrders
    OldStatus        NVARCHAR(40)   NULL,
    NewStatus        NVARCHAR(40)   NOT NULL,
    ChangedAt        DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    [Source]          NVARCHAR(20)   NOT NULL DEFAULT 'SYNC', -- SYNC / MANUAL / WEBHOOK

    CONSTRAINT PK_OrderStatusHistory PRIMARY KEY (StatusHistoryId),
    CONSTRAINT FK_OrderStatusHistory_UnifiedOrders FOREIGN KEY (UnifiedOrderId) REFERENCES [mdw].[UnifiedOrders](UnifiedOrderId)
);
GO

CREATE INDEX IX_OrderStatusHistory_UnifiedOrderId ON [mbw].[OrderStatusHistory] (UnifiedOrderId);
CREATE INDEX IX_OrderStatusHistory_ChangedAt ON [mbw].[OrderStatusHistory] (ChangedAt);
GO


-- ============================================================
-- กลุ่ม C: Loyalty / Point Engine (5 ตาราง)
-- ============================================================

-- 10. PointPolicies – กติกาการคิดแต้ม
CREATE TABLE [mbw].[PointPolicies] (
    PolicyId          INT            IDENTITY(1,1) NOT NULL,
    PolicyName        NVARCHAR(200)  NOT NULL,
    PlatformType      NVARCHAR(20)   NOT NULL DEFAULT 'ALL', -- ALL / SHOPEE / TIKTOK
    EarnFormula       NVARCHAR(50)   NOT NULL DEFAULT 'AMOUNT_DIV_100',
    EarnRate          DECIMAL(10,4)  NOT NULL DEFAULT 1.0,    -- เช่น 1 point ต่อ 100 บาท
    MinOrderAmount    DECIMAL(12,2)  NULL,                    -- ยอดขั้นต่ำที่ให้แต้ม
    EligibleStatuses  NVARCHAR(500)  NULL,                    -- JSON array เช่น ["COMPLETED","DELIVERED"]
    EffectiveFrom     DATETIME2      NOT NULL,
    EffectiveTo       DATETIME2      NULL,
    IsActive          BIT            NOT NULL DEFAULT 1,
    CreatedBy         NVARCHAR(100)  NULL,
    CreatedAt         DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_PointPolicies PRIMARY KEY (PolicyId)
);
GO

CREATE INDEX IX_PointPolicies_Active ON [mbw].[PointPolicies] (IsActive, EffectiveFrom, EffectiveTo);
GO

-- 11. PointAccounts – สรุปยอดคงเหลือแต้มต่อ member
CREATE TABLE [mbw].[PointAccounts] (
    PointAccountId   BIGINT         IDENTITY(1,1) NOT NULL,
    MemberId         BIGINT         NOT NULL,
    AvailablePoints  INT            NOT NULL DEFAULT 0,
    ReservedPoints   INT            NOT NULL DEFAULT 0,
    TotalEarned      INT            NOT NULL DEFAULT 0,
    TotalBurned      INT            NOT NULL DEFAULT 0,
    TotalExpired     INT            NOT NULL DEFAULT 0,
    LastActivityAt   DATETIME2      NULL,
    UpdatedAt        DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_PointAccounts PRIMARY KEY (PointAccountId),
    CONSTRAINT FK_PointAccounts_Members FOREIGN KEY (MemberId) REFERENCES [mbw].[Members](MemberId),
    CONSTRAINT UQ_PointAccounts_MemberId UNIQUE (MemberId)
);
GO

-- 12. PointLedger – transaction แต้มทั้งหมด (ตารางสำคัญที่สุด)
CREATE TABLE [mbw].[PointLedger] (
    LedgerId         BIGINT         IDENTITY(1,1) NOT NULL,
    MemberId         BIGINT         NOT NULL,
    TxnType          NVARCHAR(20)   NOT NULL,          -- EARN / RESERVE / RELEASE / BURN / EARN_REVERSAL / EXPIRE / ADJUST
    Points           INT            NOT NULL,           -- + earn, - burn (เก็บทั้ง +/-)
    BalanceAfter     INT            NOT NULL,           -- ยอดคงเหลือหลังรายการนี้
    PolicyId         INT            NULL,               -- FK → PointPolicies (nullable สำหรับ manual adjust)
    RefType          NVARCHAR(30)   NULL,               -- ORDER / REDEMPTION / ADJUSTMENT / CLAIM
    RefId            NVARCHAR(100)  NULL,               -- PK ของ entity ที่อ้างอิง
    OccurredAt       DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy        NVARCHAR(100)  NULL,
    IdempotencyKey   NVARCHAR(200)  NULL,               -- ป้องกันลงซ้ำ
    CreatedAt        DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_PointLedger PRIMARY KEY (LedgerId),
    CONSTRAINT FK_PointLedger_Members FOREIGN KEY (MemberId) REFERENCES [mbw].[Members](MemberId),
    CONSTRAINT FK_PointLedger_Policies FOREIGN KEY (PolicyId) REFERENCES [mbw].[PointPolicies](PolicyId),
    CONSTRAINT UQ_PointLedger_IdempotencyKey UNIQUE (IdempotencyKey)
);
GO

CREATE INDEX IX_PointLedger_MemberId ON [mbw].[PointLedger] (MemberId, OccurredAt);
CREATE INDEX IX_PointLedger_TxnType ON [mbw].[PointLedger] (TxnType, OccurredAt);
CREATE INDEX IX_PointLedger_RefId ON [mbw].[PointLedger] (RefType, RefId);
GO

-- 13. PointExpirations – แผนหมดอายุแต้ม (ถ้าเปิดใช้)
CREATE TABLE [mbw].[PointExpirations] (
    ExpirationId     BIGINT         IDENTITY(1,1) NOT NULL,
    MemberId         BIGINT         NOT NULL,
    SourceLedgerId   BIGINT         NOT NULL,          -- FK → PointLedger (earn ต้นทาง)
    OriginalPoints   INT            NOT NULL,
    RemainingPoints  INT            NOT NULL,
    ExpiresAt        DATETIME2      NOT NULL,
    ExpiredAt        DATETIME2      NULL,               -- จริงๆ หมดอายุเมื่อไร
    [Status]         NVARCHAR(20)   NOT NULL DEFAULT 'Active', -- Active / Expired / Consumed

    CONSTRAINT PK_PointExpirations PRIMARY KEY (ExpirationId),
    CONSTRAINT FK_PointExpirations_Members FOREIGN KEY (MemberId) REFERENCES [mbw].[Members](MemberId),
    CONSTRAINT FK_PointExpirations_Ledger FOREIGN KEY (SourceLedgerId) REFERENCES [mbw].[PointLedger](LedgerId)
);
GO

CREATE INDEX IX_PointExpirations_MemberId ON [mbw].[PointExpirations] (MemberId, ExpiresAt);
CREATE INDEX IX_PointExpirations_Status ON [mbw].[PointExpirations] ([Status], ExpiresAt);
GO

-- 14. PointAdjustments – ปรับแต้มด้วยมือ
CREATE TABLE [mbw].[PointAdjustments] (
    AdjustmentId     BIGINT         IDENTITY(1,1) NOT NULL,
    MemberId         BIGINT         NOT NULL,
    AdjustType       NVARCHAR(10)   NOT NULL,          -- ADD / DEDUCT
    Points           INT            NOT NULL,
    Reason           NVARCHAR(500)  NOT NULL,
    ApprovedBy       NVARCHAR(100)  NULL,
    ApprovedAt       DATETIME2      NULL,
    LedgerId         BIGINT         NULL,               -- FK → PointLedger (record ที่สร้างตาม)
    CreatedBy        NVARCHAR(100)  NOT NULL,
    CreatedAt        DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_PointAdjustments PRIMARY KEY (AdjustmentId),
    CONSTRAINT FK_PointAdjustments_Members FOREIGN KEY (MemberId) REFERENCES [mbw].[Members](MemberId),
    CONSTRAINT FK_PointAdjustments_Ledger FOREIGN KEY (LedgerId) REFERENCES [mbw].[PointLedger](LedgerId)
);
GO

CREATE INDEX IX_PointAdjustments_MemberId ON [mbw].[PointAdjustments] (MemberId, CreatedAt);
GO


-- ============================================================
-- กลุ่ม D: Reward / Redemption (4 ตาราง)
-- ============================================================

-- 15. RewardCatalog – reward ที่เปิดให้แลก
CREATE TABLE [mbw].[RewardCatalog] (
    RewardId         INT            IDENTITY(1,1) NOT NULL,
    RewardName       NVARCHAR(200)  NOT NULL,
    [Description]    NVARCHAR(1000) NULL,
    PlatformType     NVARCHAR(20)   NULL,              -- SHOPEE / TIKTOK / ALL / NULL
    RewardType       NVARCHAR(30)   NOT NULL DEFAULT 'DISCOUNT_CODE', -- DISCOUNT_CODE / FREE_ITEM / VOUCHER
    PointsCost       INT            NOT NULL,
    StockTotal       INT            NOT NULL DEFAULT 0,
    StockRemaining   INT            NOT NULL DEFAULT 0,
    IsActive         BIT            NOT NULL DEFAULT 1,
    ValidFrom        DATETIME2      NULL,
    ValidTo          DATETIME2      NULL,
    ImageUrl         NVARCHAR(500)  NULL,
    CreatedAt        DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_RewardCatalog PRIMARY KEY (RewardId)
);
GO

CREATE INDEX IX_RewardCatalog_Active ON [mbw].[RewardCatalog] (IsActive, ValidFrom, ValidTo);
GO

-- 16. RewardCodes – code ส่วนลดจริง
CREATE TABLE [mbw].[RewardCodes] (
    RewardCodeId     BIGINT         IDENTITY(1,1) NOT NULL,
    RewardId         INT            NOT NULL,
    Code             NVARCHAR(100)  NOT NULL,
    [Status]         NVARCHAR(20)   NOT NULL DEFAULT 'Available', -- Available / Reserved / Issued / Used / Expired / Voided
    ReservedAt       DATETIME2      NULL,
    IssuedAt         DATETIME2      NULL,
    UsedAt           DATETIME2      NULL,
    ExpiredAt        DATETIME2      NULL,
    RedemptionId     BIGINT         NULL,              -- FK → RewardRedemptions (set เมื่อ issue)

    CONSTRAINT PK_RewardCodes PRIMARY KEY (RewardCodeId),
    CONSTRAINT FK_RewardCodes_Catalog FOREIGN KEY (RewardId) REFERENCES [mbw].[RewardCatalog](RewardId)
);
GO

CREATE INDEX IX_RewardCodes_RewardId ON [mbw].[RewardCodes] (RewardId, [Status]);
CREATE INDEX IX_RewardCodes_Code ON [mbw].[RewardCodes] (Code);
GO

-- 17. RewardRedemptions – การแลกแต้มแต่ละครั้ง
CREATE TABLE [mbw].[RewardRedemptions] (
    RedemptionId     BIGINT         IDENTITY(1,1) NOT NULL,
    MemberId         BIGINT         NOT NULL,
    RewardId         INT            NOT NULL,
    RewardCodeId     BIGINT         NULL,              -- set หลัง code ถูก assign
    PointsSpent      INT            NOT NULL,
    [Status]         NVARCHAR(20)   NOT NULL DEFAULT 'Reserved', -- Reserved / Completed / Cancelled / Failed
    ReservedAt       DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    CompletedAt      DATETIME2      NULL,
    CancelledAt      DATETIME2      NULL,
    LedgerId         BIGINT         NULL,              -- FK → PointLedger (RESERVE / BURN entry)
    CreatedAt        DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_RewardRedemptions PRIMARY KEY (RedemptionId),
    CONSTRAINT FK_RewardRedemptions_Members FOREIGN KEY (MemberId) REFERENCES [mbw].[Members](MemberId),
    CONSTRAINT FK_RewardRedemptions_Catalog FOREIGN KEY (RewardId) REFERENCES [mbw].[RewardCatalog](RewardId),
    CONSTRAINT FK_RewardRedemptions_Code FOREIGN KEY (RewardCodeId) REFERENCES [mbw].[RewardCodes](RewardCodeId),
    CONSTRAINT FK_RewardRedemptions_Ledger FOREIGN KEY (LedgerId) REFERENCES [mbw].[PointLedger](LedgerId)
);
GO

CREATE INDEX IX_RewardRedemptions_MemberId ON [mbw].[RewardRedemptions] (MemberId, CreatedAt);
CREATE INDEX IX_RewardRedemptions_Status ON [mbw].[RewardRedemptions] ([Status], CreatedAt);
GO

-- 18. OutboxMessages – ประวัติการส่ง code/notification ผ่าน LINE
CREATE TABLE [mbw].[OutboxMessages] (
    OutboxId         BIGINT         IDENTITY(1,1) NOT NULL,
    MemberId         BIGINT         NOT NULL,
    MessageType      NVARCHAR(30)   NOT NULL,          -- REWARD_CODE / NOTIFICATION / REMINDER
    Channel          NVARCHAR(20)   NOT NULL DEFAULT 'LINE', -- LINE / SMS / EMAIL
    Payload          NVARCHAR(MAX)  NULL,               -- JSON body ของ message
    [Status]         NVARCHAR(20)   NOT NULL DEFAULT 'Pending', -- Pending / Sent / Failed / Retrying
    SentAt           DATETIME2      NULL,
    RetryCount       INT            NOT NULL DEFAULT 0,
    LastError        NVARCHAR(1000) NULL,
    DeliveryRef      NVARCHAR(200)  NULL,               -- reference จาก LINE API
    CreatedAt        DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_OutboxMessages PRIMARY KEY (OutboxId),
    CONSTRAINT FK_OutboxMessages_Members FOREIGN KEY (MemberId) REFERENCES [mbw].[Members](MemberId)
);
GO

CREATE INDEX IX_OutboxMessages_MemberId ON [mbw].[OutboxMessages] (MemberId);
CREATE INDEX IX_OutboxMessages_Status ON [mbw].[OutboxMessages] ([Status], CreatedAt);
GO


-- ============================================================
-- กลุ่ม E: LINE Integration (1 ตาราง)
-- ============================================================

-- 19. LineMessageInbox – เก็บข้อความ LINE สำหรับ parsing/auto-mapping
CREATE TABLE [mbw].[LineMessageInbox] (
    MessageEventId   BIGINT         IDENTITY(1,1) NOT NULL,
    MemberId         BIGINT         NULL,              -- nullable: อาจยังระบุ member ไม่ได้
    LineUserId       NVARCHAR(200)  NOT NULL,
    MessageType      NVARCHAR(20)   NOT NULL,          -- TEXT / IMAGE / STICKER
    RawPayload       NVARCHAR(MAX)  NULL,               -- JSON ข้อความต้นฉบับ
    ProcessStatus    NVARCHAR(20)   NOT NULL DEFAULT 'New', -- New / Processed / Ignored / Failed
    ExtractedDataJson NVARCHAR(MAX) NULL,               -- ผลลัพธ์จาก parsing
    ProcessedAt      DATETIME2      NULL,
    CreatedAt        DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_LineMessageInbox PRIMARY KEY (MessageEventId),
    CONSTRAINT FK_LineMessageInbox_Members FOREIGN KEY (MemberId) REFERENCES [mbw].[Members](MemberId)
);
GO

CREATE INDEX IX_LineMessageInbox_LineUserId ON [mbw].[LineMessageInbox] (LineUserId, CreatedAt);
CREATE INDEX IX_LineMessageInbox_Status ON [mbw].[LineMessageInbox] (ProcessStatus, CreatedAt);
GO


-- ============================================================
-- กลุ่ม F: Admin / Integration Monitoring (4 ตาราง)
-- ============================================================

-- 20. AdminRoles – กำหนดสิทธิ์ role
CREATE TABLE [mbw].[AdminRoles] (
    RoleId           INT            IDENTITY(1,1) NOT NULL,
    RoleName         NVARCHAR(50)   NOT NULL,          -- MappingAdmin / PointAdmin / Support / SuperAdmin
    Permissions      NVARCHAR(MAX)  NULL,               -- JSON array ของ permissions
    CreatedAt        DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_AdminRoles PRIMARY KEY (RoleId),
    CONSTRAINT UQ_AdminRoles_RoleName UNIQUE (RoleName)
);
GO

-- 21. AdminAuditLogs – บันทึกทุกการกระทำของ admin
CREATE TABLE [mbw].[AdminAuditLogs] (
    AuditId          BIGINT         IDENTITY(1,1) NOT NULL,
    UserId           INT            NOT NULL,          -- FK → dbo.Users.Id
    ActionType       NVARCHAR(50)   NOT NULL,          -- APPROVE_MAPPING / REJECT_MAPPING / ADJUST_POINTS / VOID_CODE
    EntityType       NVARCHAR(50)   NULL,              -- Members / MemberPlatformAccounts / PointLedger / RewardCodes
    EntityId         NVARCHAR(100)  NULL,              -- PK ของ entity ที่ถูก action
    OldValue         NVARCHAR(MAX)  NULL,               -- JSON ค่าเดิม
    NewValue         NVARCHAR(MAX)  NULL,               -- JSON ค่าใหม่
    IpAddress        NVARCHAR(50)   NULL,
    CreatedAt        DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_AdminAuditLogs PRIMARY KEY (AuditId),
    CONSTRAINT FK_AdminAuditLogs_Users FOREIGN KEY (UserId) REFERENCES [dbo].[Users](Id)
);
GO

CREATE INDEX IX_AdminAuditLogs_UserId ON [mbw].[AdminAuditLogs] (UserId, CreatedAt);
CREATE INDEX IX_AdminAuditLogs_ActionType ON [mbw].[AdminAuditLogs] (ActionType, CreatedAt);
CREATE INDEX IX_AdminAuditLogs_Entity ON [mbw].[AdminAuditLogs] (EntityType, EntityId);
GO

-- 22. WebhookInbox – เก็บ webhook ที่รับเข้ามา (idempotency + replay)
CREATE TABLE [mbw].[WebhookInbox] (
    InboxId          BIGINT         IDENTITY(1,1) NOT NULL,
    [Source]         NVARCHAR(20)   NOT NULL,           -- LINE / SHOPEE / TIKTOK
    EventType        NVARCHAR(50)   NOT NULL,
    EventKey         NVARCHAR(200)  NOT NULL,           -- UNIQUE สำหรับ idempotency
    RawPayload       NVARCHAR(MAX)  NULL,
    ProcessStatus    NVARCHAR(20)   NOT NULL DEFAULT 'New', -- New / Processed / Failed / Skipped
    ProcessedAt      DATETIME2      NULL,
    RetryCount       INT            NOT NULL DEFAULT 0,
    CreatedAt        DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_WebhookInbox PRIMARY KEY (InboxId),
    CONSTRAINT UQ_WebhookInbox_EventKey UNIQUE (EventKey)
);
GO

CREATE INDEX IX_WebhookInbox_Source ON [mbw].[WebhookInbox] ([Source], EventType, CreatedAt);
CREATE INDEX IX_WebhookInbox_Status ON [mbw].[WebhookInbox] (ProcessStatus, CreatedAt);
GO

-- 23. ApiCallLogs – เก็บ log การเรียก external API
CREATE TABLE [mbw].[ApiCallLogs] (
    ApiLogId         BIGINT         IDENTITY(1,1) NOT NULL,
    ApiName          NVARCHAR(100)  NOT NULL,           -- เช่น Shopee.GetOrder, TikTok.IssueCoupon
    RequestMethod    NVARCHAR(10)   NOT NULL,           -- GET / POST / PUT / DELETE
    RequestUrl       NVARCHAR(500)  NOT NULL,
    RequestRef       NVARCHAR(200)  NULL,               -- order id, member id ฯลฯ
    RequestPayload   NVARCHAR(MAX)  NULL,
    ResponseStatus   INT            NULL,               -- HTTP status code
    ResponsePayload  NVARCHAR(MAX)  NULL,
    DurationMs       INT            NULL,
    ErrorMessage     NVARCHAR(1000) NULL,
    CreatedAt        DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_ApiCallLogs PRIMARY KEY (ApiLogId)
);
GO

CREATE INDEX IX_ApiCallLogs_ApiName ON [mbw].[ApiCallLogs] (ApiName, CreatedAt);
CREATE INDEX IX_ApiCallLogs_RequestRef ON [mbw].[ApiCallLogs] (RequestRef) WHERE RequestRef IS NOT NULL;
GO


-- ============================================================
-- Seed Data: default roles
-- ============================================================
INSERT INTO [mbw].[AdminRoles] (RoleName, Permissions) VALUES
    ('SuperAdmin',    '["*"]'),
    ('MappingAdmin',  '["mapping.view","mapping.approve","mapping.reject","member.view"]'),
    ('PointAdmin',    '["point.view","point.adjust","member.view","order.view"]'),
    ('Support',       '["member.view","order.view","point.view","reward.view"]');
GO


PRINT '========================================';
PRINT '  Schema [mbw] created successfully!';
PRINT '  23 tables + 4 default roles';
PRINT '========================================';
GO
