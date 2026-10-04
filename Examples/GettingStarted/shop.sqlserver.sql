-- The same shop for SQL Server. Create a database called Shop first, then run this file against it.
CREATE TABLE dbo.Customer (
    CustomerId  int IDENTITY(1,1) PRIMARY KEY,
    Name        nvarchar(50) NOT NULL,
    Email       nvarchar(100) NULL,
    CreditLimit decimal(10,2) NULL CHECK (CreditLimit >= 0),
    Status      nvarchar(10) NOT NULL DEFAULT 'Active' CHECK (Status IN ('Active', 'On hold', 'Closed')),
    IsTaxable   bit NOT NULL DEFAULT 0
);

CREATE TABLE dbo.Product (
    ProductId int IDENTITY(1,1) PRIMARY KEY,
    Name      nvarchar(80) NOT NULL,
    Price     decimal(10,2) NOT NULL CHECK (Price > 0),
    Rating    int NULL CHECK (Rating BETWEEN 1 AND 5)
);

CREATE TABLE dbo.SalesOrder (
    SalesOrderId int IDENTITY(1,1) PRIMARY KEY,
    CustomerId   int NOT NULL REFERENCES dbo.Customer (CustomerId),
    ProductId    int NOT NULL REFERENCES dbo.Product (ProductId),
    OrderDate    date NOT NULL,
    Quantity     int NOT NULL CHECK (Quantity >= 1)
);

INSERT INTO dbo.Customer (Name, Email, CreditLimit) VALUES ('Acme Ltd', 'orders@acme.example', 5000), ('Globex', NULL, 0);
INSERT INTO dbo.Product (Name, Price, Rating) VALUES ('Widget', 9.99, 4), ('Gadget', 24.50, NULL);
INSERT INTO dbo.SalesOrder (CustomerId, ProductId, OrderDate, Quantity) VALUES (1, 1, '2026-01-15', 10), (2, 2, '2026-02-01', 1);
