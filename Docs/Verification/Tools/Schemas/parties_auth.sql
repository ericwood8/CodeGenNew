-- parties.sql with an empty Employee table for the sign-in, for PartyTables=Party:PartyIdentifier with ApiAuth
-- (UserTable=Employee, UserNameColumn=Login, UserRoleColumn=IsManager, UserAdminColumn=IsAdministrator). POST /api/auth/first-person adds the first administrator.
CREATE TABLE Employee (
    EmployeeId INTEGER PRIMARY KEY AUTOINCREMENT,
    FullName VARCHAR(40) NOT NULL,
    Login VARCHAR(30) NOT NULL,
    PasswordHash VARCHAR(200) NOT NULL DEFAULT 'x',
    IsManager BOOLEAN NOT NULL DEFAULT 0,
    IsAdministrator BOOLEAN NOT NULL DEFAULT 0
);
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
