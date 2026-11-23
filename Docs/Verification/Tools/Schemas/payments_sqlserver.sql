-- The tables of payments.sql for SQL Server, in the schema pay, for BankAccountTables, InstallmentTables and PaymentTermsTables (generate with --schema pay).
-- sqlcmd -S <server> -E -C -I -b -d InvoiceSystem_test -i payments_sqlserver.sql      Can be run again: a table that exists is left alone.
IF SCHEMA_ID('pay') IS NULL
    EXEC ('CREATE SCHEMA pay');
GO
IF OBJECT_ID('pay.PayDocument', 'U') IS NULL
CREATE TABLE pay.PayDocument (
    PayDocumentId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_pay_PayDocument PRIMARY KEY,
    Number VARCHAR(20) NOT NULL,
    OrderedOn DATE NOT NULL,
    Total DECIMAL(12, 2) NOT NULL
);
GO
IF OBJECT_ID('pay.PaymentTerms', 'U') IS NULL
CREATE TABLE pay.PaymentTerms (
    PaymentTermsId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_pay_PaymentTerms PRIMARY KEY,
    Code VARCHAR(12) NOT NULL CONSTRAINT UQ_pay_PaymentTerms_Code UNIQUE,
    Name VARCHAR(80) NOT NULL,
    DaysToPay INT NOT NULL,
    EarlyDiscountPercent DECIMAL(5, 2) NULL,
    EarlyDiscountDays INT NULL,
    LatePenaltyPercent DECIMAL(5, 2) NULL
);
GO
IF OBJECT_ID('pay.PartyBankAccount', 'U') IS NULL
CREATE TABLE pay.PartyBankAccount (
    PartyBankAccountId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_pay_PartyBankAccount PRIMARY KEY,
    AccountHolder VARCHAR(100) NOT NULL,
    Iban VARCHAR(34) NULL,
    AccountNumber VARCHAR(20) NULL,
    RoutingNumber VARCHAR(12) NULL,
    BankName VARCHAR(100) NULL
);
GO
IF OBJECT_ID('pay.PaymentMeans', 'U') IS NULL
CREATE TABLE pay.PaymentMeans (
    PaymentMeansId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_pay_PaymentMeans PRIMARY KEY,
    PayDocumentId INT NOT NULL CONSTRAINT FK_pay_PaymentMeans_Document REFERENCES pay.PayDocument (PayDocumentId),
    PaymentTermsId INT NULL CONSTRAINT FK_pay_PaymentMeans_Terms REFERENCES pay.PaymentTerms (PaymentTermsId),
    MeansCode VARCHAR(14) NOT NULL,
    DueDate DATE NULL
);
GO
IF OBJECT_ID('pay.PaymentInstallment', 'U') IS NULL
CREATE TABLE pay.PaymentInstallment (
    PaymentInstallmentId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_pay_PaymentInstallment PRIMARY KEY,
    PayDocumentId INT NOT NULL CONSTRAINT FK_pay_PaymentInstallment_Document REFERENCES pay.PayDocument (PayDocumentId),
    SequenceNo INT NOT NULL,
    DueDate DATE NOT NULL,
    Amount DECIMAL(12, 2) NOT NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_pay_PaymentInstallment_Sequence' AND object_id = OBJECT_ID('pay.PaymentInstallment'))
    CREATE UNIQUE INDEX UX_pay_PaymentInstallment_Sequence ON pay.PaymentInstallment (PayDocumentId, SequenceNo);
GO
