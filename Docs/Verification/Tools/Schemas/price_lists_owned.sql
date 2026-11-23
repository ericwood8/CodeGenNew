-- SQLite. price_lists.sql with an Employee table (the user table) and an owner column on the price list only (its price rows are scoped through their list), for PriceListTables, ApiAuth, OwnedTables=PriceList
-- and UserOwnerColumn=EmployeeId
CREATE TABLE Employee (
    EmployeeId INTEGER PRIMARY KEY AUTOINCREMENT,
    Login VARCHAR(30) NOT NULL,
    PasswordHash VARCHAR(200) NOT NULL DEFAULT 'x',
    IsManager BOOLEAN NOT NULL DEFAULT 0,
    IsAdministrator BOOLEAN NOT NULL DEFAULT 0
);
CREATE TABLE PriceItem (
    PriceItemId INTEGER PRIMARY KEY AUTOINCREMENT,
    Name VARCHAR(80) NOT NULL,
    Sku VARCHAR(40) NULL,
    UnitName VARCHAR(20) NULL,
    Notes VARCHAR(200) NULL
);
CREATE TABLE PriceList (
    PriceListId INTEGER PRIMARY KEY AUTOINCREMENT,
    EmployeeId INTEGER NOT NULL REFERENCES Employee (EmployeeId),
    Name VARCHAR(80) NOT NULL,
    CurrencyCode VARCHAR(3) NOT NULL,
    ValidFrom DATE NOT NULL,
    ValidTo DATE NULL,
    CustomerGroup VARCHAR(40) NULL
);
CREATE TABLE PriceListItem (
    PriceListItemId INTEGER PRIMARY KEY AUTOINCREMENT,
    PriceListId INTEGER NOT NULL REFERENCES PriceList (PriceListId),
    ItemId INTEGER NOT NULL REFERENCES PriceItem (PriceItemId),
    MinQuantity DECIMAL(18, 3) NOT NULL,
    Price DECIMAL(18, 4) NOT NULL
);
CREATE UNIQUE INDEX UX_PriceListItem_Break ON PriceListItem (PriceListId, ItemId, MinQuantity);
