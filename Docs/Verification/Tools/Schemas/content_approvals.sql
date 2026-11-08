-- A post whose publication needs a second person: ContentTables=Post, RowApprovals=Post, UserTable=Employee, UserRoleColumn=IsManager, UserAdminColumn=IsAdministrator
CREATE TABLE Employee (
    EmployeeId INTEGER PRIMARY KEY AUTOINCREMENT,
    FullName VARCHAR(40) NOT NULL,
    Login VARCHAR(30) NOT NULL,
    PasswordHash VARCHAR(200) NOT NULL DEFAULT 'x',
    IsManager BOOLEAN NOT NULL DEFAULT 0,
    IsAdministrator BOOLEAN NOT NULL DEFAULT 0
);

CREATE TABLE Post (
    PostId INTEGER PRIMARY KEY AUTOINCREMENT,
    Title VARCHAR(150) NOT NULL,
    Slug VARCHAR(150) NOT NULL,
    Body TEXT NOT NULL,
    Status VARCHAR(12) NOT NULL DEFAULT 'draft',
    PublishedOn DATETIME NULL,
    CreatedOn DATETIME NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE UNIQUE INDEX UX_Post_Slug ON Post (Slug);

CREATE TABLE "PostApproval" (
    "PostApprovalId" INTEGER PRIMARY KEY AUTOINCREMENT,
    "PostId" INTEGER NOT NULL REFERENCES "Post" ("PostId"),
    "ApprovalStatusId" INTEGER NOT NULL DEFAULT 1,
    "Reason" VARCHAR(500) NULL,
    "RequestedBy" VARCHAR(50) NOT NULL,
    "RequestedOn" DATETIME NOT NULL,
    "DecidedBy" VARCHAR(50) NULL,
    "DecidedOn" DATETIME NULL,
    "DecisionNote" VARCHAR(500) NULL,
    "Revision" INTEGER NOT NULL DEFAULT 1
);

INSERT INTO Employee (FullName, Login, IsManager, IsAdministrator) VALUES ('Ann Writer', 'ann', 0, 0);
INSERT INTO Employee (FullName, Login, IsManager, IsAdministrator) VALUES ('Bob Editor', 'bob', 1, 0);
INSERT INTO Post (Title, Slug, Body, Status) VALUES ('Coming soon', 'coming-soon', 'A draft', 'draft');
INSERT INTO Post (Title, Slug, Body, Status, PublishedOn) VALUES ('Welcome', 'welcome', 'First post', 'published', '2026-01-01 09:00:00');
