-- The tables of catalog.sql for MySQL in snake case, for FacetTables=CatalogProductAttribute (generate with OnlyTables=catalog_product,catalog_product_attribute and NamingStyle=Pascal; the names are prefixed because the sample database already has a product table).
-- MYSQL_PWD=... mysql -h 127.0.0.1 -u <login> <database> < catalog_mysql.sql      Can be run again: a table that exists is left alone and the rows are added once (an older attribute table without number_value is rebuilt).
DROP PROCEDURE IF EXISTS catalog_seed;
DELIMITER //
CREATE PROCEDURE catalog_seed()
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'catalog_product_attribute')
       AND NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = 'catalog_product_attribute' AND column_name = 'number_value') THEN
        DROP TABLE catalog_product_attribute;
        TRUNCATE TABLE catalog_product;
    END IF;
    CREATE TABLE IF NOT EXISTS catalog_product (
        catalog_product_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
        catalog_number VARCHAR(40) NOT NULL,
        name VARCHAR(120) NOT NULL,
        description VARCHAR(255) NULL,
        manufacturer VARCHAR(60) NOT NULL,
        UNIQUE KEY uq_catalog_product_catalog_number (catalog_number)
    );
    CREATE TABLE IF NOT EXISTS catalog_product_attribute (
        catalog_product_attribute_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
        catalog_product_id INT NOT NULL,
        name VARCHAR(40) NOT NULL,
        value VARCHAR(80) NULL,
        number_value DECIMAL(18, 4) NULL,
        KEY ix_catalog_product_attribute_product_name (catalog_product_id, name),
        KEY ix_catalog_product_attribute_name_value (name, value),
        CONSTRAINT fk_catalog_product_attribute_product FOREIGN KEY (catalog_product_id) REFERENCES catalog_product (catalog_product_id)
    );
    IF NOT EXISTS (SELECT 1 FROM catalog_product) THEN
    INSERT INTO catalog_product (catalog_number, name, description, manufacturer) VALUES
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
    INSERT INTO catalog_product_attribute (catalog_product_id, name, value, number_value) VALUES
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
END //
DELIMITER ;
CALL catalog_seed();
DROP PROCEDURE catalog_seed;
