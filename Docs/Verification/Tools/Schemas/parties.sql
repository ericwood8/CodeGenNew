-- A party (a customer, a supplier, a carrier or all three) and its identifiers (a scheme and a value), for PartyTables=Party:PartyIdentifier
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
