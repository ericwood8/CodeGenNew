-- default_rows.sql with a soft delete flag on the address, for DefaultRowTables together with SoftDeleteTables=CustomerAddress:IsDeleted
-- A customer with several addresses of a type (billing or shipping), one of each type the default, for DefaultRowTables=CustomerAddress:IsDefault:CustomerId+AddressType
-- (the unique index is the database's own guard; the generated API clears the old mark first, in the same transaction)
CREATE TABLE Customer (
    CustomerId INTEGER PRIMARY KEY AUTOINCREMENT,
    Name VARCHAR(100) NOT NULL,
    Email VARCHAR(100) NULL,
    City VARCHAR(60) NULL,
    Phone VARCHAR(30) NULL,
    Notes VARCHAR(200) NULL
);
CREATE TABLE CustomerAddress (
    CustomerAddressId INTEGER PRIMARY KEY AUTOINCREMENT,
    CustomerId INTEGER NOT NULL REFERENCES Customer (CustomerId),
    AddressType VARCHAR(10) NOT NULL CHECK (AddressType IN ('billing', 'shipping')),
    Line1 VARCHAR(200) NOT NULL,
    Line2 VARCHAR(200) NULL,
    City VARCHAR(100) NOT NULL,
    Region VARCHAR(50) NULL,
    PostalCode VARCHAR(25) NULL,
    Country VARCHAR(100) NOT NULL,
    IsDefault BIT NOT NULL DEFAULT 0,
    IsDeleted BIT NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX UX_CustomerAddress_Default ON CustomerAddress (CustomerId, AddressType) WHERE IsDefault = 1;
