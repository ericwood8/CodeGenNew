-- An ordinary table of the InvoiceSystem sample (SQL Server) that is kept EMPTY: no seed script writes to it and no test may leave a row in it.
-- It stands for a table that has just been made, so the templates are checked against a table with no rows (nothing to sample, nothing to read).
-- Safe to run again.
IF OBJECT_ID(N'dbo.EmptyTest', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EmptyTest (
        EmptyTestId int IDENTITY(1,1) NOT NULL,
        Name varchar(40) NOT NULL,
        Code varchar(10) NOT NULL,
        Amount decimal(10,2) NOT NULL,
        StartDate date NOT NULL,
        IsTaxable bit NOT NULL,
        Note varchar(100) NULL,
        CONSTRAINT PK_EmptyTest PRIMARY KEY (EmptyTestId)
    );
END
