-- Fix RewardRedemptions Schema Mismatch
-- Add missing columns to mbw.RewardRedemptions to match C# Entity

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('mbw.RewardRedemptions') AND name = 'LedgerId')
BEGIN
    ALTER TABLE mbw.RewardRedemptions ADD LedgerId bigint NULL;
END;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('mbw.RewardRedemptions') AND name = 'ReservedAt')
BEGIN
    ALTER TABLE mbw.RewardRedemptions ADD ReservedAt datetime2 NULL;
END;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('mbw.RewardRedemptions') AND name = 'CompletedAt')
BEGIN
    ALTER TABLE mbw.RewardRedemptions ADD CompletedAt datetime2 NULL;
END;
