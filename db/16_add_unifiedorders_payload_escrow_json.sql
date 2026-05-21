-- ============================================================
-- 16_add_unifiedorders_payload_escrow_json.sql
-- Store raw platform escrow payload on mdw.UnifiedOrders.
-- ============================================================

IF COL_LENGTH('mdw.UnifiedOrders', 'PayloadEscrowJson') IS NULL
    ALTER TABLE mdw.UnifiedOrders ADD PayloadEscrowJson NVARCHAR(MAX) NULL;
GO

-- Optional one-time backfill from the legacy ShopeeOrders table, if it exists.
IF OBJECT_ID('mdw.ShopeeOrders', 'U') IS NOT NULL
   AND COL_LENGTH('mdw.ShopeeOrders', 'PayloadEscrowJson') IS NOT NULL
BEGIN
    EXEC('
        UPDATE u
        SET PayloadEscrowJson = COALESCE(u.PayloadEscrowJson, s.PayloadEscrowJson)
        FROM mdw.UnifiedOrders u
        INNER JOIN mdw.ShopeeOrders s
            ON s.OrderSn = u.ExternalOrderId
        WHERE u.Channel = ''Shopee''
          AND s.PayloadEscrowJson IS NOT NULL;
    ');
END
GO
