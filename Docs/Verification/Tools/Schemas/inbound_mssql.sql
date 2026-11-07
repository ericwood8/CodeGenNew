-- The inbound landing tables for the SQL Server copies of the InvoiceSystem sample (InvoiceSystem and InvoiceSystem_test). Settings:
-- Tables=InboundWebhook,InboundErrorReport, InboundTables=InboundWebhook,InboundErrorReport:Payload
-- The same tables as Schemas/inbound.sql under other names, since the sample already holds a WebhookEvent of another shape.
-- Run again safely: sqlcmd -S <server> -E -I -b -d <database> -i inbound_mssql.sql
IF OBJECT_ID('dbo.InboundWebhook', 'U') IS NULL
CREATE TABLE dbo.InboundWebhook (
    InboundWebhookId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InboundWebhook PRIMARY KEY,
    Source VARCHAR(40) NULL,
    EventId VARCHAR(80) NULL,
    Body VARCHAR(4000) NOT NULL,
    ReceivedOn DATETIME NOT NULL CONSTRAINT DF_InboundWebhook_ReceivedOn DEFAULT GETDATE(),
    ProcessedOn DATETIME NULL
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_InboundWebhook_Event' AND object_id = OBJECT_ID('dbo.InboundWebhook'))
CREATE UNIQUE INDEX UX_InboundWebhook_Event ON dbo.InboundWebhook (Source, EventId) WHERE EventId IS NOT NULL;
GO

IF OBJECT_ID('dbo.InboundErrorReport', 'U') IS NULL
CREATE TABLE dbo.InboundErrorReport (
    InboundErrorReportId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InboundErrorReport PRIMARY KEY,
    Title VARCHAR(100) NULL,
    Severity VARCHAR(20) NULL,
    Payload VARCHAR(MAX) NOT NULL,
    ReceivedAt DATETIME NOT NULL CONSTRAINT DF_InboundErrorReport_ReceivedAt DEFAULT GETDATE(),
    IsProcessed BIT NOT NULL CONSTRAINT DF_InboundErrorReport_IsProcessed DEFAULT 0
);
GO
