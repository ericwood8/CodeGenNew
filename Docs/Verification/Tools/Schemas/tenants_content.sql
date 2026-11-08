-- Schemas/tenants_signin.sql with a tenant-owned Post, for ContentTables=Post with TenantHostColumn=Slug: the public routes read the tenant of the request host and a slug is unique per tenant.
CREATE TABLE Tenant (
    TenantId INTEGER PRIMARY KEY AUTOINCREMENT,
    Name VARCHAR(80) NOT NULL,
    Slug VARCHAR(40) NOT NULL UNIQUE,
    IsActive BOOLEAN NOT NULL DEFAULT 1,
    CreatedOn DATETIME NOT NULL
);

CREATE TABLE Employee (
    EmployeeId INTEGER PRIMARY KEY AUTOINCREMENT,
    FullName VARCHAR(40) NOT NULL,
    Login VARCHAR(30) NOT NULL,
    PasswordHash VARCHAR(200) NOT NULL DEFAULT 'x',
    IsManager BOOLEAN NOT NULL DEFAULT 0,
    IsAdministrator BOOLEAN NOT NULL DEFAULT 0
);

CREATE TABLE EmployeeTenant (
    EmployeeTenantId INTEGER PRIMARY KEY AUTOINCREMENT,
    EmployeeId INTEGER NOT NULL REFERENCES Employee (EmployeeId),
    TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId),
    Role VARCHAR(30) NULL
);

CREATE TABLE Post (
    PostId INTEGER PRIMARY KEY AUTOINCREMENT,
    TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId),
    Title VARCHAR(150) NOT NULL,
    Slug VARCHAR(150) NOT NULL,
    Body TEXT NOT NULL,
    Status VARCHAR(12) NOT NULL DEFAULT 'draft',
    PublishedOn DATETIME NULL
);
CREATE UNIQUE INDEX UX_Post_Tenant_Slug ON Post (TenantId, Slug);

INSERT INTO Tenant (Name, Slug, IsActive, CreatedOn) VALUES ('Northwind Traders', 'northwind', 1, '2026-01-05 09:00:00'), ('Contoso Services', 'contoso', 1, '2026-02-10 09:00:00');
INSERT INTO Employee (FullName, Login, IsManager, IsAdministrator) VALUES ('Ada Lovelace', 'ada', 1, 0), ('Pat Platform', 'pat', 0, 1);
INSERT INTO EmployeeTenant (EmployeeId, TenantId, Role) VALUES (1, 1, 'Administrator');
INSERT INTO Post (TenantId, Title, Slug, Body, Status, PublishedOn) VALUES (1, 'Welcome', 'welcome', 'Northwind first post', 'published', '2026-01-01 09:00:00'), (2, 'Welcome', 'welcome', 'Contoso first post', 'published', '2026-01-02 09:00:00');
