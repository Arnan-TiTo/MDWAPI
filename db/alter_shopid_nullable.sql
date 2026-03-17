-- Make ShopId nullable in MemberPlatformAccounts
-- Drop FK + UQ constraints first, then alter column, then re-add

-- 1. Drop FK constraint
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_MemberPlatformAccounts_Shops')
    ALTER TABLE [mbw].[MemberPlatformAccounts] DROP CONSTRAINT FK_MemberPlatformAccounts_Shops;

-- 2. Drop unique constraint (contains ShopId)  
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_MemberPlatformAccounts_PlatShopKey')
    ALTER TABLE [mbw].[MemberPlatformAccounts] DROP CONSTRAINT UQ_MemberPlatformAccounts_PlatShopKey;

-- 3. Alter column to nullable
ALTER TABLE [mbw].[MemberPlatformAccounts] ALTER COLUMN ShopId INT NULL;

-- 4. Re-add FK (now nullable)
ALTER TABLE [mbw].[MemberPlatformAccounts]
    ADD CONSTRAINT FK_MemberPlatformAccounts_Shops 
    FOREIGN KEY (ShopId) REFERENCES [mdw].[Shops](Id);

-- 5. Re-add unique constraint with filtered index (allow NULL ShopId)
CREATE UNIQUE NONCLUSTERED INDEX UQ_MemberPlatformAccounts_PlatShopKey
    ON [mbw].[MemberPlatformAccounts] (PlatformType, ShopId, PlatformAccountKey)
    WHERE ShopId IS NOT NULL;

PRINT 'ShopId is now nullable in MemberPlatformAccounts';
