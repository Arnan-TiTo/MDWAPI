-- สร้างตาราง mdw.UnifiedOrderLabels สำหรับเก็บ mapping label file
IF NOT EXISTS (SELECT * FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id 
               WHERE s.name = 'mdw' AND t.name = 'UnifiedOrderLabels')
BEGIN
    CREATE TABLE mdw.UnifiedOrderLabels (
        Id              BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Channel         NVARCHAR(20)   NOT NULL,
        ShopId          BIGINT         NULL,
        OrderExternalNo NVARCHAR(100)  NOT NULL,
        Location        NVARCHAR(500)  NOT NULL,    -- relative path on server
        FileName        NVARCHAR(260)  NOT NULL,
        DocumentType    NVARCHAR(50)   NULL,         -- e.g. NORMAL_AIR_WAYBILL
        FileSizeBytes   BIGINT         NOT NULL DEFAULT 0,
        CreatedDate     DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME()
    );

    CREATE INDEX IX_UnifiedOrderLabels_Channel_OrderExternalNo
        ON mdw.UnifiedOrderLabels (Channel, OrderExternalNo);

    PRINT 'Created mdw.UnifiedOrderLabels';
END
ELSE
    PRINT 'mdw.UnifiedOrderLabels already exists';
GO
