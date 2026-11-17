-- The tables of rates.sql, rate_documents.sql and rates_stamp.sql for SQL Server, in the schema rate, for RateTables=ExchangeRate (generate with --schema rate).
-- sqlcmd -S <server> -E -C -I -b -d InvoiceSystem_test -i rates_sqlserver.sql      Can be run again: a table that exists is left alone.
IF SCHEMA_ID('rate') IS NULL
    EXEC ('CREATE SCHEMA rate');
GO
IF OBJECT_ID('rate.ExchangeRate', 'U') IS NULL
CREATE TABLE rate.ExchangeRate (
    ExchangeRateId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_rate_ExchangeRate PRIMARY KEY,
    FromCurrency VARCHAR(3) NOT NULL,
    ToCurrency VARCHAR(3) NOT NULL,
    Rate DECIMAL(18, 6) NOT NULL,
    RateDate DATE NOT NULL,
    Source VARCHAR(60) NULL,
    Notes VARCHAR(200) NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_rate_ExchangeRate_PairDay' AND object_id = OBJECT_ID('rate.ExchangeRate'))
    CREATE UNIQUE INDEX UX_rate_ExchangeRate_PairDay ON rate.ExchangeRate (FromCurrency, ToCurrency, RateDate);
GO
IF OBJECT_ID('rate.RatedInvoice', 'U') IS NULL
CREATE TABLE rate.RatedInvoice (
    RatedInvoiceId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_rate_RatedInvoice PRIMARY KEY,
    CustomerName VARCHAR(60) NOT NULL,
    CurrencyCode VARCHAR(3) NOT NULL,
    FxRate DECIMAL(18, 6) NOT NULL CONSTRAINT DF_rate_RatedInvoice_FxRate DEFAULT 1,
    Total DECIMAL(18, 2) NOT NULL,
    BaseTotal DECIMAL(18, 2) NOT NULL CONSTRAINT DF_rate_RatedInvoice_BaseTotal DEFAULT 0,
    InvoiceDate DATE NOT NULL,
    Notes VARCHAR(200) NULL
);
GO
GO
IF OBJECT_ID('rate.RateUser', 'U') IS NULL
CREATE TABLE rate.RateUser (
    RateUserId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_rate_RateUser PRIMARY KEY,
    FullName VARCHAR(40) NOT NULL,
    Login VARCHAR(30) NOT NULL,
    PasswordHash VARCHAR(200) NOT NULL CONSTRAINT DF_rate_RateUser_Hash DEFAULT 'x',
    IsManager BIT NOT NULL CONSTRAINT DF_rate_RateUser_Manager DEFAULT 0,
    IsAdministrator BIT NOT NULL CONSTRAINT DF_rate_RateUser_Admin DEFAULT 0
);
GO
IF OBJECT_ID('rate.ExchangeRateStamp', 'U') IS NULL
CREATE TABLE rate.ExchangeRateStamp (
    ExchangeRateStampId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_rate_ExchangeRateStamp PRIMARY KEY,
    FromCurrency VARCHAR(3) NOT NULL,
    ToCurrency VARCHAR(3) NOT NULL,
    Rate DECIMAL(18, 6) NOT NULL,
    RateDate DATETIME2 NOT NULL
);
GO
