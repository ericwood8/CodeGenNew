-- The tables of tax_lines.sql for PostgreSQL under other names (the sample database already holds a TaxCategory), for
-- TaxLines=LevyLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:LevyCategory:Rate:ValidFrom:ValidTo:LineDate:InvoiceId:Code (generate with OnlyTables=LevyCategory,LevyLine).
-- PGPASSWORD=... psql -h localhost -U <login> -d <database> -f tax_lines_postgres.sql      Can be run again.
CREATE TABLE IF NOT EXISTS "LevyCategory" (
    "TaxCategoryId" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "Code" varchar(10) NOT NULL,
    "Name" varchar(60) NOT NULL,
    "Rate" numeric(9, 4) NOT NULL,
    "ExemptionReason" varchar(200) NULL,
    "ValidFrom" date NOT NULL DEFAULT '2000-01-01',
    "ValidTo" date NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "UX_LevyCategory_Code" ON "LevyCategory" ("Code");
CREATE TABLE IF NOT EXISTS "LevyLine" (
    "InvoiceLineId" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "InvoiceId" integer NOT NULL DEFAULT 1,
    "LineDate" date NOT NULL DEFAULT '2024-06-01',
    "Description" varchar(100) NOT NULL,
    "TaxCategoryId" integer NULL REFERENCES "LevyCategory" ("TaxCategoryId"),
    "TaxRate" numeric(9, 4) NOT NULL DEFAULT 0,
    "LineTotal" numeric(18, 2) NOT NULL,
    "TaxAmount" numeric(18, 2) NOT NULL DEFAULT 0
);
