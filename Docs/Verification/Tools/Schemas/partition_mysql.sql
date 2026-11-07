-- Inbound tables to be partitioned by month, for the MySQL copy of the InvoiceSystem sample. Settings:
-- InboundTables=PartitionedWebhook,PartitionedErrorReport:Payload, PartitionTables=PartitionedWebhook,PartitionedErrorReport
-- No unique index on the event id (see partition_mssql.sql). Rows in five months, so the script has old partitions to make.
CREATE TABLE IF NOT EXISTS PartitionedWebhook (
    PartitionedWebhookId int NOT NULL AUTO_INCREMENT PRIMARY KEY,
    Source varchar(40) NULL,
    EventId varchar(80) NULL,
    Body varchar(4000) NOT NULL,
    ReceivedOn datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ProcessedOn datetime NULL,
    KEY IX_PartitionedWebhook_Source (Source)
);
INSERT INTO PartitionedWebhook (Source, EventId, Body, ReceivedOn, ProcessedOn)
SELECT * FROM (
    SELECT 'seed' AS s, 'seed-1' AS e, '{"seed":1}' AS b, '2026-07-03 10:00:00' AS r, '2026-07-03 10:05:00' AS p
    UNION ALL SELECT 'seed', 'seed-2', '{"seed":2}', '2026-07-28 23:59:00', NULL
    UNION ALL SELECT 'seed', 'seed-3', '{"seed":3}', '2026-08-15 12:00:00', NULL
    UNION ALL SELECT 'seed', 'seed-4', '{"seed":4}', '2026-09-01 00:00:00', NULL
    UNION ALL SELECT 'seed', 'seed-5', '{"seed":5}', '2026-10-20 08:30:00', NULL
    UNION ALL SELECT 'seed', 'seed-6', '{"seed":6}', '2026-11-02 09:15:00', NULL) AS v
WHERE NOT EXISTS (SELECT 1 FROM PartitionedWebhook);

CREATE TABLE IF NOT EXISTS PartitionedErrorReport (
    PartitionedErrorReportId int NOT NULL AUTO_INCREMENT PRIMARY KEY,
    Title varchar(100) NULL,
    Severity varchar(20) NULL,
    Payload text NOT NULL,
    ReceivedAt datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    IsProcessed tinyint(1) NOT NULL DEFAULT 0,
    KEY IX_PartitionedErrorReport_Severity (Severity)
);
INSERT INTO PartitionedErrorReport (Title, Severity, Payload, ReceivedAt, IsProcessed)
SELECT * FROM (
    SELECT 'seed' AS t, 'error' AS s, '{"seed":1}' AS p, '2026-08-04 10:00:00' AS r, 1 AS i
    UNION ALL SELECT 'seed', 'warning', '{"seed":2}', '2026-09-30 23:59:00', 0
    UNION ALL SELECT 'seed', 'error', '{"seed":3}', '2026-10-01 00:00:00', 0
    UNION ALL SELECT 'seed', 'info', '{"seed":4}', '2026-11-03 17:45:00', 0) AS v
WHERE NOT EXISTS (SELECT 1 FROM PartitionedErrorReport);
