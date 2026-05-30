-- ============================================================
-- 12_refresh_adw_views_after_unifiedorders_schema_change.sql
-- Refresh ADW views after adding columns to mdw.UnifiedOrders.
--
-- Why:
--   adw.vw_OrderMerged uses SELECT u.*. SQL Server stores view metadata,
--   so adding columns to mdw.UnifiedOrders can leave downstream views with
--   shifted/stale column types until the view metadata is refreshed.
--   This can break FlowAccount export with errors like:
--   "Expression type decimal is invalid for COLLATE clause."
-- ============================================================

EXEC sp_refreshview 'adw.vw_OrderMerged';
GO

EXEC sp_refreshview 'adw.vw_OrderMergedItems';
GO

EXEC sp_refreshview 'adw.vw_OrderExport';
GO

EXEC sp_refreshview 'adw.vw_OrderExportFormatTH';
GO

EXEC sp_refreshview 'adw.vw_OrderExportCashSaleFormatTH';
GO
