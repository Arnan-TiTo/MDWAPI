-- =============================================
-- Create UnifiedReturns table
-- Schema: mdw (same as UnifiedOrders)
-- =============================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id
               WHERE s.name = 'mdw' AND t.name = 'UnifiedReturns')
BEGIN
    CREATE TABLE mdw.UnifiedReturns (
        UnifiedReturnId     BIGINT IDENTITY(1,1) PRIMARY KEY,
        UnifiedOrderId      BIGINT NULL,  -- FK → mdw.UnifiedOrders
        Channel             NVARCHAR(20) NOT NULL,
        ShopId              BIGINT NULL,
        ExternalOrderId     NVARCHAR(100) NULL,   -- order_sn
        ExternalReturnId    NVARCHAR(100) NOT NULL, -- return_sn (unique per channel+shop)
        ReturnStatus        NVARCHAR(40) NULL,      -- REQUESTED / ACCEPTED / REFUND_PAID / CLOSED
        ReturnReason        NVARCHAR(200) NULL,     -- reason code
        TextReason          NVARCHAR(500) NULL,     -- buyer text
        ReturnType          NVARCHAR(40) NULL,      -- RETURN_REFUND / REFUND_ONLY
        ReturnSolution      NVARCHAR(40) NULL,
        NegotiationStatus   NVARCHAR(40) NULL,
        RefundAmount        DECIMAL(18,2) NULL,
        Currency            NVARCHAR(8) NULL,
        ReturnItemsJson     NVARCHAR(MAX) NULL,     -- JSON array of returned items
        ImagesJson          NVARCHAR(MAX) NULL,     -- JSON array of proof images
        CreatedAtUtc        DATETIME2 NULL,          -- return create_time
        UpdatedAtUtc        DATETIME2 NULL,          -- return update_time
        IngestedAtUtc       DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        RawJson             NVARCHAR(MAX) NULL       -- raw API response
    );

    PRINT 'Created table mdw.UnifiedReturns';
END;
GO

-- Indexes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UnifiedReturns_Channel_ReturnId')
BEGIN
    CREATE UNIQUE INDEX IX_UnifiedReturns_Channel_ReturnId
        ON mdw.UnifiedReturns (Channel, ShopId, ExternalReturnId);
    PRINT 'Created unique index IX_UnifiedReturns_Channel_ReturnId';
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UnifiedReturns_OrderId')
BEGIN
    CREATE INDEX IX_UnifiedReturns_OrderId
        ON mdw.UnifiedReturns (UnifiedOrderId)
        WHERE UnifiedOrderId IS NOT NULL;
    PRINT 'Created index IX_UnifiedReturns_OrderId';
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UnifiedReturns_ExternalOrderId')
BEGIN
    CREATE INDEX IX_UnifiedReturns_ExternalOrderId
        ON mdw.UnifiedReturns (Channel, ExternalOrderId)
        WHERE ExternalOrderId IS NOT NULL;
    PRINT 'Created index IX_UnifiedReturns_ExternalOrderId';
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UnifiedReturns_Status')
BEGIN
    CREATE INDEX IX_UnifiedReturns_Status
        ON mdw.UnifiedReturns (ReturnStatus, Channel);
    PRINT 'Created index IX_UnifiedReturns_Status';
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UnifiedReturns_CreatedAt')
BEGIN
    CREATE INDEX IX_UnifiedReturns_CreatedAt
        ON mdw.UnifiedReturns (CreatedAtUtc DESC);
    PRINT 'Created index IX_UnifiedReturns_CreatedAt';
END;
GO

PRINT 'Done: UnifiedReturns table + indexes';
