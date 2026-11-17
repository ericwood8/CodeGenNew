-- SQLite. Exchange rates whose day is a date and time, for RateTables=ExchangeRateStamp: python rate_check.py <base url> ExchangeRateStamp.
CREATE TABLE ExchangeRateStamp (
    ExchangeRateStampId INTEGER PRIMARY KEY AUTOINCREMENT,
    FromCurrency VARCHAR(3) NOT NULL,
    ToCurrency VARCHAR(3) NOT NULL,
    Rate DOUBLE NOT NULL,
    RateDate DATETIME NOT NULL
);
