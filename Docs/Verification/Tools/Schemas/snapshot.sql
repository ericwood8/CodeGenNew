-- Order lines that keep what the product and the tax category looked like when the line was written, for
-- SnapshotColumns=OrderItem:ProductName<-Product.Name+UnitPrice<-Product.Price+TaxRate<-TaxCategory.Rate:LineTotal=Quantity*UnitPrice
CREATE TABLE Product (
    ProductId INTEGER PRIMARY KEY AUTOINCREMENT,
    Name VARCHAR(100) NOT NULL,
    Price DECIMAL(10, 2) NOT NULL,
    Sku VARCHAR(30) NULL,
    Description VARCHAR(200) NULL,
    Unit VARCHAR(20) NULL
);
CREATE TABLE TaxCategory (
    TaxCategoryId INTEGER PRIMARY KEY AUTOINCREMENT,
    Name VARCHAR(60) NOT NULL,
    Rate DECIMAL(5, 2) NOT NULL,
    Code VARCHAR(20) NULL,
    Notes VARCHAR(100) NULL,
    Region VARCHAR(40) NULL
);
CREATE TABLE SalesOrder (
    SalesOrderId INTEGER PRIMARY KEY AUTOINCREMENT,
    Reference VARCHAR(40) NOT NULL,
    Customer VARCHAR(100) NOT NULL,
    Note VARCHAR(200) NULL,
    Channel VARCHAR(30) NULL,
    Remarks VARCHAR(100) NULL
);
CREATE TABLE OrderItem (
    OrderItemId INTEGER PRIMARY KEY AUTOINCREMENT,
    SalesOrderId INTEGER NOT NULL REFERENCES SalesOrder (SalesOrderId),
    ProductId INTEGER NOT NULL REFERENCES Product (ProductId),
    TaxCategoryId INTEGER NULL REFERENCES TaxCategory (TaxCategoryId),
    ProductName VARCHAR(100) NOT NULL,
    UnitPrice DECIMAL(10, 2) NOT NULL,
    TaxRate DECIMAL(5, 2) NULL,
    Quantity INTEGER NOT NULL,
    LineTotal DECIMAL(12, 2) NOT NULL
);
