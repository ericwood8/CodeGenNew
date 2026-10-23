-- A many-to-many junction table for the InvoiceSystem sample (SQL Server): which employees work with which customers.
-- The key is the pair of columns; deleting an employee removes their links, a customer that is linked cannot be deleted.
-- Safe to run again.
IF OBJECT_ID(N'dbo.EmployeeCustomer', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EmployeeCustomer (
        EmployeeId int NOT NULL,
        CustomerId int NOT NULL,
        CONSTRAINT PK_EmployeeCustomer PRIMARY KEY (EmployeeId, CustomerId),
        CONSTRAINT FK_EmployeeCustomer_Employee FOREIGN KEY (EmployeeId) REFERENCES dbo.Employee (EmployeeId) ON DELETE CASCADE,
        CONSTRAINT FK_EmployeeCustomer_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customer (CustomerId)
    );
END
