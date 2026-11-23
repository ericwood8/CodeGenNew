-- The tables of price_lists.sql for PostgreSQL, for PriceListTables (generate with OnlyTables=PriceItem,PriceList,PriceListItem).
-- PGPASSWORD=... psql -h localhost -U <login> -d <database> -f price_lists_postgres.sql      Can be run again.
CREATE TABLE IF NOT EXISTS "PriceItem" (
    "PriceItemId" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "Name" varchar(80) NOT NULL,
    "Sku" varchar(40) NULL,
    "UnitName" varchar(20) NULL,
    "Notes" varchar(200) NULL
);
CREATE TABLE IF NOT EXISTS "PriceList" (
    "PriceListId" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "Name" varchar(80) NOT NULL,
    "CurrencyCode" varchar(3) NOT NULL,
    "ValidFrom" date NOT NULL,
    "ValidTo" date NULL,
    "CustomerGroup" varchar(40) NULL
);
CREATE TABLE IF NOT EXISTS "PriceListItem" (
    "PriceListItemId" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "PriceListId" integer NOT NULL REFERENCES "PriceList" ("PriceListId"),
    "ItemId" integer NOT NULL REFERENCES "PriceItem" ("PriceItemId"),
    "MinQuantity" numeric(18, 3) NOT NULL,
    "Price" numeric(18, 4) NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "UX_PriceListItem_Break" ON "PriceListItem" ("PriceListId", "ItemId", "MinQuantity");
