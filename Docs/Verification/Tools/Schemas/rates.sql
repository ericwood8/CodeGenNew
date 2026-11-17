-- SQLite. Exchange rates for RateTables=ExchangeRate.
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
