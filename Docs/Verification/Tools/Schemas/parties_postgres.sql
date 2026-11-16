-- The tables of parties.sql for PostgreSQL, for PartyTables=Party:PartyIdentifier (generate with OnlyTables=Party,PartyIdentifier).
-- PGPASSWORD=... psql -h localhost -U <login> -d <database> -f parties_postgres.sql      Can be run again.
-- The unique index is on the two columns as they are stored: a pair that differs only in case passes it, and the generated rule refuses it.
CREATE TABLE IF NOT EXISTS "Party" (
    "PartyId" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "LegalName" varchar(100) NOT NULL,
    "TradingName" varchar(100) NULL,
    "City" varchar(60) NULL,
    "Phone" varchar(30) NULL,
    "Notes" varchar(200) NULL
);
CREATE TABLE IF NOT EXISTS "PartyIdentifier" (
    "PartyIdentifierId" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "PartyId" integer NOT NULL REFERENCES "Party" ("PartyId"),
    "Scheme" varchar(20) NOT NULL,
    "Value" varchar(50) NOT NULL,
    "IssuedBy" varchar(60) NULL,
    "Notes" varchar(200) NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "UX_PartyIdentifier_Pair" ON "PartyIdentifier" ("Scheme", "Value");
