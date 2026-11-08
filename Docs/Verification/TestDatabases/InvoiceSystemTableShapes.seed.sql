-- Rows for the tables of InvoiceSystemTableShapes.sql (spec item 143, entries 1 to 49). Re-runnable: a row that is there already (by its natural key) is not added again.
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

GO

-- Rows for the trading, logistics and content-integrity tables. Orders, invoices, items, customers and assets are the first rows of those tables.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;

DECLARE @c1 INT = (SELECT MIN(CustomerId) FROM dbo.Customer);
DECLARE @c2 INT = (SELECT MIN(CustomerId) FROM dbo.Customer WHERE CustomerId > @c1);
DECLARE @i1 INT = (SELECT MIN(ItemId) FROM dbo.Item);
DECLARE @i2 INT = (SELECT MIN(ItemId) FROM dbo.Item WHERE ItemId > @i1);
DECLARE @i3 INT = (SELECT MIN(ItemId) FROM dbo.Item WHERE ItemId > @i2);
DECLARE @i4 INT = (SELECT MIN(ItemId) FROM dbo.Item WHERE ItemId > @i3);
DECLARE @o1 INT = (SELECT MIN(SalesOrderId) FROM dbo.SalesOrder);
DECLARE @o2 INT = (SELECT MIN(SalesOrderId) FROM dbo.SalesOrder WHERE SalesOrderId > @o1);
DECLARE @o3 INT = (SELECT MIN(SalesOrderId) FROM dbo.SalesOrder WHERE SalesOrderId > @o2);
DECLARE @ol1 INT = (SELECT MIN(SalesOrderLineId) FROM dbo.SalesOrderLine WHERE SalesOrderId = @o1);
DECLARE @ol2 INT = (SELECT MIN(SalesOrderLineId) FROM dbo.SalesOrderLine WHERE SalesOrderId = @o1 AND SalesOrderLineId > @ol1);
DECLARE @v1 INT = (SELECT MIN(SalesInvoiceId) FROM dbo.SalesInvoice);
DECLARE @v2 INT = (SELECT MIN(SalesInvoiceId) FROM dbo.SalesInvoice WHERE SalesInvoiceId > @v1);
DECLARE @vl1 INT = (SELECT MIN(LineId) FROM dbo.SalesInvoiceLine WHERE SalesInvoiceId = @v1);
DECLARE @s1 INT = (SELECT MIN(ShipmentId) FROM dbo.Shipment);
DECLARE @si1 INT = (SELECT MIN(ShipmentItemId) FROM dbo.ShipmentItem);
DECLARE @w1 INT = (SELECT MIN(WarehouseId) FROM dbo.Warehouse);
DECLARE @w2 INT = (SELECT MIN(WarehouseId) FROM dbo.Warehouse WHERE WarehouseId > @w1);
DECLARE @a1 INT = (SELECT MIN(AssetId) FROM dbo.Asset);
DECLARE @a2 INT = (SELECT MIN(AssetId) FROM dbo.Asset WHERE AssetId > @a1);

-- Currencies, exchange rates and tax categories
INSERT INTO dbo.Currency (Code, Name, Symbol, DecimalPlaces)
SELECT v.C, v.N, v.S, v.D FROM (VALUES ('USD', 'US dollar', '$', 2), ('EUR', 'Euro', 'EUR', 2), ('GBP', 'Pound sterling', 'GBP', 2), ('JPY', 'Japanese yen', 'JPY', 0), ('CAD', 'Canadian dollar', 'C$', 2)) v (C, N, S, D)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Currency x WHERE x.Code = v.C);

INSERT INTO dbo.ExchangeRate (FromCurrencyId, ToCurrencyId, Rate, RateDate, Source)
SELECT f.CurrencyId, t.CurrencyId, v.R, v.D, 'seed' FROM (VALUES
    ('USD', 'EUR', 0.92000000, '2026-11-02'), ('USD', 'EUR', 0.92500000, '2026-11-03'), ('EUR', 'USD', 1.08695652, '2026-11-02'),
    ('USD', 'GBP', 0.79000000, '2026-11-02'), ('USD', 'JPY', 151.25000000, '2026-11-02'), ('USD', 'CAD', 1.37000000, '2026-11-02')) v (F, T, R, D)
JOIN dbo.Currency f ON f.Code = v.F JOIN dbo.Currency t ON t.Code = v.T
WHERE NOT EXISTS (SELECT 1 FROM dbo.ExchangeRate x WHERE x.FromCurrencyId = f.CurrencyId AND x.ToCurrencyId = t.CurrencyId AND x.RateDate = v.D);

INSERT INTO dbo.TaxCategory (Code, Name, Rate, ExemptionReason, ValidFrom, ValidTo)
SELECT v.C, v.N, v.R, v.E, v.F, v.T FROM (VALUES
    ('STD', 'Standard rate', 0.0800, NULL, '2024-01-01', '2026-12-31'), ('STD', 'Standard rate', 0.0850, NULL, '2027-01-01', NULL),
    ('RED', 'Reduced rate', 0.0400, NULL, '2024-01-01', NULL), ('ZERO', 'Zero rated', 0.0000, NULL, '2024-01-01', NULL),
    ('EXEMPT', 'Exempt', 0.0000, 'Exempt supply under the local tax act', '2024-01-01', NULL)) v (C, N, R, E, F, T)
WHERE NOT EXISTS (SELECT 1 FROM dbo.TaxCategory x WHERE x.Code = v.C AND x.ValidFrom = v.F);

-- Allowances and charges, then the tax subtotals and the totals of each order worked out from them
INSERT INTO dbo.AllowanceCharge (SalesOrderId, SalesOrderLineId, IsCharge, ReasonCode, Reason, BaseAmount, MultiplierPercent, Amount)
SELECT v.O, v.L, v.X, v.RC, v.R, v.B, v.P, v.A FROM (VALUES
    (@o1, NULL, 0, 'VOLUME', 'Volume discount', 100.00, 5.0000, 5.00),
    (@o1, NULL, 1, 'HANDLING', 'Handling charge', NULL, NULL, 7.50),
    (@o2, NULL, 1, 'FREIGHT', 'Freight', NULL, NULL, 12.00),
    (@o2, NULL, 0, 'PROMPT', 'Prompt payment allowance', 50.00, 2.0000, 1.00)) v (O, L, X, RC, R, B, P, A)
WHERE v.O IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.AllowanceCharge x WHERE x.SalesOrderId = v.O AND x.Reason = v.R);

INSERT INTO dbo.SalesOrderTotal (SalesOrderId, LineExtensionAmount, AllowanceTotalAmount, ChargeTotalAmount, TaxExclusiveAmount, TaxAmount, TaxInclusiveAmount, PrepaidAmount, RoundingAmount, PayableAmount)
SELECT o.SalesOrderId, s.Ext, s.Al, s.Ch, s.Ext - s.Al + s.Ch, s.Tax, s.Ext - s.Al + s.Ch + s.Tax, 0, 0, s.Ext - s.Al + s.Ch + s.Tax
FROM dbo.SalesOrder o
CROSS APPLY (SELECT Ext = CONVERT(DECIMAL(12,2), ISNULL((SELECT SUM(l.UnitPrice * l.Quantity) FROM dbo.SalesOrderLine l WHERE l.SalesOrderId = o.SalesOrderId), 0)),
                    Al = CONVERT(DECIMAL(12,2), ISNULL((SELECT SUM(a.Amount) FROM dbo.AllowanceCharge a WHERE a.SalesOrderId = o.SalesOrderId AND a.IsCharge = 0), 0)),
                    Ch = CONVERT(DECIMAL(12,2), ISNULL((SELECT SUM(a.Amount) FROM dbo.AllowanceCharge a WHERE a.SalesOrderId = o.SalesOrderId AND a.IsCharge = 1), 0)),
                    Tax = CONVERT(DECIMAL(12,2), 0)) s
WHERE o.SalesOrderId IN (@o1, @o2, @o3) AND NOT EXISTS (SELECT 1 FROM dbo.SalesOrderTotal x WHERE x.SalesOrderId = o.SalesOrderId);

UPDATE t SET TaxAmount = CONVERT(DECIMAL(12,2), ROUND(t.TaxExclusiveAmount * 0.0800, 2)), TaxInclusiveAmount = t.TaxExclusiveAmount + CONVERT(DECIMAL(12,2), ROUND(t.TaxExclusiveAmount * 0.0800, 2)),
    PayableAmount = t.TaxExclusiveAmount + CONVERT(DECIMAL(12,2), ROUND(t.TaxExclusiveAmount * 0.0800, 2)) - t.PrepaidAmount + t.RoundingAmount
FROM dbo.SalesOrderTotal t WHERE t.TaxAmount = 0 AND t.SalesOrderId IN (@o1, @o2, @o3);

INSERT INTO dbo.SalesOrderTaxSubtotal (SalesOrderId, TaxCategoryId, TaxableAmount, TaxAmount)
SELECT t.SalesOrderId, c.TaxCategoryId, t.TaxExclusiveAmount, t.TaxAmount
FROM dbo.SalesOrderTotal t JOIN dbo.TaxCategory c ON c.Code = 'STD' AND c.ValidFrom = '2024-01-01'
WHERE t.SalesOrderId IN (@o1, @o2, @o3) AND NOT EXISTS (SELECT 1 FROM dbo.SalesOrderTaxSubtotal x WHERE x.SalesOrderId = t.SalesOrderId AND x.TaxCategoryId = c.TaxCategoryId);

-- Price lists with quantity breaks
INSERT INTO dbo.PriceList (Name, CurrencyId, CustomerGroup, ValidFrom, ValidTo)
SELECT v.N, c.CurrencyId, v.G, v.F, v.T FROM (VALUES
    ('Retail 2027', 'USD', NULL, '2027-01-01', '2027-12-31'), ('Wholesale 2027', 'USD', 'wholesale', '2027-01-01', '2027-12-31'), ('Europe 2027', 'EUR', 'europe', '2027-01-01', NULL)) v (N, C, G, F, T)
JOIN dbo.Currency c ON c.Code = v.C WHERE NOT EXISTS (SELECT 1 FROM dbo.PriceList x WHERE x.Name = v.N);

INSERT INTO dbo.PriceListLine (PriceListId, ItemId, MinQuantity, UnitPrice)
SELECT p.PriceListId, v.I, v.Q, v.P FROM (VALUES
    ('Retail 2027', @i1, 1, 12.00), ('Retail 2027', @i1, 10, 11.00), ('Retail 2027', @i2, 1, 25.00),
    ('Wholesale 2027', @i1, 1, 9.50), ('Wholesale 2027', @i1, 100, 8.25), ('Wholesale 2027', @i2, 1, 20.00), ('Wholesale 2027', @i3, 1, 7.75),
    ('Europe 2027', @i1, 1, 11.20), ('Europe 2027', @i4, 5, 31.40)) v (L, I, Q, P)
JOIN dbo.PriceList p ON p.Name = v.L
WHERE v.I IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.PriceListLine x WHERE x.PriceListId = p.PriceListId AND x.ItemId = v.I AND x.MinQuantity = v.Q);

-- Parties and what hangs off them
INSERT INTO dbo.Party (LegalName, TradingName, CustomerId, CountryCode)
SELECT v.L, v.T, v.C, v.K FROM (VALUES
    ('Northwind Traders Inc.', 'Northwind', @c1, 'US'), ('Acme Hardware Supply LLC', 'Acme Hardware', NULL, 'US'), ('Blue Freight Logistics GmbH', 'Blue Freight', NULL, 'DE'),
    ('Contoso Wholesale Ltd', 'Contoso', @c2, 'GB'), ('Fabrikam Packaging SA', NULL, NULL, 'FR'), ('Global Payer Ltd', NULL, NULL, 'GB')) v (L, T, C, K)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Party x WHERE x.LegalName = v.L);

INSERT INTO dbo.PartyRole (PartyId, RoleCode)
SELECT p.PartyId, v.R FROM (VALUES
    ('Northwind Traders Inc.', 'customer'), ('Acme Hardware Supply LLC', 'supplier'), ('Blue Freight Logistics GmbH', 'carrier'), ('Contoso Wholesale Ltd', 'customer'),
    ('Contoso Wholesale Ltd', 'supplier'), ('Fabrikam Packaging SA', 'supplier'), ('Global Payer Ltd', 'payer')) v (L, R)
JOIN dbo.Party p ON p.LegalName = v.L WHERE NOT EXISTS (SELECT 1 FROM dbo.PartyRole x WHERE x.PartyId = p.PartyId AND x.RoleCode = v.R);

INSERT INTO dbo.PartyIdentification (PartyId, SchemeCode, IdentifierValue)
SELECT p.PartyId, v.S, v.I FROM (VALUES
    ('Northwind Traders Inc.', 'DUNS', '081466849'), ('Northwind Traders Inc.', 'EIN', '12-3456789'), ('Acme Hardware Supply LLC', 'DUNS', '079512346'),
    ('Blue Freight Logistics GmbH', 'GLN', '4012345000009'), ('Contoso Wholesale Ltd', 'CRN', '07654321'), ('Fabrikam Packaging SA', 'SIREN', '552100554')) v (L, S, I)
JOIN dbo.Party p ON p.LegalName = v.L WHERE NOT EXISTS (SELECT 1 FROM dbo.PartyIdentification x WHERE x.SchemeCode = v.S AND x.IdentifierValue = v.I);

INSERT INTO dbo.PartyName (PartyId, NameType, Name)
SELECT p.PartyId, v.T, v.N FROM (VALUES
    ('Northwind Traders Inc.', 'legal', 'Northwind Traders Inc.'), ('Northwind Traders Inc.', 'trading', 'Northwind'), ('Northwind Traders Inc.', 'alias', 'NW Traders'),
    ('Acme Hardware Supply LLC', 'legal', 'Acme Hardware Supply LLC'), ('Acme Hardware Supply LLC', 'trading', 'Acme Hardware'),
    ('Contoso Wholesale Ltd', 'legal', 'Contoso Wholesale Ltd')) v (L, T, N)
JOIN dbo.Party p ON p.LegalName = v.L WHERE NOT EXISTS (SELECT 1 FROM dbo.PartyName x WHERE x.PartyId = p.PartyId AND x.NameType = v.T AND x.Name = v.N);

INSERT INTO dbo.PartyTaxRegistration (PartyId, CountryCode, TaxSchemeCode, TaxNumber, ValidTo)
SELECT p.PartyId, v.K, v.S, v.N, v.V FROM (VALUES
    ('Blue Freight Logistics GmbH', 'DE', 'VAT', 'DE123456789', NULL), ('Contoso Wholesale Ltd', 'GB', 'VAT', 'GB987654321', NULL),
    ('Fabrikam Packaging SA', 'FR', 'VAT', 'FR40552100554', NULL), ('Northwind Traders Inc.', 'US', 'EIN', '12-3456789', '2030-12-31')) v (L, K, S, N, V)
JOIN dbo.Party p ON p.LegalName = v.L WHERE NOT EXISTS (SELECT 1 FROM dbo.PartyTaxRegistration x WHERE x.CountryCode = v.K AND x.TaxSchemeCode = v.S AND x.TaxNumber = v.N);

INSERT INTO dbo.PartyContact (PartyId, ContactName, JobTitle)
SELECT p.PartyId, v.N, v.J FROM (VALUES
    ('Northwind Traders Inc.', 'Dana Whitfield', 'Purchasing manager'), ('Acme Hardware Supply LLC', 'Sam Okafor', 'Account executive'),
    ('Blue Freight Logistics GmbH', 'Lena Vogel', 'Dispatcher'), ('Contoso Wholesale Ltd', 'Priya Nair', 'Sales director')) v (L, N, J)
JOIN dbo.Party p ON p.LegalName = v.L WHERE NOT EXISTS (SELECT 1 FROM dbo.PartyContact x WHERE x.PartyId = p.PartyId AND x.ContactName = v.N);

INSERT INTO dbo.PartyCommunication (PartyId, PartyContactId, Channel, ChannelValue, IsPreferred)
SELECT p.PartyId, c.PartyContactId, v.H, v.V, v.P FROM (VALUES
    ('Northwind Traders Inc.', 'Dana Whitfield', 'email', 'dana.whitfield@example.com', 1), ('Northwind Traders Inc.', 'Dana Whitfield', 'phone', '+1 555 0100', 0),
    ('Northwind Traders Inc.', NULL, 'web', 'https://northwind.example.com', 1), ('Acme Hardware Supply LLC', 'Sam Okafor', 'email', 'sam.okafor@example.com', 1),
    ('Blue Freight Logistics GmbH', 'Lena Vogel', 'phone', '+49 30 555 0101', 1), ('Blue Freight Logistics GmbH', 'Lena Vogel', 'email', 'dispatch@example.com', 1),
    ('Contoso Wholesale Ltd', 'Priya Nair', 'mobile', '+44 20 5550 0102', 1)) v (L, CN, H, V, P)
JOIN dbo.Party p ON p.LegalName = v.L LEFT JOIN dbo.PartyContact c ON c.PartyId = p.PartyId AND c.ContactName = v.CN
WHERE NOT EXISTS (SELECT 1 FROM dbo.PartyCommunication x WHERE x.PartyId = p.PartyId AND x.Channel = v.H AND x.ChannelValue = v.V);

-- Payment terms, bank accounts, means of payment and instalments
INSERT INTO dbo.PaymentTerms (Code, Name, DaysToPay, EarlyDiscountPercent, EarlyDiscountDays, LatePenaltyPercent)
SELECT v.C, v.N, v.D, v.P, v.E, v.L FROM (VALUES
    ('NET30', 'Net 30 days', 30, NULL, NULL, 1.50), ('2/10NET30', '2% in 10 days, net 30', 30, 2.00, 10, 1.50), ('NET60', 'Net 60 days', 60, NULL, NULL, 1.00), ('COD', 'Cash on delivery', 0, NULL, NULL, NULL)) v (C, N, D, P, E, L)
WHERE NOT EXISTS (SELECT 1 FROM dbo.PaymentTerms x WHERE x.Code = v.C);

INSERT INTO dbo.PartyBankAccount (PartyId, AccountHolder, Iban, AccountNumber, RoutingNumber, BankName, Bic, IsDefault)
SELECT p.PartyId, v.H, v.I, v.A, v.R, v.B, v.C, v.D FROM (VALUES
    ('Acme Hardware Supply LLC', 'Acme Hardware Supply LLC', NULL, '000123456789', '021000021', 'Example Bank NA', NULL, 1),
    ('Blue Freight Logistics GmbH', 'Blue Freight Logistics GmbH', 'DE89370400440532013000', NULL, NULL, 'Example Bank AG', 'COBADEFFXXX', 1),
    ('Contoso Wholesale Ltd', 'Contoso Wholesale Ltd', 'GB29NWBK60161331926819', NULL, NULL, 'Example Bank PLC', 'NWBKGB2L', 1)) v (L, H, I, A, R, B, C, D)
JOIN dbo.Party p ON p.LegalName = v.L WHERE NOT EXISTS (SELECT 1 FROM dbo.PartyBankAccount x WHERE x.PartyId = p.PartyId AND x.AccountHolder = v.H);

INSERT INTO dbo.PaymentMeans (SalesOrderId, PaymentTermsId, MeansCode, DueDate, PayeeBankAccountId, PaymentReference)
SELECT v.O, t.PaymentTermsId, v.M, v.D, a.PartyBankAccountId, v.R FROM (VALUES
    (@o1, 'NET30', 'transfer', '2026-12-15', 'Acme Hardware Supply LLC', 'PM-0001'), (@o2, '2/10NET30', 'transfer', '2026-12-20', 'Acme Hardware Supply LLC', 'PM-0002'), (@o3, 'COD', 'cash', NULL, NULL, 'PM-0003')) v (O, T, M, D, P, R)
JOIN dbo.PaymentTerms t ON t.Code = v.T LEFT JOIN dbo.Party p ON p.LegalName = v.P LEFT JOIN dbo.PartyBankAccount a ON a.PartyId = p.PartyId
WHERE v.O IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.PaymentMeans x WHERE x.PaymentReference = v.R);

INSERT INTO dbo.PaymentInstalment (SalesOrderId, SequenceNo, DueDate, Amount)
SELECT v.O, v.S, v.D, v.A FROM (VALUES (@o1, 1, '2026-12-01', 50.00), (@o1, 2, '2027-01-01', 50.00), (@o1, 3, '2027-02-01', 50.00)) v (O, S, D, A)
WHERE v.O IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.PaymentInstalment x WHERE x.SalesOrderId = v.O AND x.SequenceNo = v.S);

-- Receipts, credit and debit notes, references and responses
INSERT INTO dbo.GoodsReceipt (SalesOrderId, ReceivedOn, ReceivedBy, Note)
SELECT v.O, v.D, v.B, v.N FROM (VALUES (@o1, '2026-11-04', 'Receiving dock', 'Partly received'), (@o2, '2026-11-05', 'Receiving dock', NULL)) v (O, D, B, N)
WHERE v.O IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.GoodsReceipt x WHERE x.SalesOrderId = v.O AND x.ReceivedOn = v.D);

INSERT INTO dbo.GoodsReceiptLine (GoodsReceiptId, SalesOrderLineId, QuantityReceived, QuantityRejected, RejectReason)
SELECT r.GoodsReceiptId, v.L, v.Q, v.J, v.R FROM (VALUES (@o1, '2026-11-04', @ol1, 1, 0, NULL), (@o1, '2026-11-04', @ol2, 1, 1, 'Damaged in transit')) v (O, D, L, Q, J, R)
JOIN dbo.GoodsReceipt r ON r.SalesOrderId = v.O AND r.ReceivedOn = v.D
WHERE v.L IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.GoodsReceiptLine x WHERE x.GoodsReceiptId = r.GoodsReceiptId AND x.SalesOrderLineId = v.L);

INSERT INTO dbo.CreditNote (SalesInvoiceId, NoteType, IsSelfBilled, Status, IssuedOn, CurrencyCode, Reason)
SELECT v.V, v.T, v.B, v.S, v.D, 'USD', v.R FROM (VALUES (@v1, 'credit', 0, 'issued', '2026-11-05', 'Returned goods'), (@v1, 'debit', 0, 'draft', NULL, 'Freight undercharged'), (@v2, 'credit', 1, 'issued', '2026-11-06', 'Self-billed adjustment')) v (V, T, B, S, D, R)
WHERE v.V IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.CreditNote x WHERE x.SalesInvoiceId = v.V AND x.Reason = v.R);

INSERT INTO dbo.CreditNoteLine (CreditNoteId, SalesInvoiceLineId, Description, Quantity, UnitPrice)
SELECT n.CreditNoteId, v.L, v.D, v.Q, v.P FROM (VALUES ('Returned goods', @vl1, 'Item returned', 1, 12.50), ('Returned goods', NULL, 'Restocking fee refunded', 1, 2.00), ('Freight undercharged', NULL, 'Freight difference', 1, 8.00)) v (R, L, D, Q, P)
JOIN dbo.CreditNote n ON n.Reason = v.R WHERE NOT EXISTS (SELECT 1 FROM dbo.CreditNoteLine x WHERE x.CreditNoteId = n.CreditNoteId AND x.Description = v.D);

INSERT INTO dbo.DocumentReference (FromDocumentType, FromDocumentId, ToDocumentType, ToDocumentId, ReferenceKind, ReferencedOn)
SELECT v.FT, v.F, v.TT, v.T, v.K, v.D FROM (VALUES
    ('CreditNote', (SELECT MIN(CreditNoteId) FROM dbo.CreditNote), 'SalesInvoice', @v1, 'billing', '2026-11-05'),
    ('GoodsReceipt', (SELECT MIN(GoodsReceiptId) FROM dbo.GoodsReceipt), 'SalesOrder', @o1, 'order', '2026-11-04'),
    ('SalesInvoice', @v1, 'SalesOrder', @o1, 'order', '2026-10-20')) v (FT, F, TT, T, K, D)
WHERE v.F IS NOT NULL AND v.T IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.DocumentReference x WHERE x.FromDocumentType = v.FT AND x.FromDocumentId = v.F AND x.ToDocumentType = v.TT AND x.ToDocumentId = v.T AND x.ReferenceKind = v.K);

INSERT INTO dbo.DocumentResponse (SalesOrderId, ResponseCode, Reason, RespondedOn, RespondedByPartyId)
SELECT v.O, v.C, v.R, '2026-11-03T09:00:00', p.PartyId FROM (VALUES (@o1, 'changed', 'One line short shipped', 'Acme Hardware Supply LLC'), (@o2, 'accepted', NULL, 'Acme Hardware Supply LLC')) v (O, C, R, P)
JOIN dbo.Party p ON p.LegalName = v.P WHERE v.O IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.DocumentResponse x WHERE x.SalesOrderId = v.O AND x.ResponseCode = v.C);

INSERT INTO dbo.LineResponse (DocumentResponseId, SalesOrderLineId, ResponseCode, AcceptedQuantity, Reason)
SELECT r.DocumentResponseId, v.L, v.C, v.Q, v.R FROM (VALUES (@o1, @ol1, 'accepted', 1, NULL), (@o1, @ol2, 'changed', 0, 'Back-ordered')) v (O, L, C, Q, R)
JOIN dbo.DocumentResponse r ON r.SalesOrderId = v.O AND r.ResponseCode = 'changed'
WHERE v.L IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.LineResponse x WHERE x.DocumentResponseId = r.DocumentResponseId AND x.SalesOrderLineId = v.L);

-- Requests for quotation and quotations
INSERT INTO dbo.RequestForQuotation (Title, Status, IssuedOn, RespondBy)
SELECT v.T, v.S, v.I, v.R FROM (VALUES ('Spring hardware restock', 'sent', '2026-11-01', '2026-11-15'), ('Packaging materials', 'draft', NULL, NULL)) v (T, S, I, R)
WHERE NOT EXISTS (SELECT 1 FROM dbo.RequestForQuotation x WHERE x.Title = v.T);

INSERT INTO dbo.RequestForQuotationLine (RequestForQuotationId, ItemId, Quantity)
SELECT q.RequestForQuotationId, v.I, v.N FROM (VALUES ('Spring hardware restock', @i1, 200), ('Spring hardware restock', @i2, 50), ('Packaging materials', @i3, 1000)) v (T, I, N)
JOIN dbo.RequestForQuotation q ON q.Title = v.T WHERE v.I IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.RequestForQuotationLine x WHERE x.RequestForQuotationId = q.RequestForQuotationId AND x.ItemId = v.I);

INSERT INTO dbo.Quotation (RequestForQuotationId, SupplierPartyId, RevisionNo, Status, CurrencyCode, ValidUntil, AcceptedOn)
SELECT q.RequestForQuotationId, p.PartyId, v.R, v.S, 'USD', v.V, v.A FROM (VALUES
    ('Spring hardware restock', 'Acme Hardware Supply LLC', 1, 'superseded', '2026-11-10', NULL), ('Spring hardware restock', 'Acme Hardware Supply LLC', 2, 'accepted', '2026-11-20', '2026-11-06'),
    ('Spring hardware restock', 'Contoso Wholesale Ltd', 1, 'rejected', '2026-11-20', NULL)) v (T, P, R, S, V, A)
JOIN dbo.RequestForQuotation q ON q.Title = v.T JOIN dbo.Party p ON p.LegalName = v.P
WHERE NOT EXISTS (SELECT 1 FROM dbo.Quotation x WHERE x.RequestForQuotationId = q.RequestForQuotationId AND x.SupplierPartyId = p.PartyId AND x.RevisionNo = v.R);

INSERT INTO dbo.QuotationLine (QuotationId, RequestForQuotationLineId, UnitPrice, LeadTimeDays)
SELECT qt.QuotationId, l.RequestForQuotationLineId, v.P, v.D FROM (VALUES
    ('Acme Hardware Supply LLC', 1, @i1, 9.80, 14), ('Acme Hardware Supply LLC', 1, @i2, 21.00, 14), ('Acme Hardware Supply LLC', 2, @i1, 9.40, 10), ('Acme Hardware Supply LLC', 2, @i2, 20.50, 10),
    ('Contoso Wholesale Ltd', 1, @i1, 10.10, 21), ('Contoso Wholesale Ltd', 1, @i2, 22.00, 21)) v (S, R, I, P, D)
JOIN dbo.RequestForQuotation q ON q.Title = 'Spring hardware restock' JOIN dbo.Party p ON p.LegalName = v.S
JOIN dbo.Quotation qt ON qt.RequestForQuotationId = q.RequestForQuotationId AND qt.SupplierPartyId = p.PartyId AND qt.RevisionNo = v.R
JOIN dbo.RequestForQuotationLine l ON l.RequestForQuotationId = q.RequestForQuotationId AND l.ItemId = v.I
WHERE NOT EXISTS (SELECT 1 FROM dbo.QuotationLine x WHERE x.QuotationId = qt.QuotationId AND x.RequestForQuotationLineId = l.RequestForQuotationLineId);

-- Item identification, classification, lots and serial numbers
INSERT INTO dbo.ItemIdentifier (ItemId, SchemeCode, IdentifierValue, PartyId)
SELECT v.I, v.S, v.V, p.PartyId FROM (VALUES
    (@i1, 'gtin', '00012345678905', NULL), (@i1, 'supplier', 'ACME-H-100', 'Acme Hardware Supply LLC'), (@i2, 'gtin', '00012345678912', NULL),
    (@i2, 'manufacturer', 'MFG-2-200', NULL), (@i3, 'sku', 'SKU-0003', NULL), (@i4, 'buyer', 'NW-4-400', 'Northwind Traders Inc.')) v (I, S, V, P)
LEFT JOIN dbo.Party p ON p.LegalName = v.P
WHERE v.I IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.ItemIdentifier x WHERE x.SchemeCode = v.S AND x.IdentifierValue = v.V);

INSERT INTO dbo.ItemRelation (ItemId, RelatedItemId, RelationType)
SELECT v.I, v.R, v.T FROM (VALUES (@i1, @i2, 'substitute'), (@i1, @i3, 'accessory'), (@i4, @i2, 'replaced-by')) v (I, R, T)
WHERE v.I IS NOT NULL AND v.R IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.ItemRelation x WHERE x.ItemId = v.I AND x.RelatedItemId = v.R AND x.RelationType = v.T);

INSERT INTO dbo.ClassificationScheme (Code, Name, Version)
SELECT v.C, v.N, v.V FROM (VALUES ('UNSPSC', 'United Nations Standard Products and Services Code', 'v24'), ('HS', 'Harmonized System tariff codes', '2022')) v (C, N, V)
WHERE NOT EXISTS (SELECT 1 FROM dbo.ClassificationScheme x WHERE x.Code = v.C);

INSERT INTO dbo.ClassificationCategory (ClassificationSchemeId, ParentCategoryId, Code, Name)
SELECT s.ClassificationSchemeId, NULL, v.C, v.N FROM (VALUES ('UNSPSC', '27000000', 'Tools and general machinery'), ('UNSPSC', '31000000', 'Manufacturing components and supplies'), ('HS', '82', 'Tools, implements, cutlery'))  v (S, C, N)
JOIN dbo.ClassificationScheme s ON s.Code = v.S WHERE NOT EXISTS (SELECT 1 FROM dbo.ClassificationCategory x WHERE x.ClassificationSchemeId = s.ClassificationSchemeId AND x.Code = v.C);

INSERT INTO dbo.ClassificationCategory (ClassificationSchemeId, ParentCategoryId, Code, Name)
SELECT s.ClassificationSchemeId, par.ClassificationCategoryId, v.C, v.N FROM (VALUES ('UNSPSC', '27110000', 'Hand tools', '27000000'), ('HS', '8205', 'Hand tools not elsewhere specified', '82')) v (S, C, N, P)
JOIN dbo.ClassificationScheme s ON s.Code = v.S JOIN dbo.ClassificationCategory par ON par.ClassificationSchemeId = s.ClassificationSchemeId AND par.Code = v.P
WHERE NOT EXISTS (SELECT 1 FROM dbo.ClassificationCategory x WHERE x.ClassificationSchemeId = s.ClassificationSchemeId AND x.Code = v.C);

INSERT INTO dbo.ClassificationCategory (ClassificationSchemeId, ParentCategoryId, Code, Name)
SELECT s.ClassificationSchemeId, par.ClassificationCategoryId, '27111500', 'Hammers and mallets' FROM dbo.ClassificationScheme s
JOIN dbo.ClassificationCategory par ON par.ClassificationSchemeId = s.ClassificationSchemeId AND par.Code = '27110000'
WHERE s.Code = 'UNSPSC' AND NOT EXISTS (SELECT 1 FROM dbo.ClassificationCategory x WHERE x.ClassificationSchemeId = s.ClassificationSchemeId AND x.Code = '27111500');

INSERT INTO dbo.ItemClassification (ItemId, ClassificationCategoryId)
SELECT v.I, c.ClassificationCategoryId FROM (VALUES (@i1, 'UNSPSC', '27111500'), (@i2, 'UNSPSC', '27110000'), (@i1, 'HS', '8205'), (@i3, 'UNSPSC', '31000000')) v (I, S, C)
JOIN dbo.ClassificationScheme s ON s.Code = v.S JOIN dbo.ClassificationCategory c ON c.ClassificationSchemeId = s.ClassificationSchemeId AND c.Code = v.C
WHERE v.I IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.ItemClassification x WHERE x.ItemId = v.I AND x.ClassificationCategoryId = c.ClassificationCategoryId);

INSERT INTO dbo.Lot (ItemId, LotNumber, ManufacturedOn, BestBefore)
SELECT v.I, v.L, v.M, v.B FROM (VALUES (@i1, 'L2026-001', '2026-06-01', '2028-06-01'), (@i1, 'L2026-002', '2026-09-01', '2028-09-01'), (@i3, 'L2026-A01', '2026-08-15', '2027-02-15')) v (I, L, M, B)
WHERE v.I IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Lot x WHERE x.ItemId = v.I AND x.LotNumber = v.L);

INSERT INTO dbo.SerialNumber (ItemId, LotId, Serial, Status, SalesInvoiceLineId)
SELECT v.I, l.LotId, v.S, v.T, v.V FROM (VALUES (@i1, 'L2026-001', 'SN-0001', 'sold', @vl1), (@i1, 'L2026-001', 'SN-0002', 'in-stock', NULL), (@i1, 'L2026-002', 'SN-0003', 'in-stock', NULL), (@i1, NULL, 'SN-0004', 'scrapped', NULL)) v (I, L, S, T, V)
LEFT JOIN dbo.Lot l ON l.ItemId = v.I AND l.LotNumber = v.L
WHERE v.I IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.SerialNumber x WHERE x.ItemId = v.I AND x.Serial = v.S);

-- Storage locations, stock per location and the location ledger
INSERT INTO dbo.StorageLocation (WarehouseId, ParentStorageLocationId, Code, Name, Latitude, Longitude)
SELECT v.W, NULL, v.C, v.N, v.A, v.O FROM (VALUES (@w1, 'MAIN', 'Main building', 40.712800, -74.006000), (@w2, 'ANNEX', 'Annex', 34.052200, -118.243700)) v (W, C, N, A, O)
WHERE v.W IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.StorageLocation x WHERE x.WarehouseId = v.W AND x.Code = v.C);

INSERT INTO dbo.StorageLocation (WarehouseId, ParentStorageLocationId, Code, Name)
SELECT p.WarehouseId, p.StorageLocationId, v.C, v.N FROM (VALUES ('MAIN', 'A-01', 'Aisle A bin 01'), ('MAIN', 'A-02', 'Aisle A bin 02'), ('ANNEX', 'B-01', 'Aisle B bin 01')) v (P, C, N)
JOIN dbo.StorageLocation p ON p.Code = v.P AND p.ParentStorageLocationId IS NULL
WHERE NOT EXISTS (SELECT 1 FROM dbo.StorageLocation x WHERE x.WarehouseId = p.WarehouseId AND x.Code = v.C);

INSERT INTO dbo.ItemLocationStock (ItemId, StorageLocationId, OnHand, Reserved, MinQuantity, MaxQuantity, ReorderQuantity, LeadTimeDays)
SELECT v.I, l.StorageLocationId, v.H, v.R, v.N, v.X, v.Q, v.D FROM (VALUES
    (@i1, 'A-01', 120, 20, 25, 400, 100, 7), (@i1, 'B-01', 40, 0, 10, 200, 50, 10), (@i2, 'A-02', 15, 5, 10, NULL, 25, 14), (@i3, 'A-01', 300, 50, 100, 1000, 500, 5)) v (I, L, H, R, N, X, Q, D)
JOIN dbo.StorageLocation l ON l.Code = v.L
WHERE v.I IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.ItemLocationStock x WHERE x.ItemId = v.I AND x.StorageLocationId = l.StorageLocationId);

INSERT INTO dbo.LocationStockLedger (ItemId, StorageLocationId, LotId, Kind, Quantity, TransferGroup, Reference, OccurredOn)
SELECT v.I, l.StorageLocationId, lot.LotId, v.K, v.Q, v.G, v.R, v.D FROM (VALUES
    (@i1, 'A-01', 'L2026-001', 'receipt', 200, NULL, 'PO-1001', '2026-10-01T08:00:00'),
    (@i1, 'A-01', 'L2026-001', 'sale', -80, NULL, 'SO-2001', '2026-10-15T10:30:00'),
    (@i1, 'A-01', 'L2026-001', 'transfer-out', -40, CONVERT(UNIQUEIDENTIFIER, '6F9619FF-8B86-D011-B42D-00C04FC964FF'), 'TR-0001', '2026-10-20T09:00:00'),
    (@i1, 'B-01', 'L2026-001', 'transfer-in', 40, CONVERT(UNIQUEIDENTIFIER, '6F9619FF-8B86-D011-B42D-00C04FC964FF'), 'TR-0001', '2026-10-20T09:00:00')) v (I, L, T, K, Q, G, R, D)
JOIN dbo.StorageLocation l ON l.Code = v.L LEFT JOIN dbo.Lot lot ON lot.ItemId = v.I AND lot.LotNumber = v.T
WHERE v.I IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.LocationStockLedger x WHERE x.Reference = v.R AND x.StorageLocationId = l.StorageLocationId AND x.Kind = v.K);

-- Packing units, transport legs, tracking events and endorsements
INSERT INTO dbo.HandlingUnit (ShipmentId, ParentHandlingUnitId, UnitType, Code, LengthCm, WidthCm, HeightCm, WeightKg, SealNumber, AllowsHazardous)
SELECT @s1, NULL, 'pallet', 'PAL-0001', 120.00, 80.00, 150.00, 220.500, 'SEAL-7781', 0
WHERE @s1 IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.HandlingUnit WHERE Code = 'PAL-0001');

INSERT INTO dbo.HandlingUnit (ShipmentId, ParentHandlingUnitId, UnitType, Code, LengthCm, WidthCm, HeightCm, WeightKg, SealNumber, AllowsHazardous)
SELECT @s1, p.HandlingUnitId, v.T, v.C, v.L, v.W, v.H, v.K, NULL, 0 FROM (VALUES ('carton', 'CTN-0001', 60.00, 40.00, 40.00, 18.250), ('carton', 'CTN-0002', 60.00, 40.00, 40.00, 17.900)) v (T, C, L, W, H, K)
JOIN dbo.HandlingUnit p ON p.Code = 'PAL-0001' WHERE @s1 IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.HandlingUnit x WHERE x.Code = v.C);

INSERT INTO dbo.HandlingUnitItem (HandlingUnitId, ShipmentItemId, Quantity, IsHazardous, StorageTempMinC, StorageTempMaxC)
SELECT u.HandlingUnitId, @si1, 1, 0, 5.0, 30.0 FROM dbo.HandlingUnit u
WHERE @si1 IS NOT NULL AND u.Code = 'CTN-0001' AND NOT EXISTS (SELECT 1 FROM dbo.HandlingUnitItem x WHERE x.HandlingUnitId = u.HandlingUnitId AND x.ShipmentItemId = @si1);

INSERT INTO dbo.TransportMode (Code, Name, VehicleLabel)
SELECT v.C, v.N, v.L FROM (VALUES ('road', 'Road', 'Plate number'), ('rail', 'Rail', 'Wagon number'), ('air', 'Air', 'Flight number'), ('sea', 'Sea', 'Vessel name')) v (C, N, L)
WHERE NOT EXISTS (SELECT 1 FROM dbo.TransportMode x WHERE x.Code = v.C);

INSERT INTO dbo.Consignment (ShipmentId, CarrierPartyId, Reference, Status)
SELECT @s1, p.PartyId, 'CON-0001', 'in-transit' FROM dbo.Party p WHERE @s1 IS NOT NULL AND p.LegalName = 'Blue Freight Logistics GmbH' AND NOT EXISTS (SELECT 1 FROM dbo.Consignment WHERE Reference = 'CON-0001');

INSERT INTO dbo.TransportLeg (ConsignmentId, LegNo, TransportModeId, FromPlace, ToPlace, PlannedDeparture, PlannedArrival, ActualDeparture, ActualArrival, VehicleReference)
SELECT c.ConsignmentId, v.N, m.TransportModeId, v.F, v.T, v.PD, v.PA, v.AD, v.AA, v.R FROM (VALUES
    (1, 'road', 'New York warehouse', 'JFK airport', '2026-11-03T08:00:00', '2026-11-03T10:00:00', '2026-11-03T08:10:00', '2026-11-03T10:05:00', 'NY-4821'),
    (2, 'air', 'JFK airport', 'Frankfurt airport', '2026-11-03T18:00:00', '2026-11-04T07:30:00', '2026-11-03T18:40:00', NULL, 'LH401')) v (N, M, F, T, PD, PA, AD, AA, R)
JOIN dbo.Consignment c ON c.Reference = 'CON-0001' JOIN dbo.TransportMode m ON m.Code = v.M
WHERE NOT EXISTS (SELECT 1 FROM dbo.TransportLeg x WHERE x.ConsignmentId = c.ConsignmentId AND x.LegNo = v.N);

INSERT INTO dbo.TransportEvent (ConsignmentId, TransportLegId, EventCode, OccurredOn, Place, Note)
SELECT c.ConsignmentId, l.TransportLegId, v.E, v.D, v.P, v.O FROM (VALUES
    (1, 'departed', '2026-11-03T08:10:00', 'New York warehouse', NULL), (1, 'arrived', '2026-11-03T10:05:00', 'JFK airport', NULL),
    (2, 'departed', '2026-11-03T18:40:00', 'JFK airport', 'Forty minutes late'), (2, 'delayed', '2026-11-03T21:00:00', 'Atlantic', 'Headwinds')) v (N, E, D, P, O)
JOIN dbo.Consignment c ON c.Reference = 'CON-0001' JOIN dbo.TransportLeg l ON l.ConsignmentId = c.ConsignmentId AND l.LegNo = v.N
WHERE NOT EXISTS (SELECT 1 FROM dbo.TransportEvent x WHERE x.ConsignmentId = c.ConsignmentId AND x.EventCode = v.E AND x.OccurredOn = v.D);

INSERT INTO dbo.ConsignmentEndorsement (ConsignmentId, PartyId, SequenceNo, EndorsedOn)
SELECT c.ConsignmentId, p.PartyId, v.S, v.D FROM (VALUES ('Acme Hardware Supply LLC', 1, '2026-11-02'), ('Blue Freight Logistics GmbH', 2, '2026-11-03')) v (L, S, D)
JOIN dbo.Consignment c ON c.Reference = 'CON-0001' JOIN dbo.Party p ON p.LegalName = v.L
WHERE NOT EXISTS (SELECT 1 FROM dbo.ConsignmentEndorsement x WHERE x.ConsignmentId = c.ConsignmentId AND x.SequenceNo = v.S);

-- Requested, promised and actual delivery dates
INSERT INTO dbo.DeliverySchedule (SalesOrderLineId, RequestedFrom, RequestedTo, PromisedFrom, PromisedTo, ActualOn)
SELECT v.L, v.RF, v.RT, v.PF, v.PT, v.A FROM (VALUES (@ol1, '2026-11-10', '2026-11-12', '2026-11-12', '2026-11-14', '2026-11-16'), (@ol2, '2026-11-10', '2026-11-20', '2026-11-18', '2026-11-20', NULL)) v (L, RF, RT, PF, PT, A)
WHERE v.L IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.DeliverySchedule x WHERE x.SalesOrderLineId = v.L);

INSERT INTO dbo.DeliveryPromise (DeliveryScheduleId, PromisedFrom, PromisedTo, ChangedOn, Reason)
SELECT s.DeliveryScheduleId, v.F, v.T, v.C, v.R FROM (VALUES (@ol1, '2026-11-10', '2026-11-12', '2026-10-25T09:00:00', 'First promise'), (@ol1, '2026-11-12', '2026-11-14', '2026-11-09T14:00:00', 'Supplier delay')) v (L, F, T, C, R)
JOIN dbo.DeliverySchedule s ON s.SalesOrderLineId = v.L WHERE NOT EXISTS (SELECT 1 FROM dbo.DeliveryPromise x WHERE x.DeliveryScheduleId = s.DeliveryScheduleId AND x.ChangedOn = v.C);

-- Delivery terms and contracts
INSERT INTO dbo.DeliveryTerm (Incoterm, Name, NeedsNamedPlace)
SELECT v.C, v.N, v.P FROM (VALUES ('EXW', 'Ex Works', 1), ('FCA', 'Free Carrier', 1), ('CPT', 'Carriage Paid To', 1), ('CIP', 'Carriage and Insurance Paid To', 1), ('DAP', 'Delivered at Place', 1),
    ('DPU', 'Delivered at Place Unloaded', 1), ('DDP', 'Delivered Duty Paid', 1), ('FAS', 'Free Alongside Ship', 1), ('FOB', 'Free On Board', 1), ('CFR', 'Cost and Freight', 1), ('CIF', 'Cost, Insurance and Freight', 1)) v (C, N, P)
WHERE NOT EXISTS (SELECT 1 FROM dbo.DeliveryTerm x WHERE x.Incoterm = v.C);

INSERT INTO dbo.Contract (ContractNumber, PartyId, Title, Status, ValidFrom, ValidTo, RenewalMode, PriceListId, PaymentTermsId, DeliveryTermId, DeliveryNamedPlace)
SELECT v.N, p.PartyId, v.T, v.S, v.F, v.V, v.R, pl.PriceListId, pt.PaymentTermsId, dt.DeliveryTermId, v.D FROM (VALUES
    ('C-2027-001', 'Contoso Wholesale Ltd', 'Annual wholesale supply', 'active', '2027-01-01', '2027-12-31', 'manual', 'Wholesale 2027', 'NET30', 'FCA', 'New York warehouse'),
    ('C-2026-009', 'Acme Hardware Supply LLC', 'Hardware supply 2026', 'expired', '2026-01-01', '2026-12-31', 'none', NULL, 'NET60', 'DAP', 'Los Angeles annex'),
    ('C-2027-002', 'Fabrikam Packaging SA', 'Packaging framework', 'draft', '2027-02-01', NULL, 'auto', NULL, 'NET30', 'EXW', 'Lyon plant')) v (N, P, T, S, F, V, R, L, M, E, D)
JOIN dbo.Party p ON p.LegalName = v.P LEFT JOIN dbo.PriceList pl ON pl.Name = v.L LEFT JOIN dbo.PaymentTerms pt ON pt.Code = v.M LEFT JOIN dbo.DeliveryTerm dt ON dt.Incoterm = v.E
WHERE NOT EXISTS (SELECT 1 FROM dbo.Contract x WHERE x.ContractNumber = v.N);

INSERT INTO dbo.ContractCondition (ContractId, ConditionCode, Description)
SELECT c.ContractId, v.K, v.D FROM (VALUES ('C-2027-001', 'RETURNS', 'Unused goods may be returned within 30 days'), ('C-2027-001', 'MINORDER', 'Minimum order value of 500 USD'), ('C-2026-009', 'WARRANTY', 'Twelve months from delivery')) v (N, K, D)
JOIN dbo.Contract c ON c.ContractNumber = v.N WHERE NOT EXISTS (SELECT 1 FROM dbo.ContractCondition x WHERE x.ContractId = c.ContractId AND x.ConditionCode = v.K);

-- A statement, reminders and a remittance advice
INSERT INTO dbo.CustomerStatement (CustomerId, PeriodFrom, PeriodTo, OpeningBalance, ClosingBalance, IssuedOn)
SELECT @c1, '2026-10-01', '2026-10-31', 100.00, 200.00, '2026-11-01T08:00:00' WHERE @c1 IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.CustomerStatement WHERE CustomerId = @c1 AND PeriodFrom = '2026-10-01' AND PeriodTo = '2026-10-31');

INSERT INTO dbo.CustomerStatementLine (CustomerStatementId, LineNumber, LineDate, Description, SalesInvoiceId, DebitAmount, CreditAmount, RunningBalance)
SELECT s.CustomerStatementId, v.N, v.D, v.T, v.V, v.B, v.C, v.R FROM (VALUES (1, '2026-10-05', 'Invoice', @v1, 250.00, 0.00, 350.00), (2, '2026-10-20', 'Payment received', NULL, 0.00, 150.00, 200.00)) v (N, D, T, V, B, C, R)
JOIN dbo.CustomerStatement s ON s.CustomerId = @c1 AND s.PeriodFrom = '2026-10-01' AND s.PeriodTo = '2026-10-31'
WHERE NOT EXISTS (SELECT 1 FROM dbo.CustomerStatementLine x WHERE x.CustomerStatementId = s.CustomerStatementId AND x.LineNumber = v.N);

INSERT INTO dbo.PaymentReminder (SalesInvoiceId, ReminderLevel, DaysOverdue, FeeAmount, IssuedOn, SentOn)
SELECT v.V, v.L, v.D, v.F, v.I, v.S FROM (VALUES (@v1, 1, 10, 0.00, '2026-11-02T08:00:00', '2026-11-02T08:05:00'), (@v1, 2, 25, 10.00, '2026-11-17T08:00:00', NULL), (@v2, 1, 7, 0.00, '2026-11-03T08:00:00', '2026-11-03T08:05:00')) v (V, L, D, F, I, S)
WHERE v.V IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.PaymentReminder x WHERE x.SalesInvoiceId = v.V AND x.ReminderLevel = v.L);

INSERT INTO dbo.RemittanceAdvice (PayerPartyId, PaymentReference, PaidOn, CurrencyCode, TotalAmount)
SELECT p.PartyId, 'REM-2026-0001', '2026-11-05', 'USD', 300.00 FROM dbo.Party p WHERE p.LegalName = 'Global Payer Ltd' AND NOT EXISTS (SELECT 1 FROM dbo.RemittanceAdvice WHERE PaymentReference = 'REM-2026-0001');

INSERT INTO dbo.RemittanceAdviceLine (RemittanceAdviceId, SalesInvoiceId, AmountPaid)
SELECT r.RemittanceAdviceId, v.V, v.A FROM (VALUES (@v1, 200.00), (@v2, 100.00)) v (V, A)
JOIN dbo.RemittanceAdvice r ON r.PaymentReference = 'REM-2026-0001' WHERE v.V IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.RemittanceAdviceLine x WHERE x.RemittanceAdviceId = r.RemittanceAdviceId AND x.SalesInvoiceId = v.V);

-- File hashes and the distribution of documents
INSERT INTO dbo.AssetFileHash (AssetId, Algorithm, HashValue, ExternalUri, VerifiedOn, VerifyStatus)
SELECT v.A, 'sha256', v.H, v.U, v.D, v.S FROM (VALUES
    (@a1, 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855', NULL, '2026-11-01T08:00:00', 'ok'),
    (@a2, '9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08', 'https://files.example.com/brochure.pdf', '2026-11-01T08:00:00', 'mismatch')) v (A, H, U, D, S)
WHERE v.A IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.AssetFileHash x WHERE x.AssetId = v.A AND x.Algorithm = 'sha256');

INSERT INTO dbo.DocumentDistribution (DocumentType, DocumentId, PartyId, Channel, SentOn, Status)
SELECT v.T, v.D, p.PartyId, v.C, v.S, v.U FROM (VALUES
    ('SalesInvoice', @v1, 'Northwind Traders Inc.', 'email', '2026-10-05T09:00:00', 'read'), ('SalesInvoice', @v1, 'Northwind Traders Inc.', 'post', NULL, 'queued'),
    ('PaymentReminder', 1, 'Northwind Traders Inc.', 'email', '2026-11-02T08:05:00', 'sent'), ('Quotation', 1, 'Acme Hardware Supply LLC', 'edi', '2026-11-01T10:00:00', 'failed')) v (T, D, P, C, S, U)
JOIN dbo.Party p ON p.LegalName = v.P WHERE v.D IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.DocumentDistribution x WHERE x.DocumentType = v.T AND x.DocumentId = v.D AND x.PartyId = p.PartyId AND x.Channel = v.C);

PRINT 'table shape seed rows are in';
