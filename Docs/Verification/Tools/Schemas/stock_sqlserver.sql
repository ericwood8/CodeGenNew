-- The tables of stock.sql for SQL Server, in the schema stk, for StockTables=StockLevel (generate with --schema stk).
-- sqlcmd -S <server> -E -C -I -b -d InvoiceSystem_test -i stock_sqlserver.sql      Can be run again: a table that exists is left alone.
IF SCHEMA_ID('stk') IS NULL
    EXEC ('CREATE SCHEMA stk');
GO
IF OBJECT_ID('stk.StockItem', 'U') IS NULL
CREATE TABLE stk.StockItem (
    StockItemId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_stk_StockItem PRIMARY KEY,
    Name VARCHAR(60) NOT NULL
);
GO
IF OBJECT_ID('stk.StockLevel', 'U') IS NULL
CREATE TABLE stk.StockLevel (
    StockLevelId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_stk_StockLevel PRIMARY KEY,
    ItemId INT NOT NULL CONSTRAINT FK_stk_StockLevel_Item REFERENCES stk.StockItem (StockItemId) CONSTRAINT UQ_stk_StockLevel_Item UNIQUE,
    OnHand INT NOT NULL CONSTRAINT DF_stk_StockLevel_OnHand DEFAULT 0,
    Reserved INT NOT NULL CONSTRAINT DF_stk_StockLevel_Reserved DEFAULT 0,
    ReorderLevel INT NOT NULL CONSTRAINT DF_stk_StockLevel_Reorder DEFAULT 0,
    CONSTRAINT CK_stk_StockLevel_Available CHECK (OnHand - Reserved >= 0)
);
GO
IF OBJECT_ID('stk.StockLedger', 'U') IS NULL
CREATE TABLE stk.StockLedger (
    StockLedgerId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_stk_StockLedger PRIMARY KEY,
    ItemId INT NOT NULL CONSTRAINT FK_stk_StockLedger_Item REFERENCES stk.StockItem (StockItemId),
    Kind VARCHAR(12) NOT NULL CONSTRAINT CK_stk_StockLedger_Kind CHECK (Kind IN ('receipt', 'sale', 'return', 'adjustment', 'reservation', 'release')),
    Quantity INT NOT NULL,
    OccurredOn DATETIME NOT NULL CONSTRAINT DF_stk_StockLedger_OccurredOn DEFAULT GETDATE(),
    Reference VARCHAR(60) NULL,
    ExpiresOn DATETIME NULL
);
GO
IF OBJECT_ID('stk.StockReservation', 'U') IS NULL
CREATE TABLE stk.StockReservation (
    StockReservationId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_stk_StockReservation PRIMARY KEY,
    ItemId INT NOT NULL CONSTRAINT FK_stk_StockReservation_Item REFERENCES stk.StockItem (StockItemId),
    Quantity INT NOT NULL CONSTRAINT CK_stk_StockReservation_Quantity CHECK (Quantity > 0),
    Status VARCHAR(10) NOT NULL CONSTRAINT DF_stk_StockReservation_Status DEFAULT 'held' CONSTRAINT CK_stk_StockReservation_Status CHECK (Status IN ('held', 'committed', 'released', 'expired')),
    Reference VARCHAR(60) NULL,
    CreatedOn DATETIME NOT NULL CONSTRAINT DF_stk_StockReservation_CreatedOn DEFAULT GETDATE(),
    ExpiresOn DATETIME NOT NULL,
    ResolvedOn DATETIME NULL
);
GO
