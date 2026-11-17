-- A party with the child tables of a party master: identifiers, names, tax registrations, contact methods and the roles it plays.
-- PartyTables=Party:PartyIdentifier, DefaultRowTables=PartyName:IsPrimary:PartyId+NameType,ContactMethod:IsPreferred:PartyId+MethodType,
-- EffectiveDatedTables=TaxRegistration:ValidFrom:ValidTo:PartyId+Country. PartyRole is the junction of a party and a role (a customer, a supplier, a carrier).
CREATE TABLE Party (
    PartyId INTEGER PRIMARY KEY AUTOINCREMENT,
    LegalName VARCHAR(100) NOT NULL,
    TradingName VARCHAR(100) NULL,
    City VARCHAR(60) NULL,
    Phone VARCHAR(30) NULL,
    Notes VARCHAR(200) NULL
);
CREATE TABLE PartyIdentifier (
    PartyIdentifierId INTEGER PRIMARY KEY AUTOINCREMENT,
    PartyId INTEGER NOT NULL REFERENCES Party (PartyId),
    Scheme VARCHAR(20) NOT NULL,
    Value VARCHAR(50) NOT NULL,
    IssuedBy VARCHAR(60) NULL,
    Notes VARCHAR(200) NULL
);
CREATE UNIQUE INDEX UX_PartyIdentifier_Pair ON PartyIdentifier (Scheme, Value);
CREATE TABLE PartyName (
    PartyNameId INTEGER PRIMARY KEY AUTOINCREMENT,
    PartyId INTEGER NOT NULL REFERENCES Party (PartyId),
    NameType VARCHAR(20) NOT NULL,
    Name VARCHAR(100) NOT NULL,
    IsPrimary BOOLEAN NOT NULL DEFAULT 0,
    Notes VARCHAR(200) NULL
);
CREATE TABLE TaxRegistration (
    TaxRegistrationId INTEGER PRIMARY KEY AUTOINCREMENT,
    PartyId INTEGER NOT NULL REFERENCES Party (PartyId),
    Country VARCHAR(2) NOT NULL,
    TaxNumber VARCHAR(30) NOT NULL,
    ValidFrom DATE NOT NULL,
    ValidTo DATE NULL,
    Notes VARCHAR(200) NULL
);
CREATE UNIQUE INDEX UX_TaxRegistration_Number ON TaxRegistration (Country, TaxNumber, ValidFrom);
CREATE TABLE ContactMethod (
    ContactMethodId INTEGER PRIMARY KEY AUTOINCREMENT,
    PartyId INTEGER NOT NULL REFERENCES Party (PartyId),
    MethodType VARCHAR(20) NOT NULL,
    Value VARCHAR(100) NOT NULL,
    IsPreferred BOOLEAN NOT NULL DEFAULT 0,
    Notes VARCHAR(200) NULL
);
CREATE TABLE Role (
    RoleId INTEGER PRIMARY KEY AUTOINCREMENT,
    RoleName VARCHAR(30) NOT NULL,
    Description VARCHAR(100) NULL,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    IsActive BOOLEAN NOT NULL DEFAULT 1,
    Notes VARCHAR(200) NULL
);
CREATE TABLE PartyRole (
    PartyId INTEGER NOT NULL REFERENCES Party (PartyId),
    RoleId INTEGER NOT NULL REFERENCES Role (RoleId),
    PRIMARY KEY (PartyId, RoleId)
);
CREATE TABLE Customer (
    CustomerId INT NOT NULL PRIMARY KEY REFERENCES Party (PartyId),
    CreditLimit DECIMAL(12, 2) NOT NULL DEFAULT 0,
    PaymentTerms VARCHAR(30) NULL,
    DiscountPercent DECIMAL(5, 2) NOT NULL DEFAULT 0,
    Notes VARCHAR(200) NULL
);
CREATE TABLE Supplier (
    SupplierId INT NOT NULL PRIMARY KEY REFERENCES Party (PartyId),
    LeadTimeDays INTEGER NOT NULL DEFAULT 0,
    MinimumOrder DECIMAL(12, 2) NOT NULL DEFAULT 0,
    PaymentTerms VARCHAR(30) NULL,
    Notes VARCHAR(200) NULL
);
INSERT INTO Role (RoleName) VALUES ('Customer'), ('Supplier'), ('Carrier');
