-- Tables with a NOT NULL Name and a NOT NULL IsActive column (the NameActive screens) that are also in
-- SoftDeleteTables=Department and EffectiveDatedTables=Season:ValidFrom:ValidTo
CREATE TABLE Department (
    DepartmentId INTEGER PRIMARY KEY AUTOINCREMENT,
    Name VARCHAR(40) NOT NULL,
    IsActive BOOLEAN NOT NULL,
    Code VARCHAR(10) NOT NULL,
    Budget DECIMAL(10,2) NOT NULL,
    IsDeleted BIT NOT NULL DEFAULT 0,
    DeletedOn DATETIME NULL,
    DeletedBy VARCHAR(50) NULL
);

CREATE TABLE Season (
    SeasonId INTEGER PRIMARY KEY AUTOINCREMENT,
    Name VARCHAR(40) NOT NULL,
    IsActive BOOLEAN NOT NULL,
    Region VARCHAR(10) NOT NULL,
    ValidFrom DATE NOT NULL,
    ValidTo DATE NULL,
    Note VARCHAR(50) NULL
);

INSERT INTO Department (Name, IsActive, Code, Budget) VALUES ('Sales', 1, 'S1', 1000), ('Support', 1, 'S2', 500);
INSERT INTO Season (Name, IsActive, Region, ValidFrom, ValidTo) VALUES ('Winter', 1, 'North', '2025-12-01', '2026-02-28'), ('Summer', 1, 'North', '2026-06-01', NULL);
