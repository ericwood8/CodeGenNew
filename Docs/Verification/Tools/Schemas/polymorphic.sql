-- Two link tables that attach to a Product, a Customer or an Opportunity: Attachment (a delete of a target row is refused while it has attachments) and Remark (the remarks are removed with the row),
-- for PolymorphicLinks=Attachment:OwnerType:OwnerId:Product|Customer|Opportunity,Remark:OwnerKind:OwnerRef:Product|Customer:remove
CREATE TABLE Product (
    ProductId INTEGER PRIMARY KEY AUTOINCREMENT,
    Name VARCHAR(100) NOT NULL,
    Sku VARCHAR(30) NOT NULL,
    Price DECIMAL(10,2) NOT NULL DEFAULT 0,
    Category VARCHAR(40) NULL,
    Notes VARCHAR(200) NULL
);
CREATE TABLE Customer (
    CustomerId INTEGER PRIMARY KEY AUTOINCREMENT,
    Name VARCHAR(100) NOT NULL,
    Email VARCHAR(100) NULL,
    City VARCHAR(60) NULL,
    Phone VARCHAR(30) NULL,
    Notes VARCHAR(200) NULL
);
CREATE TABLE Opportunity (
    OpportunityId INTEGER PRIMARY KEY AUTOINCREMENT,
    Title VARCHAR(100) NOT NULL,
    Stage VARCHAR(30) NOT NULL,
    Amount DECIMAL(10,2) NOT NULL DEFAULT 0,
    SalesRep VARCHAR(60) NULL,
    Notes VARCHAR(200) NULL
);
CREATE TABLE Attachment (
    AttachmentId INTEGER PRIMARY KEY AUTOINCREMENT,
    OwnerType VARCHAR(30) NOT NULL,
    OwnerId INTEGER NOT NULL,
    Title VARCHAR(120) NOT NULL,
    Url VARCHAR(300) NULL,
    Caption VARCHAR(200) NULL
);
CREATE INDEX IX_Attachment_Owner ON Attachment (OwnerType, OwnerId);
CREATE TABLE Remark (
    RemarkId INTEGER PRIMARY KEY AUTOINCREMENT,
    OwnerKind VARCHAR(30) NOT NULL,
    OwnerRef INTEGER NOT NULL,
    Subject VARCHAR(100) NULL,
    Body VARCHAR(500) NOT NULL,
    Author VARCHAR(60) NULL
);
CREATE INDEX IX_Remark_Owner ON Remark (OwnerKind, OwnerRef);
