-- Add BuyerUsername column to UnifiedOrders table
ALTER TABLE mdw.UnifiedOrders
ADD BuyerUsername NVARCHAR(200) NULL;

