-- The tables of allowance_charges_document.sql for SQL Server, in the schema allow, for
-- AllowanceCharges=InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:InvoiceId (generate with --schema allow).
-- sqlcmd -S <server> -E -C -I -b -d InvoiceSystem_test -i allowance_charges_sqlserver.sql      Can be run again: a table that exists is left alone.
IF SCHEMA_ID('allow') IS NULL
    EXEC ('CREATE SCHEMA allow');
GO
IF OBJECT_ID('allow.Invoice', 'U') IS NULL
CREATE TABLE allow.Invoice (
    InvoiceId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_allow_Invoice PRIMARY KEY,
    Customer VARCHAR(60) NOT NULL,
    InvoiceDate DATE NOT NULL CONSTRAINT DF_allow_Invoice_Date DEFAULT '2024-06-01',
    Reference VARCHAR(30) NULL,
    Notes VARCHAR(200) NULL,
    Currency VARCHAR(3) NOT NULL CONSTRAINT DF_allow_Invoice_Currency DEFAULT 'USD'
);
GO
IF OBJECT_ID('allow.InvoiceAllowanceCharge', 'U') IS NULL
CREATE TABLE allow.InvoiceAllowanceCharge (
    InvoiceAllowanceChargeId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_allow_InvoiceAllowanceCharge PRIMARY KEY,
    InvoiceId INT NOT NULL CONSTRAINT FK_allow_InvoiceAllowanceCharge_Invoice REFERENCES allow.Invoice (InvoiceId),
    IsCharge BIT NOT NULL,
    ReasonCode VARCHAR(10) NULL,
    BaseAmount DECIMAL(18, 2) NULL,
    Percentage DECIMAL(9, 4) NULL,
    Amount DECIMAL(18, 2) NOT NULL
);
GO
