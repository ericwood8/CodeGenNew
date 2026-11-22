-- The tables of allowance_charges_tenant.sql for SQL Server, in the schema allowten (generate with --schema allowten). live_allowance_scoped_run.py drops and makes them again on every run:
-- sqlcmd -S <server> -E -C -I -b -d InvoiceSystem_test -i allowance_charges_tenant_sqlserver.sql
IF SCHEMA_ID('allowten') IS NULL
    EXEC ('CREATE SCHEMA allowten');
GO
DROP TABLE IF EXISTS allowten.InvoiceAllowanceCharge;
DROP TABLE IF EXISTS allowten.EmployeeTenant;
DROP TABLE IF EXISTS allowten.Employee;
DROP TABLE IF EXISTS allowten.Tenant;
GO
CREATE TABLE allowten.Tenant (
    TenantId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_allowten_Tenant PRIMARY KEY,
    Name VARCHAR(80) NOT NULL,
    Slug VARCHAR(40) NOT NULL CONSTRAINT UQ_allowten_Tenant_Slug UNIQUE,
    IsActive BIT NOT NULL CONSTRAINT DF_allowten_Tenant_Active DEFAULT 1,
    CreatedOn DATETIME2 NOT NULL
);
GO
CREATE TABLE allowten.Employee (
    EmployeeId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_allowten_Employee PRIMARY KEY,
    FullName VARCHAR(40) NOT NULL,
    Login VARCHAR(30) NOT NULL,
    PasswordHash VARCHAR(200) NOT NULL CONSTRAINT DF_allowten_Employee_Hash DEFAULT 'x',
    IsManager BIT NOT NULL CONSTRAINT DF_allowten_Employee_Manager DEFAULT 0,
    IsAdministrator BIT NOT NULL CONSTRAINT DF_allowten_Employee_Admin DEFAULT 0
);
GO
CREATE TABLE allowten.EmployeeTenant (
    EmployeeTenantId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_allowten_EmployeeTenant PRIMARY KEY,
    EmployeeId INT NOT NULL CONSTRAINT FK_allowten_EmployeeTenant_Employee REFERENCES allowten.Employee (EmployeeId),
    TenantId INT NOT NULL CONSTRAINT FK_allowten_EmployeeTenant_Tenant REFERENCES allowten.Tenant (TenantId),
    Role VARCHAR(30) NULL
);
GO
CREATE TABLE allowten.InvoiceAllowanceCharge (
    InvoiceAllowanceChargeId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_allowten_InvoiceAllowanceCharge PRIMARY KEY,
    TenantId INT NOT NULL CONSTRAINT FK_allowten_InvoiceAllowanceCharge_Tenant REFERENCES allowten.Tenant (TenantId),
    InvoiceId INT NOT NULL,
    IsCharge BIT NOT NULL,
    ReasonCode VARCHAR(10) NULL,
    BaseAmount DECIMAL(18, 2) NULL,
    Percentage DECIMAL(9, 4) NULL,
    Amount DECIMAL(18, 2) NOT NULL
);
GO
INSERT INTO allowten.Tenant (Name, Slug, IsActive, CreatedOn) VALUES ('Northwind Traders', 'northwind', 1, '2026-01-05 09:00:00'), ('Contoso Services', 'contoso', 1, '2026-02-10 09:00:00');
GO
