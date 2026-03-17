-- Create EarnFormulas table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'EarnFormulas' AND schema_id = SCHEMA_ID('mbw'))
BEGIN
    CREATE TABLE [mbw].[EarnFormulas] (
        [FormulaId]   INT            IDENTITY(1,1) NOT NULL,
        [FormulaCode] NVARCHAR(50)   NOT NULL,
        [FormulaName] NVARCHAR(200)  NOT NULL,
        [Expression]  NVARCHAR(500)  NOT NULL,
        [Description] NVARCHAR(1000) NULL,
        [Variables]   NVARCHAR(200)  NULL,
        [IsSystem]    BIT            NOT NULL DEFAULT(0),
        [CreatedAt]   DATETIME2      NOT NULL DEFAULT(GETUTCDATE()),
        CONSTRAINT [PK_EarnFormulas] PRIMARY KEY ([FormulaId]),
        CONSTRAINT [UQ_EarnFormulas_Code] UNIQUE ([FormulaCode])
    );
    
    -- Insert default system formulas
    INSERT INTO [mbw].[EarnFormulas] ([FormulaCode], [FormulaName], [Expression], [Description], [Variables], [IsSystem])
    VALUES
        ('AMOUNT_DIV_100', 'ยอดสั่งซื้อ ÷ 100 × Rate', 'Amount / 100 * Rate', 'ทุก ฿100 ได้ 1 แต้ม (คูณด้วย Rate)', 'Amount,Rate', 1),
        ('FIXED', 'แต้มคงที่', 'Rate', 'ได้แต้มเท่ากับ Rate ทุกออเดอร์', 'Rate', 1);
    
    PRINT 'Created mbw.EarnFormulas with default formulas';
END
ELSE
    PRINT 'mbw.EarnFormulas already exists';
