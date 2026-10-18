-- Fixtures for TimeEntry_test, on top of a copy of the real TimeEntry. Every row it adds is tagged ZZ (a name or a note starting with ZZ), so a test can find it
-- and a re-run removes it first. Never run against the real database.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() NOT LIKE '%[_]test'
    THROW 50000, 'This seed is for a database named *_test.', 1;

BEGIN TRAN;

-- ===== an earlier seed, children first =====
DELETE FROM ProjectTask WHERE Name LIKE 'ZZ%';
DELETE FROM Project WHERE Name LIKE 'ZZ%';
DELETE FROM Holiday WHERE Name LIKE 'ZZ%';
DELETE FROM RestrictLeave WHERE Reason LIKE 'ZZ%';
DELETE FROM E_DonateLeave WHERE Note LIKE 'ZZ%';

-- ===== holidays: to create, edit, clone and delete; a name with quotes, wildcards, brackets and accents =====
DECLARE @country char(3) = (SELECT TOP 1 SY_IsoCountry_Alpha3Code FROM Holiday ORDER BY HolidayId);
INSERT Holiday (SY_IsoCountry_Alpha3Code, [Date], Name) VALUES
 (@country, '2030-01-01', N'ZZ Free to delete 1'),
 (@country, '2030-01-02', N'ZZ Free to delete 2'),
 (@country, '2030-01-03', N'ZZ O''Brien "Q" 100% _x [y] \ <b>&amp; caf' + NCHAR(233));

-- ===== a project that is in use (it has a task) and one that is free =====
INSERT Project (Name, IsDefault, IsActive) VALUES (N'ZZ Project free', 0, 1), (N'ZZ Project in use', 0, 1);
DECLARE @project int = (SELECT ProjectId FROM Project WHERE Name = N'ZZ Project in use');
INSERT ProjectTask (ProjectId, Name, IsDefault, IsActive) VALUES (@project, N'ZZ Task 1', 0, 1), (@project, N'ZZ Task 2', 0, 1);

-- ===== a restriction whose period is reversed (stored that way), and a good one =====
INSERT RestrictLeave (FromDateTime, ToDateTime, Reason) VALUES
 ('2030-02-10', '2030-02-01', N'ZZ reversed period (stored)'),
 ('2030-03-01', '2030-03-05', N'ZZ good period');

-- ===== donated leave, to edit and delete =====
DECLARE @from int = (SELECT MIN(EmployeeId) FROM Employee), @to int = (SELECT MIN(EmployeeId) FROM Employee WHERE EmployeeId > (SELECT MIN(EmployeeId) FROM Employee));
INSERT E_DonateLeave (DonateFrom_EmployeeId, DonateTo_EmployeeId, WhenDonated, HoursDonated, Note) VALUES
 (@from, @to, GETDATE(), 4, N'ZZ donation 1'),
 (@from, @to, GETDATE(), 8, N'ZZ donation 2');

-- ===== the last page of a grid holds one row (a page is 10 rows): fillers until the count ends in 1 =====
DECLARE @n int = 1;
WHILE (SELECT COUNT(*) FROM Holiday) % 10 <> 1
BEGIN
    INSERT Holiday (SY_IsoCountry_Alpha3Code, [Date], Name) VALUES (@country, DATEADD(DAY, @n, '2031-01-01'), N'ZZ Filler ' + CAST(@n AS nvarchar(10)));
    SET @n += 1;
END;
SET @n = 1;
WHILE (SELECT COUNT(*) FROM Project) % 10 <> 1
BEGIN
    INSERT Project (Name, IsDefault, IsActive) VALUES (N'ZZ Project filler ' + CAST(@n AS nvarchar(10)), 0, 1);
    SET @n += 1;
END;

COMMIT;

SELECT 'Holiday' AS [table], COUNT(*) AS [rows], SUM(CASE WHEN Name LIKE 'ZZ%' THEN 1 ELSE 0 END) AS seeded FROM Holiday
UNION ALL SELECT 'Project', COUNT(*), SUM(CASE WHEN Name LIKE 'ZZ%' THEN 1 ELSE 0 END) FROM Project
UNION ALL SELECT 'RestrictLeave', COUNT(*), SUM(CASE WHEN Reason LIKE 'ZZ%' THEN 1 ELSE 0 END) FROM RestrictLeave
UNION ALL SELECT 'E_DonateLeave', COUNT(*), SUM(CASE WHEN Note LIKE 'ZZ%' THEN 1 ELSE 0 END) FROM E_DonateLeave;
