-- SQLite. tax_lines.sql with a category that cannot be NULL on the line (the desktop sample row then needs a category row), for
-- TaxLines=InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory:Rate and, with dating and subtotals,
-- TaxLines=InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory:Rate:ValidFrom:ValidTo:LineDate:InvoiceId:Code
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
    InvoiceId INTEGER NOT NULL DEFAULT 1,
    LineDate DATE NOT NULL DEFAULT '2024-06-01',
    Description VARCHAR(100) NOT NULL,
    TaxCategoryId INTEGER NOT NULL REFERENCES TaxCategory (TaxCategoryId),
    TaxRate DECIMAL(9, 4) NOT NULL DEFAULT 0,
    LineTotal DECIMAL(18, 2) NOT NULL,
    TaxAmount DECIMAL(18, 2) NOT NULL DEFAULT 0
);
