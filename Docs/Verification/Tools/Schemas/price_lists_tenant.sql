-- SQLite. price_lists.sql in a shared-schema multi-tenant database: the item, the price list and its price rows each carry a TenantId, for PriceListTables, ApiAuth, TenantTable=Tenant,
-- TenantMembership=EmployeeTenant:EmployeeId:TenantId:Role and UserTable=Employee
CREATE TABLE Tenant (
    TenantId INTEGER PRIMARY KEY AUTOINCREMENT,
    Name VARCHAR(80) NOT NULL,
    Slug VARCHAR(40) NOT NULL UNIQUE,
    IsActive BOOLEAN NOT NULL DEFAULT 1,
    CreatedOn DATETIME NOT NULL
);
CREATE TABLE Employee (
    EmployeeId INTEGER PRIMARY KEY AUTOINCREMENT,
    FullName VARCHAR(40) NOT NULL,
    Login VARCHAR(30) NOT NULL,
    PasswordHash VARCHAR(200) NOT NULL DEFAULT 'x',
    IsManager BOOLEAN NOT NULL DEFAULT 0,
    IsAdministrator BOOLEAN NOT NULL DEFAULT 0
);
CREATE TABLE EmployeeTenant (
    EmployeeTenantId INTEGER PRIMARY KEY AUTOINCREMENT,
    EmployeeId INTEGER NOT NULL REFERENCES Employee (EmployeeId),
    TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId),
    Role VARCHAR(30) NULL
);
CREATE TABLE PriceItem (
    PriceItemId INTEGER PRIMARY KEY AUTOINCREMENT,
    TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId),
    Name VARCHAR(80) NOT NULL,
    Sku VARCHAR(40) NULL,
    UnitName VARCHAR(20) NULL
);
CREATE TABLE PriceList (
    PriceListId INTEGER PRIMARY KEY AUTOINCREMENT,
    TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId),
    Name VARCHAR(80) NOT NULL,
    CurrencyCode VARCHAR(3) NOT NULL,
    ValidFrom DATE NOT NULL,
    ValidTo DATE NULL,
    CustomerGroup VARCHAR(40) NULL
);
CREATE TABLE PriceListItem (
    PriceListItemId INTEGER PRIMARY KEY AUTOINCREMENT,
    TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId),
    PriceListId INTEGER NOT NULL REFERENCES PriceList (PriceListId),
    ItemId INTEGER NOT NULL REFERENCES PriceItem (PriceItemId),
    MinQuantity DECIMAL(18, 3) NOT NULL,
    Price DECIMAL(18, 4) NOT NULL
);
CREATE UNIQUE INDEX UX_PriceListItem_Break ON PriceListItem (PriceListId, ItemId, MinQuantity);
