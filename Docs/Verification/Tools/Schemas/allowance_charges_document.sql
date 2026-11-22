-- SQLite. allowance_charges.sql with an Invoice the rows belong to (a foreign key on InvoiceId, so the Invoice page has the allowances and charges as a child grid and shows their totals), for
-- AllowanceCharges=InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:InvoiceId
CREATE TABLE Invoice (
    InvoiceId INTEGER PRIMARY KEY AUTOINCREMENT,
    Customer VARCHAR(60) NOT NULL,
    InvoiceDate DATE NOT NULL DEFAULT '2024-06-01',
    Reference VARCHAR(30) NULL,
    Notes VARCHAR(200) NULL,
    Currency VARCHAR(3) NOT NULL DEFAULT 'USD'
);
CREATE TABLE InvoiceAllowanceCharge (
    InvoiceAllowanceChargeId INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceId INTEGER NOT NULL REFERENCES Invoice (InvoiceId),
    IsCharge BOOLEAN NOT NULL,
    ReasonCode VARCHAR(10) NULL,
    BaseAmount DECIMAL(18, 2) NULL,
    Percentage DECIMAL(9, 4) NULL,
    Amount DECIMAL(18, 2) NOT NULL
);
