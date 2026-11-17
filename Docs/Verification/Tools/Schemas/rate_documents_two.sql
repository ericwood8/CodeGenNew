-- SQLite. rate_documents.sql and a second set: CardRate and CardInvoice, for
-- RateTables=ExchangeRate,CardRate and RateDocuments=SalesInvoice:CurrencyCode:FxRate:Total:BaseTotal:InvoiceDate,CardInvoice:CurrencyCode:FxRate:Total:BaseTotal:InvoiceDate:CardRate:EUR with BaseCurrency=USD:
-- a document names its own rate table and its own base currency.
CREATE TABLE ExchangeRate (
    ExchangeRateId INTEGER PRIMARY KEY AUTOINCREMENT,
    FromCurrency VARCHAR(3) NOT NULL,
    ToCurrency VARCHAR(3) NOT NULL,
    Rate DECIMAL(18, 6) NOT NULL,
    RateDate DATE NOT NULL,
    Source VARCHAR(60) NULL,
    Notes VARCHAR(200) NULL
);
CREATE UNIQUE INDEX UX_ExchangeRate_PairDay ON ExchangeRate (FromCurrency, ToCurrency, RateDate);
CREATE TABLE SalesInvoice (
    SalesInvoiceId INTEGER PRIMARY KEY AUTOINCREMENT,
    CustomerName VARCHAR(60) NOT NULL,
    CurrencyCode VARCHAR(3) NOT NULL,
    FxRate DECIMAL(18, 6) NOT NULL DEFAULT 1,
    Total DECIMAL(18, 2) NOT NULL,
    BaseTotal DECIMAL(18, 2) NOT NULL DEFAULT 0,
    InvoiceDate DATE NOT NULL,
    Notes VARCHAR(200) NULL
);
CREATE TABLE CardRate (
    CardRateId INTEGER PRIMARY KEY AUTOINCREMENT,
    FromCurrency VARCHAR(3) NOT NULL,
    ToCurrency VARCHAR(3) NOT NULL,
    Rate DECIMAL(18, 6) NOT NULL,
    RateDate DATE NOT NULL,
    Source VARCHAR(60) NULL,
    Notes VARCHAR(200) NULL
);
CREATE UNIQUE INDEX UX_CardRate_PairDay ON CardRate (FromCurrency, ToCurrency, RateDate);
CREATE TABLE CardInvoice (
    CardInvoiceId INTEGER PRIMARY KEY AUTOINCREMENT,
    CustomerName VARCHAR(60) NOT NULL,
    CurrencyCode VARCHAR(3) NOT NULL,
    FxRate DECIMAL(18, 6) NOT NULL DEFAULT 1,
    Total DECIMAL(18, 2) NOT NULL,
    BaseTotal DECIMAL(18, 2) NOT NULL DEFAULT 0,
    InvoiceDate DATE NOT NULL,
    Notes VARCHAR(200) NULL
);
