-- ============================================================
-- 18_backfill_shopee_platform_fee_from_payload.sql
-- Recalculate UnifiedOrders.PlatformFee from stored Shopee escrow payload.
-- Includes seller_order_processing_fee.
-- ============================================================

UPDATE o
SET PlatformFee = ABS(v.PlatformFee)
FROM mdw.UnifiedOrders o
CROSS APPLY
(
    SELECT
        TRY_CONVERT(decimal(18,4), COALESCE(JSON_VALUE(o.PayloadEscrowJson, '$.response.order_income.platform_fee'), JSON_VALUE(o.PayloadEscrowJson, '$.order_income.platform_fee'))) AS PlatformFee,
        TRY_CONVERT(decimal(18,4), COALESCE(JSON_VALUE(o.PayloadEscrowJson, '$.response.order_income.seller_platform_fee'), JSON_VALUE(o.PayloadEscrowJson, '$.order_income.seller_platform_fee'))) AS SellerPlatformFee,
        TRY_CONVERT(decimal(18,4), COALESCE(JSON_VALUE(o.PayloadEscrowJson, '$.response.order_income.infrastructure_fee'), JSON_VALUE(o.PayloadEscrowJson, '$.order_income.infrastructure_fee'))) AS InfrastructureFee,
        TRY_CONVERT(decimal(18,4), COALESCE(JSON_VALUE(o.PayloadEscrowJson, '$.response.order_income.campaign_fee'), JSON_VALUE(o.PayloadEscrowJson, '$.order_income.campaign_fee'))) AS CampaignFee,
        TRY_CONVERT(decimal(18,4), COALESCE(JSON_VALUE(o.PayloadEscrowJson, '$.response.order_income.seller_order_processing_fee'), JSON_VALUE(o.PayloadEscrowJson, '$.order_income.seller_order_processing_fee'))) AS SellerOrderProcessingFee
) p
CROSS APPLY
(
    SELECT TOP (1) CandidateFee AS PlatformFee
    FROM (VALUES
        (1, p.PlatformFee),
        (2, p.SellerPlatformFee),
        (3, p.InfrastructureFee),
        (4, p.CampaignFee),
        (5, p.SellerOrderProcessingFee)
    ) c(SortOrder, CandidateFee)
    WHERE CandidateFee IS NOT NULL
      AND CandidateFee <> 0
    ORDER BY SortOrder
) v
WHERE o.Channel = 'Shopee'
  AND ISJSON(o.PayloadEscrowJson) = 1;
GO
