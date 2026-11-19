-- The tables of tax_lines.sql for SQL Server in a tax schema, for
-- TaxLines=InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory:Rate:ValidFrom:ValidTo:LineDate:InvoiceId:Code (generate with --schema tax).
-- sqlcmd -S <server> -E -C -d <database> -I -f 65001 -i tax_lines_sqlserver.sql      Can be run again.
IF SCHEMA_ID('tax') IS NULL EXEC('CREATE SCHEMA tax');
GO
IF OBJECT_ID('tax.TaxCategory') IS NULL
    CREATE TABLE tax.TaxCategory (
        TaxCategoryId INT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_tax_TaxCategory PRIMARY KEY,
        Code VARCHAR(10) NOT NULL,
        Name VARCHAR(60) NOT NULL,
        Rate DECIMAL(9, 4) NOT NULL,
        ExemptionReason VARCHAR(200) NULL,
        ValidFrom DATE NOT NULL CONSTRAINT DF_tax_TaxCategory_ValidFrom DEFAULT '2000-01-01',
        ValidTo DATE NULL,
        CONSTRAINT UX_tax_TaxCategory_Code UNIQUE (Code)
    );
GO
IF OBJECT_ID('tax.InvoiceLine') IS NULL
    CREATE TABLE tax.InvoiceLine (
        InvoiceLineId INT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_tax_InvoiceLine PRIMARY KEY,
        InvoiceId INT NOT NULL CONSTRAINT DF_tax_InvoiceLine_InvoiceId DEFAULT 1,
        LineDate DATE NOT NULL CONSTRAINT DF_tax_InvoiceLine_LineDate DEFAULT '2024-06-01',
        Description VARCHAR(100) NOT NULL,
        TaxCategoryId INT NULL CONSTRAINT FK_tax_InvoiceLine_Category REFERENCES tax.TaxCategory (TaxCategoryId),
        TaxRate DECIMAL(9, 4) NOT NULL CONSTRAINT DF_tax_InvoiceLine_TaxRate DEFAULT 0,
        LineTotal DECIMAL(18, 2) NOT NULL,
        TaxAmount DECIMAL(18, 2) NOT NULL CONSTRAINT DF_tax_InvoiceLine_TaxAmount DEFAULT 0
    );
GO
