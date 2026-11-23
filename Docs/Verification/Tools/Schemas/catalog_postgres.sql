-- The tables of catalog.sql for PostgreSQL, in the schema cat, for FacetTables=ProductAttribute (generate with --schema cat).
-- PGPASSWORD=... psql -h localhost -U <login> -d <database> -f catalog_postgres.sql      Can be run again: the rows are added once (an older attribute table without NumberValue is rebuilt).
CREATE SCHEMA IF NOT EXISTS cat;
DO $$
BEGIN
    IF to_regclass('cat."ProductAttribute"') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'cat' AND table_name = 'ProductAttribute' AND column_name = 'NumberValue') THEN
        DROP TABLE cat."ProductAttribute";
        TRUNCATE cat."Product" RESTART IDENTITY;
    END IF;
END $$;
CREATE TABLE IF NOT EXISTS cat."Product" (
    "ProductId" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "CatalogNumber" varchar(40) NOT NULL UNIQUE,
    "Name" varchar(120) NOT NULL,
    "Description" varchar(255) NULL,
    "Manufacturer" varchar(60) NOT NULL
);
CREATE TABLE IF NOT EXISTS cat."ProductAttribute" (
    "ProductAttributeId" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "ProductId" integer NOT NULL REFERENCES cat."Product" ("ProductId"),
    "Name" varchar(40) NOT NULL,
    "Value" varchar(80) NULL,
    "NumberValue" numeric(18, 4) NULL
);
CREATE INDEX IF NOT EXISTS "IX_cat_ProductAttribute_Product_Name" ON cat."ProductAttribute" ("ProductId", "Name");
CREATE INDEX IF NOT EXISTS "IX_cat_ProductAttribute_Name_Value" ON cat."ProductAttribute" ("Name", "Value");
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM cat."Product") THEN
INSERT INTO cat."Product" ("CatalogNumber", "Name", "Description", "Manufacturer") VALUES
 ('NM-B-14/2', 'Building Wire NM-B Romex 14-2 AWG Copper White 100 Ft', 'Non-metallic sheathed cable', 'Southwire'),
 ('NM-B-12/2', 'Building Wire NM-B Romex 12-2 AWG Copper Yellow 250 Ft', 'Non-metallic sheathed cable', 'Southwire'),
 ('NM-B-14/3', 'Building Wire NM-B Romex 14-3 AWG Copper Blue 250 Ft', 'Non-metallic sheathed cable', 'Southwire'),
 ('MC12/2', 'Armored Cable Aluminum MC 12-2 AWG 2 Conductor Solid', 'Metal clad cable', 'AFC Cable'),
 ('EMT-3/4', 'EMT Steel Conduit Straight 3/4 In 10 Ft Galvanized', 'Electrical metallic tubing', 'Allied Tube'),
 ('EMT-1', 'EMT Steel Conduit Straight 1 In 10 Ft Galvanized', 'Electrical metallic tubing', 'Allied Tube'),
 ('232', '4 In Square Box 2-1/8 In Steel', 'Screw on nail on', 'RACO'),
 ('560-DC2', 'Single Barrel MC AC Connector 3/8 In Die-Cast Zinc', NULL, 'Bridgeport'),
 ('PS-210', 'Strut Channel With Holes Half Slotted Steel 10 Ft', NULL, 'Power-Strut'),
 ('46-1700', 'Electrical Tape General Purpose 60 Ft 7 mm', 'Vinyl tape 80 degree C', 'Ideal'),
 ('LED-A19-9', 'LED Lamp A19 9 Watt 120 Volt', NULL, 'Sylvania'),
 ('SW-1221', 'Toggle Switch 15 Amp 120 Volt White', NULL, 'Pass and Seymour');
INSERT INTO cat."ProductAttribute" ("ProductId", "Name", "Value", "NumberValue") VALUES
 (1, 'Voltage', '600 V', 600), (1, 'Material', 'Copper', NULL), (1, 'Size', '14 AWG', NULL), (1, 'Color', 'White', NULL),
 (2, 'Voltage', '600 V', 600), (2, 'Material', 'Copper', NULL), (2, 'Size', '12 AWG', NULL), (2, 'Color', 'Yellow', NULL),
 (3, 'Voltage', '600 V', 600), (3, 'Material', 'Copper', NULL), (3, 'Size', '14 AWG', NULL), (3, 'Color', 'Blue', NULL),
 (4, 'Voltage', '600 V', 600), (4, 'Material', 'Aluminum', NULL), (4, 'Size', '12 AWG', NULL),
 (5, 'Material', 'Steel', NULL), (5, 'Size', '3/4 In', NULL),
 (6, 'Material', 'Steel', NULL), (6, 'Size', '1 In', NULL),
 (7, 'Material', 'Steel', NULL), (7, 'Size', '4 In', NULL),
 (8, 'Material', 'Zinc', NULL), (8, 'Size', '3/8 In', NULL),
 (9, 'Material', 'Steel', NULL), (9, 'Size', '1-5/8 In', NULL),
 (10, 'Material', 'Vinyl', NULL), (10, 'Voltage', '600 V', 600),
 (11, 'Voltage', '120 V', 120), (11, 'Color', 'White', NULL),
 (12, 'Voltage', '120 V', 120), (12, 'Color', 'White', NULL), (12, 'Amperage', '15 A', 15),
 (12, 'Amperage', NULL, NULL),
 (1, 'Length', '100 Ft', 100), (2, 'Length', '250 Ft', 250), (3, 'Length', '250 Ft', 250), (5, 'Length', '10 Ft', 10), (6, 'Length', '10 Ft', 10), (7, 'Length', '10 Ft', 10);
    END IF;
END $$;
