-- =============================================
-- Fix PointLedger IdempotencyKey Index
-- Change from UNIQUE CONSTRAINT to filtered UNIQUE INDEX
-- to allow multiple NULL values
-- Date: 2026-03-30
-- =============================================

-- Drop the old constraint (does not allow multiple NULLs)
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_PointLedger_IdempotencyKey')
BEGIN
    ALTER TABLE [mbw].[PointLedger] DROP CONSTRAINT [UQ_PointLedger_IdempotencyKey];
    PRINT 'Dropped: UQ_PointLedger_IdempotencyKey';
END

-- Create filtered unique index (allows multiple NULLs)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PointLedger_IdempotencyKey' AND object_id = OBJECT_ID('mbw.PointLedger'))
BEGIN
    CREATE UNIQUE INDEX [IX_PointLedger_IdempotencyKey] 
    ON [mbw].[PointLedger] ([IdempotencyKey]) 
    WHERE [IdempotencyKey] IS NOT NULL;
    PRINT 'Created: IX_PointLedger_IdempotencyKey (filtered unique)';
END

PRINT 'Done.';
GO
