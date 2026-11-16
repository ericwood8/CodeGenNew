-- The tables of parties.sql for SQL Server, in a schema of their own (party) so that they sit beside the other tables of the database. Can be run again: a table that exists is left alone.
-- sqlcmd -S <server> -E -C -I -b -d InvoiceSystem_test -i parties_sqlserver.sql
-- for PartyTables=Party:PartyIdentifier  (generate with --schema party)
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
