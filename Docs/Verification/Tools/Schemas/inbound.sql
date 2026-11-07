-- Inbound event landing tables: InboundTables=WebhookEvent,ErrorReport:Payload
-- WebhookEvent keeps a sender's event id (applied once), the source and a processed time; ErrorReport has a processed flag and no event id.
CREATE TABLE WebhookEvent (
    WebhookEventId INTEGER PRIMARY KEY AUTOINCREMENT,
    Source VARCHAR(40) NULL,
    EventId VARCHAR(80) NULL,
    Body VARCHAR(4000) NOT NULL,
    ReceivedOn DATETIME NOT NULL,
    ProcessedOn DATETIME NULL
);
CREATE UNIQUE INDEX UX_WebhookEvent_Event ON WebhookEvent (Source, EventId);

CREATE TABLE ErrorReport (
    ErrorReportId INTEGER PRIMARY KEY AUTOINCREMENT,
    Title VARCHAR(100) NULL,
    Severity VARCHAR(20) NULL,
    Payload TEXT NOT NULL,
    ReceivedAt DATETIME NOT NULL,
    IsProcessed BOOLEAN NOT NULL DEFAULT 0
);

CREATE TABLE Customer (
    CustomerId INTEGER PRIMARY KEY AUTOINCREMENT,
    Name VARCHAR(60) NOT NULL,
    Email VARCHAR(80) NULL,
    City VARCHAR(40) NULL,
    IsActive BOOLEAN NOT NULL DEFAULT 1,
    Notes VARCHAR(200) NULL
);
