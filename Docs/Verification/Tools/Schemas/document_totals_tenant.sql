-- SQLite. document_totals.sql in a shared-schema multi-tenant database: the invoice, its lines and its allowance rows each carry a TenantId, for DocumentTotals with TaxLines and AllowanceCharges, ApiAuth,
-- TenantTable=Tenant, TenantMembership=EmployeeTenant:EmployeeId:TenantId:Role and UserTable=Employee
CREATE TABLE Tenant (
    TenantId INTEGER PRIMARY KEY AUTOINCREMENT,
    Name VARCHAR(80) NOT NULL,
    Slug VARCHAR(40) NOT NULL UNIQUE,
    IsActive BOOLEAN NOT NULL DEFAULT 1,
    CreatedOn DATETIME NOT NULL
);
CREATE TABLE Employee (
    EmployeeId INTEGER PRIMARY KEY AUTOINCREMENT,
    FullName VARCHAR(40) NOT NULL,
    Login VARCHAR(30) NOT NULL,
    PasswordHash VARCHAR(200) NOT NULL DEFAULT 'x',
    IsManager BOOLEAN NOT NULL DEFAULT 0,
    IsAdministrator BOOLEAN NOT NULL DEFAULT 0
);
CREATE TABLE EmployeeTenant (
    EmployeeTenantId INTEGER PRIMARY KEY AUTOINCREMENT,
    EmployeeId INTEGER NOT NULL REFERENCES Employee (EmployeeId),
    TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId),
    Role VARCHAR(30) NULL
);
CREATE TABLE Invoice (
    InvoiceId INTEGER PRIMARY KEY AUTOINCREMENT,
    TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId),
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
    TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId),
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
    TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId),
    InvoiceId INTEGER NOT NULL REFERENCES Invoice (InvoiceId),
    IsCharge BOOLEAN NOT NULL,
    ReasonCode VARCHAR(10) NULL,
    BaseAmount DECIMAL(18, 2) NULL,
    Percentage DECIMAL(9, 4) NULL,
    Amount DECIMAL(18, 2) NOT NULL,
    TaxCategoryId INTEGER NULL REFERENCES TaxCategory (TaxCategoryId),
    TaxRate DECIMAL(9, 4) NOT NULL DEFAULT 0,
    TaxAmount DECIMAL(18, 2) NOT NULL DEFAULT 0
);
INSERT INTO Tenant (Name, Slug, IsActive, CreatedOn) VALUES ('Northwind Traders', 'northwind', 1, '2026-01-05 09:00:00'), ('Contoso Services', 'contoso', 1, '2026-02-10 09:00:00');
