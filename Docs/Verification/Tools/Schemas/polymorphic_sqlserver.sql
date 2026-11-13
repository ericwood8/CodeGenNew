-- The tables of polymorphic.sql for SQL Server, in a schema of their own (poly) so that they sit beside the InvoiceSystem tables of the same names. Can be run again: a table that exists is left alone.
-- sqlcmd -S <server> -U <login> -C -I -b -d InvoiceSystem_test -i polymorphic_sqlserver.sql
-- for PolymorphicLinks=Attachment:OwnerType:OwnerId:Product|Customer|Opportunity,Remark:OwnerKind:OwnerRef:Product|Customer:remove  (generate with --schema poly)
IF SCHEMA_ID('poly') IS NULL
    EXEC ('CREATE SCHEMA poly');
GO
IF OBJECT_ID('poly.Product', 'U') IS NULL
CREATE TABLE poly.Product (
    ProductId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_poly_Product PRIMARY KEY,
    Name VARCHAR(100) NOT NULL,
    Sku VARCHAR(30) NOT NULL,
    Price DECIMAL(10,2) NOT NULL CONSTRAINT DF_poly_Product_Price DEFAULT 0,
    Category VARCHAR(40) NULL,
    Notes VARCHAR(200) NULL
);
GO
IF OBJECT_ID('poly.Customer', 'U') IS NULL
CREATE TABLE poly.Customer (
    CustomerId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_poly_Customer PRIMARY KEY,
    Name VARCHAR(100) NOT NULL,
    Email VARCHAR(100) NULL,
    City VARCHAR(60) NULL,
    Phone VARCHAR(30) NULL,
    Notes VARCHAR(200) NULL
);
GO
IF OBJECT_ID('poly.Opportunity', 'U') IS NULL
CREATE TABLE poly.Opportunity (
    OpportunityId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_poly_Opportunity PRIMARY KEY,
    Title VARCHAR(100) NOT NULL,
    Stage VARCHAR(30) NOT NULL,
    Amount DECIMAL(10,2) NOT NULL CONSTRAINT DF_poly_Opportunity_Amount DEFAULT 0,
    SalesRep VARCHAR(60) NULL,
    Notes VARCHAR(200) NULL
);
GO
IF OBJECT_ID('poly.Attachment', 'U') IS NULL
CREATE TABLE poly.Attachment (
    AttachmentId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_poly_Attachment PRIMARY KEY,
    OwnerType VARCHAR(30) NOT NULL,
    OwnerId INT NOT NULL,
    Title VARCHAR(120) NOT NULL,
    Url VARCHAR(300) NULL,
    Caption VARCHAR(200) NULL
);
GO
IF OBJECT_ID('poly.Remark', 'U') IS NULL
CREATE TABLE poly.Remark (
    RemarkId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_poly_Remark PRIMARY KEY,
    OwnerKind VARCHAR(30) NOT NULL,
    OwnerRef INT NOT NULL,
    Subject VARCHAR(100) NULL,
    Body VARCHAR(500) NOT NULL,
    Author VARCHAR(60) NULL
);
GO
IF COL_LENGTH('poly.Remark', 'Subject') IS NULL
    ALTER TABLE poly.Remark ADD Subject VARCHAR(100) NULL;
GO
