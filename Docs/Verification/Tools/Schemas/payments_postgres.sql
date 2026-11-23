-- The tables of payments.sql for PostgreSQL, in the schema pay (the public schema has a type named PaymentTerms), for BankAccountTables, InstallmentTables and PaymentTermsTables (generate with --schema pay).
-- PGPASSWORD=... psql -h localhost -U <login> -d <database> -f payments_postgres.sql      Can be run again.
CREATE SCHEMA IF NOT EXISTS pay;
CREATE TABLE IF NOT EXISTS pay."PayDocument" (
    "PayDocumentId" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "Number" varchar(20) NOT NULL,
    "OrderedOn" date NOT NULL,
    "Total" numeric(12, 2) NOT NULL
);
CREATE TABLE IF NOT EXISTS pay."PaymentTerms" (
    "PaymentTermsId" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "Code" varchar(12) NOT NULL UNIQUE,
    "Name" varchar(80) NOT NULL,
    "DaysToPay" integer NOT NULL,
    "EarlyDiscountPercent" numeric(5, 2) NULL,
    "EarlyDiscountDays" integer NULL,
    "LatePenaltyPercent" numeric(5, 2) NULL
);
CREATE TABLE IF NOT EXISTS pay."PartyBankAccount" (
    "PartyBankAccountId" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "AccountHolder" varchar(100) NOT NULL,
    "Iban" varchar(34) NULL,
    "AccountNumber" varchar(20) NULL,
    "RoutingNumber" varchar(12) NULL,
    "BankName" varchar(100) NULL
);
CREATE TABLE IF NOT EXISTS pay."PaymentMeans" (
    "PaymentMeansId" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "PayDocumentId" integer NOT NULL REFERENCES pay."PayDocument" ("PayDocumentId"),
    "PaymentTermsId" integer NULL REFERENCES pay."PaymentTerms" ("PaymentTermsId"),
    "MeansCode" varchar(14) NOT NULL,
    "DueDate" date NULL
);
CREATE TABLE IF NOT EXISTS pay."PaymentInstallment" (
    "PaymentInstallmentId" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "PayDocumentId" integer NOT NULL REFERENCES pay."PayDocument" ("PayDocumentId"),
    "SequenceNo" integer NOT NULL,
    "DueDate" date NOT NULL,
    "Amount" numeric(12, 2) NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "UX_PaymentInstallment_Sequence" ON pay."PaymentInstallment" ("PayDocumentId", "SequenceNo");
CREATE TABLE IF NOT EXISTS pay."PayTransaction" (
    "PayTransactionId" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "PayDocumentId" integer NOT NULL REFERENCES pay."PayDocument" ("PayDocumentId"),
    "Kind" varchar(12) NOT NULL,
    "Status" varchar(12) NOT NULL,
    "Amount" numeric(12, 2) NOT NULL,
    "CurrencyCode" char(3) NOT NULL DEFAULT 'USD',
    "GatewayId" varchar(100) NOT NULL UNIQUE,
    "IdempotencyKey" varchar(100) NOT NULL UNIQUE,
    "Payload" text NULL,
    "CreatedOn" timestamp NOT NULL DEFAULT now()
);
