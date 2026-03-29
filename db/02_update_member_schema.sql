-- =============================================
-- Update Existing Members Table Schema
-- =============================================

-- 1. Add Language & Country Code
ALTER TABLE VCINDW.mbw.Members
ADD PreferredLanguage nvarchar(10) COLLATE SQL_Latin1_General_CP1_CI_AS NULL;


ALTER TABLE VCINDW.mbw.Members
ADD PhoneCountryCode nvarchar(10) COLLATE SQL_Latin1_General_CP1_CI_AS NULL;


-- 2. Add RegisterChannelId & CurrentTierId
ALTER TABLE VCINDW.mbw.Members
ADD RegisterChannelId int NULL,
    CurrentTierId int NULL;


-- 3. Add Foreign Key Constraints
ALTER TABLE VCINDW.mbw.Members
ADD CONSTRAINT FK_Members_MemberChannels FOREIGN KEY (RegisterChannelId)
    REFERENCES VCINDW.mbw.MemberChannels(ChannelId);


ALTER TABLE VCINDW.mbw.Members
ADD CONSTRAINT FK_Members_TierMasters FOREIGN KEY (CurrentTierId)
    REFERENCES VCINDW.mbw.TierMasters(TierId);

