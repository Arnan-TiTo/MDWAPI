-- =============================================
-- Create New Tables for LIFF Member System
-- Part 1: Registration & Masters
-- =============================================

-- 1. MemberChannels
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

CREATE NONCLUSTERED INDEX IX_MemberChannels_IsActive_SortOrder ON VCINDW.mbw.MemberChannels (IsActive, SortOrder);


-- 2. RegistrationProductOptions
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

CREATE NONCLUSTERED INDEX IX_RegistrationProductOptions_IsActive_SortOrder ON VCINDW.mbw.RegistrationProductOptions (IsActive, SortOrder);


-- 3. MemberRegistrationAnswers
CREATE TABLE VCINDW.mbw.MemberRegistrationAnswers (
    AnswerId bigint IDENTITY(1,1) NOT NULL,
    MemberId bigint NOT NULL,
    OptionId int NOT NULL,
    OtherText nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    CreatedAt datetime2 DEFAULT sysutcdatetime() NOT NULL,
    CONSTRAINT PK_MemberRegistrationAnswers PRIMARY KEY (AnswerId),
    CONSTRAINT UQ_MemberRegistrationAnswers_MemberId_OptionId UNIQUE (MemberId, OptionId),
    CONSTRAINT FK_MemberRegistrationAnswers_Members FOREIGN KEY (MemberId) REFERENCES VCINDW.mbw.Members(MemberId),
    CONSTRAINT FK_MemberRegistrationAnswers_RegistrationProductOptions FOREIGN KEY (OptionId) REFERENCES VCINDW.mbw.RegistrationProductOptions(OptionId)
);

CREATE NONCLUSTERED INDEX IX_MemberRegistrationAnswers_MemberId ON VCINDW.mbw.MemberRegistrationAnswers (MemberId);


-- =============================================
-- Part 2: Legal & Consent
-- =============================================

-- 4. ContentDocuments
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
    CONSTRAINT UQ_ContentDocuments_DocumentType_VersionNo_LanguageCode UNIQUE (DocumentType, VersionNo, LanguageCode)
);

CREATE NONCLUSTERED INDEX IX_ContentDocuments_DocumentType_IsActive_PublishedAt ON VCINDW.mbw.ContentDocuments (DocumentType, IsActive, PublishedAt);


-- 5. MemberConsentLogs
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
    CONSTRAINT FK_MemberConsentLogs_Members FOREIGN KEY (MemberId) REFERENCES VCINDW.mbw.Members(MemberId),
    CONSTRAINT FK_MemberConsentLogs_ContentDocuments FOREIGN KEY (DocumentId) REFERENCES VCINDW.mbw.ContentDocuments(DocumentId)
);

CREATE NONCLUSTERED INDEX IX_MemberConsentLogs_MemberId_AcceptedAt ON VCINDW.mbw.MemberConsentLogs (MemberId, AcceptedAt DESC);


-- =============================================
-- Part 3: Tiers & Notifications
-- =============================================

-- 6. TierMasters
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


-- 7. MemberTierHistories
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
    CONSTRAINT FK_MemberTierHistories_Members FOREIGN KEY (MemberId) REFERENCES VCINDW.mbw.Members(MemberId),
    CONSTRAINT FK_MemberTierHistories_TierMasters FOREIGN KEY (TierId) REFERENCES VCINDW.mbw.TierMasters(TierId),
    CONSTRAINT FK_MemberTierHistories_PreviousTierMasters FOREIGN KEY (PreviousTierId) REFERENCES VCINDW.mbw.TierMasters(TierId)
);


-- 8. MemberNotifications
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
    CONSTRAINT FK_MemberNotifications_Members FOREIGN KEY (MemberId) REFERENCES VCINDW.mbw.Members(MemberId)
);


-- =============================================
-- Part 4: Rewards & Fulfillment
-- =============================================

-- 9. RewardRedemptions
drop table VCINDW.mbw.RewardRedemptions;
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
    CONSTRAINT FK_RewardRedemptions_Members FOREIGN KEY (MemberId) REFERENCES VCINDW.mbw.Members(MemberId)
);

-- 10. RewardFulfillments
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
    CONSTRAINT FK_RewardFulfillments_RewardRedemptions FOREIGN KEY (RedemptionId) REFERENCES VCINDW.mbw.RewardRedemptions(RedemptionId)
);

-- 11. RewardRedemptionHistories
CREATE TABLE VCINDW.mbw.RewardRedemptionHistories (
    RedemptionHistoryId bigint IDENTITY(1,1) NOT NULL,
    RedemptionId bigint NOT NULL,
    OldStatus nvarchar(30) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    NewStatus nvarchar(30) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    ChangedAt datetime2 DEFAULT sysutcdatetime() NOT NULL,
    ChangedBy nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    Remark nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    CONSTRAINT PK_RewardRedemptionHistories PRIMARY KEY (RedemptionHistoryId),
    CONSTRAINT FK_RewardRedemptionHistories_RewardRedemptions FOREIGN KEY (RedemptionId) REFERENCES VCINDW.mbw.RewardRedemptions(RedemptionId)
);
