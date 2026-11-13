-- The tables of snapshot.sql for SQL Server, in a schema of their own (snap) so that they sit beside the InvoiceSystem tables of the same names. Can be run again: a table that exists is left alone.
-- sqlcmd -S <server> -E -C -I -b -d InvoiceSystem_test -i snapshot_sqlserver.sql
-- for SnapshotColumns=OrderItem:ProductName<-Product.Name+UnitPrice<-Product.Price+TaxRate<-TaxCategory.Rate:LineTotal=Quantity*UnitPrice  (generate with --schema snap)
IF SCHEMA_ID('snap') IS NULL
    EXEC ('CREATE SCHEMA snap');
GO
IF OBJECT_ID('snap.Product', 'U') IS NULL
CREATE TABLE snap.Product (
    ProductId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_snap_Product PRIMARY KEY,
    Name VARCHAR(100) NOT NULL,
    Price DECIMAL(10, 2) NOT NULL,
    Sku VARCHAR(30) NULL,
    Description VARCHAR(200) NULL,
    Unit VARCHAR(20) NULL
);
GO
IF OBJECT_ID('snap.TaxCategory', 'U') IS NULL
CREATE TABLE snap.TaxCategory (
    TaxCategoryId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_snap_TaxCategory PRIMARY KEY,
    Name VARCHAR(60) NOT NULL,
    Rate DECIMAL(5, 2) NOT NULL,
    Code VARCHAR(20) NULL,
    Notes VARCHAR(100) NULL,
    Region VARCHAR(40) NULL
);
GO
IF OBJECT_ID('snap.SalesOrder', 'U') IS NULL
CREATE TABLE snap.SalesOrder (
    SalesOrderId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_snap_SalesOrder PRIMARY KEY,
    Reference VARCHAR(40) NOT NULL,
    Customer VARCHAR(100) NOT NULL,
    Note VARCHAR(200) NULL,
    Channel VARCHAR(30) NULL,
    Remarks VARCHAR(100) NULL
);
GO
IF OBJECT_ID('snap.OrderItem', 'U') IS NULL
CREATE TABLE snap.OrderItem (
    OrderItemId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_snap_OrderItem PRIMARY KEY,
    SalesOrderId INT NOT NULL CONSTRAINT FK_snap_OrderItem_SalesOrder REFERENCES snap.SalesOrder (SalesOrderId),
    ProductId INT NOT NULL CONSTRAINT FK_snap_OrderItem_Product REFERENCES snap.Product (ProductId),
    TaxCategoryId INT NULL CONSTRAINT FK_snap_OrderItem_TaxCategory REFERENCES snap.TaxCategory (TaxCategoryId),
    ProductName VARCHAR(100) NOT NULL,
    UnitPrice DECIMAL(10, 2) NOT NULL,
    TaxRate DECIMAL(5, 2) NULL,
    Quantity INT NOT NULL,
    LineTotal DECIMAL(12, 2) NOT NULL
);
GO
