$conn = New-Object System.Data.SqlClient.SqlConnection("Server=localhost;Database=VCINDW;User Id=sa;Password=Admin@9999;TrustServerCertificate=True;")
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = @"
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_MemberPlatformAccounts_Shops')
    ALTER TABLE [mbw].[MemberPlatformAccounts] DROP CONSTRAINT FK_MemberPlatformAccounts_Shops;
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_MemberPlatformAccounts_PlatShopKey')
    ALTER TABLE [mbw].[MemberPlatformAccounts] DROP CONSTRAINT UQ_MemberPlatformAccounts_PlatShopKey;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_MemberPlatformAccounts_PlatShopKey' AND object_id = OBJECT_ID('mbw.MemberPlatformAccounts'))
    DROP INDEX UQ_MemberPlatformAccounts_PlatShopKey ON [mbw].[MemberPlatformAccounts];
ALTER TABLE [mbw].[MemberPlatformAccounts] ALTER COLUMN ShopId INT NULL;
ALTER TABLE [mbw].[MemberPlatformAccounts] ADD CONSTRAINT FK_MemberPlatformAccounts_Shops FOREIGN KEY (ShopId) REFERENCES [mdw].[Shops](Id);
"@
$cmd.ExecuteNonQuery() | Out-Null
$conn.Close()
Write-Host "Migration done! ShopId is now nullable."
