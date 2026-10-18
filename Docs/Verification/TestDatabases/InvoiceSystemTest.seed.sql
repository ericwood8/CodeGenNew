-- Fixtures for InvoiceSystem_test, on top of a copy of the real InvoiceSystem. Every row it adds is tagged ZZ- (AccountNumber, ItemNumber), so a test can find it
-- and a re-run removes it first. Never run against the real database.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() NOT LIKE '%[_]test'
    THROW 50000, 'This seed is for a database named *_test.', 1;

BEGIN TRAN;

-- ===== an earlier seed, children first =====
DELETE l FROM SalesInvoiceLine l
    LEFT JOIN SalesInvoice i ON i.SalesInvoiceId = l.SalesInvoiceId
    LEFT JOIN Customer c ON c.CustomerId = i.CustomerId
    LEFT JOIN Item it ON it.ItemId = l.ItemId
    WHERE c.AccountNumber LIKE 'ZZ-%' OR it.ItemNumber LIKE 'ZZ-%';
DELETE i FROM SalesInvoice i JOIN Customer c ON c.CustomerId = i.CustomerId WHERE c.AccountNumber LIKE 'ZZ-%';
DELETE ci FROM CustomerItem ci
    LEFT JOIN Customer c ON c.CustomerId = ci.CustomerId
    LEFT JOIN Item it ON it.ItemId = ci.ItemId
    WHERE c.AccountNumber LIKE 'ZZ-%' OR it.ItemNumber LIKE 'ZZ-%';
DELETE s FROM CustomerMonthlySummary s JOIN Customer c ON c.CustomerId = s.CustomerId WHERE c.AccountNumber LIKE 'ZZ-%';
DELETE FROM Customer WHERE AccountNumber LIKE 'ZZ-%';
DELETE FROM Item WHERE ItemNumber LIKE 'ZZ-%';

-- ===== customers: stored values that already break a rule (one rule each, so a test knows which value it is looking at) =====
INSERT Customer (AccountNumber, CustomerName, CustomerStatusId, IsTaxable, DateAdded, RequireCustomerPO, IgnoreBadStatus, CreditLimit, BillingEmailAddress, BillingPhoneNumber, BillingZip, DateClosed, AlternateName)
VALUES
 ('ZZ-BADEMAIL',      'ZZ Bad email',            1, 0, GETDATE(), 0, 0, 100,  'not-an-email', NULL,  NULL, NULL, 'unchanged'),
 ('ZZ-BADPHONE',      'ZZ Bad phone',            1, 0, GETDATE(), 0, 0, 100,  NULL,           'abc',  NULL, NULL, 'unchanged'),
 ('ZZ-BADZIP',        'ZZ Bad postal code',      1, 0, GETDATE(), 0, 0, 100,  NULL,           NULL,   '#',  NULL, 'unchanged'),
 ('ZZ-NEGCREDIT',     'ZZ Negative credit',      1, 0, GETDATE(), 0, 0, -5,   NULL,           NULL,   NULL, NULL, 'unchanged'),
 ('ZZ-OKCREDIT',      'ZZ Credit 250',           1, 0, GETDATE(), 0, 0, 250,  NULL,           NULL,   NULL, NULL, 'unchanged'),
 ('ZZ-FUTURECLOSED',  'ZZ Closed in the future', 1, 0, GETDATE(), 0, 0, 100,  NULL,           NULL,   NULL, DATEADD(YEAR, 5, GETDATE()), 'unchanged');

-- ===== customers: to create, edit, clone and delete =====
INSERT Customer (AccountNumber, CustomerName, CustomerStatusId, IsTaxable, DateAdded, RequireCustomerPO, IgnoreBadStatus, CreditLimit, AlternateName)
VALUES
 ('ZZ-FREE1',   'ZZ Free to delete 1', 1, 0, GETDATE(), 0, 0, 100, 'free'),
 ('ZZ-FREE2',   'ZZ Free to delete 2', 1, 0, GETDATE(), 0, 0, 100, 'free'),
 ('ZZ-FREE3',   'ZZ Free to delete 3', 1, 0, GETDATE(), 0, 0, 100, 'free'),
 ('ZZ-INUSE',   'ZZ In use',           1, 0, GETDATE(), 0, 0, 100, 'has an invoice, a summary and a purchase'),
 -- quotes, wildcards, brackets, a backslash, markup and accents, for search, sort and the forms
 ('ZZ-HOSTILE', 'ZZ O''Brien "Q" 100% _x [y] \ <b>&amp;', 1, 0, GETDATE(), 0, 0, 100, 'caf' + CHAR(233) + ' ' + CHAR(252) + CHAR(246) + ' -- ; DROP TABLE x');

-- ===== items =====
INSERT Item (ItemNumber, ItemDescription, ItemStatusId, DateAdded, IsTaxable, IsDecimalAllowed, IsOkToDiscount, IsOkToBackorder, ListPrice, LastCost)
VALUES
 ('ZZ-ITEM-NEGPRICE', 'ZZ Stored negative price', 1, GETDATE(), 0, 0, 0, 0, -5,  -3),
 ('ZZ-ITEM-OKPRICE',  'ZZ Price 20',              1, GETDATE(), 0, 0, 0, 0, 20,  10),
 ('ZZ-ITEM-FREE1',    'ZZ Free to delete 1',      1, GETDATE(), 0, 0, 0, 0, 20,  10),
 ('ZZ-ITEM-FREE2',    'ZZ Free to delete 2',      1, GETDATE(), 0, 0, 0, 0, 20,  10),
 ('ZZ-ITEM-INUSE',    'ZZ In use',                1, GETDATE(), 0, 0, 0, 0, 20,  10);

-- ===== the rows that make ZZ-INUSE and ZZ-ITEM-INUSE "in use": a summary, an invoice with two lines, a purchase =====
DECLARE @customer int = (SELECT CustomerId FROM Customer WHERE AccountNumber = 'ZZ-INUSE');
DECLARE @item int = (SELECT ItemId FROM Item WHERE ItemNumber = 'ZZ-ITEM-INUSE');
DECLARE @other int = (SELECT ItemId FROM Item WHERE ItemNumber = 'ZZ-ITEM-OKPRICE');

INSERT CustomerMonthlySummary (CustomerId, MonthNumber, [Year], TotalOfInvoices, IsClosed, IsReviewed) VALUES (@customer, 1, YEAR(GETDATE()), 0, 0, 0);
DECLARE @summary int = SCOPE_IDENTITY();

INSERT SalesInvoice (CustomerId, CustomerMonthlySummaryId, InvoiceDate, IsPrinted, IsDisputed, RevisionNumber, IsWillCall, IsShipComplete, IsBlindShip, CreateDate, CreateUser, InvoiceStatusId, Notes)
VALUES (@customer, @summary, GETDATE(), 0, 0, 1, 0, 0, 0, GETDATE(), 'seed', 1, 'ZZ invoice for the child-row editor');
DECLARE @invoice int = SCOPE_IDENTITY();

INSERT SalesInvoiceLine (SalesInvoiceId, LineNumber, ItemId, Quantity, ItemPrice, IsTaxable, IsCommissionPaid, DiscountPercent)
VALUES (@invoice, 1, @item, 2, 20, 0, 0, 0), (@invoice, 2, @other, 1, 20, 0, 0, 0);

INSERT CustomerItem (CustomerId, ItemId, PurchasedDate) VALUES (@customer, @item, GETDATE());

-- ===== the last page of a grid holds one row (a page is 10 rows): fillers until the count ends in 1 =====
DECLARE @n int = 1;
WHILE (SELECT COUNT(*) FROM Customer) % 10 <> 1
BEGIN
    INSERT Customer (AccountNumber, CustomerName, CustomerStatusId, IsTaxable, DateAdded, RequireCustomerPO, IgnoreBadStatus)
    VALUES ('ZZ-FILL' + CAST(@n AS varchar(10)), 'ZZ Filler ' + CAST(@n AS varchar(10)), 1, 0, GETDATE(), 0, 0);
    SET @n += 1;
END;
SET @n = 1;
WHILE (SELECT COUNT(*) FROM Item) % 10 <> 1
BEGIN
    INSERT Item (ItemNumber, ItemDescription, ItemStatusId, DateAdded, IsTaxable, IsDecimalAllowed, IsOkToDiscount, IsOkToBackorder)
    VALUES ('ZZ-ITEM-FILL' + CAST(@n AS varchar(10)), 'ZZ Filler ' + CAST(@n AS varchar(10)), 1, GETDATE(), 0, 0, 0, 0);
    SET @n += 1;
END;

COMMIT;

SELECT 'Customer' AS [table], COUNT(*) AS [rows], SUM(CASE WHEN AccountNumber LIKE 'ZZ-%' THEN 1 ELSE 0 END) AS seeded FROM Customer
UNION ALL SELECT 'Item', COUNT(*), SUM(CASE WHEN ItemNumber LIKE 'ZZ-%' THEN 1 ELSE 0 END) FROM Item;
