-- Create ThailandAddress table in mbw schema
IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'mbw')
BEGIN
    EXEC('CREATE SCHEMA [mbw]')
END;

IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[mbw].[ThailandAddress]') AND type in (N'U'))
BEGIN
    DROP TABLE [mbw].[ThailandAddress]
END;

CREATE TABLE [mbw].[ThailandAddress] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [tambonID] NVARCHAR(200) NOT NULL,
    [subDistrict] NVARCHAR(200) NOT NULL,
    [district] NVARCHAR(200) NOT NULL,
    [province] NVARCHAR(200) NOT NULL,
    [postcode] NVARCHAR(20) NOT NULL,
    CONSTRAINT [PK_ThailandAddress] PRIMARY KEY ([Id])
);

CREATE INDEX [IX_ThailandAddress_province] ON [mbw].[ThailandAddress] ([province]);
CREATE INDEX [IX_ThailandAddress_district] ON [mbw].[ThailandAddress] ([district]);
CREATE INDEX [IX_ThailandAddress_tambonID] ON [mbw].[ThailandAddress] ([tambonID]);
