-- The tables of parties_full.sql for SQL Server, in the schema party (the tables of parties_sqlserver.sql are part of it, so either script can be run first). Can be run again: a table that exists is left alone.
-- sqlcmd -S <server> -E -C -I -b -d InvoiceSystem_test -i parties_full_sqlserver.sql
-- for PartyTables=Party:PartyIdentifier:names=PartyName:contacts=ContactMethod:taxes=TaxRegistration:roles=PartyRole  (generate with --schema party)
-- parties_full.sql is for SQLite: AUTOINCREMENT and a key that is not an identity written as INT are SQLite's.
IF SCHEMA_ID('party') IS NULL
    EXEC ('CREATE SCHEMA party');
GO
IF OBJECT_ID('party.Party', 'U') IS NULL
CREATE TABLE party.Party (
    PartyId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_party_Party PRIMARY KEY,
    LegalName VARCHAR(100) NOT NULL,
    TradingName VARCHAR(100) NULL,
    City VARCHAR(60) NULL,
    Phone VARCHAR(30) NULL,
    Notes VARCHAR(200) NULL
);
GO
IF OBJECT_ID('party.PartyIdentifier', 'U') IS NULL
CREATE TABLE party.PartyIdentifier (
    PartyIdentifierId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_party_PartyIdentifier PRIMARY KEY,
    PartyId INT NOT NULL CONSTRAINT FK_party_PartyIdentifier_Party REFERENCES party.Party (PartyId),
    Scheme VARCHAR(20) NOT NULL,
    Value VARCHAR(50) NOT NULL,
    IssuedBy VARCHAR(60) NULL,
    Notes VARCHAR(200) NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_party_PartyIdentifier_Pair' AND object_id = OBJECT_ID('party.PartyIdentifier'))
    CREATE UNIQUE INDEX UX_party_PartyIdentifier_Pair ON party.PartyIdentifier (Scheme, Value);
GO
IF OBJECT_ID('party.PartyName', 'U') IS NULL
CREATE TABLE party.PartyName (
    PartyNameId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_party_PartyName PRIMARY KEY,
    PartyId INT NOT NULL CONSTRAINT FK_party_PartyName_Party REFERENCES party.Party (PartyId),
    NameType VARCHAR(20) NOT NULL,
    Name VARCHAR(100) NOT NULL,
    IsPrimary BIT NOT NULL CONSTRAINT DF_party_PartyName_IsPrimary DEFAULT 0,
    Notes VARCHAR(200) NULL
);
GO
IF OBJECT_ID('party.TaxRegistration', 'U') IS NULL
CREATE TABLE party.TaxRegistration (
    TaxRegistrationId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_party_TaxRegistration PRIMARY KEY,
    PartyId INT NOT NULL CONSTRAINT FK_party_TaxRegistration_Party REFERENCES party.Party (PartyId),
    Country VARCHAR(2) NOT NULL,
    TaxNumber VARCHAR(30) NOT NULL,
    ValidFrom DATE NOT NULL,
    ValidTo DATE NULL,
    Notes VARCHAR(200) NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_party_TaxRegistration_Number' AND object_id = OBJECT_ID('party.TaxRegistration'))
    CREATE UNIQUE INDEX UX_party_TaxRegistration_Number ON party.TaxRegistration (Country, TaxNumber, ValidFrom);
GO
IF OBJECT_ID('party.ContactMethod', 'U') IS NULL
CREATE TABLE party.ContactMethod (
    ContactMethodId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_party_ContactMethod PRIMARY KEY,
    PartyId INT NOT NULL CONSTRAINT FK_party_ContactMethod_Party REFERENCES party.Party (PartyId),
    MethodType VARCHAR(20) NOT NULL,
    Value VARCHAR(100) NOT NULL,
    IsPreferred BIT NOT NULL CONSTRAINT DF_party_ContactMethod_IsPreferred DEFAULT 0,
    Notes VARCHAR(200) NULL
);
GO
IF OBJECT_ID('party.Customer', 'U') IS NULL
CREATE TABLE party.Customer (
    CustomerId INT NOT NULL CONSTRAINT PK_party_Customer PRIMARY KEY CONSTRAINT FK_party_Customer_Party REFERENCES party.Party (PartyId),
    CreditLimit DECIMAL(12, 2) NOT NULL CONSTRAINT DF_party_Customer_CreditLimit DEFAULT 0,
    PaymentTerms VARCHAR(30) NULL,
    DiscountPercent DECIMAL(5, 2) NOT NULL CONSTRAINT DF_party_Customer_DiscountPercent DEFAULT 0,
    Notes VARCHAR(200) NULL
);
GO
IF OBJECT_ID('party.Supplier', 'U') IS NULL
CREATE TABLE party.Supplier (
    SupplierId INT NOT NULL CONSTRAINT PK_party_Supplier PRIMARY KEY CONSTRAINT FK_party_Supplier_Party REFERENCES party.Party (PartyId),
    LeadTimeDays INT NOT NULL CONSTRAINT DF_party_Supplier_LeadTimeDays DEFAULT 0,
    MinimumOrder DECIMAL(12, 2) NOT NULL CONSTRAINT DF_party_Supplier_MinimumOrder DEFAULT 0,
    PaymentTerms VARCHAR(30) NULL,
    Notes VARCHAR(200) NULL
);
GO
IF OBJECT_ID('party.Role', 'U') IS NULL
CREATE TABLE party.Role (
    RoleId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_party_Role PRIMARY KEY,
    RoleName VARCHAR(30) NOT NULL,
    Description VARCHAR(100) NULL,
    SortOrder INT NOT NULL CONSTRAINT DF_party_Role_SortOrder DEFAULT 0,
    IsActive BIT NOT NULL CONSTRAINT DF_party_Role_IsActive DEFAULT 1,
    Notes VARCHAR(200) NULL
);
GO
IF OBJECT_ID('party.PartyRole', 'U') IS NULL
CREATE TABLE party.PartyRole (
    PartyId INT NOT NULL CONSTRAINT FK_party_PartyRole_Party REFERENCES party.Party (PartyId),
    RoleId INT NOT NULL CONSTRAINT FK_party_PartyRole_Role REFERENCES party.Role (RoleId),
    CONSTRAINT PK_party_PartyRole PRIMARY KEY (PartyId, RoleId)
);
GO
IF NOT EXISTS (SELECT 1 FROM party.Role)
    INSERT INTO party.Role (RoleName) VALUES ('Customer'), ('Supplier'), ('Carrier');
GO
