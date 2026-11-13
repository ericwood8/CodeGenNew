-- The tables of default_rows.sql for SQL Server, in a schema of their own (dflt) so that they sit beside the InvoiceSystem tables of the same names. Can be run again: a table that exists is left alone.
-- sqlcmd -S <server> -E -C -I -b -d InvoiceSystem_test -i default_rows_sqlserver.sql
-- for DefaultRowTables=CustomerAddress:IsDefault:CustomerId+AddressType  (generate with --schema dflt)
IF SCHEMA_ID('dflt') IS NULL
    EXEC ('CREATE SCHEMA dflt');
GO
IF OBJECT_ID('dflt.Customer', 'U') IS NULL
CREATE TABLE dflt.Customer (
    CustomerId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_dflt_Customer PRIMARY KEY,
    Name VARCHAR(100) NOT NULL,
    Email VARCHAR(100) NULL,
    City VARCHAR(60) NULL,
    Phone VARCHAR(30) NULL,
    Notes VARCHAR(200) NULL
);
GO
IF OBJECT_ID('dflt.CustomerAddress', 'U') IS NULL
CREATE TABLE dflt.CustomerAddress (
    CustomerAddressId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_dflt_CustomerAddress PRIMARY KEY,
    CustomerId INT NOT NULL CONSTRAINT FK_dflt_CustomerAddress_Customer REFERENCES dflt.Customer (CustomerId),
    AddressType VARCHAR(10) NOT NULL CONSTRAINT CK_dflt_CustomerAddress_Type CHECK (AddressType IN ('billing', 'shipping')),
    Line1 VARCHAR(200) NOT NULL,
    Line2 VARCHAR(200) NULL,
    City VARCHAR(100) NOT NULL,
    Region VARCHAR(50) NULL,
    PostalCode VARCHAR(25) NULL,
    Country VARCHAR(100) NOT NULL,
    IsDefault BIT NOT NULL CONSTRAINT DF_dflt_CustomerAddress_IsDefault DEFAULT 0
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_dflt_CustomerAddress_Default' AND object_id = OBJECT_ID('dflt.CustomerAddress'))
    CREATE UNIQUE INDEX UX_dflt_CustomerAddress_Default ON dflt.CustomerAddress (CustomerId, AddressType) WHERE IsDefault = 1;
GO
