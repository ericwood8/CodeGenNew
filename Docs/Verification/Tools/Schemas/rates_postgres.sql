-- The table of rates.sql for PostgreSQL, for RateTables=ExchangeRate (generate with OnlyTables=ExchangeRate).
-- PGPASSWORD=... psql -h localhost -U <login> -d <database> -f rates_postgres.sql      Can be run again.
CREATE TABLE IF NOT EXISTS "ExchangeRate" (
    "ExchangeRateId" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "FromCurrency" varchar(3) NOT NULL,
    "ToCurrency" varchar(3) NOT NULL,
    "Rate" numeric(18, 6) NOT NULL,
    "RateDate" date NOT NULL,
    "Source" varchar(60) NULL,
    "Notes" varchar(200) NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "UX_ExchangeRate_PairDay" ON "ExchangeRate" ("FromCurrency", "ToCurrency", "RateDate");
CREATE TABLE IF NOT EXISTS "RatedInvoice" (
    "RatedInvoiceId" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "CustomerName" varchar(60) NOT NULL,
    "CurrencyCode" varchar(3) NOT NULL,
    "FxRate" numeric(18, 6) NOT NULL DEFAULT 1,
    "Total" numeric(18, 2) NOT NULL,
    "BaseTotal" numeric(18, 2) NOT NULL DEFAULT 0,
    "InvoiceDate" date NOT NULL,
    "Notes" varchar(200) NULL
);
CREATE TABLE IF NOT EXISTS "RateUser" (
    "RateUserId" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "FullName" varchar(40) NOT NULL,
    "Login" varchar(30) NOT NULL,
    "PasswordHash" varchar(200) NOT NULL DEFAULT 'x',
    "IsManager" boolean NOT NULL DEFAULT false,
    "IsAdministrator" boolean NOT NULL DEFAULT false
);
CREATE TABLE IF NOT EXISTS "ExchangeRateStamp" (
    "ExchangeRateStampId" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "FromCurrency" varchar(3) NOT NULL,
    "ToCurrency" varchar(3) NOT NULL,
    "Rate" numeric(18, 6) NOT NULL,
    "RateDate" timestamp NOT NULL
);
