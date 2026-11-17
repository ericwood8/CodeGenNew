-- SQLite. rates.sql with an empty Employee table for the sign-in, for RateTables=ExchangeRate with ApiAuth
-- (UserTable=Employee, UserNameColumn=Login, UserRoleColumn=IsManager, UserAdminColumn=IsAdministrator). POST /api/auth/first-person adds the first administrator.
CREATE TABLE Employee (
    EmployeeId INTEGER PRIMARY KEY AUTOINCREMENT,
    FullName VARCHAR(40) NOT NULL,
    Login VARCHAR(30) NOT NULL,
    PasswordHash VARCHAR(200) NOT NULL DEFAULT 'x',
    IsManager BOOLEAN NOT NULL DEFAULT 0,
    IsAdministrator BOOLEAN NOT NULL DEFAULT 0
);
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
