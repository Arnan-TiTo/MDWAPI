-- ============================================================
-- MBW (Member Wallet) - Create Schema & All Tables
-- Database: SQL Server
-- ============================================================

-- Schema
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'mbw')
    EXEC('CREATE SCHEMA mbw');
GO

-- ============================================================
-- MASTER / LOOKUP TABLES
-- ============================================================

-- TierMasters
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'mbw' AND t.name = 'TierMasters')
CREATE TABLE mbw.TierMasters (
    TierId          INT             NOT NULL IDENTITY(1,1),
    TierCode        NVARCHAR(50)    NOT NULL,
    TierName        NVARCHAR(200)   NOT NULL,
    MinPoints       DECIMAL(18,2)   NOT NULL DEFAULT 0,
    MaxPoints       DECIMAL(18,2)   NULL,
    MinSpendAmount  DECIMAL(18,2)   NOT NULL DEFAULT 0,
    MaxSpendAmount  DECIMAL(18,2)   NULL,
    TierColor       NVARCHAR(20)    NULL,
    IconUrl         NVARCHAR(500)   NULL,
    Description     NVARCHAR(1000)  NULL,
    SortOrder       INT             NOT NULL DEFAULT 0,
    IsActive        BIT             NOT NULL DEFAULT 1,
    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt       DATETIME2       NULL,
    CONSTRAINT PK_TierMasters PRIMARY KEY (TierId),
    CONSTRAINT UQ_TierMasters_TierCode UNIQUE (TierCode)
);
GO

-- MemberChannels
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'mbw' AND t.name = 'MemberChannels')
CREATE TABLE mbw.MemberChannels (
    ChannelId       INT             NOT NULL IDENTITY(1,1),
    ChannelCode     NVARCHAR(50)    NOT NULL,
    ChannelName     NVARCHAR(200)   NOT NULL,
    Description     NVARCHAR(500)   NULL,
    IsActive        BIT             NOT NULL DEFAULT 1,
    SortOrder       INT             NOT NULL DEFAULT 0,
    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt       DATETIME2       NULL,
    CONSTRAINT PK_MemberChannels PRIMARY KEY (ChannelId),
    CONSTRAINT UQ_MemberChannels_ChannelCode UNIQUE (ChannelCode)
);
GO

-- RegistrationProductOptions
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'mbw' AND t.name = 'RegistrationProductOptions')
CREATE TABLE mbw.RegistrationProductOptions (
    OptionId            INT             NOT NULL IDENTITY(1,1),
    OptionCode          NVARCHAR(50)    NOT NULL,
    OptionName          NVARCHAR(200)   NOT NULL,
    IsAllowOtherText    BIT             NOT NULL DEFAULT 0,
    IsActive            BIT             NOT NULL DEFAULT 1,
    SortOrder           INT             NOT NULL DEFAULT 0,
    CreatedAt           DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt           DATETIME2       NULL,
    CONSTRAINT PK_RegistrationProductOptions PRIMARY KEY (OptionId),
    CONSTRAINT UQ_RegistrationProductOptions_Code UNIQUE (OptionCode)
);
GO

-- PointPolicies
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'mbw' AND t.name = 'PointPolicies')
CREATE TABLE mbw.PointPolicies (
    PolicyId            INT             NOT NULL IDENTITY(1,1),
    PolicyName          NVARCHAR(200)   NOT NULL,
    PlatformType        NVARCHAR(20)    NOT NULL DEFAULT 'ALL',
    EarnFormula         NVARCHAR(50)    NOT NULL DEFAULT 'AMOUNT_DIV_100',
    EarnRate            DECIMAL(10,4)   NOT NULL DEFAULT 1.0,
    MinOrderAmount      DECIMAL(12,2)   NULL,
    EligibleStatuses    NVARCHAR(500)   NULL,
    ExpiryDays          INT             NULL,
    EffectiveFrom       DATETIME2       NOT NULL,
    EffectiveTo         DATETIME2       NULL,
    IsActive            BIT             NOT NULL DEFAULT 1,
    CreatedBy           NVARCHAR(100)   NULL,
    CreatedAt           DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_PointPolicies PRIMARY KEY (PolicyId)
);
GO

-- ContentDocuments
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'mbw' AND t.name = 'ContentDocuments')
CREATE TABLE mbw.ContentDocuments (
    DocumentId      BIGINT          NOT NULL IDENTITY(1,1),
    DocumentType    NVARCHAR(50)    NOT NULL,   -- TERMS, PRIVACY, POLICY
    VersionNo       NVARCHAR(30)    NOT NULL,
    LanguageCode    NVARCHAR(10)    NOT NULL DEFAULT 'th',
    Title           NVARCHAR(300)   NOT NULL,
    ContentHtml     NVARCHAR(MAX)   NOT NULL,
    ContentText     NVARCHAR(MAX)   NULL,
    IsActive        BIT             NOT NULL DEFAULT 1,
    EffectiveFrom   DATETIME2       NOT NULL,
    EffectiveTo     DATETIME2       NULL,
    PublishedAt     DATETIME2       NOT NULL,
    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt       DATETIME2       NULL,
    CONSTRAINT PK_ContentDocuments PRIMARY KEY (DocumentId)
);
CREATE INDEX IX_ContentDocuments_Type_Lang_Active ON mbw.ContentDocuments (DocumentType, LanguageCode, IsActive);
GO

-- LineOaConfigs
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'mbw' AND t.name = 'LineOaConfigs')
CREATE TABLE mbw.LineOaConfigs (
    LineOaConfigId      INT             NOT NULL IDENTITY(1,1),
    CompanysId          INT             NOT NULL,
    LineOaName          NVARCHAR(200)   NOT NULL,
    LoginChannelId      NVARCHAR(50)    NULL,
    LoginChannelSecret  NVARCHAR(100)   NULL,
    LoginCallbackUrl    NVARCHAR(500)   NULL,
    MsgChannelSecret    NVARCHAR(100)   NULL,
    MsgChannelToken     NVARCHAR(500)   NOT NULL,
    LiffId              NVARCHAR(50)    NULL,
    IsActive            BIT             NOT NULL DEFAULT 1,
    CreatedAt           DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt           DATETIME2       NULL,
    CONSTRAINT PK_LineOaConfigs PRIMARY KEY (LineOaConfigId)
);
CREATE INDEX IX_LineOaConfigs_CompanysId ON mbw.LineOaConfigs (CompanysId);
GO

-- ============================================================
-- MEMBER CORE TABLE
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'mbw' AND t.name = 'Members')
CREATE TABLE mbw.Members (
    MemberId            BIGINT          NOT NULL IDENTITY(1,1),
    MemberCode          NVARCHAR(50)    NOT NULL,
    DisplayName         NVARCHAR(200)   NULL,
    NamePrefix          NVARCHAR(50)    NULL,
    Phone               NVARCHAR(20)    NULL,
    Email               NVARCHAR(200)   NULL,
    MemberType          NVARCHAR(50)    NULL,
    FirstName           NVARCHAR(100)   NULL,
    LastName            NVARCHAR(100)   NULL,
    BirthDate           DATE            NULL,
    Age                 INT             NULL,
    Gender              NVARCHAR(20)    NULL,
    Address             NVARCHAR(500)   NULL,
    Subdistrict         NVARCHAR(100)   NULL,
    District            NVARCHAR(100)   NULL,
    Province            NVARCHAR(100)   NULL,
    ZipCode             NVARCHAR(20)    NULL,
    MembershipTier      NVARCHAR(100)   NULL,
    Tags                NVARCHAR(1000)  NULL,
    Branch              NVARCHAR(100)   NULL,
    PointsForTier       DECIMAL(18,2)   NOT NULL DEFAULT 0,
    UsageCount          INT             NOT NULL DEFAULT 0,
    LastActiveAt        DATETIME2       NULL,
    LastActiveDays      INT             NULL,
    Status              NVARCHAR(20)    NOT NULL DEFAULT 'Active',
    ConsentAccepted     BIT             NOT NULL DEFAULT 0,
    ConsentedAt         DATETIME2       NULL,
    RegisteredAt        DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    CompanysId          INT             NULL,
    RegisterChannelId   INT             NULL,
    CurrentTierId       INT             NULL,
    PreferredLanguage   NVARCHAR(10)    NULL,
    PhoneCountryCode    NVARCHAR(10)    NULL,
    HowYouKnowMe        NVARCHAR(1000)  NULL,
    CreatedAt           DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt           DATETIME2       NULL,
    CONSTRAINT PK_Members PRIMARY KEY (MemberId),
    CONSTRAINT UQ_Members_MemberCode UNIQUE (MemberCode),
    CONSTRAINT FK_Members_TierMasters FOREIGN KEY (CurrentTierId) REFERENCES mbw.TierMasters (TierId) ON DELETE SET NULL,
    CONSTRAINT FK_Members_MemberChannels FOREIGN KEY (RegisterChannelId) REFERENCES mbw.MemberChannels (ChannelId) ON DELETE SET NULL
);
CREATE INDEX IX_Members_Phone ON mbw.Members (Phone) WHERE Phone IS NOT NULL;
CREATE INDEX IX_Members_Email ON mbw.Members (Email) WHERE Email IS NOT NULL;
CREATE INDEX IX_Members_Status ON mbw.Members (Status);
GO

IF COL_LENGTH('mbw.Members', 'NamePrefix') IS NULL
    ALTER TABLE mbw.Members ADD NamePrefix NVARCHAR(50) NULL;
GO

-- ============================================================
-- MEMBER IDENTITY & PLATFORM ACCOUNTS
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'mbw' AND t.name = 'MemberIdentities')
CREATE TABLE mbw.MemberIdentities (
    MemberIdentityId    BIGINT          NOT NULL IDENTITY(1,1),
    MemberId            BIGINT          NOT NULL,
    ProviderType        NVARCHAR(30)    NOT NULL,   -- LINE, GOOGLE, FACEBOOK
    ProviderUserKey     NVARCHAR(200)   NOT NULL,
    DisplayName         NVARCHAR(200)   NULL,
    PictureUrl          NVARCHAR(500)   NULL,
    CompanysId          INT             NULL,
    LinkedAt            DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    IsActive            BIT             NOT NULL DEFAULT 1,
    CONSTRAINT PK_MemberIdentities PRIMARY KEY (MemberIdentityId),
    CONSTRAINT UQ_MemberIdentities_Provider UNIQUE (ProviderType, ProviderUserKey),
    CONSTRAINT FK_MemberIdentities_Members FOREIGN KEY (MemberId) REFERENCES mbw.Members (MemberId)
);
CREATE INDEX IX_MemberIdentities_MemberId ON mbw.MemberIdentities (MemberId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'mbw' AND t.name = 'MemberPlatformAccounts')
CREATE TABLE mbw.MemberPlatformAccounts (
    MemberPlatformAccountId BIGINT          NOT NULL IDENTITY(1,1),
    MemberId                BIGINT          NOT NULL,
    PlatformType            NVARCHAR(20)    NOT NULL,   -- SHOPEE, LAZADA, TIKTOK
    ShopId                  INT             NULL,
    PlatformAccountKey      NVARCHAR(200)   NOT NULL,
    PlatformAccountName     NVARCHAR(200)   NULL,
    VerifiedStatus          NVARCHAR(20)    NOT NULL DEFAULT 'Pending',
    VerifiedAt              DATETIME2       NULL,
    VerifiedBy              NVARCHAR(100)   NULL,
    LinkMethod              NVARCHAR(20)    NOT NULL DEFAULT 'MANUAL',
    ConfidenceScore         DECIMAL(5,2)    NULL,
    EffectiveFrom           DATETIME2       NULL,
    EffectiveTo             DATETIME2       NULL,
    IsPrimary               BIT             NOT NULL DEFAULT 0,
    CreatedAt               DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_MemberPlatformAccounts PRIMARY KEY (MemberPlatformAccountId),
    CONSTRAINT UQ_MemberPlatformAccounts UNIQUE (PlatformType, ShopId, PlatformAccountKey),
    CONSTRAINT FK_MemberPlatformAccounts_Members FOREIGN KEY (MemberId) REFERENCES mbw.Members (MemberId)
);
CREATE INDEX IX_MemberPlatformAccounts_MemberId ON mbw.MemberPlatformAccounts (MemberId);
GO

-- MemberMappingRequests
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'mbw' AND t.name = 'MemberMappingRequests')
CREATE TABLE mbw.MemberMappingRequests (
    RequestId               BIGINT          NOT NULL IDENTITY(1,1),
    MemberId                BIGINT          NOT NULL,
    PlatformType            NVARCHAR(20)    NOT NULL,
    ShopId                  INT             NULL,
    PlatformAccountKey      NVARCHAR(200)   NOT NULL,
    PlatformAccountName     NVARCHAR(200)   NULL,
    SourceType              NVARCHAR(30)    NOT NULL DEFAULT 'ADMIN',
    RequestStatus           NVARCHAR(20)    NOT NULL DEFAULT 'Pending',
    ConfidenceScore         DECIMAL(5,2)    NULL,
    ReviewedBy              NVARCHAR(100)   NULL,
    ReviewedAt              DATETIME2       NULL,
    ReviewNote              NVARCHAR(1000)  NULL,
    CreatedAt               DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_MemberMappingRequests PRIMARY KEY (RequestId),
    CONSTRAINT FK_MemberMappingRequests_Members FOREIGN KEY (MemberId) REFERENCES mbw.Members (MemberId)
);
CREATE INDEX IX_MemberMappingRequests_MemberId ON mbw.MemberMappingRequests (MemberId);
CREATE INDEX IX_MemberMappingRequests_Status_Created ON mbw.MemberMappingRequests (RequestStatus, CreatedAt);
GO

-- MemberMappingEvidence
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'mbw' AND t.name = 'MemberMappingEvidence')
CREATE TABLE mbw.MemberMappingEvidence (
    EvidenceId      BIGINT          NOT NULL IDENTITY(1,1),
    RequestId       BIGINT          NOT NULL,
    EvidenceType    NVARCHAR(30)    NOT NULL,
    EvidenceValue   NVARCHAR(MAX)   NULL,
    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_MemberMappingEvidence PRIMARY KEY (EvidenceId),
    CONSTRAINT FK_MemberMappingEvidence_Requests FOREIGN KEY (RequestId) REFERENCES mbw.MemberMappingRequests (RequestId)
);
CREATE INDEX IX_MemberMappingEvidence_RequestId ON mbw.MemberMappingEvidence (RequestId);
GO

-- ============================================================
-- POINTS SYSTEM
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'mbw' AND t.name = 'PointAccounts')
CREATE TABLE mbw.PointAccounts (
    PointAccountId  BIGINT      NOT NULL IDENTITY(1,1),
    MemberId        BIGINT      NOT NULL,
    AvailablePoints INT         NOT NULL DEFAULT 0,
    PendingPoints   INT         NOT NULL DEFAULT 0,
    ReservedPoints  INT         NOT NULL DEFAULT 0,
    TotalEarned     INT         NOT NULL DEFAULT 0,
    TotalBurned     INT         NOT NULL DEFAULT 0,
    TotalExpired    INT         NOT NULL DEFAULT 0,
    LastActivityAt  DATETIME2   NULL,
    UpdatedAt       DATETIME2   NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_PointAccounts PRIMARY KEY (PointAccountId),
    CONSTRAINT UQ_PointAccounts_MemberId UNIQUE (MemberId),
    CONSTRAINT FK_PointAccounts_Members FOREIGN KEY (MemberId) REFERENCES mbw.Members (MemberId)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'mbw' AND t.name = 'PointLedger')
CREATE TABLE mbw.PointLedger (
    LedgerId        BIGINT          NOT NULL IDENTITY(1,1),
    MemberId        BIGINT          NOT NULL,
    TxnType         NVARCHAR(20)    NOT NULL,   -- EARN, BURN, EXPIRE, ADJUST
    Points          INT             NOT NULL,
    BalanceAfter    INT             NOT NULL,
    PolicyId        INT             NULL,
    RefType         NVARCHAR(30)    NULL,
    RefId           NVARCHAR(100)   NULL,
    IsPending       BIT             NOT NULL DEFAULT 0,
    ReadyAt         DATETIME2       NULL,
    OccurredAt      DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy       NVARCHAR(100)   NULL,
    IdempotencyKey  NVARCHAR(200)   NULL,
    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_PointLedger PRIMARY KEY (LedgerId),
    CONSTRAINT UQ_PointLedger_IdempotencyKey UNIQUE (IdempotencyKey) WHERE IdempotencyKey IS NOT NULL
);
CREATE INDEX IX_PointLedger_Member_Occurred ON mbw.PointLedger (MemberId, OccurredAt);
CREATE INDEX IX_PointLedger_TxnType_Occurred ON mbw.PointLedger (TxnType, OccurredAt);
CREATE INDEX IX_PointLedger_RefType_RefId ON mbw.PointLedger (RefType, RefId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'mbw' AND t.name = 'PointExpirations')
CREATE TABLE mbw.PointExpirations (
    ExpirationId        BIGINT      NOT NULL IDENTITY(1,1),
    MemberId            BIGINT      NOT NULL,
    SourceLedgerId      BIGINT      NOT NULL,
    OriginalPoints      INT         NOT NULL,
    RemainingPoints     INT         NOT NULL,
    ExpiresAt           DATETIME2   NOT NULL,
    ExpiredAt           DATETIME2   NULL,
    Status              NVARCHAR(20) NOT NULL DEFAULT 'Active',    -- Active, Used, Expired, Cancelled
    CONSTRAINT PK_PointExpirations PRIMARY KEY (ExpirationId)
);
CREATE INDEX IX_PointExpirations_Member_Expires ON mbw.PointExpirations (MemberId, ExpiresAt);
CREATE INDEX IX_PointExpirations_Status_Expires ON mbw.PointExpirations (Status, ExpiresAt);
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'mbw' AND t.name = 'PointAdjustments')
CREATE TABLE mbw.PointAdjustments (
    AdjustmentId    BIGINT          NOT NULL IDENTITY(1,1),
    MemberId        BIGINT          NOT NULL,
    AdjustType      NVARCHAR(10)    NOT NULL,   -- ADD, DEDUCT
    Points          INT             NOT NULL,
    Reason          NVARCHAR(500)   NOT NULL,
    ApprovedBy      NVARCHAR(100)   NULL,
    ApprovedAt      DATETIME2       NULL,
    LedgerId        BIGINT          NULL,
    CreatedBy       NVARCHAR(100)   NOT NULL,
    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_PointAdjustments PRIMARY KEY (AdjustmentId)
);
CREATE INDEX IX_PointAdjustments_Member_Created ON mbw.PointAdjustments (MemberId, CreatedAt);
GO

-- ============================================================
-- REWARDS SYSTEM
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'mbw' AND t.name = 'RewardCatalog')
CREATE TABLE mbw.RewardCatalog (
    RewardId        INT             NOT NULL IDENTITY(1,1),
    RewardName      NVARCHAR(200)   NOT NULL,
    Description     NVARCHAR(1000)  NULL,
    PlatformType    NVARCHAR(20)    NULL,
    RewardType      NVARCHAR(30)    NOT NULL DEFAULT 'DISCOUNT_CODE',  -- DISCOUNT_CODE, PHYSICAL, VOUCHER
    PointsCost      INT             NOT NULL DEFAULT 0,
    StockTotal      INT             NOT NULL DEFAULT 0,
    StockRemaining  INT             NOT NULL DEFAULT 0,
    IsActive        BIT             NOT NULL DEFAULT 1,
    ValidFrom       DATETIME2       NULL,
    ValidTo         DATETIME2       NULL,
    ImageUrl        NVARCHAR(500)   NULL,
    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_RewardCatalog PRIMARY KEY (RewardId)
);
CREATE INDEX IX_RewardCatalog_Active_Valid ON mbw.RewardCatalog (IsActive, ValidFrom, ValidTo);
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'mbw' AND t.name = 'RewardCodes')
CREATE TABLE mbw.RewardCodes (
    RewardCodeId    BIGINT          NOT NULL IDENTITY(1,1),
    RewardId        INT             NOT NULL,
    Code            NVARCHAR(100)   NOT NULL,
    Status          NVARCHAR(20)    NOT NULL DEFAULT 'Available',   -- Available, Reserved, Issued, Used, Expired
    ReservedAt      DATETIME2       NULL,
    IssuedAt        DATETIME2       NULL,
    UsedAt          DATETIME2       NULL,
    ExpiredAt       DATETIME2       NULL,
    RedemptionId    BIGINT          NULL,
    CONSTRAINT PK_RewardCodes PRIMARY KEY (RewardCodeId),
    CONSTRAINT FK_RewardCodes_Catalog FOREIGN KEY (RewardId) REFERENCES mbw.RewardCatalog (RewardId)
);
CREATE INDEX IX_RewardCodes_RewardId_Status ON mbw.RewardCodes (RewardId, Status);
CREATE INDEX IX_RewardCodes_Code ON mbw.RewardCodes (Code);
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'mbw' AND t.name = 'RewardRedemptions')
CREATE TABLE mbw.RewardRedemptions (
    RedemptionId        BIGINT          NOT NULL IDENTITY(1,1),
    RedemptionCode      NVARCHAR(50)    NOT NULL,
    MemberId            BIGINT          NOT NULL,
    RewardId            INT             NOT NULL,
    RewardCodeId        BIGINT          NULL,
    RewardNameSnapshot  NVARCHAR(200)   NOT NULL,
    RewardTypeSnapshot  NVARCHAR(30)    NULL,
    PointsSpent         INT             NOT NULL DEFAULT 0,
    Status              NVARCHAR(20)    NOT NULL DEFAULT 'Reserved', -- Reserved, Completed, Cancelled, Used
    CouponCode          NVARCHAR(200)   NULL,
    QrPayload           NVARCHAR(MAX)   NULL,
    ReservedAt          DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    CompletedAt         DATETIME2       NULL,
    CancelledAt         DATETIME2       NULL,
    ExpiresAt           DATETIME2       NULL,
    UsedAt              DATETIME2       NULL,
    LedgerId            BIGINT          NULL,
    CreatedAt           DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_RewardRedemptions PRIMARY KEY (RedemptionId),
    CONSTRAINT UQ_RewardRedemptions_Code UNIQUE (RedemptionCode),
    CONSTRAINT FK_RewardRedemptions_Members FOREIGN KEY (MemberId) REFERENCES mbw.Members (MemberId),
    CONSTRAINT FK_RewardRedemptions_Catalog FOREIGN KEY (RewardId) REFERENCES mbw.RewardCatalog (RewardId)
);
CREATE INDEX IX_RewardRedemptions_Member_Created ON mbw.RewardRedemptions (MemberId, CreatedAt);
CREATE INDEX IX_RewardRedemptions_Status_Created ON mbw.RewardRedemptions (Status, CreatedAt);
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'mbw' AND t.name = 'RewardFulfillments')
CREATE TABLE mbw.RewardFulfillments (
    FulfillmentId       BIGINT          NOT NULL IDENTITY(1,1),
    RedemptionId        BIGINT          NOT NULL,
    FulfillmentType     NVARCHAR(30)    NOT NULL,   -- DIGITAL, PHYSICAL
    FulfillmentStatus   NVARCHAR(30)    NOT NULL,   -- Pending, Processing, Shipped, Delivered, Failed
    RecipientName       NVARCHAR(200)   NULL,
    Phone               NVARCHAR(30)    NULL,
    AddressSnapshot     NVARCHAR(1000)  NULL,
    CarrierName         NVARCHAR(100)   NULL,
    TrackingNo          NVARCHAR(100)   NULL,
    ShippedAt           DATETIME2       NULL,
    DeliveredAt         DATETIME2       NULL,
    CreatedAt           DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt           DATETIME2       NULL,
    CONSTRAINT PK_RewardFulfillments PRIMARY KEY (FulfillmentId),
    CONSTRAINT UQ_RewardFulfillments_Redemption UNIQUE (RedemptionId),
    CONSTRAINT FK_RewardFulfillments_Redemptions FOREIGN KEY (RedemptionId) REFERENCES mbw.RewardRedemptions (RedemptionId)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'mbw' AND t.name = 'RewardRedemptionHistories')
CREATE TABLE mbw.RewardRedemptionHistories (
    RedemptionHistoryId BIGINT          NOT NULL IDENTITY(1,1),
    RedemptionId        BIGINT          NOT NULL,
    OldStatus           NVARCHAR(30)    NULL,
    NewStatus           NVARCHAR(30)    NOT NULL,
    ChangedAt           DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    ChangedBy           NVARCHAR(100)   NULL,
    Remark              NVARCHAR(500)   NULL,
    CONSTRAINT PK_RewardRedemptionHistories PRIMARY KEY (RedemptionHistoryId),
    CONSTRAINT FK_RewardRedemptionHistories_Redemptions FOREIGN KEY (RedemptionId) REFERENCES mbw.RewardRedemptions (RedemptionId)
);
CREATE INDEX IX_RewardRedemptionHistories_Redemption_Changed ON mbw.RewardRedemptionHistories (RedemptionId, ChangedAt);
GO

-- ============================================================
-- TIER HISTORY
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'mbw' AND t.name = 'MemberTierHistories')
CREATE TABLE mbw.MemberTierHistories (
    MemberTierHistoryId BIGINT          NOT NULL IDENTITY(1,1),
    MemberId            BIGINT          NOT NULL,
    TierId              INT             NOT NULL,
    PreviousTierId      INT             NULL,
    TierPoints          DECIMAL(18,2)   NOT NULL DEFAULT 0,
    SpendAmount         DECIMAL(18,2)   NOT NULL DEFAULT 0,
    WindowStartDate     DATETIME2       NULL,
    WindowEndDate       DATETIME2       NULL,
    CalculatedAt        DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    Reason              NVARCHAR(500)   NULL,
    CreatedAt           DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_MemberTierHistories PRIMARY KEY (MemberTierHistoryId)
);
CREATE INDEX IX_MemberTierHistories_Member_Calculated ON mbw.MemberTierHistories (MemberId, CalculatedAt);
GO

-- ============================================================
-- FORMS & CONSENT
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'mbw' AND t.name = 'MemberRegistrationAnswers')
CREATE TABLE mbw.MemberRegistrationAnswers (
    AnswerId    BIGINT          NOT NULL IDENTITY(1,1),
    MemberId    BIGINT          NOT NULL,
    OptionId    INT             NOT NULL,
    OtherText   NVARCHAR(500)   NULL,
    CreatedAt   DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_MemberRegistrationAnswers PRIMARY KEY (AnswerId),
    CONSTRAINT UQ_MemberRegistrationAnswers UNIQUE (MemberId, OptionId),
    CONSTRAINT FK_MemberRegistrationAnswers_Members FOREIGN KEY (MemberId) REFERENCES mbw.Members (MemberId),
    CONSTRAINT FK_MemberRegistrationAnswers_Options FOREIGN KEY (OptionId) REFERENCES mbw.RegistrationProductOptions (OptionId)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'mbw' AND t.name = 'MemberConsentLogs')
CREATE TABLE mbw.MemberConsentLogs (
    ConsentLogId        BIGINT          NOT NULL IDENTITY(1,1),
    MemberId            BIGINT          NOT NULL,
    DocumentId          BIGINT          NOT NULL,
    AcceptedFlag        BIT             NOT NULL DEFAULT 0,
    AcceptedAt          DATETIME2       NOT NULL,
    AcceptedFromChannel NVARCHAR(30)    NOT NULL DEFAULT 'LIFF',
    AcceptedIp          NVARCHAR(50)    NULL,
    AcceptedUserAgent   NVARCHAR(1000)  NULL,
    CreatedAt           DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_MemberConsentLogs PRIMARY KEY (ConsentLogId)
);
CREATE INDEX IX_MemberConsentLogs_Member_Accepted ON mbw.MemberConsentLogs (MemberId, AcceptedAt);
GO

-- ============================================================
-- NOTIFICATIONS
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'mbw' AND t.name = 'MemberNotifications')
CREATE TABLE mbw.MemberNotifications (
    NotificationId  BIGINT          NOT NULL IDENTITY(1,1),
    MemberId        BIGINT          NOT NULL,
    NotificationType NVARCHAR(50)   NOT NULL,   -- POINT_EARN, POINT_EXPIRE, REWARD, TIER_UP, etc.
    Title           NVARCHAR(200)   NOT NULL,
    Message         NVARCHAR(1000)  NOT NULL,
    RefType         NVARCHAR(50)    NULL,
    RefId           NVARCHAR(100)   NULL,
    IsRead          BIT             NOT NULL DEFAULT 0,
    ReadAt          DATETIME2       NULL,
    DisplayUntil    DATETIME2       NULL,
    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_MemberNotifications PRIMARY KEY (NotificationId)
);
CREATE INDEX IX_MemberNotifications_Member_Created ON mbw.MemberNotifications (MemberId, CreatedAt);
GO

-- ============================================================
-- AUDIT
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'mbw' AND t.name = 'AuditLogs')
CREATE TABLE mbw.AuditLogs (
    AuditId         BIGINT          NOT NULL IDENTITY(1,1),
    Action          NVARCHAR(100)   NOT NULL,
    Description     NVARCHAR(1000)  NULL,
    PerformedBy     NVARCHAR(100)   NOT NULL,
    MemberId        BIGINT          NULL,
    PerformedAt     DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    IpAddress       NVARCHAR(45)    NULL,
    UserAgent       NVARCHAR(500)   NULL,
    CONSTRAINT PK_AuditLogs PRIMARY KEY (AuditId)
);
CREATE INDEX IX_AuditLogs_PerformedAt ON mbw.AuditLogs (PerformedAt);
CREATE INDEX IX_AuditLogs_MemberId ON mbw.AuditLogs (MemberId) WHERE MemberId IS NOT NULL;
GO

-- ============================================================
-- LINE MESSAGE INBOX
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'mbw' AND t.name = 'LineMessageInbox')
CREATE TABLE mbw.LineMessageInbox (
    MessageEventId      BIGINT          NOT NULL IDENTITY(1,1),
    MemberId            BIGINT          NULL,
    LineUserId          NVARCHAR(200)   NOT NULL,
    MessageType         NVARCHAR(20)    NOT NULL,   -- text, image, postback, follow
    RawPayload          NVARCHAR(MAX)   NULL,
    ProcessStatus       NVARCHAR(20)    NOT NULL DEFAULT 'New',    -- New, Processing, Done, Error
    ExtractedDataJson   NVARCHAR(MAX)   NULL,
    ProcessedAt         DATETIME2       NULL,
    CreatedAt           DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_LineMessageInbox PRIMARY KEY (MessageEventId)
);
CREATE INDEX IX_LineMessageInbox_LineUser_Created ON mbw.LineMessageInbox (LineUserId, CreatedAt);
CREATE INDEX IX_LineMessageInbox_Status_Created ON mbw.LineMessageInbox (ProcessStatus, CreatedAt);
GO

-- ============================================================
-- ORDER MEMBER LINKS
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'mbw' AND t.name = 'OrderMemberLinks')
CREATE TABLE mbw.OrderMemberLinks (
    OrderMemberLinkId       BIGINT          NOT NULL IDENTITY(1,1),
    UnifiedOrderId          BIGINT          NOT NULL,
    MemberId                BIGINT          NOT NULL,
    MemberPlatformAccountId BIGINT          NULL,
    LinkMethod              NVARCHAR(30)    NOT NULL,   -- AUTO, MANUAL, PHONE, EMAIL
    LinkedAt                DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    LinkedBy                NVARCHAR(100)   NULL,
    CONSTRAINT PK_OrderMemberLinks PRIMARY KEY (OrderMemberLinkId)
);
CREATE INDEX IX_OrderMemberLinks_UnifiedOrderId ON mbw.OrderMemberLinks (UnifiedOrderId);
CREATE INDEX IX_OrderMemberLinks_MemberId ON mbw.OrderMemberLinks (MemberId);
GO


PRINT 'All tables created successfully.';
