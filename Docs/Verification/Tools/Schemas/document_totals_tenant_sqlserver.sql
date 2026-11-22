-- SQL Server, schema totals. document_totals_sqlserver.sql with tenant-limited invoices, lines and allowance rows (a TenantId on each) and the user tables of an ApiAuth project.
-- Made again on every run of live_totals_scoped_run.py.
IF SCHEMA_ID('totals') IS NULL
    EXEC ('CREATE SCHEMA totals');
GO
DROP TABLE IF EXISTS totals.InvoiceAllowanceCharge;
DROP TABLE IF EXISTS totals.InvoiceLine;
DROP TABLE IF EXISTS totals.TaxCategory;
DROP TABLE IF EXISTS totals.Invoice;
DROP TABLE IF EXISTS totals.EmployeeTenant;
DROP TABLE IF EXISTS totals.Employee;
DROP TABLE IF EXISTS totals.Tenant;
GO
CREATE TABLE totals.Tenant (
    TenantId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_totals_Tenant PRIMARY KEY,
    Name VARCHAR(80) NOT NULL,
    Slug VARCHAR(40) NOT NULL CONSTRAINT UQ_totals_Tenant_Slug UNIQUE,
    IsActive BIT NOT NULL CONSTRAINT DF_totals_Tenant_Active DEFAULT 1,
    CreatedOn DATETIME2 NOT NULL
);
GO
CREATE TABLE totals.Employee (
    EmployeeId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_totals_Employee PRIMARY KEY,
    FullName VARCHAR(40) NOT NULL,
    Login VARCHAR(30) NOT NULL,
    PasswordHash VARCHAR(200) NOT NULL CONSTRAINT DF_totals_Employee_Hash DEFAULT 'x',
    IsManager BIT NOT NULL CONSTRAINT DF_totals_Employee_Manager DEFAULT 0,
    IsAdministrator BIT NOT NULL CONSTRAINT DF_totals_Employee_Admin DEFAULT 0
);
GO
CREATE TABLE totals.EmployeeTenant (
    EmployeeTenantId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_totals_EmployeeTenant PRIMARY KEY,
    EmployeeId INT NOT NULL CONSTRAINT FK_totals_EmployeeTenant_Employee REFERENCES totals.Employee (EmployeeId),
    TenantId INT NOT NULL CONSTRAINT FK_totals_EmployeeTenant_Tenant REFERENCES totals.Tenant (TenantId),
    Role VARCHAR(30) NULL
);
GO
CREATE TABLE totals.Invoice (
    InvoiceId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_totals_Invoice PRIMARY KEY,
    TenantId INT NOT NULL CONSTRAINT FK_totals_Invoice_Tenant REFERENCES totals.Tenant (TenantId),
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
    TenantId INT NOT NULL CONSTRAINT FK_totals_InvoiceLine_Tenant REFERENCES totals.Tenant (TenantId),
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
    TenantId INT NOT NULL CONSTRAINT FK_totals_InvoiceAllowanceCharge_Tenant REFERENCES totals.Tenant (TenantId),
    InvoiceId INT NOT NULL CONSTRAINT FK_totals_InvoiceAllowanceCharge_Invoice REFERENCES totals.Invoice (InvoiceId),
    IsCharge BIT NOT NULL,
    ReasonCode VARCHAR(10) NULL,
    BaseAmount DECIMAL(18, 2) NULL,
    Percentage DECIMAL(9, 4) NULL,
    Amount DECIMAL(18, 2) NOT NULL,
    TaxCategoryId INT NULL CONSTRAINT FK_totals_InvoiceAllowanceCharge_Category REFERENCES totals.TaxCategory (TaxCategoryId),
    TaxRate DECIMAL(9, 4) NOT NULL CONSTRAINT DF_totals_InvoiceAllowanceCharge_Rate DEFAULT 0,
    TaxAmount DECIMAL(18, 2) NOT NULL CONSTRAINT DF_totals_InvoiceAllowanceCharge_Tax DEFAULT 0
);
GO
