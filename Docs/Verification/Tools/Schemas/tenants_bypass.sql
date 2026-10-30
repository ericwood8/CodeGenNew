-- Schemas/tenants.sql with a Tag table, a ProjectTag junction table and a ProjectApproval side table, for tenant_bypass_check.py: the dashboard, a junction table, the row approvals and the host name of a tenant.
-- Use it with the settings of tenants.sql and RowApprovals=Project, UserRoleColumn=IsManager, UserAdminColumn=IsAdministrator, UserNameColumn=Login, Dashboard=true and TenantHostColumn=Slug.
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

CREATE TABLE Organization (
    OrganizationId INTEGER PRIMARY KEY AUTOINCREMENT,
    TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId),
    Name VARCHAR(80) NOT NULL,
    City VARCHAR(60) NULL,
    IsActive BOOLEAN NOT NULL DEFAULT 1
);

CREATE TABLE Project (
    ProjectId INTEGER PRIMARY KEY AUTOINCREMENT,
    TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId),
    Name VARCHAR(80) NOT NULL,
    Description VARCHAR(200) NULL,
    Budget DECIMAL(12,2) NOT NULL DEFAULT 0,
    StartedOn DATE NULL
);

CREATE TABLE Tag (
    TagId INTEGER PRIMARY KEY AUTOINCREMENT,
    TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId),
    Name VARCHAR(40) NOT NULL,
    Colour VARCHAR(20) NULL,
    Notes VARCHAR(100) NULL,
    Rank INTEGER NULL
);
CREATE UNIQUE INDEX UX_Tag_Name ON Tag (Name);

CREATE TABLE ProjectTag (
    ProjectId INTEGER NOT NULL REFERENCES Project (ProjectId),
    TagId INTEGER NOT NULL REFERENCES Tag (TagId),
    PRIMARY KEY (ProjectId, TagId)
);

CREATE TABLE ProjectApproval (
    ProjectApprovalId INTEGER PRIMARY KEY AUTOINCREMENT,
    ProjectId INTEGER NOT NULL REFERENCES Project (ProjectId),
    ApprovalStatusId INTEGER NOT NULL DEFAULT 1,
    Reason VARCHAR(500) NULL,
    RequestedBy VARCHAR(50) NOT NULL,
    RequestedOn DATETIME NOT NULL,
    DecidedBy VARCHAR(50) NULL,
    DecidedOn DATETIME NULL,
    DecisionNote VARCHAR(500) NULL,
    Revision INTEGER NOT NULL DEFAULT 1
);

INSERT INTO Tenant (Name, Slug, IsActive, CreatedOn) VALUES
    ('Northwind Traders', 'northwind', 1, '2026-01-05 09:00:00'), ('Contoso Services', 'contoso', 1, '2026-02-10 09:00:00'), ('Fabrikam Retired', 'fabrikam', 0, '2025-03-01 09:00:00');
INSERT INTO Employee (FullName, Login, IsManager, IsAdministrator) VALUES ('Ada Lovelace', 'ada', 1, 0), ('Grace Hopper', 'grace', 1, 0), ('Pat Platform', 'pat', 0, 1), ('Linus Worker', 'linus', 0, 0);
INSERT INTO EmployeeTenant (EmployeeId, TenantId, Role) VALUES (1, 1, 'Administrator'), (1, 2, 'Employee'), (2, 2, 'Administrator'), (4, 1, 'Employee'), (4, 2, 'Employee');
INSERT INTO Organization (TenantId, Name, City) VALUES (1, 'Northwind East', 'Boston'), (1, 'Northwind West', 'Seattle'), (2, 'Contoso Cloud', 'Dublin');
INSERT INTO Project (TenantId, Name, Budget, StartedOn) VALUES (1, 'Apollo', 1000, '2026-03-01'), (1, 'Gemini', 2000, '2026-04-01'), (2, 'Mercury', 3000, '2026-05-01');
INSERT INTO Tag (TenantId, Name) VALUES (1, 'urgent'), (2, 'later');
INSERT INTO ProjectTag (ProjectId, TagId) VALUES (1, 1), (3, 2);
INSERT INTO ProjectApproval (ProjectId, ApprovalStatusId, RequestedBy, RequestedOn) VALUES (1, 1, 'linus', '2026-05-01 10:00:00'), (3, 1, 'linus', '2026-05-02 10:00:00');
