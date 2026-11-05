-- Feature flags over a multi-tenant database, for features_check.py. Settings:
-- TenantTable=Tenant, TenantMembership=EmployeeTenant:EmployeeId:TenantId:Role, ApiAuth=true, UserTable=Employee, UserNameColumn=Login, UserRoleColumn=IsManager, UserAdminColumn=IsAdministrator,
-- UserOwnerColumn=EmployeeId, FeatureTables=Feature,FeatureOverride
-- and, for the flags a plan switches on: SubscriptionTable=Subscription, PlanTable=Plan, FeaturePlanTable=PlanFeature (the tables are there either way).
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

CREATE TABLE Project (
    ProjectId INTEGER PRIMARY KEY AUTOINCREMENT,
    TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId),
    Name VARCHAR(80) NOT NULL,
    Description VARCHAR(200) NULL,
    Budget DECIMAL(12,2) NOT NULL DEFAULT 0,
    StartedOn DATE NULL
);

CREATE TABLE Feature (
    FeatureId INTEGER PRIMARY KEY AUTOINCREMENT,
    Name VARCHAR(60) NOT NULL UNIQUE,
    Description VARCHAR(200) NULL,
    DefaultEnabled BOOLEAN NOT NULL DEFAULT 0
);

CREATE TABLE FeatureOverride (
    FeatureOverrideId INTEGER PRIMARY KEY AUTOINCREMENT,
    FeatureId INTEGER NOT NULL REFERENCES Feature (FeatureId),
    TenantId INTEGER NULL REFERENCES Tenant (TenantId),
    EmployeeId INTEGER NULL REFERENCES Employee (EmployeeId),
    IsEnabled BOOLEAN NOT NULL
);




INSERT INTO Tenant (Name, Slug, IsActive, CreatedOn) VALUES ('Northwind Traders', 'northwind', 1, '2026-01-05 09:00:00'), ('Contoso Services', 'contoso', 1, '2026-02-10 09:00:00');
INSERT INTO Employee (FullName, Login, IsManager, IsAdministrator) VALUES ('Ada Lovelace', 'ada', 1, 0), ('Grace Hopper', 'grace', 0, 0), ('Pat Platform', 'pat', 0, 1);
INSERT INTO EmployeeTenant (EmployeeId, TenantId, Role) VALUES (1, 1, 'Administrator'), (1, 2, 'Employee'), (2, 2, 'Administrator');
INSERT INTO Project (TenantId, Name, Budget, StartedOn) VALUES (1, 'Apollo', 1000, '2026-03-01'), (2, 'Mercury', 3000, '2026-05-01');
INSERT INTO Feature (Name, Description, DefaultEnabled) VALUES ('new-invoice-screen', 'The redesigned invoice screen', 0), ('csv-export', 'Export to CSV', 1);
