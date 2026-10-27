CREATE TABLE ErrorLog (
    ErrorLogId INTEGER PRIMARY KEY AUTOINCREMENT,
    LoggedOn DATETIME NOT NULL,
    Level VARCHAR(10) NOT NULL,
    UserName VARCHAR(50) NULL,
    Source VARCHAR(100) NULL,
    Message VARCHAR(500) NOT NULL
);

CREATE TABLE ErrorLogArchive (
    ErrorLogId INTEGER NOT NULL PRIMARY KEY,
    LoggedOn DATETIME NOT NULL,
    Level VARCHAR(10) NOT NULL,
    UserName VARCHAR(50) NULL,
    Source VARCHAR(100) NULL,
    Message VARCHAR(500) NOT NULL,
    ArchivedOn DATETIME NOT NULL
);

CREATE TABLE JobQueue (
    JobQueueId INTEGER PRIMARY KEY AUTOINCREMENT,
    JobType VARCHAR(40) NOT NULL,
    Payload VARCHAR(500) NULL,
    Status VARCHAR(10) NOT NULL DEFAULT 'pending',
    RetryCount INTEGER NOT NULL DEFAULT 0,
    CreatedOn DATETIME NOT NULL,
    ClaimedOn DATETIME NULL,
    ClaimedBy VARCHAR(50) NULL,
    ProcessedOn DATETIME NULL,
    LastError VARCHAR(500) NULL
);

INSERT INTO ErrorLog (LoggedOn, Level, UserName, Source, Message) VALUES
    ('2026-01-05 10:00:00', 'Error', 'jdoe', 'InvoiceApi', 'Invoice 7 could not be saved'),
    ('2026-02-11 09:30:00', 'Warning', 'akim', 'InvoiceApi', 'Slow query on Invoice search'),
    ('2026-10-20 08:15:00', 'Info', 'jdoe', 'Login', 'Signed in'),
    ('2026-10-25 14:45:00', 'Error', NULL, 'Scheduler', 'Nightly job did not start');

INSERT INTO ErrorLogArchive (ErrorLogId, LoggedOn, Level, UserName, Source, Message, ArchivedOn) VALUES
    (9001, '2026-04-02 10:15:00', 'Error', 'jdoe', 'InvoiceApi', 'Invoice 55 could not be saved', '2026-10-25 02:00:00'),
    (9002, '2026-04-03 11:40:00', 'Warning', 'akim', 'InvoiceApi', 'Slow query on Invoice search', '2026-10-25 02:00:00'),
    (9003, '2026-04-09 09:05:00', 'Error', NULL, 'Scheduler', 'Nightly job did not start', '2026-10-25 02:00:00');

INSERT INTO JobQueue (JobType, Payload, Status, RetryCount, CreatedOn, ClaimedOn, ClaimedBy, ProcessedOn, LastError) VALUES
    ('SendInvoice', '{"invoiceId":101}', 'done', 0, '2026-10-20 08:00:00', '2026-10-20 08:00:05', 'worker-1', '2026-10-20 08:00:09', NULL),
    ('SendInvoice', '{"invoiceId":102}', 'done', 1, '2026-10-20 08:05:00', '2026-10-20 08:07:00', 'worker-2', '2026-10-20 08:07:04', NULL),
    ('SendInvoice', '{"invoiceId":103}', 'pending', 0, '2026-10-26 08:10:00', NULL, NULL, NULL, NULL),
    ('SendInvoice', '{"invoiceId":104}', 'pending', 0, '2026-10-26 08:12:00', NULL, NULL, NULL, NULL),
    ('RebuildIndex', '{"table":"Invoice"}', 'failed', 3, '2026-10-24 22:00:00', NULL, 'worker-3', '2026-10-24 22:31:00', 'Lock timeout expired'),
    ('ExportReport', '{"report":"sales"}', 'failed', 3, '2026-10-23 06:00:00', NULL, 'worker-2', '2026-10-23 06:02:00', 'Report template missing');
