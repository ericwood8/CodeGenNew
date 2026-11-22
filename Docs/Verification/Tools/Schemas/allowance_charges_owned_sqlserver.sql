-- The tables of allowance_charges_owned.sql for SQL Server, in the schema allowown (generate with --schema allowown). live_allowance_scoped_run.py drops and makes them again on every run:
-- sqlcmd -S <server> -E -C -I -b -d InvoiceSystem_test -i allowance_charges_owned_sqlserver.sql
IF SCHEMA_ID('allowown') IS NULL
    EXEC ('CREATE SCHEMA allowown');
GO
DROP TABLE IF EXISTS allowown.InvoiceAllowanceCharge;
DROP TABLE IF EXISTS allowown.Employee;
GO
CREATE TABLE allowown.Employee (
    EmployeeId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_allowown_Employee PRIMARY KEY,
    Login VARCHAR(30) NOT NULL,
    PasswordHash VARCHAR(200) NOT NULL CONSTRAINT DF_allowown_Employee_Hash DEFAULT 'x',
    IsManager BIT NOT NULL CONSTRAINT DF_allowown_Employee_Manager DEFAULT 0,
    IsAdministrator BIT NOT NULL CONSTRAINT DF_allowown_Employee_Admin DEFAULT 0
);
GO
CREATE TABLE allowown.InvoiceAllowanceCharge (
    InvoiceAllowanceChargeId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_allowown_InvoiceAllowanceCharge PRIMARY KEY,
    EmployeeId INT NOT NULL,
    InvoiceId INT NOT NULL,
    IsCharge BIT NOT NULL,
    ReasonCode VARCHAR(10) NULL,
    BaseAmount DECIMAL(18, 2) NULL,
    Percentage DECIMAL(9, 4) NULL,
    Amount DECIMAL(18, 2) NOT NULL
);
GO
