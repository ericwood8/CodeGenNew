-- Session and token tables, for matrix_check.py (--schema tokens). Settings:
-- ApiAuth=true, UserTable=Employee, UserNameColumn=Login, UserRoleColumn=IsManager, UserAdminColumn=IsAdministrator, UserOwnerColumn=EmployeeId,
-- TokenTables=Session,PasswordReset:CodeHash:ExpiresOn:UsedOn,EmailCode:CodeHash:ExpiresOn:IsUsed,RefreshToken, TokenAnonymousSpend=RefreshToken, ArchiveTables=Session:30:delete
CREATE TABLE Employee (
    EmployeeId INTEGER PRIMARY KEY AUTOINCREMENT,
    FullName VARCHAR(40) NOT NULL,
    Login VARCHAR(30) NOT NULL,
    PasswordHash VARCHAR(200) NOT NULL DEFAULT 'x',
    IsManager BOOLEAN NOT NULL DEFAULT 0,
    IsAdministrator BOOLEAN NOT NULL DEFAULT 0
);

CREATE TABLE Session (
    SessionId INTEGER PRIMARY KEY AUTOINCREMENT,
    EmployeeId INTEGER NOT NULL REFERENCES Employee (EmployeeId),
    TokenHash VARCHAR(64) NOT NULL,
    CreatedOn DATETIME NOT NULL,
    ExpiresOn DATETIME NOT NULL,
    RevokedOn DATETIME NULL,
    UserAgent VARCHAR(200) NULL
);

CREATE TABLE PasswordReset (
    PasswordResetId INTEGER PRIMARY KEY AUTOINCREMENT,
    Email VARCHAR(80) NOT NULL,
    CodeHash VARCHAR(64) NOT NULL,
    ExpiresOn DATETIME NOT NULL,
    UsedOn DATETIME NULL
);

CREATE TABLE EmailCode (
    EmailCodeId INTEGER PRIMARY KEY AUTOINCREMENT,
    Email VARCHAR(80) NOT NULL,
    CodeHash VARCHAR(64) NOT NULL,
    ExpiresOn DATETIME NOT NULL,
    IsUsed BOOLEAN NOT NULL DEFAULT 0
);

CREATE TABLE RefreshToken (
    RefreshTokenId INTEGER PRIMARY KEY AUTOINCREMENT,
    EmployeeId INTEGER NOT NULL REFERENCES Employee (EmployeeId),
    TokenHash VARCHAR(64) NOT NULL,
    FamilyId VARCHAR(40) NOT NULL,
    ExpiresOn DATETIME NOT NULL,
    UsedOn DATETIME NULL,
    RevokedOn DATETIME NULL
);

CREATE TABLE Customer (
    CustomerId INTEGER PRIMARY KEY AUTOINCREMENT,
    Name VARCHAR(60) NOT NULL,
    Email VARCHAR(80) NULL,
    City VARCHAR(40) NULL,
    Region VARCHAR(40) NULL,
    Notes VARCHAR(200) NULL
);

INSERT INTO Employee (FullName, Login, IsManager, IsAdministrator) VALUES ('Admin Person', 'admin', 1, 1);
INSERT INTO Employee (FullName, Login, IsManager, IsAdministrator) VALUES ('Ordinary Person', 'ordinary', 0, 0);
