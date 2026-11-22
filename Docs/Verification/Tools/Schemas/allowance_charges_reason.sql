-- SQLite. allowance_charges.sql with the reason as a look-up table (AllowanceReason) instead of a text code, for
-- AllowanceCharges=InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:InvoiceId
CREATE TABLE AllowanceReason (
    AllowanceReasonId INTEGER PRIMARY KEY AUTOINCREMENT,
    Code VARCHAR(10) NOT NULL,
    Name VARCHAR(60) NOT NULL,
    Description VARCHAR(200) NULL,
    IsActive BOOLEAN NOT NULL DEFAULT 1,
    SortOrder INTEGER NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX UX_AllowanceReason_Code ON AllowanceReason (Code);
CREATE TABLE InvoiceAllowanceCharge (
    InvoiceAllowanceChargeId INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceId INTEGER NOT NULL,
    AllowanceReasonId INTEGER NULL REFERENCES AllowanceReason (AllowanceReasonId),
    IsCharge BOOLEAN NOT NULL,
    BaseAmount DECIMAL(18, 2) NULL,
    Percentage DECIMAL(9, 4) NULL,
    Amount DECIMAL(18, 2) NOT NULL
);
