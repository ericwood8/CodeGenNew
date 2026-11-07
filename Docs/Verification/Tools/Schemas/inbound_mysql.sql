-- The inbound landing tables for the MySQL copy of the InvoiceSystem sample. Settings:
-- Tables=InboundWebhook,InboundErrorReport, InboundTables=InboundWebhook,InboundErrorReport:Payload
-- The same tables as Schemas/inbound.sql under other names, like the SQL Server copy. MySQL ignores NULL in a unique index.
CREATE TABLE IF NOT EXISTS InboundWebhook (
    InboundWebhookId int NOT NULL AUTO_INCREMENT PRIMARY KEY,
    Source varchar(40) NULL,
    EventId varchar(80) NULL,
    Body varchar(4000) NOT NULL,
    ReceivedOn datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ProcessedOn datetime NULL,
    UNIQUE KEY UX_InboundWebhook_Event (Source, EventId)
);

CREATE TABLE IF NOT EXISTS InboundErrorReport (
    InboundErrorReportId int NOT NULL AUTO_INCREMENT PRIMARY KEY,
    Title varchar(100) NULL,
    Severity varchar(20) NULL,
    Payload text NOT NULL,
    ReceivedAt datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    IsProcessed tinyint(1) NOT NULL DEFAULT 0
);
