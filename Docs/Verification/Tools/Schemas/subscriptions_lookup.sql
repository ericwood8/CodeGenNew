-- The same database with the status of a subscription held in a look-up table (SubscriptionStatus), for the ASP.NET API.
-- A multi-tenant database with plans, plan limits and one subscription per tenant (the first tenant, where the generated tests work, is on a plan with room), for
-- TenantTable=Tenant, TenantMembership=EmployeeTenant:EmployeeId:TenantId:Role, UserTable=Employee,
-- SubscriptionTable=Subscription, PlanLimitTable=PlanLimit, PlanLimits=Project:projects,EmployeeTenant:seats
-- and PlanTable=Plan.
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

CREATE TABLE Plan (
    PlanId INTEGER PRIMARY KEY AUTOINCREMENT,
    Name VARCHAR(40) NOT NULL,
    MonthlyPrice DECIMAL(10,2) NOT NULL DEFAULT 0,
    BillingPeriod VARCHAR(20) NOT NULL DEFAULT 'month'
);

CREATE TABLE PlanLimit (
    PlanLimitId INTEGER PRIMARY KEY AUTOINCREMENT,
    PlanId INTEGER NOT NULL REFERENCES Plan (PlanId),
    LimitKey VARCHAR(40) NOT NULL,
    LimitValue INTEGER NULL
);

CREATE TABLE SubscriptionStatus (
    SubscriptionStatusId INTEGER PRIMARY KEY,
    Name VARCHAR(20) NOT NULL
);

CREATE TABLE Subscription (
    SubscriptionId INTEGER PRIMARY KEY AUTOINCREMENT,
    TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId),
    PlanId INTEGER NOT NULL REFERENCES Plan (PlanId),
    StatusId INTEGER NOT NULL REFERENCES SubscriptionStatus (SubscriptionStatusId),
    CurrentPeriodStart DATETIME NULL,
    CurrentPeriodEnd DATETIME NULL,
    GatewaySubscriptionId VARCHAR(80) NULL
);

INSERT INTO Tenant (Name, Slug, IsActive, CreatedOn) VALUES
    ('Northwind Traders', 'northwind', 1, '2026-01-05 09:00:00'), ('Contoso Services', 'contoso', 1, '2026-02-10 09:00:00'), ('Fabrikam Starters', 'fabrikam', 1, '2026-03-01 09:00:00');
INSERT INTO Employee (FullName, Login, IsManager, IsAdministrator) VALUES ('Ada Lovelace', 'ada', 1, 0), ('Grace Hopper', 'grace', 0, 0), ('Pat Platform', 'pat', 0, 1);
INSERT INTO EmployeeTenant (EmployeeId, TenantId, Role) VALUES (1, 1, 'Administrator'), (1, 2, 'Employee'), (2, 2, 'Administrator');
INSERT INTO Project (TenantId, Name, Budget, StartedOn) VALUES (1, 'Apollo', 1000, '2026-03-01'), (1, 'Gemini', 2000, '2026-04-01'), (2, 'Mercury', 3000, '2026-05-01');
INSERT INTO Plan (Name, MonthlyPrice, BillingPeriod) VALUES ('Free', 0, 'month'), ('Pro', 29, 'month'), ('Enterprise', 199, 'month');
INSERT INTO PlanLimit (PlanId, LimitKey, LimitValue) VALUES (1, 'projects', 2), (1, 'seats', 1), (2, 'projects', 10), (2, 'seats', NULL);
INSERT INTO SubscriptionStatus (SubscriptionStatusId, Name) VALUES (1, 'trialing'), (2, 'active'), (3, 'past_due'), (4, 'cancelled');
INSERT INTO Subscription (TenantId, PlanId, StatusId, CurrentPeriodStart, CurrentPeriodEnd, GatewaySubscriptionId) VALUES
    (1, 2, 2, '2026-10-01 00:00:00', '2026-11-01 00:00:00', 'sub_northwind'), (2, 1, 1, '2026-10-20 00:00:00', '2027-12-31 00:00:00', 'sub_contoso'), (3, 1, 3, '2026-09-01 00:00:00', '2026-10-01 00:00:00', 'sub_fabrikam');
