-- The tables of snapshot.sql for the standing PostgreSQL InvoiceSystem database (it has none of these names), for
-- SnapshotColumns=OrderItem:ProductName<-Product.Name+UnitPrice<-Product.Price+TaxRate<-TaxCategory.Rate:LineTotal=Quantity*UnitPrice (generate with --only Product,TaxCategory,SalesOrder,OrderItem).
-- PGPASSWORD=... psql -h localhost -U <login> -d InvoiceSystem -f snapshot_postgres.sql      Can be run again.
CREATE TABLE IF NOT EXISTS "Product" (
    "ProductId" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "Name" varchar(100) NOT NULL,
    "Price" numeric(10, 2) NOT NULL,
    "Sku" varchar(30) NULL,
    "Description" varchar(200) NULL,
    "Unit" varchar(20) NULL
);
CREATE TABLE IF NOT EXISTS "TaxCategory" (
    "TaxCategoryId" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "Name" varchar(60) NOT NULL,
    "Rate" numeric(5, 2) NOT NULL,
    "Code" varchar(20) NULL,
    "Notes" varchar(100) NULL,
    "Region" varchar(40) NULL
);
CREATE TABLE IF NOT EXISTS "SalesOrder" (
    "SalesOrderId" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "Reference" varchar(40) NOT NULL,
    "Customer" varchar(100) NOT NULL,
    "Note" varchar(200) NULL,
    "Channel" varchar(30) NULL,
    "Remarks" varchar(100) NULL
);
CREATE TABLE IF NOT EXISTS "OrderItem" (
    "OrderItemId" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "SalesOrderId" integer NOT NULL REFERENCES "SalesOrder" ("SalesOrderId"),
    "ProductId" integer NOT NULL REFERENCES "Product" ("ProductId"),
    "TaxCategoryId" integer NULL REFERENCES "TaxCategory" ("TaxCategoryId"),
    "ProductName" varchar(100) NOT NULL,
    "UnitPrice" numeric(10, 2) NOT NULL,
    "TaxRate" numeric(5, 2) NULL,
    "Quantity" integer NOT NULL,
    "LineTotal" numeric(12, 2) NOT NULL
);
