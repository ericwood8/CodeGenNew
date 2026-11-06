-- The session and token test tables for a MySQL copy of the InvoiceSystem sample. Settings:
-- ApiAuth=true, UserTable=Employee, UserNameColumn=Login, UserRoleColumn=IsManager, UserAdminColumn=IsAdministrator,
-- TokenTables=AuthToken:TokenHash:ExpiresOn:UsedOn+RevokedOn:FamilyId, TokenAnonymousSpend=AuthToken
CREATE TABLE IF NOT EXISTS Employee (
    EmployeeId int NOT NULL AUTO_INCREMENT PRIMARY KEY,
    Name varchar(50) NOT NULL,
    IsManager tinyint(1) NOT NULL DEFAULT 0,
    Login varchar(50) NULL,
    PasswordHash varchar(200) NULL,
    IsAdministrator tinyint(1) NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS AuthToken (
    AuthTokenId int NOT NULL AUTO_INCREMENT PRIMARY KEY,
    EmployeeId int NOT NULL,
    Kind varchar(20) NOT NULL,
    TokenHash varchar(128) NOT NULL,
    FamilyId varchar(40) NULL,
    CreatedOn datetime NOT NULL,
    ExpiresOn datetime NOT NULL,
    UsedOn datetime NULL,
    RevokedOn datetime NULL,
    CONSTRAINT FK_AuthToken_Employee FOREIGN KEY (EmployeeId) REFERENCES Employee (EmployeeId),
    CONSTRAINT CK_AuthToken_Kind CHECK (Kind IN ('verify', 'reset', 'refresh', 'session'))
);

INSERT INTO Employee (Name, IsManager, Login, IsAdministrator)
SELECT 'Joe Doe', 1, 'jdoe', 1 WHERE NOT EXISTS (SELECT 1 FROM Employee);
INSERT INTO Employee (Name, IsManager, Login, IsAdministrator)
SELECT 'Ann Kim', 0, 'akim', 0 FROM DUAL WHERE (SELECT count(*) FROM Employee) = 1;

INSERT INTO AuthToken (EmployeeId, Kind, TokenHash, FamilyId, CreatedOn, ExpiresOn)
SELECT 1, 'refresh', 'E6D451559BC1627DC28CFC5FE5D890EC331826F1B188B939005B2564721F7E0D', 'seed-family', now(), now() + interval 30 day
FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM AuthToken);
