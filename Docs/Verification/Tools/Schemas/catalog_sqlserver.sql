-- The tables of catalog.sql for SQL Server, in the schema cat, for FacetTables=ProductAttribute (generate with --schema cat).
-- sqlcmd -S <server> -E -C -I -b -d InvoiceSystem_test -i catalog_sqlserver.sql      Can be run again: a table that exists is left alone and the rows are added once (an older attribute table without NumberValue is rebuilt).
IF SCHEMA_ID('cat') IS NULL
    EXEC ('CREATE SCHEMA cat');
GO
IF OBJECT_ID('cat.ProductAttribute', 'U') IS NOT NULL AND COL_LENGTH('cat.ProductAttribute', 'NumberValue') IS NULL
    DROP TABLE cat.ProductAttribute;
GO
IF OBJECT_ID('cat.Product', 'U') IS NOT NULL AND OBJECT_ID('cat.ProductAttribute', 'U') IS NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE referenced_object_id = OBJECT_ID('cat.Product'))
    TRUNCATE TABLE cat.Product;
GO
IF OBJECT_ID('cat.Product', 'U') IS NULL
CREATE TABLE cat.Product (
    ProductId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cat_Product PRIMARY KEY,
    CatalogNumber VARCHAR(40) NOT NULL CONSTRAINT UQ_cat_Product_CatalogNumber UNIQUE,
    Name VARCHAR(120) NOT NULL,
    Description VARCHAR(255) NULL,
    Manufacturer VARCHAR(60) NOT NULL
);
GO
IF OBJECT_ID('cat.ProductAttribute', 'U') IS NULL
CREATE TABLE cat.ProductAttribute (
    ProductAttributeId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cat_ProductAttribute PRIMARY KEY,
    ProductId INT NOT NULL CONSTRAINT FK_cat_ProductAttribute_Product REFERENCES cat.Product (ProductId),
    Name VARCHAR(40) NOT NULL,
    Value VARCHAR(80) NULL,
    NumberValue DECIMAL(18, 4) NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_cat_ProductAttribute_Product_Name' AND object_id = OBJECT_ID('cat.ProductAttribute'))
    CREATE INDEX IX_cat_ProductAttribute_Product_Name ON cat.ProductAttribute (ProductId, Name);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_cat_ProductAttribute_Name_Value' AND object_id = OBJECT_ID('cat.ProductAttribute'))
    CREATE INDEX IX_cat_ProductAttribute_Name_Value ON cat.ProductAttribute (Name, Value);
GO
GO
IF NOT EXISTS (SELECT 1 FROM cat.Product)
BEGIN
INSERT INTO cat.Product (CatalogNumber, Name, Description, Manufacturer) VALUES
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
INSERT INTO cat.ProductAttribute (ProductId, Name, Value, NumberValue) VALUES
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
END
GO
