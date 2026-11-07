-- Inbound tables to be partitioned by month, for the SQL Server copies of the InvoiceSystem sample (InvoiceSystem_test). Settings:
-- InboundTables=PartitionedWebhook,PartitionedErrorReport:Payload, PartitionTables=PartitionedWebhook,PartitionedErrorReport
-- No unique index on the event id: the script turns a unique index that lacks the date into a plain one, and a filtered one is refused.
-- Rows in five months, so the script has old partitions to make. Run again safely: sqlcmd -S <server> -E -I -b -d <database> -i partition_mssql.sql
IF OBJECT_ID('dbo.PartitionedWebhook', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PartitionedWebhook (
        PartitionedWebhookId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PartitionedWebhook PRIMARY KEY,
        Source VARCHAR(40) NULL,
        EventId VARCHAR(80) NULL,
        Body VARCHAR(4000) NOT NULL,
        ReceivedOn DATETIME NOT NULL CONSTRAINT DF_PartitionedWebhook_ReceivedOn DEFAULT GETDATE(),
        ProcessedOn DATETIME NULL
    );
    CREATE INDEX IX_PartitionedWebhook_Source ON dbo.PartitionedWebhook (Source);
    INSERT INTO dbo.PartitionedWebhook (Source, EventId, Body, ReceivedOn, ProcessedOn) VALUES
        ('seed', 'seed-1', '{"seed":1}', '2026-07-03 10:00', '2026-07-03 10:05'),
        ('seed', 'seed-2', '{"seed":2}', '2026-07-28 23:59', NULL),
        ('seed', 'seed-3', '{"seed":3}', '2026-08-15 12:00', NULL),
        ('seed', 'seed-4', '{"seed":4}', '2026-09-01 00:00', NULL),
        ('seed', 'seed-5', '{"seed":5}', '2026-10-20 08:30', NULL),
        ('seed', 'seed-6', '{"seed":6}', '2026-11-02 09:15', NULL);
END
GO

IF OBJECT_ID('dbo.PartitionedErrorReport', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PartitionedErrorReport (
        PartitionedErrorReportId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PartitionedErrorReport PRIMARY KEY,
        Title VARCHAR(100) NULL,
        Severity VARCHAR(20) NULL,
        Payload VARCHAR(MAX) NOT NULL,
        ReceivedAt DATETIME NOT NULL CONSTRAINT DF_PartitionedErrorReport_ReceivedAt DEFAULT GETDATE(),
        IsProcessed BIT NOT NULL CONSTRAINT DF_PartitionedErrorReport_IsProcessed DEFAULT 0
    );
    CREATE INDEX IX_PartitionedErrorReport_Severity ON dbo.PartitionedErrorReport (Severity);
    INSERT INTO dbo.PartitionedErrorReport (Title, Severity, Payload, ReceivedAt, IsProcessed) VALUES
        ('seed', 'error', '{"seed":1}', '2026-08-04 10:00', 1),
        ('seed', 'warning', '{"seed":2}', '2026-09-30 23:59', 0),
        ('seed', 'error', '{"seed":3}', '2026-10-01 00:00', 0),
        ('seed', 'info', '{"seed":4}', '2026-11-03 17:45', 0);
END
GO
