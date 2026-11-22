-- SQLite. An Invoice with lines (a tax category and tax on each), allowance and charge rows and the totals the server writes, for
-- DocumentTotals=Invoice:InvoiceLine:InvoiceId:LineTotal:Lines=ItemsTotal:Allowances=AllowanceTotal:Charges=ChargeTotal:TaxExclusive=TaxExclusive:Tax=TaxTotal:TaxInclusive=TaxInclusive:Prepaid=Prepaid:Rounding=Rounding:Payable=Payable:Currency=Currency:Round=CHF/0.05
-- with TaxLines=InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory:Rate and AllowanceCharges=InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:InvoiceId
CREATE TABLE Invoice (
    InvoiceId INTEGER PRIMARY KEY AUTOINCREMENT,
    Customer VARCHAR(60) NOT NULL,
    InvoiceDate DATE NOT NULL DEFAULT '2024-06-01',
    Currency VARCHAR(3) NOT NULL DEFAULT 'USD',
    ItemsTotal DECIMAL(18, 2) NOT NULL DEFAULT 0,
    AllowanceTotal DECIMAL(18, 2) NOT NULL DEFAULT 0,
    ChargeTotal DECIMAL(18, 2) NOT NULL DEFAULT 0,
    TaxExclusive DECIMAL(18, 2) NOT NULL DEFAULT 0,
    TaxTotal DECIMAL(18, 2) NOT NULL DEFAULT 0,
    TaxInclusive DECIMAL(18, 2) NOT NULL DEFAULT 0,
    Prepaid DECIMAL(18, 2) NOT NULL DEFAULT 0,
    Rounding DECIMAL(18, 2) NOT NULL DEFAULT 0,
    Payable DECIMAL(18, 2) NOT NULL DEFAULT 0
);
CREATE TABLE TaxCategory (
    TaxCategoryId INTEGER PRIMARY KEY AUTOINCREMENT,
    Code VARCHAR(10) NOT NULL,
    Name VARCHAR(60) NOT NULL,
    Rate DECIMAL(9, 4) NOT NULL,
    ExemptionReason VARCHAR(200) NULL,
    ValidFrom DATE NOT NULL DEFAULT '2000-01-01',
    ValidTo DATE NULL
);
CREATE UNIQUE INDEX UX_TaxCategory_Code ON TaxCategory (Code);
CREATE TABLE InvoiceLine (
    InvoiceLineId INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceId INTEGER NOT NULL REFERENCES Invoice (InvoiceId),
    LineDate DATE NOT NULL DEFAULT '2024-06-01',
    Description VARCHAR(100) NOT NULL,
    TaxCategoryId INTEGER NULL REFERENCES TaxCategory (TaxCategoryId),
    TaxRate DECIMAL(9, 4) NOT NULL DEFAULT 0,
    LineTotal DECIMAL(18, 2) NOT NULL,
    TaxAmount DECIMAL(18, 2) NOT NULL DEFAULT 0
);
CREATE TABLE InvoiceAllowanceCharge (
    InvoiceAllowanceChargeId INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceId INTEGER NOT NULL REFERENCES Invoice (InvoiceId),
    IsCharge BOOLEAN NOT NULL,
    ReasonCode VARCHAR(10) NULL,
    BaseAmount DECIMAL(18, 2) NULL,
    Percentage DECIMAL(9, 4) NULL,
    Amount DECIMAL(18, 2) NOT NULL,
    TaxCategoryId INTEGER NULL REFERENCES TaxCategory (TaxCategoryId)
);
