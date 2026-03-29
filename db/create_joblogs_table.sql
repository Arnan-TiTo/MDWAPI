-- Create JobLogs table (matching production schema)
CREATE TABLE [dbo].[JobLogs] (
    [Id] BIGINT IDENTITY(1,1) NOT NULL,
    [RunId] NVARCHAR(50) NULL,
    [Category] NVARCHAR(50) NOT NULL,
    [Phase] NVARCHAR(50) NOT NULL,
    [Step] NVARCHAR(50) NULL,
    [Level] NVARCHAR(20) NOT NULL DEFAULT 'INFO',
    [Message] NVARCHAR(MAX) NOT NULL,
    [JobId] BIGINT NULL,
    [JobName] NVARCHAR(200) NULL,
    [HttpStatus] INT NULL,
    [DurationMs] BIGINT NULL,
    [MetaJson] NVARCHAR(MAX) NULL,
    [CreatedAtUtc] DATETIME2(7) NOT NULL DEFAULT GETUTCDATE(),
    
    CONSTRAINT [PK_JobLogs] PRIMARY KEY CLUSTERED ([Id] ASC)
);

-- Create indexes for better query performance
CREATE NONCLUSTERED INDEX [IX_JobLogs_RunId] 
    ON [dbo].[JobLogs] ([RunId]) 
    INCLUDE ([CreatedAtUtc], [Level]);

CREATE NONCLUSTERED INDEX [IX_JobLogs_JobId] 
    ON [dbo].[JobLogs] ([JobId]) 
    INCLUDE ([CreatedAtUtc], [Level], [Phase]);

CREATE NONCLUSTERED INDEX [IX_JobLogs_CreatedAtUtc] 
    ON [dbo].[JobLogs] ([CreatedAtUtc] DESC);

CREATE NONCLUSTERED INDEX [IX_JobLogs_Level] 
    ON [dbo].[JobLogs] ([Level]) 
    INCLUDE ([CreatedAtUtc], [JobName]);

GO

-- Verify table creation
SELECT 
    'Table created successfully!' AS Status,
    COUNT(*) AS RowCount 
FROM [dbo].[JobLogs];
