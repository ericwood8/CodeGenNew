-- Rows for the tables of InvoiceSystemTableShapes.sql (spec item 143). Re-runnable: a row that is there already (by its natural key) is not added again.
-- Added to the real InvoiceSystem and to InvoiceSystem_test:  sqlcmd -S <server> -E -I -d <database> -b -i InvoiceSystemTableShapes.seed.sql
-- The people are the Employee rows jdoe (administrator) and akim; customers and items are the first rows of those tables. Tokens are hashes of a made-up text, never a real secret.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;

DECLARE @jdoe INT = (SELECT TOP 1 EmployeeId FROM dbo.Employee WHERE Login = 'jdoe');
DECLARE @akim INT = (SELECT TOP 1 EmployeeId FROM dbo.Employee WHERE Login = 'akim');
IF @jdoe IS NULL SET @jdoe = (SELECT MIN(EmployeeId) FROM dbo.Employee);
IF @akim IS NULL SET @akim = (SELECT MAX(EmployeeId) FROM dbo.Employee);
DECLARE @t1 INT = (SELECT TenantId FROM dbo.Tenant WHERE Slug = 'northwind');
DECLARE @t2 INT = (SELECT TenantId FROM dbo.Tenant WHERE Slug = 'contoso');
DECLARE @t3 INT = (SELECT TenantId FROM dbo.Tenant WHERE Slug = 'fabrikam');
DECLARE @c1 INT = (SELECT MIN(CustomerId) FROM dbo.Customer);
DECLARE @c2 INT = (SELECT MIN(CustomerId) FROM dbo.Customer WHERE CustomerId > @c1);
DECLARE @c3 INT = (SELECT MIN(CustomerId) FROM dbo.Customer WHERE CustomerId > @c2);
DECLARE @i1 INT = (SELECT MIN(ItemId) FROM dbo.Item);
DECLARE @i2 INT = (SELECT MIN(ItemId) FROM dbo.Item WHERE ItemId > @i1);
DECLARE @i3 INT = (SELECT MIN(ItemId) FROM dbo.Item WHERE ItemId > @i2);
DECLARE @i4 INT = (SELECT MIN(ItemId) FROM dbo.Item WHERE ItemId > @i3);

-- 5. Tenants: who works where, with a role per tenant
INSERT INTO dbo.EmployeeTenant (EmployeeId, TenantId, Role)
SELECT v.E, v.T, v.R FROM (VALUES (@jdoe, @t1, 'Administrator'), (@akim, @t1, 'Employee'), (@jdoe, @t2, 'Employee'), (@akim, @t2, 'Administrator')) v (E, T, R)
WHERE v.E IS NOT NULL AND v.T IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.EmployeeTenant m WHERE m.EmployeeId = v.E AND m.TenantId = v.T);

-- 5 and 15. Tenant-owned projects, one of them soft deleted
INSERT INTO dbo.Project (TenantId, Name, Description, Budget, StartedOn, IsDeleted, DeletedOn, DeletedBy)
SELECT v.T, v.N, v.D, v.B, v.S, v.X, v.O, v.Y
FROM (VALUES
    (@t1, 'Apollo', 'Warehouse rollout', 12000.00, CAST('2026-03-01' AS DATE), 0, NULL, NULL),
    (@t1, 'Gemini', 'Billing rewrite', 8500.00, CAST('2026-04-15' AS DATE), 0, NULL, NULL),
    (@t1, 'Vostok', 'Cancelled pilot', 500.00, CAST('2026-01-10' AS DATE), 1, CAST('2026-05-01 10:00' AS DATETIME), 'jdoe'),
    (@t2, 'Mercury', 'Storefront launch', 20000.00, CAST('2026-05-01' AS DATE), 0, NULL, NULL),
    (@t2, 'Voyager', 'Data migration', 4300.00, CAST('2026-06-20' AS DATE), 0, NULL, NULL)
) v (T, N, D, B, S, X, O, Y)
WHERE v.T IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Project p WHERE p.TenantId = v.T AND p.Name = v.N);

-- 7. A tree of categories
IF NOT EXISTS (SELECT 1 FROM dbo.Category)
BEGIN
    INSERT INTO dbo.Category (Name, ParentCategoryId, SortOrder, Description) VALUES ('All products', NULL, 0, 'The root');
    DECLARE @root INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Category (Name, ParentCategoryId, SortOrder, Description) VALUES ('Hardware', @root, 1, 'Things you can drop on your foot');
    DECLARE @hw INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Category (Name, ParentCategoryId, SortOrder, Description) VALUES ('Software', @root, 2, 'Things you cannot');
    DECLARE @sw INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Category (Name, ParentCategoryId, SortOrder) VALUES ('Fasteners', @hw, 1), ('Tools', @hw, 2), ('Licences', @sw, 1), ('Subscriptions', @sw, 2);
    DECLARE @tools INT = (SELECT CategoryId FROM dbo.Category WHERE Name = 'Tools');
    INSERT INTO dbo.Category (Name, ParentCategoryId, SortOrder) VALUES ('Hand tools', @tools, 1), ('Power tools', @tools, 2);
END;

-- 8, 7. Content with a publication state and a slug, and a thread of comments
INSERT INTO dbo.Post (Title, Slug, Body, Status, PublishedOn, AuthorEmployeeId)
SELECT v.T, v.S, v.B, v.St, v.P, @jdoe
FROM (VALUES
    ('Welcome to the shop', 'welcome-to-the-shop', 'We are open.', 'published', CAST('2026-06-01 09:00' AS DATETIME)),
    ('Spring sale', 'spring-sale', 'Twenty percent off fasteners.', 'published', CAST('2026-04-01 08:00' AS DATETIME)),
    ('Holiday hours (draft)', 'holiday-hours-draft', 'To be written.', 'draft', NULL),
    ('Old catalogue', 'old-catalogue', 'Kept for reference.', 'archived', CAST('2025-01-15 12:00' AS DATETIME))
) v (T, S, B, St, P)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Post p WHERE p.Slug = v.S);

IF NOT EXISTS (SELECT 1 FROM dbo.PostComment)
BEGIN
    DECLARE @post INT = (SELECT PostId FROM dbo.Post WHERE Slug = 'welcome-to-the-shop');
    INSERT INTO dbo.PostComment (PostId, ParentPostCommentId, AuthorEmployeeId, Body) VALUES (@post, NULL, @akim, 'Great to see the shop open.');
    DECLARE @first INT = SCOPE_IDENTITY();
    INSERT INTO dbo.PostComment (PostId, ParentPostCommentId, AuthorEmployeeId, Body) VALUES (@post, @first, @jdoe, 'Thank you, more items soon.');
    DECLARE @second INT = SCOPE_IDENTITY();
    INSERT INTO dbo.PostComment (PostId, ParentPostCommentId, AuthorEmployeeId, Body) VALUES (@post, @second, @akim, 'Looking forward to it.'), (@post, NULL, @jdoe, 'A second top-level comment.');
END;

-- 15. Prices that are valid for a period (no overlap inside an item and price type)
INSERT INTO dbo.ItemPrice (ItemId, PriceType, Price, ValidFrom, ValidTo)
SELECT v.I, 'list', v.P, v.F, v.T
FROM (VALUES
    (@i1, 9.50, CAST('2025-01-01' AS DATE), CAST('2025-12-31' AS DATE)),
    (@i1, 10.25, CAST('2026-01-01' AS DATE), CAST('2026-06-30' AS DATE)),
    (@i1, 11.00, CAST('2026-07-01' AS DATE), CAST(NULL AS DATE)),
    (@i2, 24.00, CAST('2026-01-01' AS DATE), CAST(NULL AS DATE)),
    (@i3, 3.75, CAST('2025-06-01' AS DATE), CAST('2026-02-28' AS DATE)),
    (@i3, 4.10, CAST('2026-03-01' AS DATE), CAST(NULL AS DATE))
) v (I, P, F, T)
WHERE v.I IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.ItemPrice x WHERE x.ItemId = v.I AND x.PriceType = 'list' AND x.ValidFrom = v.F);

-- 17 and 4. Orders that keep the product as it was, and the carts around them
IF NOT EXISTS (SELECT 1 FROM dbo.SalesOrder)
BEGIN
    INSERT INTO dbo.SalesOrder (CustomerId, Status, CurrencyCode, PlacedOn, Total, Notes) VALUES (@c1, 'placed', 'USD', '2026-09-10 14:05', 0, 'Seed order, placed');
    DECLARE @o1 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.SalesOrder (CustomerId, Status, CurrencyCode, PlacedOn, Total, Notes) VALUES (@c2, 'shipped', 'USD', '2026-09-12 09:30', 0, 'Seed order, shipped');
    DECLARE @o2 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.SalesOrder (CustomerId, Status, CurrencyCode, Total, Notes) VALUES (@c3, 'draft', 'USD', 0, 'Seed order, still a draft');
    INSERT INTO dbo.SalesOrderLine (SalesOrderId, ItemId, ItemNumber, ItemDescription, UnitPrice, TaxRate, Quantity)
    SELECT o, i.ItemId, i.ItemNumber, i.ItemDescription, p, 0.0825, q
    FROM (VALUES (@o1, @i1, 11.00, 4), (@o1, @i2, 24.00, 1), (@o2, @i3, 4.10, 10), (@o2, @i4, 7.25, 2)) v (o, i, p, q)
    JOIN dbo.Item i ON i.ItemId = v.i;
    UPDATE o SET Total = (SELECT ISNULL(SUM(LineTotal), 0) FROM dbo.SalesOrderLine l WHERE l.SalesOrderId = o.SalesOrderId) FROM dbo.SalesOrder o;
END;

IF NOT EXISTS (SELECT 1 FROM dbo.Cart)
BEGIN
    INSERT INTO dbo.Cart (CustomerId, SessionKey, Status, LastTouchedOn) VALUES (@c1, NULL, 'open', GETDATE());
    DECLARE @k1 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Cart (CustomerId, SessionKey, Status, CreatedOn, LastTouchedOn) VALUES (NULL, 'anon-7f3a91', 'abandoned', DATEADD(DAY, -40, GETDATE()), DATEADD(DAY, -40, GETDATE()));
    DECLARE @k2 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Cart (CustomerId, Status, SalesOrderId) SELECT @c1, 'converted', MIN(SalesOrderId) FROM dbo.SalesOrder WHERE CustomerId = @c1;
    INSERT INTO dbo.CartItem (CartId, ItemId, Quantity, UnitPrice) VALUES (@k1, @i1, 2, 11.00), (@k1, @i3, 5, 4.10), (@k2, @i2, 1, 24.00);
END;

-- 6. Tokens: only the hash
INSERT INTO dbo.AuthToken (EmployeeId, Kind, TokenHash, ExpiresOn, UsedOn, RevokedOn)
SELECT v.E, v.K, CONVERT(VARCHAR(128), HASHBYTES('SHA2_256', v.S), 2), v.X, v.U, v.R
FROM (VALUES
    (@jdoe, 'session', 'seed-session-jdoe', DATEADD(DAY, 7, GETDATE()), CAST(NULL AS DATETIME), CAST(NULL AS DATETIME)),
    (@akim, 'refresh', 'seed-refresh-akim', DATEADD(DAY, 30, GETDATE()), NULL, NULL),
    (@akim, 'reset', 'seed-reset-akim', DATEADD(HOUR, 1, GETDATE()), NULL, NULL),
    (@akim, 'reset', 'seed-reset-akim-used', DATEADD(HOUR, -5, GETDATE()), DATEADD(HOUR, -6, GETDATE()), NULL),
    (@jdoe, 'session', 'seed-session-revoked', DATEADD(DAY, 3, GETDATE()), NULL, DATEADD(DAY, -1, GETDATE()))
) v (E, K, S, X, U, R)
WHERE NOT EXISTS (SELECT 1 FROM dbo.AuthToken t WHERE t.TokenHash = CONVERT(VARCHAR(128), HASHBYTES('SHA2_256', v.S), 2));

-- 9, 28, 29. Assets, their renditions and what they are attached to
INSERT INTO dbo.Asset (FileName, MimeType, SizeBytes, StorageKey, PublicUrl, UploadedByEmployeeId)
SELECT v.F, v.M, v.Z, v.K, 'https://assets.example.test/' + v.K, @jdoe
FROM (VALUES
    ('brochure.pdf', 'application/pdf', CAST(482113 AS BIGINT), 'docs/brochure.pdf'),
    ('hammer.jpg', 'image/jpeg', CAST(912004 AS BIGINT), 'img/hammer.jpg'),
    ('logo.png', 'image/png', CAST(30421 AS BIGINT), 'img/logo.png')
) v (F, M, Z, K)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Asset a WHERE a.StorageKey = v.K);

INSERT INTO dbo.RenditionKind (Name, MaxWidth, MaxHeight)
SELECT v.N, v.W, v.H FROM (VALUES ('thumbnail', 150, 150), ('medium', 640, 480), ('large', 1600, 1200), ('mobile', 360, 640)) v (N, W, H)
WHERE NOT EXISTS (SELECT 1 FROM dbo.RenditionKind k WHERE k.Name = v.N);

INSERT INTO dbo.AssetRendition (AssetId, RenditionKindId, Url, Width, Height, SizeBytes)
SELECT a.AssetId, k.RenditionKindId, 'https://assets.example.test/' + k.Name + '/' + a.StorageKey, k.MaxWidth, k.MaxHeight, a.SizeBytes / 10
FROM dbo.Asset a CROSS JOIN dbo.RenditionKind k
WHERE a.MimeType LIKE 'image/%' AND k.Name IN ('thumbnail', 'medium', 'mobile')
  AND NOT EXISTS (SELECT 1 FROM dbo.AssetRendition r WHERE r.AssetId = a.AssetId AND r.RenditionKindId = k.RenditionKindId);

INSERT INTO dbo.Attachment (AssetId, RecordType, RecordId, Note)
SELECT a.AssetId, v.T, v.R, v.N
FROM (VALUES ('docs/brochure.pdf', 'Customer', @c1, 'Welcome brochure'), ('img/hammer.jpg', 'Item', @i1, 'Product photo'), ('img/logo.png', 'Post', (SELECT MIN(PostId) FROM dbo.Post), 'Header logo')) v (K, T, R, N)
JOIN dbo.Asset a ON a.StorageKey = v.K
WHERE v.R IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Attachment x WHERE x.AssetId = a.AssetId AND x.RecordType = v.T AND x.RecordId = v.R);

-- 10. Inbound events
INSERT INTO dbo.WebhookEvent (Source, EventId, Signature, Body, IsProcessed, ProcessedOn)
SELECT v.S, v.E, 'sha256=seed', v.B, v.P, CASE WHEN v.P = 1 THEN GETDATE() END
FROM (VALUES
    ('payments', 'evt_1001', '{"type":"payment.succeeded","amount":4400}', 1),
    ('payments', 'evt_1002', '{"type":"payment.failed","amount":1200}', 0),
    ('shipping', 'evt_2001', '{"type":"label.created","carrier":"UPS"}', 0)
) v (S, E, B, P)
WHERE NOT EXISTS (SELECT 1 FROM dbo.WebhookEvent w WHERE w.Source = v.S AND w.EventId = v.E);

IF NOT EXISTS (SELECT 1 FROM dbo.PageView)
    INSERT INTO dbo.PageView (ViewedOn, Path, SessionKey, Referrer, UserAgent) VALUES
        (DATEADD(MINUTE, -50, GETDATE()), '/', 's-100', NULL, 'Mozilla/5.0'), (DATEADD(MINUTE, -45, GETDATE()), '/items', 's-100', '/', 'Mozilla/5.0'),
        (DATEADD(MINUTE, -30, GETDATE()), '/', 's-101', 'https://search.example.test', 'Mozilla/5.0'), (DATEADD(MINUTE, -5, GETDATE()), '/cart', 's-101', '/items', 'Mozilla/5.0');

-- 11. Plans, limits and subscriptions
INSERT INTO dbo.[Plan] (Name, MonthlyPrice, BillingPeriod) SELECT v.N, v.P, 'month' FROM (VALUES ('Free', 0), ('Pro', 29), ('Enterprise', 199)) v (N, P) WHERE NOT EXISTS (SELECT 1 FROM dbo.[Plan] p WHERE p.Name = v.N);
INSERT INTO dbo.PlanLimit (PlanId, LimitKey, LimitValue)
SELECT p.PlanId, v.K, v.V FROM (VALUES ('Free', 'projects', 2), ('Free', 'seats', 2), ('Pro', 'projects', 10), ('Pro', 'seats', 25), ('Enterprise', 'projects', NULL), ('Enterprise', 'seats', NULL)) v (N, K, V)
JOIN dbo.[Plan] p ON p.Name = v.N WHERE NOT EXISTS (SELECT 1 FROM dbo.PlanLimit l WHERE l.PlanId = p.PlanId AND l.LimitKey = v.K);
INSERT INTO dbo.Subscription (TenantId, PlanId, Status, CurrentPeriodStart, CurrentPeriodEnd, GatewaySubscriptionId)
SELECT t.TenantId, p.PlanId, v.S, v.A, v.B, v.G
FROM (VALUES ('northwind', 'Pro', 'active', CAST('2026-10-01' AS DATETIME), CAST('2026-11-01' AS DATETIME), 'sub_northwind'),
             ('contoso', 'Free', 'trialing', CAST('2026-10-20' AS DATETIME), CAST('2027-12-31' AS DATETIME), 'sub_contoso'),
             ('fabrikam', 'Free', 'past_due', CAST('2026-09-01' AS DATETIME), CAST('2026-10-01' AS DATETIME), 'sub_fabrikam')) v (Slug, PlanName, S, A, B, G)
JOIN dbo.Tenant t ON t.Slug = v.Slug JOIN dbo.[Plan] p ON p.Name = v.PlanName
WHERE NOT EXISTS (SELECT 1 FROM dbo.Subscription s WHERE s.TenantId = t.TenantId);

-- 12. Usage
IF NOT EXISTS (SELECT 1 FROM dbo.UsageEvent)
BEGIN
    INSERT INTO dbo.UsageEvent (TenantId, Kind, Quantity, OccurredOn) VALUES
        (@t1, 'api_calls', 1200, '2026-09-30 12:00'), (@t1, 'api_calls', 800, '2026-10-12 12:00'), (@t1, 'storage_gb', 3.5, '2026-10-15 00:00'),
        (@t2, 'api_calls', 150, '2026-10-20 12:00'), (@t2, 'storage_gb', 0.4, '2026-10-21 00:00');
    INSERT INTO dbo.UsageMonthly (TenantId, Kind, UsageMonth, Quantity)
    SELECT TenantId, Kind, DATEFROMPARTS(YEAR(OccurredOn), MONTH(OccurredOn), 1), SUM(Quantity) FROM dbo.UsageEvent GROUP BY TenantId, Kind, DATEFROMPARTS(YEAR(OccurredOn), MONTH(OccurredOn), 1);
END;

-- 13. Roles and permissions
INSERT INTO dbo.Role (Name, Description) SELECT v.N, v.D FROM (VALUES ('Owner', 'Everything'), ('Billing', 'Plans and invoices'), ('Editor', 'Content'), ('Viewer', 'Read only')) v (N, D) WHERE NOT EXISTS (SELECT 1 FROM dbo.Role r WHERE r.Name = v.N);
INSERT INTO dbo.Permission (Code, Description) SELECT v.C, v.D FROM (VALUES ('edit_billing', 'Change the plan and payment details'), ('edit_content', 'Create and change posts'), ('publish_content', 'Publish and unpublish'), ('view_reports', 'Open the dashboard and reports')) v (C, D) WHERE NOT EXISTS (SELECT 1 FROM dbo.Permission p WHERE p.Code = v.C);
INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId FROM (VALUES ('Owner', 'edit_billing'), ('Owner', 'edit_content'), ('Owner', 'publish_content'), ('Owner', 'view_reports'), ('Billing', 'edit_billing'), ('Billing', 'view_reports'),
    ('Editor', 'edit_content'), ('Editor', 'view_reports'), ('Viewer', 'view_reports')) v (R, P)
JOIN dbo.Role r ON r.Name = v.R JOIN dbo.Permission p ON p.Code = v.P WHERE NOT EXISTS (SELECT 1 FROM dbo.RolePermission x WHERE x.RoleId = r.RoleId AND x.PermissionId = p.PermissionId);

-- 14. Feature flags
INSERT INTO dbo.FeatureFlag (FlagKey, Description, DefaultOn) SELECT v.K, v.D, v.O FROM (VALUES ('new_checkout', 'The redesigned checkout', 0), ('dark_mode', 'Dark colour scheme', 1), ('beta_reports', 'Reports in beta', 0)) v (K, D, O) WHERE NOT EXISTS (SELECT 1 FROM dbo.FeatureFlag f WHERE f.FlagKey = v.K);
IF NOT EXISTS (SELECT 1 FROM dbo.FeatureOverride)
    INSERT INTO dbo.FeatureOverride (FeatureFlagId, TenantId, EmployeeId, IsOn)
    SELECT f.FeatureFlagId, v.T, v.E, v.O FROM (VALUES ('new_checkout', @t1, CAST(NULL AS INT), 1), ('dark_mode', @t2, NULL, 0), ('beta_reports', NULL, @jdoe, 1)) v (K, T, E, O) JOIN dbo.FeatureFlag f ON f.FlagKey = v.K;

-- 16. Variants and attributes
INSERT INTO dbo.Attribute (Name, DataType) SELECT v.N, v.D FROM (VALUES ('Size', 'text'), ('Colour', 'colour'), ('Length (mm)', 'number')) v (N, D) WHERE NOT EXISTS (SELECT 1 FROM dbo.Attribute a WHERE a.Name = v.N);
INSERT INTO dbo.AttributeValue (AttributeId, Value, SortOrder)
SELECT a.AttributeId, v.V, v.S FROM (VALUES ('Size', 'S', 1), ('Size', 'M', 2), ('Size', 'L', 3), ('Colour', 'Red', 1), ('Colour', 'Blue', 2), ('Length (mm)', '100', 1), ('Length (mm)', '200', 2)) v (A, V, S)
JOIN dbo.Attribute a ON a.Name = v.A WHERE NOT EXISTS (SELECT 1 FROM dbo.AttributeValue x WHERE x.AttributeId = a.AttributeId AND x.Value = v.V);
INSERT INTO dbo.ProductVariant (ItemId, Sku, Name, Price)
SELECT v.I, v.S, v.N, v.P FROM (VALUES (@i1, 'HAM-S-RED', 'Hammer small, red', CAST(NULL AS DECIMAL(12,2))), (@i1, 'HAM-L-BLUE', 'Hammer large, blue', 13.50), (@i2, 'SCR-100', 'Screw 100 mm', NULL), (@i2, 'SCR-200', 'Screw 200 mm', 26.00)) v (I, S, N, P)
WHERE v.I IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.ProductVariant p WHERE p.Sku = v.S);
INSERT INTO dbo.VariantAttributeValue (ProductVariantId, AttributeValueId)
SELECT pv.ProductVariantId, av.AttributeValueId
FROM (VALUES ('HAM-S-RED', 'Size', 'S'), ('HAM-S-RED', 'Colour', 'Red'), ('HAM-L-BLUE', 'Size', 'L'), ('HAM-L-BLUE', 'Colour', 'Blue'), ('SCR-100', 'Length (mm)', '100'), ('SCR-200', 'Length (mm)', '200')) v (S, A, V)
JOIN dbo.ProductVariant pv ON pv.Sku = v.S JOIN dbo.Attribute a ON a.Name = v.A JOIN dbo.AttributeValue av ON av.AttributeId = a.AttributeId AND av.Value = v.V
WHERE NOT EXISTS (SELECT 1 FROM dbo.VariantAttributeValue x WHERE x.ProductVariantId = pv.ProductVariantId AND x.AttributeValueId = av.AttributeValueId);

-- 18. Stock: the level, and a ledger that explains it
IF NOT EXISTS (SELECT 1 FROM dbo.StockLevel)
BEGIN
    INSERT INTO dbo.StockLevel (ItemId, OnHand, Reserved, ReorderLevel) VALUES (@i1, 100, 10, 20), (@i2, 500, 0, 100), (@i3, 8, 3, 10), (@i4, 0, 0, 5);
    INSERT INTO dbo.StockLedger (ItemId, Kind, Quantity, OccurredOn, Reference) VALUES
        (@i1, 'receipt', 120, '2026-09-01', 'PO-5001'), (@i1, 'sale', -20, '2026-09-10', 'order 1'), (@i1, 'reservation', 10, '2026-10-20', 'cart'),
        (@i2, 'receipt', 500, '2026-09-01', 'PO-5002'), (@i3, 'receipt', 20, '2026-08-15', 'PO-4990'), (@i3, 'sale', -12, '2026-09-12', 'order 2'),
        (@i3, 'reservation', 3, '2026-10-25', 'cart'), (@i3, 'adjustment', 0, '2026-10-01', 'stock-take');
END;

-- 19. Payments
INSERT INTO dbo.PaymentTransaction (SalesOrderId, Kind, Status, Amount, GatewayId, IdempotencyKey, Payload)
SELECT o.SalesOrderId, v.K, v.S, v.A, v.G, v.Y, v.P
FROM (VALUES (1, 'authorize', 'succeeded', 68.00, 'ch_auth_1', 'idem-1', '{"gateway":"test"}'), (1, 'capture', 'succeeded', 68.00, 'ch_cap_1', 'idem-2', '{"gateway":"test"}'),
             (1, 'refund', 'succeeded', -11.00, 'ch_ref_1', 'idem-3', '{"gateway":"test","reason":"damaged"}'), (2, 'authorize', 'failed', 52.00, 'ch_auth_2', 'idem-4', '{"gateway":"test","code":"declined"}')) v (O, K, S, A, G, Y, P)
JOIN (SELECT SalesOrderId, ROW_NUMBER() OVER (ORDER BY SalesOrderId) AS n FROM dbo.SalesOrder) o ON o.n = v.O
WHERE NOT EXISTS (SELECT 1 FROM dbo.PaymentTransaction x WHERE x.GatewayId = v.G);

-- 20. Shipments
IF NOT EXISTS (SELECT 1 FROM dbo.Shipment)
BEGIN
    DECLARE @ship INT = (SELECT MIN(SalesOrderId) FROM dbo.SalesOrder WHERE Status = 'shipped');
    IF @ship IS NOT NULL
    BEGIN
        INSERT INTO dbo.Shipment (SalesOrderId, Carrier, TrackingNumber, ShippedOn, Status) VALUES (@ship, 'UPS', '1Z999AA10123456784', '2026-09-13 10:00', 'delivered');
        DECLARE @s1 INT = SCOPE_IDENTITY();
        INSERT INTO dbo.Shipment (SalesOrderId, Carrier, Status) VALUES (@ship, 'FedEx', 'packing');
        INSERT INTO dbo.ShipmentItem (ShipmentId, SalesOrderLineId, Quantity) SELECT @s1, MIN(SalesOrderLineId), 6 FROM dbo.SalesOrderLine WHERE SalesOrderId = @ship;
    END;
END;

-- 21 and 22. Addresses (one default per customer and type) and a one-to-one profile
INSERT INTO dbo.CustomerAddress (CustomerId, AddressType, Line1, City, Region, PostalCode, Country, IsDefault)
SELECT v.C, v.T, v.L, v.Ci, v.R, v.Z, 'USA', v.D
FROM (VALUES (@c1, 'billing', '100 Main St', 'Springfield', 'IL', '62701', 1), (@c1, 'shipping', '200 Dock Rd', 'Springfield', 'IL', '62702', 1), (@c1, 'shipping', '300 Annex Ave', 'Peoria', 'IL', '61602', 0),
             (@c2, 'billing', '5 Elm St', 'Dayton', 'OH', '45402', 1), (@c2, 'shipping', '5 Elm St', 'Dayton', 'OH', '45402', 1)) v (C, T, L, Ci, R, Z, D)
WHERE v.C IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.CustomerAddress a WHERE a.CustomerId = v.C AND a.AddressType = v.T AND a.Line1 = v.L);
INSERT INTO dbo.CustomerProfile (CustomerId, PreferredLanguage, PreferredCurrency, LoyaltyPoints, MarketingOptIn)
SELECT v.C, v.L, v.Cu, v.P, v.M FROM (VALUES (@c1, 'en', 'USD', 120, 1), (@c2, 'es', 'USD', 0, 0), (@c3, 'fr', 'CAD', 45, 1)) v (C, L, Cu, P, M)
WHERE v.C IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.CustomerProfile p WHERE p.CustomerId = v.C);

-- 23, 24, 25, 26. Locales, a document with versions per edit and language, channels and the links with a schedule
INSERT INTO dbo.Locale (LocaleCode, Name, FallbackLocaleCode, IsDefault) SELECT v.C, v.N, v.F, v.D FROM (VALUES ('en', 'English', CAST(NULL AS VARCHAR(10)), 1)) v (C, N, F, D) WHERE NOT EXISTS (SELECT 1 FROM dbo.Locale l WHERE l.LocaleCode = v.C);
INSERT INTO dbo.Locale (LocaleCode, Name, FallbackLocaleCode, IsDefault) SELECT v.C, v.N, 'en', 0 FROM (VALUES ('es', 'Spanish'), ('fr', 'French')) v (C, N) WHERE NOT EXISTS (SELECT 1 FROM dbo.Locale l WHERE l.LocaleCode = v.C);
INSERT INTO dbo.Channel (Name, Kind) SELECT v.N, v.K FROM (VALUES ('Web storefront', 'storefront'), ('Mobile app', 'app'), ('Weekly newsletter', 'newsletter')) v (N, K) WHERE NOT EXISTS (SELECT 1 FROM dbo.Channel c WHERE c.Name = v.N);

IF NOT EXISTS (SELECT 1 FROM dbo.Document)
BEGIN
    INSERT INTO dbo.Document (Title, CreatedByEmployeeId) VALUES ('Returns policy', @jdoe);
    DECLARE @d1 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Document (Title, CreatedByEmployeeId) VALUES ('Shipping guide', @jdoe);
    DECLARE @d2 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.DocumentVersion (DocumentId, VersionNo, LocaleCode, State, Title, Body, CreatedByEmployeeId, PublishFrom, ExpiresOn) VALUES
        (@d1, 1, 'en', 'retired', 'Returns policy', 'Return within 14 days.', @jdoe, '2026-01-01', '2026-06-30'),
        (@d1, 2, 'en', 'published', 'Returns policy', 'Return within 30 days.', @jdoe, '2026-07-01', NULL),
        (@d1, 2, 'es', 'published', 'Politica de devoluciones', 'Devolucion en 30 dias.', @jdoe, '2026-07-01', NULL),
        (@d1, 3, 'en', 'draft', 'Returns policy', 'Return within 45 days (draft).', @jdoe, NULL, NULL),
        (@d2, 1, 'en', 'published', 'Shipping guide', 'Ships in two days.', @jdoe, '2026-02-01', NULL),
        (@d2, 1, 'fr', 'draft', 'Guide de livraison', 'Expedie en deux jours.', @jdoe, NULL, NULL);
    UPDATE d SET CurrentDocumentVersionId = (SELECT TOP 1 v.DocumentVersionId FROM dbo.DocumentVersion v WHERE v.DocumentId = d.DocumentId AND v.State = 'published' AND v.LocaleCode = 'en' ORDER BY v.VersionNo DESC) FROM dbo.Document d;
    INSERT INTO dbo.ChannelLink (DocumentVersionId, ChannelId, State, PublishFrom, ExpiresOn)
    SELECT v.DocumentVersionId, c.ChannelId, x.S, x.F, x.E
    FROM (VALUES (1, 'Web storefront', 'published', CAST('2026-07-01' AS DATETIME), CAST(NULL AS DATETIME)), (1, 'Weekly newsletter', 'scheduled', CAST('2027-01-01' AS DATETIME), CAST('2027-02-01' AS DATETIME)),
                 (2, 'Mobile app', 'published', CAST('2026-02-01' AS DATETIME), NULL)) x (D, N, S, F, E)
    JOIN dbo.Channel c ON c.Name = x.N
    JOIN (SELECT DocumentVersionId, DocumentId FROM dbo.DocumentVersion WHERE State = 'published' AND LocaleCode = 'en') v ON v.DocumentId = (CASE x.D WHEN 1 THEN @d1 ELSE @d2 END);
END;

-- 27. Workspaces inside a tenant, with a role per person
IF NOT EXISTS (SELECT 1 FROM dbo.Workspace)
BEGIN
    INSERT INTO dbo.Workspace (TenantId, Name) VALUES (@t1, 'Marketing');
    DECLARE @w1 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Workspace (TenantId, ParentWorkspaceId, Name) VALUES (@t1, @w1, 'Marketing EMEA');
    DECLARE @w2 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Workspace (TenantId, Name) VALUES (@t2, 'Support');
    DECLARE @w3 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.WorkspaceMember (WorkspaceId, EmployeeId, Role) VALUES (@w1, @jdoe, 'publisher'), (@w1, @akim, 'editor'), (@w2, @akim, 'translator'), (@w3, @jdoe, 'creator');
END;

-- 30. A content type defined by data, and items whose values are JSON
INSERT INTO dbo.ContentType (Name, Description) SELECT v.N, v.D FROM (VALUES ('News article', 'A title, an author, a banner and a body'), ('Event', 'A title, a start date and a place')) v (N, D) WHERE NOT EXISTS (SELECT 1 FROM dbo.ContentType t WHERE t.Name = v.N);
INSERT INTO dbo.ContentField (ContentTypeId, FieldName, DataType, IsRequired, SortOrder)
SELECT t.ContentTypeId, v.F, v.D, v.R, v.S FROM (VALUES ('News article', 'title', 'text', 1, 1), ('News article', 'author', 'text', 1, 2), ('News article', 'banner', 'asset', 0, 3), ('News article', 'body', 'text', 1, 4),
    ('Event', 'title', 'text', 1, 1), ('Event', 'startsOn', 'date', 1, 2), ('Event', 'place', 'text', 0, 3)) v (T, F, D, R, S)
JOIN dbo.ContentType t ON t.Name = v.T WHERE NOT EXISTS (SELECT 1 FROM dbo.ContentField x WHERE x.ContentTypeId = t.ContentTypeId AND x.FieldName = v.F);
INSERT INTO dbo.ContentItem (ContentTypeId, Title, DataJson)
SELECT t.ContentTypeId, v.Ti, v.J FROM (VALUES
    ('News article', 'Shop opens', '{"title":"Shop opens","author":"jdoe","body":"We are open."}'),
    ('News article', 'New hammers', '{"title":"New hammers","author":"akim","banner":"img/hammer.jpg","body":"Two new sizes."}'),
    ('Event', 'Spring sale', '{"title":"Spring sale","startsOn":"2027-03-20","place":"Main store"}')) v (T, Ti, J)
JOIN dbo.ContentType t ON t.Name = v.T WHERE NOT EXISTS (SELECT 1 FROM dbo.ContentItem i WHERE i.ContentTypeId = t.ContentTypeId AND i.Title = v.Ti);

PRINT 'table shape seed rows are in';
