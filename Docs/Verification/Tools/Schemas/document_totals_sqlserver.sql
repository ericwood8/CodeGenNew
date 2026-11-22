-- The tables of document_totals.sql for SQL Server, in the schema totals (generate with --schema totals). live_totals_run.py drops and makes them again on every run:
-- sqlcmd -S <server> -E -C -I -b -d InvoiceSystem_test -i document_totals_sqlserver.sql
IF SCHEMA_ID('totals') IS NULL
    EXEC ('CREATE SCHEMA totals');
GO
DROP TABLE IF EXISTS totals.InvoiceAllowanceCharge;
DROP TABLE IF EXISTS totals.InvoiceLine;
DROP TABLE IF EXISTS totals.TaxCategory;
DROP TABLE IF EXISTS totals.Invoice;
GO
CREATE TABLE totals.Invoice (
    InvoiceId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_totals_Invoice PRIMARY KEY,
    Customer VARCHAR(60) NOT NULL,
    InvoiceDate DATE NOT NULL CONSTRAINT DF_totals_Invoice_Date DEFAULT '2024-06-01',
    Currency VARCHAR(3) NOT NULL CONSTRAINT DF_totals_Invoice_Currency DEFAULT 'USD',
    ItemsTotal DECIMAL(18, 2) NOT NULL CONSTRAINT DF_totals_Invoice_Items DEFAULT 0,
    AllowanceTotal DECIMAL(18, 2) NOT NULL CONSTRAINT DF_totals_Invoice_Allowance DEFAULT 0,
    ChargeTotal DECIMAL(18, 2) NOT NULL CONSTRAINT DF_totals_Invoice_Charge DEFAULT 0,
    TaxExclusive DECIMAL(18, 2) NOT NULL CONSTRAINT DF_totals_Invoice_Exclusive DEFAULT 0,
    TaxTotal DECIMAL(18, 2) NOT NULL CONSTRAINT DF_totals_Invoice_Tax DEFAULT 0,
    TaxInclusive DECIMAL(18, 2) NOT NULL CONSTRAINT DF_totals_Invoice_Inclusive DEFAULT 0,
    Prepaid DECIMAL(18, 2) NOT NULL CONSTRAINT DF_totals_Invoice_Prepaid DEFAULT 0,
    Rounding DECIMAL(18, 2) NOT NULL CONSTRAINT DF_totals_Invoice_Rounding DEFAULT 0,
    Payable DECIMAL(18, 2) NOT NULL CONSTRAINT DF_totals_Invoice_Payable DEFAULT 0
);
GO
CREATE TABLE totals.TaxCategory (
    TaxCategoryId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_totals_TaxCategory PRIMARY KEY,
    Code VARCHAR(10) NOT NULL,
    Name VARCHAR(60) NOT NULL,
    Rate DECIMAL(9, 4) NOT NULL,
    ExemptionReason VARCHAR(200) NULL,
    ValidFrom DATE NOT NULL CONSTRAINT DF_totals_TaxCategory_From DEFAULT '2000-01-01',
    ValidTo DATE NULL
);
GO
CREATE UNIQUE INDEX UX_totals_TaxCategory_Code ON totals.TaxCategory (Code);
GO
CREATE TABLE totals.InvoiceLine (
    InvoiceLineId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_totals_InvoiceLine PRIMARY KEY,
    InvoiceId INT NOT NULL CONSTRAINT FK_totals_InvoiceLine_Invoice REFERENCES totals.Invoice (InvoiceId),
    LineDate DATE NOT NULL CONSTRAINT DF_totals_InvoiceLine_Date DEFAULT '2024-06-01',
    Description VARCHAR(100) NOT NULL,
    TaxCategoryId INT NULL CONSTRAINT FK_totals_InvoiceLine_Category REFERENCES totals.TaxCategory (TaxCategoryId),
    TaxRate DECIMAL(9, 4) NOT NULL CONSTRAINT DF_totals_InvoiceLine_Rate DEFAULT 0,
    LineTotal DECIMAL(18, 2) NOT NULL,
    TaxAmount DECIMAL(18, 2) NOT NULL CONSTRAINT DF_totals_InvoiceLine_Tax DEFAULT 0
);
GO
CREATE TABLE totals.InvoiceAllowanceCharge (
    InvoiceAllowanceChargeId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_totals_InvoiceAllowanceCharge PRIMARY KEY,
    InvoiceId INT NOT NULL CONSTRAINT FK_totals_InvoiceAllowanceCharge_Invoice REFERENCES totals.Invoice (InvoiceId),
    IsCharge BIT NOT NULL,
    ReasonCode VARCHAR(10) NULL,
    BaseAmount DECIMAL(18, 2) NULL,
    Percentage DECIMAL(9, 4) NULL,
    Amount DECIMAL(18, 2) NOT NULL,
    TaxCategoryId INT NULL CONSTRAINT FK_totals_InvoiceAllowanceCharge_Category REFERENCES totals.TaxCategory (TaxCategoryId)
);
GO
