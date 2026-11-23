-- SQLite. A stock level table with its ledger and its holds for
-- StockTables=StockLevel
CREATE TABLE StockItem (
    StockItemId INTEGER PRIMARY KEY AUTOINCREMENT,
    Name VARCHAR(60) NOT NULL
);
CREATE TABLE StockLevel (
    StockLevelId INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId INTEGER NOT NULL UNIQUE REFERENCES StockItem (StockItemId),
    OnHand INTEGER NOT NULL DEFAULT 0,
    Reserved INTEGER NOT NULL DEFAULT 0,
    ReorderLevel INTEGER NOT NULL DEFAULT 0,
    CHECK (OnHand - Reserved >= 0)
);
CREATE TABLE StockLedger (
    StockLedgerId INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId INTEGER NOT NULL REFERENCES StockItem (StockItemId),
    Kind VARCHAR(12) NOT NULL CHECK (Kind IN ('receipt', 'sale', 'return', 'adjustment', 'reservation', 'release')),
    Quantity INTEGER NOT NULL,
    OccurredOn DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    Reference VARCHAR(60) NULL,
    ExpiresOn DATETIME NULL
);
CREATE TABLE StockReservation (
    StockReservationId INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId INTEGER NOT NULL REFERENCES StockItem (StockItemId),
    Quantity INTEGER NOT NULL CHECK (Quantity > 0),
    Status VARCHAR(10) NOT NULL DEFAULT 'held' CHECK (Status IN ('held', 'committed', 'released', 'expired')),
    Reference VARCHAR(60) NULL,
    CreatedOn DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ExpiresOn DATETIME NOT NULL,
    ResolvedOn DATETIME NULL
);
CREATE INDEX IX_StockLedger_Item ON StockLedger (ItemId, OccurredOn);
CREATE INDEX IX_StockReservation_Expiry ON StockReservation (Status, ExpiresOn);
