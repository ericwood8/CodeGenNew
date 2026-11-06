-- The usage ledger and its monthly totals for a MySQL copy of the InvoiceSystem sample (it has a tenant table). Settings:
-- ApiAuth=true, TenantTable=Tenant, UsageTables=UsageEvent, NoTenantTables=FeatureOverride (and UsageRollup=UsageMonthly when the roll-up is played).
-- The same tables and rows as the SQL Server InvoiceSystem and InvoiceSystem_test databases.
CREATE TABLE IF NOT EXISTS UsageEvent (
    UsageEventId int NOT NULL AUTO_INCREMENT PRIMARY KEY,
    TenantId int NOT NULL,
    Kind varchar(40) NOT NULL,
    Quantity decimal(12,2) NOT NULL DEFAULT 1,
    OccurredOn datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT FK_UsageEvent_Tenant FOREIGN KEY (TenantId) REFERENCES tenant (TenantId)
);

CREATE TABLE IF NOT EXISTS UsageMonthly (
    UsageMonthlyId int NOT NULL AUTO_INCREMENT PRIMARY KEY,
    TenantId int NOT NULL,
    Kind varchar(40) NOT NULL,
    UsageMonth date NOT NULL,
    Quantity decimal(12,2) NOT NULL,
    CONSTRAINT FK_UsageMonthly_Tenant FOREIGN KEY (TenantId) REFERENCES tenant (TenantId),
    CONSTRAINT CK_UsageMonthly_Quantity CHECK (Quantity >= 0),
    CONSTRAINT UQ_UsageMonthly UNIQUE (TenantId, Kind, UsageMonth)
);

INSERT INTO UsageEvent (TenantId, Kind, Quantity, OccurredOn)
SELECT t, k, q, o FROM (
    SELECT 1 AS t, 'api_calls' AS k, 1200.00 AS q, '2026-09-30 12:00:00' AS o UNION ALL SELECT 1, 'api_calls', 800.00, '2026-10-12 12:00:00' UNION ALL SELECT 1, 'storage_gb', 3.50, '2026-10-15 00:00:00'
    UNION ALL SELECT 2, 'api_calls', 150.00, '2026-10-20 12:00:00' UNION ALL SELECT 2, 'storage_gb', 0.40, '2026-10-21 00:00:00') AS v
WHERE NOT EXISTS (SELECT 1 FROM UsageEvent);

INSERT INTO UsageMonthly (TenantId, Kind, UsageMonth, Quantity)
SELECT t, k, m, q FROM (
    SELECT 1 AS t, 'api_calls' AS k, '2026-09-01' AS m, 1200.00 AS q UNION ALL SELECT 1, 'api_calls', '2026-10-01', 800.00 UNION ALL SELECT 1, 'storage_gb', '2026-10-01', 3.50
    UNION ALL SELECT 2, 'api_calls', '2026-10-01', 150.00 UNION ALL SELECT 2, 'storage_gb', '2026-10-01', 0.40) AS v
WHERE NOT EXISTS (SELECT 1 FROM UsageMonthly);
