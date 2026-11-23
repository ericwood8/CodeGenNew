-- The tables of stock.sql for PostgreSQL, in the schema stk, for StockTables=StockLevel (generate with --schema stk).
-- PGPASSWORD=... psql -h localhost -U <login> -d <database> -f stock_postgres.sql      Can be run again.
CREATE SCHEMA IF NOT EXISTS stk;
CREATE TABLE IF NOT EXISTS stk."StockItem" (
    "StockItemId" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "Name" varchar(60) NOT NULL
);
CREATE TABLE IF NOT EXISTS stk."StockLevel" (
    "StockLevelId" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "ItemId" integer NOT NULL UNIQUE REFERENCES stk."StockItem" ("StockItemId"),
    "OnHand" integer NOT NULL DEFAULT 0,
    "Reserved" integer NOT NULL DEFAULT 0,
    "ReorderLevel" integer NOT NULL DEFAULT 0,
    CHECK ("OnHand" - "Reserved" >= 0)
);
CREATE TABLE IF NOT EXISTS stk."StockLedger" (
    "StockLedgerId" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "ItemId" integer NOT NULL REFERENCES stk."StockItem" ("StockItemId"),
    "Kind" varchar(12) NOT NULL CHECK ("Kind" IN ('receipt', 'sale', 'return', 'adjustment', 'reservation', 'release')),
    "Quantity" integer NOT NULL,
    "OccurredOn" timestamp NOT NULL DEFAULT now(),
    "Reference" varchar(60) NULL,
    "ExpiresOn" timestamp NULL
);
CREATE TABLE IF NOT EXISTS stk."StockReservation" (
    "StockReservationId" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "ItemId" integer NOT NULL REFERENCES stk."StockItem" ("StockItemId"),
    "Quantity" integer NOT NULL CHECK ("Quantity" > 0),
    "Status" varchar(10) NOT NULL DEFAULT 'held' CHECK ("Status" IN ('held', 'committed', 'released', 'expired')),
    "Reference" varchar(60) NULL,
    "CreatedOn" timestamp NOT NULL DEFAULT now(),
    "ExpiresOn" timestamp NOT NULL,
    "ResolvedOn" timestamp NULL
);
