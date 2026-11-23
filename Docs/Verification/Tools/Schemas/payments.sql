-- SQLite. Payment terms, bank accounts, due dates and installments for
-- BankAccountTables=PartyBankAccount  InstallmentTables=PaymentInstallment:PayDocumentId:PayDocument:Total  PaymentTermsTables=PaymentMeans:PayDocumentId:PayDocument:OrderedOn  LedgerTables=PayTransaction:PayDocumentId:PayDocument:Total
CREATE TABLE PayDocument (
    PayDocumentId INTEGER PRIMARY KEY AUTOINCREMENT,
    Number VARCHAR(20) NOT NULL,
    OrderedOn DATE NOT NULL,
    Total DECIMAL(12, 2) NOT NULL
);
CREATE TABLE PaymentTerms (
    PaymentTermsId INTEGER PRIMARY KEY AUTOINCREMENT,
    Code VARCHAR(12) NOT NULL UNIQUE,
    Name VARCHAR(80) NOT NULL,
    DaysToPay INTEGER NOT NULL,
    EarlyDiscountPercent DECIMAL(5, 2) NULL,
    EarlyDiscountDays INTEGER NULL,
    LatePenaltyPercent DECIMAL(5, 2) NULL
);
CREATE TABLE PartyBankAccount (
    PartyBankAccountId INTEGER PRIMARY KEY AUTOINCREMENT,
    AccountHolder VARCHAR(100) NOT NULL,
    Iban VARCHAR(34) NULL,
    AccountNumber VARCHAR(20) NULL,
    RoutingNumber VARCHAR(12) NULL,
    BankName VARCHAR(100) NULL
);
CREATE TABLE PaymentMeans (
    PaymentMeansId INTEGER PRIMARY KEY AUTOINCREMENT,
    PayDocumentId INTEGER NOT NULL REFERENCES PayDocument (PayDocumentId),
    PaymentTermsId INTEGER NULL REFERENCES PaymentTerms (PaymentTermsId),
    MeansCode VARCHAR(14) NOT NULL,
    DueDate DATE NULL
);
CREATE TABLE PaymentInstallment (
    PaymentInstallmentId INTEGER PRIMARY KEY AUTOINCREMENT,
    PayDocumentId INTEGER NOT NULL REFERENCES PayDocument (PayDocumentId),
    SequenceNo INTEGER NOT NULL,
    DueDate DATE NOT NULL,
    Amount DECIMAL(12, 2) NOT NULL
);
CREATE UNIQUE INDEX UX_PaymentInstallment_Sequence ON PaymentInstallment (PayDocumentId, SequenceNo);
CREATE TABLE PayTransaction (
    PayTransactionId INTEGER PRIMARY KEY AUTOINCREMENT,
    PayDocumentId INTEGER NOT NULL REFERENCES PayDocument (PayDocumentId),
    Kind VARCHAR(12) NOT NULL,
    Status VARCHAR(12) NOT NULL,
    Amount DECIMAL(12, 2) NOT NULL,
    CurrencyCode CHAR(3) NOT NULL DEFAULT 'USD',
    GatewayId VARCHAR(100) NOT NULL UNIQUE,
    IdempotencyKey VARCHAR(100) NOT NULL UNIQUE,
    Payload TEXT NULL,
    CreatedOn DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);
