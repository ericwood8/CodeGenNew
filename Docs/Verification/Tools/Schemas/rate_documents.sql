-- SQLite. Exchange rates and a sales invoice that keeps the rate it was written with, for
-- RateTables=ExchangeRate RateDocuments=SalesInvoice:CurrencyCode:FxRate:Total:BaseTotal:InvoiceDate BaseCurrency=USD (and RateInverse=true).
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
