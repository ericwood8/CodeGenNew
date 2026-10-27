-- A tree with soft delete (Category), a soft-delete table (Customer) and an effective-dated table (Price), for
-- TreeTables=Category:ParentCategoryId:3, SoftDeleteTables=Category,Customer and EffectiveDatedTables=Price:ValidFrom:ValidTo
CREATE TABLE Category (
    CategoryId INTEGER PRIMARY KEY AUTOINCREMENT,
    Name VARCHAR(60) NOT NULL,
    Description VARCHAR(200) NULL,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    ParentCategoryId INTEGER NULL REFERENCES Category (CategoryId),
    IsDeleted BIT NOT NULL DEFAULT 0,
    DeletedOn DATETIME NULL,
    DeletedBy VARCHAR(50) NULL
);

CREATE TABLE Customer (
    CustomerId INTEGER PRIMARY KEY AUTOINCREMENT,
    Name VARCHAR(80) NOT NULL,
    Email VARCHAR(120) NULL,
    City VARCHAR(60) NULL,
    Notes VARCHAR(200) NULL,
    IsDeleted BIT NOT NULL DEFAULT 0,
    DeletedOn DATETIME NULL,
    DeletedBy VARCHAR(50) NULL
);

CREATE TABLE Item (
    ItemId INTEGER PRIMARY KEY AUTOINCREMENT,
    Name VARCHAR(80) NOT NULL,
    Sku VARCHAR(30) NOT NULL,
    Unit VARCHAR(10) NOT NULL DEFAULT 'each',
    Weight DECIMAL(9,2) NOT NULL DEFAULT 0,
    Notes VARCHAR(200) NULL
);

CREATE TABLE Price (
    PriceId INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId INTEGER NOT NULL REFERENCES Item (ItemId),
    Amount DECIMAL(10,2) NOT NULL,
    Currency VARCHAR(3) NOT NULL DEFAULT 'USD',
    ValidFrom DATE NOT NULL,
    ValidTo DATE NULL,
    Note VARCHAR(100) NULL
);

INSERT INTO Category (Name, SortOrder, ParentCategoryId) VALUES
    ('Hardware', 1, NULL), ('Software', 2, NULL), ('Tools', 1, 1), ('Fasteners', 2, 1), ('Hammers', 1, 3);
INSERT INTO Customer (Name, Email, City) VALUES ('Acme', 'office@acme.test', 'Springfield'), ('Globex', 'hello@globex.test', 'Shelbyville');
INSERT INTO Item (Name, Sku) VALUES ('Widget', 'W-100'), ('Gadget', 'G-200');
INSERT INTO Price (ItemId, Amount, ValidFrom, ValidTo) VALUES
    (1, 9.50, '2025-01-01', '2025-12-31'), (1, 10.50, '2026-01-01', NULL), (2, 20.00, '2026-01-01', '2026-06-30');
