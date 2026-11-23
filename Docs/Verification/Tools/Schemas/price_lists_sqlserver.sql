-- The tables of price_lists.sql for SQL Server, in the schema price, for PriceListTables (generate with --schema price).
-- sqlcmd -S <server> -E -C -I -b -d InvoiceSystem_test -i price_lists_sqlserver.sql      Can be run again: a table that exists is left alone.
IF SCHEMA_ID('price') IS NULL
    EXEC ('CREATE SCHEMA price');
GO
IF OBJECT_ID('price.PriceItem', 'U') IS NULL
CREATE TABLE price.PriceItem (
    PriceItemId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_price_PriceItem PRIMARY KEY,
    Name VARCHAR(80) NOT NULL,
    Sku VARCHAR(40) NULL,
    UnitName VARCHAR(20) NULL,
    Notes VARCHAR(200) NULL
);
GO
IF OBJECT_ID('price.PriceList', 'U') IS NULL
CREATE TABLE price.PriceList (
    PriceListId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_price_PriceList PRIMARY KEY,
    Name VARCHAR(80) NOT NULL,
    CurrencyCode VARCHAR(3) NOT NULL,
    ValidFrom DATE NOT NULL,
    ValidTo DATE NULL,
    CustomerGroup VARCHAR(40) NULL
);
GO
IF OBJECT_ID('price.PriceListItem', 'U') IS NULL
CREATE TABLE price.PriceListItem (
    PriceListItemId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_price_PriceListItem PRIMARY KEY,
    PriceListId INT NOT NULL CONSTRAINT FK_price_PriceListItem_List REFERENCES price.PriceList (PriceListId),
    ItemId INT NOT NULL CONSTRAINT FK_price_PriceListItem_Item REFERENCES price.PriceItem (PriceItemId),
    MinQuantity DECIMAL(18, 3) NOT NULL,
    Price DECIMAL(18, 4) NOT NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_price_PriceListItem_Break' AND object_id = OBJECT_ID('price.PriceListItem'))
    CREATE UNIQUE INDEX UX_price_PriceListItem_Break ON price.PriceListItem (PriceListId, ItemId, MinQuantity);
GO
