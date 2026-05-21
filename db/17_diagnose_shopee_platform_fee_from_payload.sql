-- ============================================================
-- 17_diagnose_shopee_platform_fee_from_payload.sql
-- Inspect Shopee escrow raw payload stored on mdw.UnifiedOrders.
-- Use this to verify which platform-fee-like fields Shopee sends.
-- ============================================================

-- 1) Order-level candidate fields currently mapped to UnifiedOrders.PlatformFee.
SELECT TOP (200)
    o.UnifiedOrderId,
    o.ExternalOrderId,
    o.PlatformFee AS StoredPlatformFee,
    TRY_CONVERT(decimal(18,4), COALESCE(JSON_VALUE(o.PayloadEscrowJson, '$.response.order_income.platform_fee'), JSON_VALUE(o.PayloadEscrowJson, '$.order_income.platform_fee'))) AS JsonPlatformFee,
    TRY_CONVERT(decimal(18,4), COALESCE(JSON_VALUE(o.PayloadEscrowJson, '$.response.order_income.seller_platform_fee'), JSON_VALUE(o.PayloadEscrowJson, '$.order_income.seller_platform_fee'))) AS JsonSellerPlatformFee,
    TRY_CONVERT(decimal(18,4), COALESCE(JSON_VALUE(o.PayloadEscrowJson, '$.response.order_income.infrastructure_fee'), JSON_VALUE(o.PayloadEscrowJson, '$.order_income.infrastructure_fee'))) AS JsonInfrastructureFee,
    TRY_CONVERT(decimal(18,4), COALESCE(JSON_VALUE(o.PayloadEscrowJson, '$.response.order_income.campaign_fee'), JSON_VALUE(o.PayloadEscrowJson, '$.order_income.campaign_fee'))) AS JsonCampaignFee,
    TRY_CONVERT(decimal(18,4), COALESCE(JSON_VALUE(o.PayloadEscrowJson, '$.response.order_income.seller_order_processing_fee'), JSON_VALUE(o.PayloadEscrowJson, '$.order_income.seller_order_processing_fee'))) AS JsonSellerOrderProcessingFee,
    TRY_CONVERT(decimal(18,4), COALESCE(JSON_VALUE(o.PayloadEscrowJson, '$.response.order_income.commission_fee'), JSON_VALUE(o.PayloadEscrowJson, '$.order_income.commission_fee'))) AS JsonCommissionFee,
    TRY_CONVERT(decimal(18,4), COALESCE(JSON_VALUE(o.PayloadEscrowJson, '$.response.order_income.service_fee'), JSON_VALUE(o.PayloadEscrowJson, '$.order_income.service_fee'))) AS JsonServiceFee,
    TRY_CONVERT(decimal(18,4), COALESCE(JSON_VALUE(o.PayloadEscrowJson, '$.response.order_income.seller_transaction_fee'), JSON_VALUE(o.PayloadEscrowJson, '$.order_income.seller_transaction_fee'))) AS JsonSellerTransactionFee
FROM mdw.UnifiedOrders o
WHERE o.Channel = 'Shopee'
  AND ISJSON(o.PayloadEscrowJson) = 1
ORDER BY o.UnifiedOrderId DESC;
GO

-- 2) List every order_income key containing fee/platform/campaign/infrastructure.
-- This catches new Shopee field names that are not mapped yet.
SELECT TOP (500)
    o.ExternalOrderId,
    j.[key] AS PayloadKey,
    j.[value] AS PayloadValue,
    j.[type] AS JsonType
FROM mdw.UnifiedOrders o
CROSS APPLY OPENJSON(COALESCE(JSON_QUERY(o.PayloadEscrowJson, '$.response.order_income'), JSON_QUERY(o.PayloadEscrowJson, '$.order_income'))) j
WHERE o.Channel = 'Shopee'
  AND ISJSON(o.PayloadEscrowJson) = 1
  AND (
      j.[key] LIKE '%fee%'
      OR j.[key] LIKE '%platform%'
      OR j.[key] LIKE '%campaign%'
      OR j.[key] LIKE '%infrastructure%'
  )
ORDER BY o.ExternalOrderId DESC, j.[key];
GO

-- 3) Summary: are the current PlatformFee candidate fields present and non-zero?
SELECT
    COUNT(*) AS ShopeeEscrowPayloadCount,
    SUM(CASE WHEN COALESCE(JSON_VALUE(o.PayloadEscrowJson, '$.response.order_income.platform_fee'), JSON_VALUE(o.PayloadEscrowJson, '$.order_income.platform_fee')) IS NOT NULL THEN 1 ELSE 0 END) AS HasPlatformFee,
    SUM(CASE WHEN TRY_CONVERT(decimal(18,4), COALESCE(JSON_VALUE(o.PayloadEscrowJson, '$.response.order_income.platform_fee'), JSON_VALUE(o.PayloadEscrowJson, '$.order_income.platform_fee'))) <> 0 THEN 1 ELSE 0 END) AS NonZeroPlatformFee,
    SUM(CASE WHEN COALESCE(JSON_VALUE(o.PayloadEscrowJson, '$.response.order_income.seller_platform_fee'), JSON_VALUE(o.PayloadEscrowJson, '$.order_income.seller_platform_fee')) IS NOT NULL THEN 1 ELSE 0 END) AS HasSellerPlatformFee,
    SUM(CASE WHEN TRY_CONVERT(decimal(18,4), COALESCE(JSON_VALUE(o.PayloadEscrowJson, '$.response.order_income.seller_platform_fee'), JSON_VALUE(o.PayloadEscrowJson, '$.order_income.seller_platform_fee'))) <> 0 THEN 1 ELSE 0 END) AS NonZeroSellerPlatformFee,
    SUM(CASE WHEN COALESCE(JSON_VALUE(o.PayloadEscrowJson, '$.response.order_income.infrastructure_fee'), JSON_VALUE(o.PayloadEscrowJson, '$.order_income.infrastructure_fee')) IS NOT NULL THEN 1 ELSE 0 END) AS HasInfrastructureFee,
    SUM(CASE WHEN TRY_CONVERT(decimal(18,4), COALESCE(JSON_VALUE(o.PayloadEscrowJson, '$.response.order_income.infrastructure_fee'), JSON_VALUE(o.PayloadEscrowJson, '$.order_income.infrastructure_fee'))) <> 0 THEN 1 ELSE 0 END) AS NonZeroInfrastructureFee,
    SUM(CASE WHEN COALESCE(JSON_VALUE(o.PayloadEscrowJson, '$.response.order_income.campaign_fee'), JSON_VALUE(o.PayloadEscrowJson, '$.order_income.campaign_fee')) IS NOT NULL THEN 1 ELSE 0 END) AS HasCampaignFee,
    SUM(CASE WHEN TRY_CONVERT(decimal(18,4), COALESCE(JSON_VALUE(o.PayloadEscrowJson, '$.response.order_income.campaign_fee'), JSON_VALUE(o.PayloadEscrowJson, '$.order_income.campaign_fee'))) <> 0 THEN 1 ELSE 0 END) AS NonZeroCampaignFee,
    SUM(CASE WHEN COALESCE(JSON_VALUE(o.PayloadEscrowJson, '$.response.order_income.seller_order_processing_fee'), JSON_VALUE(o.PayloadEscrowJson, '$.order_income.seller_order_processing_fee')) IS NOT NULL THEN 1 ELSE 0 END) AS HasSellerOrderProcessingFee,
    SUM(CASE WHEN TRY_CONVERT(decimal(18,4), COALESCE(JSON_VALUE(o.PayloadEscrowJson, '$.response.order_income.seller_order_processing_fee'), JSON_VALUE(o.PayloadEscrowJson, '$.order_income.seller_order_processing_fee'))) <> 0 THEN 1 ELSE 0 END) AS NonZeroSellerOrderProcessingFee
FROM mdw.UnifiedOrders o
WHERE o.Channel = 'Shopee'
  AND ISJSON(o.PayloadEscrowJson) = 1;
GO
