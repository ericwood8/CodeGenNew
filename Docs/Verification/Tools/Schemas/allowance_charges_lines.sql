-- SQLite. allowance_charges.sql with a line column, for AllowanceCharges=InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:InvoiceId:InvoiceLineId
-- (a row sits on a document, and on one of its lines when InvoiceLineId is given)
CREATE TABLE InvoiceAllowanceCharge (
    InvoiceAllowanceChargeId INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceId INTEGER NOT NULL,
    InvoiceLineId INTEGER NULL,
    IsCharge BOOLEAN NOT NULL,
    ReasonCode VARCHAR(10) NULL,
    BaseAmount DECIMAL(18, 2) NULL,
    Percentage DECIMAL(9, 4) NULL,
    Amount DECIMAL(18, 2) NOT NULL
);
