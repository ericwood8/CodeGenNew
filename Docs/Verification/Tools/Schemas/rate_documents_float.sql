-- SQLite. rate_documents.sql with floating point numbers and a date and time day, for the same settings: the Rust API reads DOUBLE as f64 (over SQLite) and DATETIME as NaiveDateTime,
-- the Python API reads DATETIME as datetime. The rate and the invoice differ in nothing else.
CREATE TABLE ExchangeRate (
    ExchangeRateId INTEGER PRIMARY KEY AUTOINCREMENT,
    FromCurrency VARCHAR(3) NOT NULL,
    ToCurrency VARCHAR(3) NOT NULL,
    Rate DOUBLE NOT NULL,
    RateDate DATETIME NOT NULL,
    Source VARCHAR(60) NULL,
    Notes VARCHAR(200) NULL
);
CREATE TABLE SalesInvoice (
    SalesInvoiceId INTEGER PRIMARY KEY AUTOINCREMENT,
    CustomerName VARCHAR(60) NOT NULL,
    CurrencyCode VARCHAR(3) NOT NULL,
    FxRate DOUBLE NOT NULL DEFAULT 1,
    Total DOUBLE NOT NULL,
    BaseTotal DOUBLE NOT NULL DEFAULT 0,
    InvoiceDate DATETIME NOT NULL,
    Notes VARCHAR(200) NULL
);
