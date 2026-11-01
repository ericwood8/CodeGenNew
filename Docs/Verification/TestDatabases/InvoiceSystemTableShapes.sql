-- The tables of the table shapes of the generator (spec item 143, entries 1 to 30), added to the real InvoiceSystem database and to InvoiceSystem_test.
-- Re-runnable: a table that exists is left alone. Run with:  sqlcmd -S <server> -E -I -d <database> -b -i InvoiceSystemTableShapes.sql
-- The log, queue, archive and tenant tables (entries 1 to 3 and 5) are already there (ErrorLog, ErrorLogArchive, JobQueue, PurgePolicy, Tenant, Organization);
-- this script adds the rest and EmployeeTenant where the database lacks it. Nothing existing is changed.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- 5. Tenants: a person works in several tenants, with a role per tenant
IF OBJECT_ID('dbo.EmployeeTenant', 'U') IS NULL
CREATE TABLE dbo.EmployeeTenant (
    EmployeeId INT NOT NULL CONSTRAINT FK_EmployeeTenant_Employee REFERENCES dbo.Employee (EmployeeId),
    TenantId INT NOT NULL CONSTRAINT FK_EmployeeTenant_Tenant REFERENCES dbo.Tenant (TenantId),
    Role VARCHAR(30) NULL,
    CONSTRAINT PK_EmployeeTenant PRIMARY KEY (EmployeeId, TenantId)
);
GO

-- 5 and 15. A tenant-owned table with soft delete
IF OBJECT_ID('dbo.Project', 'U') IS NULL
CREATE TABLE dbo.Project (
    ProjectId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Project PRIMARY KEY,
    TenantId INT NOT NULL CONSTRAINT FK_Project_Tenant REFERENCES dbo.Tenant (TenantId),
    Name VARCHAR(80) NOT NULL,
    Description VARCHAR(200) NULL,
    Budget DECIMAL(12,2) NOT NULL CONSTRAINT DF_Project_Budget DEFAULT 0,
    StartedOn DATE NULL,
    IsDeleted BIT NOT NULL CONSTRAINT DF_Project_IsDeleted DEFAULT 0,
    DeletedOn DATETIME NULL,
    DeletedBy VARCHAR(60) NULL
);
GO

-- 7. A tree: categories with a parent
IF OBJECT_ID('dbo.Category', 'U') IS NULL
CREATE TABLE dbo.Category (
    CategoryId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Category PRIMARY KEY,
    Name VARCHAR(80) NOT NULL,
    ParentCategoryId INT NULL CONSTRAINT FK_Category_Parent REFERENCES dbo.Category (CategoryId),
    SortOrder INT NOT NULL CONSTRAINT DF_Category_SortOrder DEFAULT 0,
    Description VARCHAR(200) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Category_IsActive DEFAULT 1
);
GO

-- 7. A thread: comments with a parent
IF OBJECT_ID('dbo.Post', 'U') IS NULL
CREATE TABLE dbo.Post (
    PostId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Post PRIMARY KEY,
    Title VARCHAR(150) NOT NULL,
    Slug VARCHAR(150) NOT NULL CONSTRAINT UQ_Post_Slug UNIQUE,
    Body VARCHAR(MAX) NOT NULL,
    Status VARCHAR(12) NOT NULL CONSTRAINT DF_Post_Status DEFAULT 'draft' CONSTRAINT CK_Post_Status CHECK (Status IN ('draft', 'published', 'archived')),
    PublishedOn DATETIME NULL,
    AuthorEmployeeId INT NULL CONSTRAINT FK_Post_Author REFERENCES dbo.Employee (EmployeeId),
    CreatedOn DATETIME NOT NULL CONSTRAINT DF_Post_CreatedOn DEFAULT GETDATE()
);
GO

IF OBJECT_ID('dbo.PostComment', 'U') IS NULL
CREATE TABLE dbo.PostComment (
    PostCommentId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PostComment PRIMARY KEY,
    PostId INT NOT NULL CONSTRAINT FK_PostComment_Post REFERENCES dbo.Post (PostId),
    ParentPostCommentId INT NULL CONSTRAINT FK_PostComment_Parent REFERENCES dbo.PostComment (PostCommentId),
    AuthorEmployeeId INT NULL CONSTRAINT FK_PostComment_Author REFERENCES dbo.Employee (EmployeeId),
    Body VARCHAR(1000) NOT NULL,
    CreatedOn DATETIME NOT NULL CONSTRAINT DF_PostComment_CreatedOn DEFAULT GETDATE()
);
GO

-- 15. Effective dating: a price valid for a period
IF OBJECT_ID('dbo.ItemPrice', 'U') IS NULL
CREATE TABLE dbo.ItemPrice (
    ItemPriceId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ItemPrice PRIMARY KEY,
    ItemId INT NOT NULL CONSTRAINT FK_ItemPrice_Item REFERENCES dbo.Item (ItemId),
    PriceType VARCHAR(20) NOT NULL CONSTRAINT DF_ItemPrice_PriceType DEFAULT 'list',
    Price DECIMAL(12,2) NOT NULL,
    ValidFrom DATE NOT NULL,
    ValidTo DATE NULL,
    CONSTRAINT CK_ItemPrice_Period CHECK (ValidTo IS NULL OR ValidTo >= ValidFrom)
);
GO

-- 17. Orders keep what the product looked like when they were written
IF OBJECT_ID('dbo.SalesOrder', 'U') IS NULL
CREATE TABLE dbo.SalesOrder (
    SalesOrderId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesOrder PRIMARY KEY,
    CustomerId INT NOT NULL CONSTRAINT FK_SalesOrder_Customer REFERENCES dbo.Customer (CustomerId),
    Status VARCHAR(12) NOT NULL CONSTRAINT DF_SalesOrder_Status DEFAULT 'draft' CONSTRAINT CK_SalesOrder_Status CHECK (Status IN ('draft', 'placed', 'shipped', 'cancelled')),
    CurrencyCode CHAR(3) NOT NULL CONSTRAINT DF_SalesOrder_Currency DEFAULT 'USD',
    PlacedOn DATETIME NULL,
    Total DECIMAL(12,2) NOT NULL CONSTRAINT DF_SalesOrder_Total DEFAULT 0,
    Notes VARCHAR(500) NULL
);
GO

IF OBJECT_ID('dbo.SalesOrderLine', 'U') IS NULL
CREATE TABLE dbo.SalesOrderLine (
    SalesOrderLineId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesOrderLine PRIMARY KEY,
    SalesOrderId INT NOT NULL CONSTRAINT FK_SalesOrderLine_Order REFERENCES dbo.SalesOrder (SalesOrderId),
    ItemId INT NOT NULL CONSTRAINT FK_SalesOrderLine_Item REFERENCES dbo.Item (ItemId),
    ItemNumber VARCHAR(50) NOT NULL,
    ItemDescription VARCHAR(255) NOT NULL,
    UnitPrice DECIMAL(12,2) NOT NULL,
    TaxRate DECIMAL(5,4) NOT NULL CONSTRAINT DF_SalesOrderLine_TaxRate DEFAULT 0,
    Quantity INT NOT NULL CONSTRAINT CK_SalesOrderLine_Quantity CHECK (Quantity > 0),
    LineTotal AS (CONVERT(DECIMAL(14,2), UnitPrice * Quantity * (1 + TaxRate))) PERSISTED
);
GO

-- 4. A cart and its items, a working copy of an order
IF OBJECT_ID('dbo.Cart', 'U') IS NULL
CREATE TABLE dbo.Cart (
    CartId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Cart PRIMARY KEY,
    CustomerId INT NULL CONSTRAINT FK_Cart_Customer REFERENCES dbo.Customer (CustomerId),
    SessionKey VARCHAR(64) NULL,
    CurrencyCode CHAR(3) NOT NULL CONSTRAINT DF_Cart_Currency DEFAULT 'USD',
    Status VARCHAR(12) NOT NULL CONSTRAINT DF_Cart_Status DEFAULT 'open' CONSTRAINT CK_Cart_Status CHECK (Status IN ('open', 'converted', 'abandoned')),
    CreatedOn DATETIME NOT NULL CONSTRAINT DF_Cart_CreatedOn DEFAULT GETDATE(),
    LastTouchedOn DATETIME NOT NULL CONSTRAINT DF_Cart_LastTouchedOn DEFAULT GETDATE(),
    SalesOrderId INT NULL CONSTRAINT FK_Cart_SalesOrder REFERENCES dbo.SalesOrder (SalesOrderId),
    CONSTRAINT CK_Cart_Owner CHECK (CustomerId IS NOT NULL OR SessionKey IS NOT NULL)
);
GO

IF OBJECT_ID('dbo.CartItem', 'U') IS NULL
CREATE TABLE dbo.CartItem (
    CartItemId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CartItem PRIMARY KEY,
    CartId INT NOT NULL CONSTRAINT FK_CartItem_Cart REFERENCES dbo.Cart (CartId),
    ItemId INT NOT NULL CONSTRAINT FK_CartItem_Item REFERENCES dbo.Item (ItemId),
    Quantity INT NOT NULL CONSTRAINT CK_CartItem_Quantity CHECK (Quantity > 0),
    UnitPrice DECIMAL(12,2) NOT NULL,
    AddedOn DATETIME NOT NULL CONSTRAINT DF_CartItem_AddedOn DEFAULT GETDATE(),
    CONSTRAINT UQ_CartItem_Product UNIQUE (CartId, ItemId)
);
GO

-- 6. Session and token rows: only the hash is kept
IF OBJECT_ID('dbo.AuthToken', 'U') IS NULL
CREATE TABLE dbo.AuthToken (
    AuthTokenId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuthToken PRIMARY KEY,
    EmployeeId INT NOT NULL CONSTRAINT FK_AuthToken_Employee REFERENCES dbo.Employee (EmployeeId),
    Kind VARCHAR(20) NOT NULL CONSTRAINT CK_AuthToken_Kind CHECK (Kind IN ('session', 'refresh', 'reset', 'verify')),
    TokenHash VARCHAR(128) NOT NULL CONSTRAINT UQ_AuthToken_Hash UNIQUE,
    CreatedOn DATETIME NOT NULL CONSTRAINT DF_AuthToken_CreatedOn DEFAULT GETDATE(),
    ExpiresOn DATETIME NOT NULL,
    UsedOn DATETIME NULL,
    RevokedOn DATETIME NULL
);
GO

-- 9. Asset metadata: the row points at a stored file, never holds the bytes
IF OBJECT_ID('dbo.Asset', 'U') IS NULL
CREATE TABLE dbo.Asset (
    AssetId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Asset PRIMARY KEY,
    FileName VARCHAR(200) NOT NULL,
    MimeType VARCHAR(100) NOT NULL,
    SizeBytes BIGINT NOT NULL CONSTRAINT CK_Asset_Size CHECK (SizeBytes >= 0),
    StorageKey VARCHAR(300) NOT NULL CONSTRAINT UQ_Asset_StorageKey UNIQUE,
    PublicUrl VARCHAR(500) NULL,
    UploadedOn DATETIME NOT NULL CONSTRAINT DF_Asset_UploadedOn DEFAULT GETDATE(),
    UploadedByEmployeeId INT NULL CONSTRAINT FK_Asset_UploadedBy REFERENCES dbo.Employee (EmployeeId)
);
GO

-- 28. Renditions: a derived file per asset and size
IF OBJECT_ID('dbo.RenditionKind', 'U') IS NULL
CREATE TABLE dbo.RenditionKind (
    RenditionKindId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RenditionKind PRIMARY KEY,
    Name VARCHAR(40) NOT NULL CONSTRAINT UQ_RenditionKind_Name UNIQUE,
    MaxWidth INT NOT NULL,
    MaxHeight INT NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_RenditionKind_IsActive DEFAULT 1
);
GO

IF OBJECT_ID('dbo.AssetRendition', 'U') IS NULL
CREATE TABLE dbo.AssetRendition (
    AssetRenditionId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AssetRendition PRIMARY KEY,
    AssetId INT NOT NULL CONSTRAINT FK_AssetRendition_Asset REFERENCES dbo.Asset (AssetId),
    RenditionKindId INT NOT NULL CONSTRAINT FK_AssetRendition_Kind REFERENCES dbo.RenditionKind (RenditionKindId),
    Url VARCHAR(500) NOT NULL,
    Width INT NOT NULL,
    Height INT NOT NULL,
    SizeBytes BIGINT NOT NULL,
    CONSTRAINT UQ_AssetRendition_Kind UNIQUE (AssetId, RenditionKindId)
);
GO

-- 29. A polymorphic link: a record type name and a record id
IF OBJECT_ID('dbo.Attachment', 'U') IS NULL
CREATE TABLE dbo.Attachment (
    AttachmentId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Attachment PRIMARY KEY,
    AssetId INT NOT NULL CONSTRAINT FK_Attachment_Asset REFERENCES dbo.Asset (AssetId),
    RecordType VARCHAR(40) NOT NULL CONSTRAINT CK_Attachment_Type CHECK (RecordType IN ('Customer', 'Item', 'SalesOrder', 'Post')),
    RecordId INT NOT NULL,
    Note VARCHAR(200) NULL,
    CreatedOn DATETIME NOT NULL CONSTRAINT DF_Attachment_CreatedOn DEFAULT GETDATE()
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Attachment_Record' AND object_id = OBJECT_ID('dbo.Attachment'))
    CREATE INDEX IX_Attachment_Record ON dbo.Attachment (RecordType, RecordId);
GO

-- 10. Inbound events: webhooks, system errors, search queries, page views
IF OBJECT_ID('dbo.WebhookEvent', 'U') IS NULL
CREATE TABLE dbo.WebhookEvent (
    WebhookEventId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WebhookEvent PRIMARY KEY,
    Source VARCHAR(40) NOT NULL,
    EventId VARCHAR(100) NOT NULL,
    Signature VARCHAR(200) NULL,
    Body VARCHAR(MAX) NOT NULL,
    ReceivedOn DATETIME NOT NULL CONSTRAINT DF_WebhookEvent_ReceivedOn DEFAULT GETDATE(),
    IsProcessed BIT NOT NULL CONSTRAINT DF_WebhookEvent_IsProcessed DEFAULT 0,
    ProcessedOn DATETIME NULL,
    CONSTRAINT UQ_WebhookEvent_Sender UNIQUE (Source, EventId)
);
GO

IF OBJECT_ID('dbo.PageView', 'U') IS NULL
CREATE TABLE dbo.PageView (
    PageViewId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PageView PRIMARY KEY,
    ViewedOn DATETIME NOT NULL CONSTRAINT DF_PageView_ViewedOn DEFAULT GETDATE(),
    Path VARCHAR(300) NOT NULL,
    SessionKey VARCHAR(64) NULL,
    Referrer VARCHAR(300) NULL,
    UserAgent VARCHAR(300) NULL
);
GO

-- 11. Plans, subscriptions and entitlements
IF OBJECT_ID('dbo.[Plan]', 'U') IS NULL
CREATE TABLE dbo.[Plan] (
    PlanId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Plan PRIMARY KEY,
    Name VARCHAR(40) NOT NULL CONSTRAINT UQ_Plan_Name UNIQUE,
    MonthlyPrice DECIMAL(10,2) NOT NULL CONSTRAINT DF_Plan_Price DEFAULT 0,
    BillingPeriod VARCHAR(20) NOT NULL CONSTRAINT DF_Plan_Period DEFAULT 'month',
    IsActive BIT NOT NULL CONSTRAINT DF_Plan_IsActive DEFAULT 1
);
GO

IF OBJECT_ID('dbo.PlanLimit', 'U') IS NULL
CREATE TABLE dbo.PlanLimit (
    PlanLimitId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PlanLimit PRIMARY KEY,
    PlanId INT NOT NULL CONSTRAINT FK_PlanLimit_Plan REFERENCES dbo.[Plan] (PlanId),
    LimitKey VARCHAR(40) NOT NULL,
    LimitValue INT NULL,
    CONSTRAINT UQ_PlanLimit_Key UNIQUE (PlanId, LimitKey)
);
GO

IF OBJECT_ID('dbo.Subscription', 'U') IS NULL
CREATE TABLE dbo.Subscription (
    SubscriptionId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Subscription PRIMARY KEY,
    TenantId INT NOT NULL CONSTRAINT FK_Subscription_Tenant REFERENCES dbo.Tenant (TenantId),
    PlanId INT NOT NULL CONSTRAINT FK_Subscription_Plan REFERENCES dbo.[Plan] (PlanId),
    Status VARCHAR(20) NOT NULL CONSTRAINT CK_Subscription_Status CHECK (Status IN ('trialing', 'active', 'past_due', 'cancelled')),
    CurrentPeriodStart DATETIME NULL,
    CurrentPeriodEnd DATETIME NULL,
    GatewaySubscriptionId VARCHAR(80) NULL,
    CONSTRAINT UQ_Subscription_Tenant UNIQUE (TenantId)
);
GO

-- 12. Usage metering: an append-only ledger and a monthly roll-up
IF OBJECT_ID('dbo.UsageEvent', 'U') IS NULL
CREATE TABLE dbo.UsageEvent (
    UsageEventId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_UsageEvent PRIMARY KEY,
    TenantId INT NOT NULL CONSTRAINT FK_UsageEvent_Tenant REFERENCES dbo.Tenant (TenantId),
    Kind VARCHAR(40) NOT NULL,
    Quantity DECIMAL(14,2) NOT NULL CONSTRAINT CK_UsageEvent_Quantity CHECK (Quantity >= 0),
    OccurredOn DATETIME NOT NULL CONSTRAINT DF_UsageEvent_OccurredOn DEFAULT GETDATE()
);
GO

IF OBJECT_ID('dbo.UsageMonthly', 'U') IS NULL
CREATE TABLE dbo.UsageMonthly (
    UsageMonthlyId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_UsageMonthly PRIMARY KEY,
    TenantId INT NOT NULL CONSTRAINT FK_UsageMonthly_Tenant REFERENCES dbo.Tenant (TenantId),
    Kind VARCHAR(40) NOT NULL,
    UsageMonth DATE NOT NULL,
    Quantity DECIMAL(16,2) NOT NULL,
    CONSTRAINT UQ_UsageMonthly UNIQUE (TenantId, Kind, UsageMonth)
);
GO

-- 13. Roles and permissions beyond one flag
IF OBJECT_ID('dbo.Role', 'U') IS NULL
CREATE TABLE dbo.Role (
    RoleId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Role PRIMARY KEY,
    Name VARCHAR(40) NOT NULL CONSTRAINT UQ_Role_Name UNIQUE,
    Description VARCHAR(200) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Role_IsActive DEFAULT 1
);
GO

IF OBJECT_ID('dbo.Permission', 'U') IS NULL
CREATE TABLE dbo.Permission (
    PermissionId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Permission PRIMARY KEY,
    Code VARCHAR(60) NOT NULL CONSTRAINT UQ_Permission_Code UNIQUE,
    Description VARCHAR(200) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Permission_IsActive DEFAULT 1
);
GO

IF OBJECT_ID('dbo.RolePermission', 'U') IS NULL
CREATE TABLE dbo.RolePermission (
    RoleId INT NOT NULL CONSTRAINT FK_RolePermission_Role REFERENCES dbo.Role (RoleId),
    PermissionId INT NOT NULL CONSTRAINT FK_RolePermission_Permission REFERENCES dbo.Permission (PermissionId),
    CONSTRAINT PK_RolePermission PRIMARY KEY (RoleId, PermissionId)
);
GO

-- 14. Feature flags and per-tenant overrides
IF OBJECT_ID('dbo.FeatureFlag', 'U') IS NULL
CREATE TABLE dbo.FeatureFlag (
    FeatureFlagId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FeatureFlag PRIMARY KEY,
    FlagKey VARCHAR(60) NOT NULL CONSTRAINT UQ_FeatureFlag_Key UNIQUE,
    Description VARCHAR(200) NULL,
    DefaultOn BIT NOT NULL CONSTRAINT DF_FeatureFlag_DefaultOn DEFAULT 0
);
GO

IF OBJECT_ID('dbo.FeatureOverride', 'U') IS NULL
CREATE TABLE dbo.FeatureOverride (
    FeatureOverrideId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FeatureOverride PRIMARY KEY,
    FeatureFlagId INT NOT NULL CONSTRAINT FK_FeatureOverride_Flag REFERENCES dbo.FeatureFlag (FeatureFlagId),
    TenantId INT NULL CONSTRAINT FK_FeatureOverride_Tenant REFERENCES dbo.Tenant (TenantId),
    EmployeeId INT NULL CONSTRAINT FK_FeatureOverride_Employee REFERENCES dbo.Employee (EmployeeId),
    IsOn BIT NOT NULL,
    CONSTRAINT CK_FeatureOverride_Who CHECK ((TenantId IS NULL AND EmployeeId IS NOT NULL) OR (TenantId IS NOT NULL AND EmployeeId IS NULL))
);
GO

-- 16. Product variants and dynamic attributes
IF OBJECT_ID('dbo.ProductVariant', 'U') IS NULL
CREATE TABLE dbo.ProductVariant (
    ProductVariantId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProductVariant PRIMARY KEY,
    ItemId INT NOT NULL CONSTRAINT FK_ProductVariant_Item REFERENCES dbo.Item (ItemId),
    Sku VARCHAR(60) NOT NULL CONSTRAINT UQ_ProductVariant_Sku UNIQUE,
    Name VARCHAR(100) NOT NULL,
    Price DECIMAL(12,2) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_ProductVariant_IsActive DEFAULT 1
);
GO

IF OBJECT_ID('dbo.Attribute', 'U') IS NULL
CREATE TABLE dbo.Attribute (
    AttributeId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Attribute PRIMARY KEY,
    Name VARCHAR(40) NOT NULL CONSTRAINT UQ_Attribute_Name UNIQUE,
    DataType VARCHAR(20) NOT NULL CONSTRAINT DF_Attribute_DataType DEFAULT 'text' CONSTRAINT CK_Attribute_DataType CHECK (DataType IN ('text', 'number', 'colour')),
    IsActive BIT NOT NULL CONSTRAINT DF_Attribute_IsActive DEFAULT 1
);
GO

IF OBJECT_ID('dbo.AttributeValue', 'U') IS NULL
CREATE TABLE dbo.AttributeValue (
    AttributeValueId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AttributeValue PRIMARY KEY,
    AttributeId INT NOT NULL CONSTRAINT FK_AttributeValue_Attribute REFERENCES dbo.Attribute (AttributeId),
    Value VARCHAR(80) NOT NULL,
    SortOrder INT NOT NULL CONSTRAINT DF_AttributeValue_SortOrder DEFAULT 0,
    CONSTRAINT UQ_AttributeValue UNIQUE (AttributeId, Value)
);
GO

IF OBJECT_ID('dbo.VariantAttributeValue', 'U') IS NULL
CREATE TABLE dbo.VariantAttributeValue (
    ProductVariantId INT NOT NULL CONSTRAINT FK_VariantAttributeValue_Variant REFERENCES dbo.ProductVariant (ProductVariantId),
    AttributeValueId INT NOT NULL CONSTRAINT FK_VariantAttributeValue_Value REFERENCES dbo.AttributeValue (AttributeValueId),
    CONSTRAINT PK_VariantAttributeValue PRIMARY KEY (ProductVariantId, AttributeValueId)
);
GO

-- 18. Inventory and stock reservation
IF OBJECT_ID('dbo.StockLevel', 'U') IS NULL
CREATE TABLE dbo.StockLevel (
    StockLevelId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockLevel PRIMARY KEY,
    ItemId INT NOT NULL CONSTRAINT FK_StockLevel_Item REFERENCES dbo.Item (ItemId) CONSTRAINT UQ_StockLevel_Item UNIQUE,
    OnHand INT NOT NULL CONSTRAINT DF_StockLevel_OnHand DEFAULT 0,
    Reserved INT NOT NULL CONSTRAINT DF_StockLevel_Reserved DEFAULT 0,
    ReorderLevel INT NOT NULL CONSTRAINT DF_StockLevel_Reorder DEFAULT 0,
    CONSTRAINT CK_StockLevel_Available CHECK (OnHand - Reserved >= 0)
);
GO

IF OBJECT_ID('dbo.StockLedger', 'U') IS NULL
CREATE TABLE dbo.StockLedger (
    StockLedgerId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockLedger PRIMARY KEY,
    ItemId INT NOT NULL CONSTRAINT FK_StockLedger_Item REFERENCES dbo.Item (ItemId),
    Kind VARCHAR(12) NOT NULL CONSTRAINT CK_StockLedger_Kind CHECK (Kind IN ('receipt', 'sale', 'return', 'adjustment', 'reservation', 'release')),
    Quantity INT NOT NULL,
    OccurredOn DATETIME NOT NULL CONSTRAINT DF_StockLedger_OccurredOn DEFAULT GETDATE(),
    Reference VARCHAR(60) NULL,
    ExpiresOn DATETIME NULL
);
GO

-- 19. Payment transactions: an append-only ledger, never a card number
IF OBJECT_ID('dbo.PaymentTransaction', 'U') IS NULL
CREATE TABLE dbo.PaymentTransaction (
    PaymentTransactionId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentTransaction PRIMARY KEY,
    SalesOrderId INT NOT NULL CONSTRAINT FK_PaymentTransaction_Order REFERENCES dbo.SalesOrder (SalesOrderId),
    Kind VARCHAR(12) NOT NULL CONSTRAINT CK_PaymentTransaction_Kind CHECK (Kind IN ('authorize', 'capture', 'refund', 'void')),
    Status VARCHAR(12) NOT NULL CONSTRAINT CK_PaymentTransaction_Status CHECK (Status IN ('pending', 'succeeded', 'failed')),
    Amount DECIMAL(12,2) NOT NULL,
    CurrencyCode CHAR(3) NOT NULL CONSTRAINT DF_PaymentTransaction_Currency DEFAULT 'USD',
    GatewayId VARCHAR(100) NOT NULL CONSTRAINT UQ_PaymentTransaction_Gateway UNIQUE,
    IdempotencyKey VARCHAR(100) NOT NULL CONSTRAINT UQ_PaymentTransaction_Key UNIQUE,
    Payload VARCHAR(MAX) NULL,
    CreatedOn DATETIME NOT NULL CONSTRAINT DF_PaymentTransaction_CreatedOn DEFAULT GETDATE()
);
GO

-- 20. Shipments and split fulfilment
IF OBJECT_ID('dbo.Shipment', 'U') IS NULL
CREATE TABLE dbo.Shipment (
    ShipmentId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Shipment PRIMARY KEY,
    SalesOrderId INT NOT NULL CONSTRAINT FK_Shipment_Order REFERENCES dbo.SalesOrder (SalesOrderId),
    Carrier VARCHAR(40) NOT NULL,
    TrackingNumber VARCHAR(80) NULL,
    ShippedOn DATETIME NULL,
    Status VARCHAR(12) NOT NULL CONSTRAINT DF_Shipment_Status DEFAULT 'packing' CONSTRAINT CK_Shipment_Status CHECK (Status IN ('packing', 'shipped', 'delivered', 'returned'))
);
GO

IF OBJECT_ID('dbo.ShipmentItem', 'U') IS NULL
CREATE TABLE dbo.ShipmentItem (
    ShipmentItemId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ShipmentItem PRIMARY KEY,
    ShipmentId INT NOT NULL CONSTRAINT FK_ShipmentItem_Shipment REFERENCES dbo.Shipment (ShipmentId),
    SalesOrderLineId INT NOT NULL CONSTRAINT FK_ShipmentItem_Line REFERENCES dbo.SalesOrderLine (SalesOrderLineId),
    Quantity INT NOT NULL CONSTRAINT CK_ShipmentItem_Quantity CHECK (Quantity > 0),
    CONSTRAINT UQ_ShipmentItem UNIQUE (ShipmentId, SalesOrderLineId)
);
GO

-- 21. Addresses with a type and one default per customer and type
IF OBJECT_ID('dbo.CustomerAddress', 'U') IS NULL
CREATE TABLE dbo.CustomerAddress (
    CustomerAddressId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerAddress PRIMARY KEY,
    CustomerId INT NOT NULL CONSTRAINT FK_CustomerAddress_Customer REFERENCES dbo.Customer (CustomerId),
    AddressType VARCHAR(10) NOT NULL CONSTRAINT CK_CustomerAddress_Type CHECK (AddressType IN ('billing', 'shipping')),
    Line1 VARCHAR(200) NOT NULL,
    Line2 VARCHAR(200) NULL,
    City VARCHAR(100) NOT NULL,
    Region VARCHAR(50) NULL,
    PostalCode VARCHAR(25) NULL,
    Country VARCHAR(100) NOT NULL,
    IsDefault BIT NOT NULL CONSTRAINT DF_CustomerAddress_IsDefault DEFAULT 0
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_CustomerAddress_Default' AND object_id = OBJECT_ID('dbo.CustomerAddress'))
    CREATE UNIQUE INDEX UX_CustomerAddress_Default ON dbo.CustomerAddress (CustomerId, AddressType) WHERE IsDefault = 1;
GO

-- 22. A one-to-one extension: the key of the extension is the key of the customer
IF OBJECT_ID('dbo.CustomerProfile', 'U') IS NULL
CREATE TABLE dbo.CustomerProfile (
    CustomerId INT NOT NULL CONSTRAINT PK_CustomerProfile PRIMARY KEY CONSTRAINT FK_CustomerProfile_Customer REFERENCES dbo.Customer (CustomerId),
    PreferredLanguage VARCHAR(10) NOT NULL CONSTRAINT DF_CustomerProfile_Language DEFAULT 'en',
    PreferredCurrency CHAR(3) NOT NULL CONSTRAINT DF_CustomerProfile_Currency DEFAULT 'USD',
    LoyaltyPoints INT NOT NULL CONSTRAINT DF_CustomerProfile_Points DEFAULT 0,
    MarketingOptIn BIT NOT NULL CONSTRAINT DF_CustomerProfile_OptIn DEFAULT 0
);
GO

-- 24. Locales and 23. a permanent parent with a version per edit and per language
IF OBJECT_ID('dbo.Locale', 'U') IS NULL
CREATE TABLE dbo.Locale (
    LocaleCode VARCHAR(10) NOT NULL CONSTRAINT PK_Locale PRIMARY KEY,
    Name VARCHAR(60) NOT NULL,
    FallbackLocaleCode VARCHAR(10) NULL CONSTRAINT FK_Locale_Fallback REFERENCES dbo.Locale (LocaleCode),
    IsDefault BIT NOT NULL CONSTRAINT DF_Locale_IsDefault DEFAULT 0
);
GO

IF OBJECT_ID('dbo.Document', 'U') IS NULL
CREATE TABLE dbo.Document (
    DocumentId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Document PRIMARY KEY,
    Title VARCHAR(150) NOT NULL,
    CurrentDocumentVersionId INT NULL,
    CreatedOn DATETIME NOT NULL CONSTRAINT DF_Document_CreatedOn DEFAULT GETDATE(),
    CreatedByEmployeeId INT NULL CONSTRAINT FK_Document_CreatedBy REFERENCES dbo.Employee (EmployeeId)
);
GO

IF OBJECT_ID('dbo.DocumentVersion', 'U') IS NULL
CREATE TABLE dbo.DocumentVersion (
    DocumentVersionId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DocumentVersion PRIMARY KEY,
    DocumentId INT NOT NULL CONSTRAINT FK_DocumentVersion_Document REFERENCES dbo.Document (DocumentId),
    VersionNo INT NOT NULL,
    LocaleCode VARCHAR(10) NOT NULL CONSTRAINT FK_DocumentVersion_Locale REFERENCES dbo.Locale (LocaleCode),
    State VARCHAR(10) NOT NULL CONSTRAINT DF_DocumentVersion_State DEFAULT 'draft' CONSTRAINT CK_DocumentVersion_State CHECK (State IN ('draft', 'published', 'retired')),
    Title VARCHAR(150) NOT NULL,
    Body VARCHAR(MAX) NULL,
    PublishFrom DATETIME NULL,
    ExpiresOn DATETIME NULL,
    CreatedOn DATETIME NOT NULL CONSTRAINT DF_DocumentVersion_CreatedOn DEFAULT GETDATE(),
    CreatedByEmployeeId INT NULL CONSTRAINT FK_DocumentVersion_CreatedBy REFERENCES dbo.Employee (EmployeeId),
    CONSTRAINT UQ_DocumentVersion UNIQUE (DocumentId, VersionNo, LocaleCode)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Document_CurrentVersion')
    ALTER TABLE dbo.Document ADD CONSTRAINT FK_Document_CurrentVersion FOREIGN KEY (CurrentDocumentVersionId) REFERENCES dbo.DocumentVersion (DocumentVersionId);
GO

-- 26. Delivery channels and the links of a published version to a channel, with a schedule per channel (25)
IF OBJECT_ID('dbo.Channel', 'U') IS NULL
CREATE TABLE dbo.Channel (
    ChannelId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Channel PRIMARY KEY,
    Name VARCHAR(60) NOT NULL CONSTRAINT UQ_Channel_Name UNIQUE,
    Kind VARCHAR(20) NOT NULL CONSTRAINT CK_Channel_Kind CHECK (Kind IN ('storefront', 'app', 'newsletter')),
    IsActive BIT NOT NULL CONSTRAINT DF_Channel_IsActive DEFAULT 1
);
GO

IF OBJECT_ID('dbo.ChannelLink', 'U') IS NULL
CREATE TABLE dbo.ChannelLink (
    ChannelLinkId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ChannelLink PRIMARY KEY,
    DocumentVersionId INT NOT NULL CONSTRAINT FK_ChannelLink_Version REFERENCES dbo.DocumentVersion (DocumentVersionId),
    ChannelId INT NOT NULL CONSTRAINT FK_ChannelLink_Channel REFERENCES dbo.Channel (ChannelId),
    State VARCHAR(12) NOT NULL CONSTRAINT DF_ChannelLink_State DEFAULT 'scheduled' CONSTRAINT CK_ChannelLink_State CHECK (State IN ('scheduled', 'published', 'unpublished')),
    PublishFrom DATETIME NULL,
    ExpiresOn DATETIME NULL,
    CONSTRAINT UQ_ChannelLink UNIQUE (DocumentVersionId, ChannelId)
);
GO

-- 27. Workspaces: a child scope of a tenant, with a role per person
IF OBJECT_ID('dbo.Workspace', 'U') IS NULL
CREATE TABLE dbo.Workspace (
    WorkspaceId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Workspace PRIMARY KEY,
    TenantId INT NOT NULL CONSTRAINT FK_Workspace_Tenant REFERENCES dbo.Tenant (TenantId),
    ParentWorkspaceId INT NULL CONSTRAINT FK_Workspace_Parent REFERENCES dbo.Workspace (WorkspaceId),
    Name VARCHAR(80) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Workspace_IsActive DEFAULT 1
);
GO

IF OBJECT_ID('dbo.WorkspaceMember', 'U') IS NULL
CREATE TABLE dbo.WorkspaceMember (
    WorkspaceId INT NOT NULL CONSTRAINT FK_WorkspaceMember_Workspace REFERENCES dbo.Workspace (WorkspaceId),
    EmployeeId INT NOT NULL CONSTRAINT FK_WorkspaceMember_Employee REFERENCES dbo.Employee (EmployeeId),
    Role VARCHAR(20) NOT NULL CONSTRAINT CK_WorkspaceMember_Role CHECK (Role IN ('creator', 'translator', 'editor', 'publisher')),
    CONSTRAINT PK_WorkspaceMember PRIMARY KEY (WorkspaceId, EmployeeId)
);
GO

-- 30. Content type definitions: a type, its fields, and items whose values are JSON
IF OBJECT_ID('dbo.ContentType', 'U') IS NULL
CREATE TABLE dbo.ContentType (
    ContentTypeId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ContentType PRIMARY KEY,
    Name VARCHAR(60) NOT NULL CONSTRAINT UQ_ContentType_Name UNIQUE,
    Description VARCHAR(200) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_ContentType_IsActive DEFAULT 1
);
GO

IF OBJECT_ID('dbo.ContentField', 'U') IS NULL
CREATE TABLE dbo.ContentField (
    ContentFieldId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ContentField PRIMARY KEY,
    ContentTypeId INT NOT NULL CONSTRAINT FK_ContentField_Type REFERENCES dbo.ContentType (ContentTypeId),
    FieldName VARCHAR(60) NOT NULL,
    DataType VARCHAR(20) NOT NULL CONSTRAINT CK_ContentField_DataType CHECK (DataType IN ('text', 'number', 'date', 'bool', 'asset')),
    IsRequired BIT NOT NULL CONSTRAINT DF_ContentField_IsRequired DEFAULT 0,
    SortOrder INT NOT NULL CONSTRAINT DF_ContentField_SortOrder DEFAULT 0,
    CONSTRAINT UQ_ContentField UNIQUE (ContentTypeId, FieldName)
);
GO

IF OBJECT_ID('dbo.ContentItem', 'U') IS NULL
CREATE TABLE dbo.ContentItem (
    ContentItemId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ContentItem PRIMARY KEY,
    ContentTypeId INT NOT NULL CONSTRAINT FK_ContentItem_Type REFERENCES dbo.ContentType (ContentTypeId),
    Title VARCHAR(150) NOT NULL,
    DataJson VARCHAR(MAX) NOT NULL CONSTRAINT CK_ContentItem_Json CHECK (ISJSON(DataJson) = 1),
    CreatedOn DATETIME NOT NULL CONSTRAINT DF_ContentItem_CreatedOn DEFAULT GETDATE()
);
GO
