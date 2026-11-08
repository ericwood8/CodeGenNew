-- The tables of the table shapes of the generator (spec item 143, entries 1 to 49), added to the real InvoiceSystem database and to InvoiceSystem_test.
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

-- Trading documents: tax, totals, allowances and charges, price lists, currencies
IF OBJECT_ID('dbo.Currency', 'U') IS NULL
CREATE TABLE dbo.Currency (
    CurrencyId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Currency PRIMARY KEY,
    Code CHAR(3) NOT NULL CONSTRAINT UQ_Currency_Code UNIQUE,
    Name VARCHAR(60) NOT NULL,
    Symbol VARCHAR(5) NULL,
    DecimalPlaces TINYINT NOT NULL CONSTRAINT DF_Currency_DecimalPlaces DEFAULT 2 CONSTRAINT CK_Currency_DecimalPlaces CHECK (DecimalPlaces BETWEEN 0 AND 4),
    IsActive BIT NOT NULL CONSTRAINT DF_Currency_IsActive DEFAULT 1
);
GO

IF OBJECT_ID('dbo.ExchangeRate', 'U') IS NULL
CREATE TABLE dbo.ExchangeRate (
    ExchangeRateId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExchangeRate PRIMARY KEY,
    FromCurrencyId INT NOT NULL CONSTRAINT FK_ExchangeRate_From REFERENCES dbo.Currency (CurrencyId),
    ToCurrencyId INT NOT NULL CONSTRAINT FK_ExchangeRate_To REFERENCES dbo.Currency (CurrencyId),
    Rate DECIMAL(18,8) NOT NULL CONSTRAINT CK_ExchangeRate_Rate CHECK (Rate > 0),
    RateDate DATE NOT NULL,
    Source VARCHAR(40) NULL,
    CONSTRAINT CK_ExchangeRate_Pair CHECK (FromCurrencyId <> ToCurrencyId),
    CONSTRAINT UQ_ExchangeRate_Day UNIQUE (FromCurrencyId, ToCurrencyId, RateDate)
);
GO

IF OBJECT_ID('dbo.TaxCategory', 'U') IS NULL
CREATE TABLE dbo.TaxCategory (
    TaxCategoryId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TaxCategory PRIMARY KEY,
    Code VARCHAR(10) NOT NULL,
    Name VARCHAR(80) NOT NULL,
    Rate DECIMAL(5,4) NOT NULL CONSTRAINT CK_TaxCategory_Rate CHECK (Rate BETWEEN 0 AND 1),
    ExemptionReason VARCHAR(200) NULL,
    ValidFrom DATE NOT NULL,
    ValidTo DATE NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_TaxCategory_IsActive DEFAULT 1,
    CONSTRAINT CK_TaxCategory_Period CHECK (ValidTo IS NULL OR ValidTo >= ValidFrom),
    CONSTRAINT UQ_TaxCategory_Start UNIQUE (Code, ValidFrom)
);
GO

IF OBJECT_ID('dbo.SalesOrderTaxSubtotal', 'U') IS NULL
CREATE TABLE dbo.SalesOrderTaxSubtotal (
    SalesOrderTaxSubtotalId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesOrderTaxSubtotal PRIMARY KEY,
    SalesOrderId INT NOT NULL CONSTRAINT FK_SalesOrderTaxSubtotal_Order REFERENCES dbo.SalesOrder (SalesOrderId),
    TaxCategoryId INT NOT NULL CONSTRAINT FK_SalesOrderTaxSubtotal_Category REFERENCES dbo.TaxCategory (TaxCategoryId),
    TaxableAmount DECIMAL(12,2) NOT NULL,
    TaxAmount DECIMAL(12,2) NOT NULL,
    CONSTRAINT UQ_SalesOrderTaxSubtotal UNIQUE (SalesOrderId, TaxCategoryId)
);
GO

IF OBJECT_ID('dbo.SalesOrderTotal', 'U') IS NULL
CREATE TABLE dbo.SalesOrderTotal (
    SalesOrderId INT NOT NULL CONSTRAINT PK_SalesOrderTotal PRIMARY KEY CONSTRAINT FK_SalesOrderTotal_Order REFERENCES dbo.SalesOrder (SalesOrderId),
    LineExtensionAmount DECIMAL(12,2) NOT NULL CONSTRAINT DF_SalesOrderTotal_Lines DEFAULT 0,
    AllowanceTotalAmount DECIMAL(12,2) NOT NULL CONSTRAINT DF_SalesOrderTotal_Allowance DEFAULT 0,
    ChargeTotalAmount DECIMAL(12,2) NOT NULL CONSTRAINT DF_SalesOrderTotal_Charge DEFAULT 0,
    TaxExclusiveAmount DECIMAL(12,2) NOT NULL CONSTRAINT DF_SalesOrderTotal_Exclusive DEFAULT 0,
    TaxAmount DECIMAL(12,2) NOT NULL CONSTRAINT DF_SalesOrderTotal_Tax DEFAULT 0,
    TaxInclusiveAmount DECIMAL(12,2) NOT NULL CONSTRAINT DF_SalesOrderTotal_Inclusive DEFAULT 0,
    PrepaidAmount DECIMAL(12,2) NOT NULL CONSTRAINT DF_SalesOrderTotal_Prepaid DEFAULT 0,
    RoundingAmount DECIMAL(12,2) NOT NULL CONSTRAINT DF_SalesOrderTotal_Rounding DEFAULT 0,
    PayableAmount DECIMAL(12,2) NOT NULL CONSTRAINT DF_SalesOrderTotal_Payable DEFAULT 0,
    CONSTRAINT CK_SalesOrderTotal_Exclusive CHECK (TaxExclusiveAmount = LineExtensionAmount - AllowanceTotalAmount + ChargeTotalAmount),
    CONSTRAINT CK_SalesOrderTotal_Inclusive CHECK (TaxInclusiveAmount = TaxExclusiveAmount + TaxAmount),
    CONSTRAINT CK_SalesOrderTotal_Payable CHECK (PayableAmount = TaxInclusiveAmount - PrepaidAmount + RoundingAmount)
);
GO

IF OBJECT_ID('dbo.AllowanceCharge', 'U') IS NULL
CREATE TABLE dbo.AllowanceCharge (
    AllowanceChargeId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AllowanceCharge PRIMARY KEY,
    SalesOrderId INT NOT NULL CONSTRAINT FK_AllowanceCharge_Order REFERENCES dbo.SalesOrder (SalesOrderId),
    SalesOrderLineId INT NULL CONSTRAINT FK_AllowanceCharge_Line REFERENCES dbo.SalesOrderLine (SalesOrderLineId),
    IsCharge BIT NOT NULL CONSTRAINT DF_AllowanceCharge_IsCharge DEFAULT 0,
    ReasonCode VARCHAR(10) NULL,
    Reason VARCHAR(100) NOT NULL,
    BaseAmount DECIMAL(12,2) NULL,
    MultiplierPercent DECIMAL(7,4) NULL CONSTRAINT CK_AllowanceCharge_Percent CHECK (MultiplierPercent IS NULL OR MultiplierPercent BETWEEN 0 AND 100),
    Amount DECIMAL(12,2) NOT NULL CONSTRAINT CK_AllowanceCharge_Amount CHECK (Amount >= 0)
);
GO

IF OBJECT_ID('dbo.PriceList', 'U') IS NULL
CREATE TABLE dbo.PriceList (
    PriceListId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PriceList PRIMARY KEY,
    Name VARCHAR(80) NOT NULL CONSTRAINT UQ_PriceList_Name UNIQUE,
    CurrencyId INT NOT NULL CONSTRAINT FK_PriceList_Currency REFERENCES dbo.Currency (CurrencyId),
    CustomerGroup VARCHAR(40) NULL,
    ValidFrom DATE NOT NULL,
    ValidTo DATE NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_PriceList_IsActive DEFAULT 1,
    CONSTRAINT CK_PriceList_Period CHECK (ValidTo IS NULL OR ValidTo >= ValidFrom)
);
GO

IF OBJECT_ID('dbo.PriceListLine', 'U') IS NULL
CREATE TABLE dbo.PriceListLine (
    PriceListLineId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PriceListLine PRIMARY KEY,
    PriceListId INT NOT NULL CONSTRAINT FK_PriceListLine_List REFERENCES dbo.PriceList (PriceListId),
    ItemId INT NOT NULL CONSTRAINT FK_PriceListLine_Item REFERENCES dbo.Item (ItemId),
    MinQuantity INT NOT NULL CONSTRAINT DF_PriceListLine_Min DEFAULT 1 CONSTRAINT CK_PriceListLine_Min CHECK (MinQuantity > 0),
    UnitPrice DECIMAL(12,2) NOT NULL CONSTRAINT CK_PriceListLine_Price CHECK (UnitPrice >= 0),
    CONSTRAINT UQ_PriceListLine UNIQUE (PriceListId, ItemId, MinQuantity)
);
GO

-- Parties: one party with several roles, identifiers, names, tax registrations and contact methods
IF OBJECT_ID('dbo.Party', 'U') IS NULL
CREATE TABLE dbo.Party (
    PartyId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Party PRIMARY KEY,
    LegalName VARCHAR(150) NOT NULL,
    TradingName VARCHAR(150) NULL,
    CustomerId INT NULL CONSTRAINT FK_Party_Customer REFERENCES dbo.Customer (CustomerId),
    CountryCode CHAR(2) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Party_IsActive DEFAULT 1
);
GO

IF OBJECT_ID('dbo.PartyRole', 'U') IS NULL
CREATE TABLE dbo.PartyRole (
    PartyRoleId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PartyRole PRIMARY KEY,
    PartyId INT NOT NULL CONSTRAINT FK_PartyRole_Party REFERENCES dbo.Party (PartyId),
    RoleCode VARCHAR(12) NOT NULL CONSTRAINT CK_PartyRole_Code CHECK (RoleCode IN ('customer', 'supplier', 'carrier', 'payer', 'payee')),
    CONSTRAINT UQ_PartyRole UNIQUE (PartyId, RoleCode)
);
GO

IF OBJECT_ID('dbo.PartyIdentification', 'U') IS NULL
CREATE TABLE dbo.PartyIdentification (
    PartyIdentificationId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PartyIdentification PRIMARY KEY,
    PartyId INT NOT NULL CONSTRAINT FK_PartyIdentification_Party REFERENCES dbo.Party (PartyId),
    SchemeCode VARCHAR(20) NOT NULL,
    IdentifierValue VARCHAR(60) NOT NULL,
    CONSTRAINT UQ_PartyIdentification UNIQUE (SchemeCode, IdentifierValue)
);
GO

IF OBJECT_ID('dbo.PartyName', 'U') IS NULL
CREATE TABLE dbo.PartyName (
    PartyNameId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PartyName PRIMARY KEY,
    PartyId INT NOT NULL CONSTRAINT FK_PartyName_Party REFERENCES dbo.Party (PartyId),
    NameType VARCHAR(10) NOT NULL CONSTRAINT CK_PartyName_Type CHECK (NameType IN ('legal', 'trading', 'alias')),
    Name VARCHAR(150) NOT NULL,
    CONSTRAINT UQ_PartyName UNIQUE (PartyId, NameType, Name)
);
GO

IF OBJECT_ID('dbo.PartyTaxRegistration', 'U') IS NULL
CREATE TABLE dbo.PartyTaxRegistration (
    PartyTaxRegistrationId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PartyTaxRegistration PRIMARY KEY,
    PartyId INT NOT NULL CONSTRAINT FK_PartyTaxRegistration_Party REFERENCES dbo.Party (PartyId),
    CountryCode CHAR(2) NOT NULL,
    TaxSchemeCode VARCHAR(10) NOT NULL CONSTRAINT DF_PartyTaxRegistration_Scheme DEFAULT 'VAT',
    TaxNumber VARCHAR(40) NOT NULL,
    ValidTo DATE NULL,
    CONSTRAINT UQ_PartyTaxRegistration UNIQUE (CountryCode, TaxSchemeCode, TaxNumber)
);
GO

IF OBJECT_ID('dbo.PartyContact', 'U') IS NULL
CREATE TABLE dbo.PartyContact (
    PartyContactId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PartyContact PRIMARY KEY,
    PartyId INT NOT NULL CONSTRAINT FK_PartyContact_Party REFERENCES dbo.Party (PartyId),
    ContactName VARCHAR(100) NOT NULL,
    JobTitle VARCHAR(80) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_PartyContact_IsActive DEFAULT 1
);
GO

IF OBJECT_ID('dbo.PartyCommunication', 'U') IS NULL
CREATE TABLE dbo.PartyCommunication (
    PartyCommunicationId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PartyCommunication PRIMARY KEY,
    PartyId INT NOT NULL CONSTRAINT FK_PartyCommunication_Party REFERENCES dbo.Party (PartyId),
    PartyContactId INT NULL CONSTRAINT FK_PartyCommunication_Contact REFERENCES dbo.PartyContact (PartyContactId),
    Channel VARCHAR(10) NOT NULL CONSTRAINT CK_PartyCommunication_Channel CHECK (Channel IN ('phone', 'mobile', 'fax', 'email', 'web')),
    ChannelValue VARCHAR(200) NOT NULL,
    IsPreferred BIT NOT NULL CONSTRAINT DF_PartyCommunication_IsPreferred DEFAULT 0
);
GO

-- Payment terms, bank accounts, means of payment and instalments
IF OBJECT_ID('dbo.PaymentTerms', 'U') IS NULL
CREATE TABLE dbo.PaymentTerms (
    PaymentTermsId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentTerms PRIMARY KEY,
    Code VARCHAR(12) NOT NULL CONSTRAINT UQ_PaymentTerms_Code UNIQUE,
    Name VARCHAR(80) NOT NULL,
    DaysToPay INT NOT NULL CONSTRAINT CK_PaymentTerms_Days CHECK (DaysToPay >= 0),
    EarlyDiscountPercent DECIMAL(5,2) NULL,
    EarlyDiscountDays INT NULL,
    LatePenaltyPercent DECIMAL(5,2) NULL,
    CONSTRAINT CK_PaymentTerms_Early CHECK ((EarlyDiscountPercent IS NULL AND EarlyDiscountDays IS NULL) OR (EarlyDiscountPercent BETWEEN 0 AND 100 AND EarlyDiscountDays BETWEEN 0 AND DaysToPay))
);
GO

IF OBJECT_ID('dbo.PartyBankAccount', 'U') IS NULL
CREATE TABLE dbo.PartyBankAccount (
    PartyBankAccountId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PartyBankAccount PRIMARY KEY,
    PartyId INT NOT NULL CONSTRAINT FK_PartyBankAccount_Party REFERENCES dbo.Party (PartyId),
    AccountHolder VARCHAR(100) NOT NULL,
    Iban VARCHAR(34) NULL,
    AccountNumber VARCHAR(20) NULL,
    RoutingNumber VARCHAR(12) NULL,
    BankName VARCHAR(100) NULL,
    Bic VARCHAR(11) NULL,
    IsDefault BIT NOT NULL CONSTRAINT DF_PartyBankAccount_IsDefault DEFAULT 0,
    CONSTRAINT CK_PartyBankAccount_Identified CHECK (Iban IS NOT NULL OR (AccountNumber IS NOT NULL AND RoutingNumber IS NOT NULL))
);
GO

IF OBJECT_ID('dbo.PaymentMeans', 'U') IS NULL
CREATE TABLE dbo.PaymentMeans (
    PaymentMeansId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentMeans PRIMARY KEY,
    SalesOrderId INT NOT NULL CONSTRAINT FK_PaymentMeans_Order REFERENCES dbo.SalesOrder (SalesOrderId),
    PaymentTermsId INT NULL CONSTRAINT FK_PaymentMeans_Terms REFERENCES dbo.PaymentTerms (PaymentTermsId),
    MeansCode VARCHAR(14) NOT NULL CONSTRAINT CK_PaymentMeans_Code CHECK (MeansCode IN ('transfer', 'cash', 'check', 'direct-debit', 'gateway')),
    DueDate DATE NULL,
    PayeeBankAccountId INT NULL CONSTRAINT FK_PaymentMeans_Account REFERENCES dbo.PartyBankAccount (PartyBankAccountId),
    PaymentReference VARCHAR(60) NULL
);
GO

IF OBJECT_ID('dbo.PaymentInstalment', 'U') IS NULL
CREATE TABLE dbo.PaymentInstalment (
    PaymentInstalmentId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentInstalment PRIMARY KEY,
    SalesOrderId INT NOT NULL CONSTRAINT FK_PaymentInstalment_Order REFERENCES dbo.SalesOrder (SalesOrderId),
    SequenceNo INT NOT NULL CONSTRAINT CK_PaymentInstalment_Sequence CHECK (SequenceNo > 0),
    DueDate DATE NOT NULL,
    Amount DECIMAL(12,2) NOT NULL CONSTRAINT CK_PaymentInstalment_Amount CHECK (Amount > 0),
    PaidOn DATE NULL,
    CONSTRAINT UQ_PaymentInstalment UNIQUE (SalesOrderId, SequenceNo)
);
GO

-- Documents that refer to earlier documents: receipts, credit and debit notes, references and responses
IF OBJECT_ID('dbo.GoodsReceipt', 'U') IS NULL
CREATE TABLE dbo.GoodsReceipt (
    GoodsReceiptId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_GoodsReceipt PRIMARY KEY,
    SalesOrderId INT NOT NULL CONSTRAINT FK_GoodsReceipt_Order REFERENCES dbo.SalesOrder (SalesOrderId),
    ReceivedOn DATE NOT NULL,
    ReceivedBy VARCHAR(80) NULL,
    Note VARCHAR(300) NULL
);
GO

IF OBJECT_ID('dbo.GoodsReceiptLine', 'U') IS NULL
CREATE TABLE dbo.GoodsReceiptLine (
    GoodsReceiptLineId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_GoodsReceiptLine PRIMARY KEY,
    GoodsReceiptId INT NOT NULL CONSTRAINT FK_GoodsReceiptLine_Receipt REFERENCES dbo.GoodsReceipt (GoodsReceiptId),
    SalesOrderLineId INT NOT NULL CONSTRAINT FK_GoodsReceiptLine_Line REFERENCES dbo.SalesOrderLine (SalesOrderLineId),
    QuantityReceived INT NOT NULL CONSTRAINT CK_GoodsReceiptLine_Received CHECK (QuantityReceived >= 0),
    QuantityRejected INT NOT NULL CONSTRAINT DF_GoodsReceiptLine_Rejected DEFAULT 0 CONSTRAINT CK_GoodsReceiptLine_Rejected CHECK (QuantityRejected >= 0),
    RejectReason VARCHAR(200) NULL,
    CONSTRAINT UQ_GoodsReceiptLine UNIQUE (GoodsReceiptId, SalesOrderLineId),
    CONSTRAINT CK_GoodsReceiptLine_Reason CHECK (QuantityRejected = 0 OR RejectReason IS NOT NULL)
);
GO

IF OBJECT_ID('dbo.CreditNote', 'U') IS NULL
CREATE TABLE dbo.CreditNote (
    CreditNoteId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CreditNote PRIMARY KEY,
    SalesInvoiceId INT NOT NULL CONSTRAINT FK_CreditNote_Invoice REFERENCES dbo.SalesInvoice (SalesInvoiceId),
    NoteType VARCHAR(6) NOT NULL CONSTRAINT DF_CreditNote_Type DEFAULT 'credit' CONSTRAINT CK_CreditNote_Type CHECK (NoteType IN ('credit', 'debit')),
    IsSelfBilled BIT NOT NULL CONSTRAINT DF_CreditNote_SelfBilled DEFAULT 0,
    Status VARCHAR(10) NOT NULL CONSTRAINT DF_CreditNote_Status DEFAULT 'draft' CONSTRAINT CK_CreditNote_Status CHECK (Status IN ('draft', 'issued', 'cancelled')),
    IssuedOn DATE NULL,
    CurrencyCode CHAR(3) NOT NULL CONSTRAINT DF_CreditNote_Currency DEFAULT 'USD',
    Reason VARCHAR(200) NULL
);
GO

IF OBJECT_ID('dbo.CreditNoteLine', 'U') IS NULL
CREATE TABLE dbo.CreditNoteLine (
    CreditNoteLineId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CreditNoteLine PRIMARY KEY,
    CreditNoteId INT NOT NULL CONSTRAINT FK_CreditNoteLine_Note REFERENCES dbo.CreditNote (CreditNoteId),
    SalesInvoiceLineId INT NULL CONSTRAINT FK_CreditNoteLine_InvoiceLine REFERENCES dbo.SalesInvoiceLine (LineId),
    Description VARCHAR(200) NOT NULL,
    Quantity DECIMAL(9,2) NOT NULL CONSTRAINT CK_CreditNoteLine_Quantity CHECK (Quantity > 0),
    UnitPrice DECIMAL(12,2) NOT NULL CONSTRAINT CK_CreditNoteLine_Price CHECK (UnitPrice >= 0),
    LineTotal AS (CONVERT(DECIMAL(14,2), Quantity * UnitPrice)) PERSISTED
);
GO

IF OBJECT_ID('dbo.DocumentReference', 'U') IS NULL
CREATE TABLE dbo.DocumentReference (
    DocumentReferenceId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DocumentReference PRIMARY KEY,
    FromDocumentType VARCHAR(20) NOT NULL CONSTRAINT CK_DocumentReference_From CHECK (FromDocumentType IN ('CreditNote', 'GoodsReceipt', 'SalesInvoice', 'Quotation', 'Shipment', 'Contract')),
    FromDocumentId INT NOT NULL,
    ToDocumentType VARCHAR(20) NOT NULL CONSTRAINT CK_DocumentReference_To CHECK (ToDocumentType IN ('SalesOrder', 'SalesInvoice', 'Quotation', 'Contract', 'Shipment')),
    ToDocumentId INT NOT NULL,
    ReferenceKind VARCHAR(12) NOT NULL CONSTRAINT CK_DocumentReference_Kind CHECK (ReferenceKind IN ('billing', 'order', 'despatch', 'contract', 'additional')),
    ReferencedOn DATE NULL,
    CONSTRAINT UQ_DocumentReference UNIQUE (FromDocumentType, FromDocumentId, ToDocumentType, ToDocumentId, ReferenceKind)
);
GO

IF OBJECT_ID('dbo.DocumentResponse', 'U') IS NULL
CREATE TABLE dbo.DocumentResponse (
    DocumentResponseId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DocumentResponse PRIMARY KEY,
    SalesOrderId INT NOT NULL CONSTRAINT FK_DocumentResponse_Order REFERENCES dbo.SalesOrder (SalesOrderId),
    ResponseCode VARCHAR(10) NOT NULL CONSTRAINT CK_DocumentResponse_Code CHECK (ResponseCode IN ('pending', 'accepted', 'rejected', 'changed')),
    Reason VARCHAR(300) NULL,
    RespondedOn DATETIME NOT NULL CONSTRAINT DF_DocumentResponse_RespondedOn DEFAULT GETDATE(),
    RespondedByPartyId INT NULL CONSTRAINT FK_DocumentResponse_Party REFERENCES dbo.Party (PartyId)
);
GO

IF OBJECT_ID('dbo.LineResponse', 'U') IS NULL
CREATE TABLE dbo.LineResponse (
    LineResponseId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LineResponse PRIMARY KEY,
    DocumentResponseId INT NOT NULL CONSTRAINT FK_LineResponse_Response REFERENCES dbo.DocumentResponse (DocumentResponseId),
    SalesOrderLineId INT NOT NULL CONSTRAINT FK_LineResponse_Line REFERENCES dbo.SalesOrderLine (SalesOrderLineId),
    ResponseCode VARCHAR(10) NOT NULL CONSTRAINT CK_LineResponse_Code CHECK (ResponseCode IN ('pending', 'accepted', 'rejected', 'changed')),
    AcceptedQuantity INT NULL CONSTRAINT CK_LineResponse_Quantity CHECK (AcceptedQuantity IS NULL OR AcceptedQuantity >= 0),
    Reason VARCHAR(300) NULL,
    CONSTRAINT UQ_LineResponse UNIQUE (DocumentResponseId, SalesOrderLineId)
);
GO

-- Requests for quotation and quotations
IF OBJECT_ID('dbo.RequestForQuotation', 'U') IS NULL
CREATE TABLE dbo.RequestForQuotation (
    RequestForQuotationId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RequestForQuotation PRIMARY KEY,
    Title VARCHAR(120) NOT NULL,
    Status VARCHAR(10) NOT NULL CONSTRAINT DF_RequestForQuotation_Status DEFAULT 'draft' CONSTRAINT CK_RequestForQuotation_Status CHECK (Status IN ('draft', 'sent', 'closed', 'cancelled')),
    IssuedOn DATE NULL,
    RespondBy DATE NULL,
    CONSTRAINT CK_RequestForQuotation_Dates CHECK (RespondBy IS NULL OR IssuedOn IS NULL OR RespondBy >= IssuedOn)
);
GO

IF OBJECT_ID('dbo.RequestForQuotationLine', 'U') IS NULL
CREATE TABLE dbo.RequestForQuotationLine (
    RequestForQuotationLineId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RequestForQuotationLine PRIMARY KEY,
    RequestForQuotationId INT NOT NULL CONSTRAINT FK_RequestForQuotationLine_Request REFERENCES dbo.RequestForQuotation (RequestForQuotationId),
    ItemId INT NOT NULL CONSTRAINT FK_RequestForQuotationLine_Item REFERENCES dbo.Item (ItemId),
    Quantity INT NOT NULL CONSTRAINT CK_RequestForQuotationLine_Quantity CHECK (Quantity > 0),
    CONSTRAINT UQ_RequestForQuotationLine UNIQUE (RequestForQuotationId, ItemId)
);
GO

IF OBJECT_ID('dbo.Quotation', 'U') IS NULL
CREATE TABLE dbo.Quotation (
    QuotationId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Quotation PRIMARY KEY,
    RequestForQuotationId INT NOT NULL CONSTRAINT FK_Quotation_Request REFERENCES dbo.RequestForQuotation (RequestForQuotationId),
    SupplierPartyId INT NOT NULL CONSTRAINT FK_Quotation_Supplier REFERENCES dbo.Party (PartyId),
    RevisionNo INT NOT NULL CONSTRAINT DF_Quotation_Revision DEFAULT 1 CONSTRAINT CK_Quotation_Revision CHECK (RevisionNo > 0),
    Status VARCHAR(12) NOT NULL CONSTRAINT DF_Quotation_Status DEFAULT 'draft' CONSTRAINT CK_Quotation_Status CHECK (Status IN ('draft', 'submitted', 'accepted', 'rejected', 'expired', 'superseded')),
    CurrencyCode CHAR(3) NOT NULL CONSTRAINT DF_Quotation_Currency DEFAULT 'USD',
    ValidUntil DATE NULL,
    AcceptedOn DATE NULL,
    CONSTRAINT UQ_Quotation_Revision UNIQUE (RequestForQuotationId, SupplierPartyId, RevisionNo),
    CONSTRAINT CK_Quotation_Accepted CHECK ((Status = 'accepted' AND AcceptedOn IS NOT NULL) OR (Status <> 'accepted' AND AcceptedOn IS NULL))
);
GO

IF OBJECT_ID('dbo.QuotationLine', 'U') IS NULL
CREATE TABLE dbo.QuotationLine (
    QuotationLineId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_QuotationLine PRIMARY KEY,
    QuotationId INT NOT NULL CONSTRAINT FK_QuotationLine_Quotation REFERENCES dbo.Quotation (QuotationId),
    RequestForQuotationLineId INT NOT NULL CONSTRAINT FK_QuotationLine_RequestLine REFERENCES dbo.RequestForQuotationLine (RequestForQuotationLineId),
    UnitPrice DECIMAL(12,2) NOT NULL CONSTRAINT CK_QuotationLine_Price CHECK (UnitPrice >= 0),
    LeadTimeDays INT NULL CONSTRAINT CK_QuotationLine_Lead CHECK (LeadTimeDays IS NULL OR LeadTimeDays >= 0),
    CONSTRAINT UQ_QuotationLine UNIQUE (QuotationId, RequestForQuotationLineId)
);
GO

-- Item identification, classification, lots and serial numbers
IF OBJECT_ID('dbo.ItemIdentifier', 'U') IS NULL
CREATE TABLE dbo.ItemIdentifier (
    ItemIdentifierId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ItemIdentifier PRIMARY KEY,
    ItemId INT NOT NULL CONSTRAINT FK_ItemIdentifier_Item REFERENCES dbo.Item (ItemId),
    SchemeCode VARCHAR(20) NOT NULL CONSTRAINT CK_ItemIdentifier_Scheme CHECK (SchemeCode IN ('gtin', 'sku', 'supplier', 'manufacturer', 'buyer')),
    IdentifierValue VARCHAR(60) NOT NULL,
    PartyId INT NULL CONSTRAINT FK_ItemIdentifier_Party REFERENCES dbo.Party (PartyId),
    CONSTRAINT UQ_ItemIdentifier UNIQUE (SchemeCode, IdentifierValue)
);
GO

IF OBJECT_ID('dbo.ItemRelation', 'U') IS NULL
CREATE TABLE dbo.ItemRelation (
    ItemRelationId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ItemRelation PRIMARY KEY,
    ItemId INT NOT NULL CONSTRAINT FK_ItemRelation_Item REFERENCES dbo.Item (ItemId),
    RelatedItemId INT NOT NULL CONSTRAINT FK_ItemRelation_Related REFERENCES dbo.Item (ItemId),
    RelationType VARCHAR(12) NOT NULL CONSTRAINT CK_ItemRelation_Type CHECK (RelationType IN ('substitute', 'accessory', 'replaced-by')),
    CONSTRAINT CK_ItemRelation_Distinct CHECK (ItemId <> RelatedItemId),
    CONSTRAINT UQ_ItemRelation UNIQUE (ItemId, RelatedItemId, RelationType)
);
GO

IF OBJECT_ID('dbo.ClassificationScheme', 'U') IS NULL
CREATE TABLE dbo.ClassificationScheme (
    ClassificationSchemeId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ClassificationScheme PRIMARY KEY,
    Code VARCHAR(20) NOT NULL CONSTRAINT UQ_ClassificationScheme_Code UNIQUE,
    Name VARCHAR(80) NOT NULL,
    Version VARCHAR(20) NULL
);
GO

IF OBJECT_ID('dbo.ClassificationCategory', 'U') IS NULL
CREATE TABLE dbo.ClassificationCategory (
    ClassificationCategoryId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ClassificationCategory PRIMARY KEY,
    ClassificationSchemeId INT NOT NULL CONSTRAINT FK_ClassificationCategory_Scheme REFERENCES dbo.ClassificationScheme (ClassificationSchemeId),
    ParentCategoryId INT NULL CONSTRAINT FK_ClassificationCategory_Parent REFERENCES dbo.ClassificationCategory (ClassificationCategoryId),
    Code VARCHAR(20) NOT NULL,
    Name VARCHAR(120) NOT NULL,
    CONSTRAINT UQ_ClassificationCategory UNIQUE (ClassificationSchemeId, Code)
);
GO

IF OBJECT_ID('dbo.ItemClassification', 'U') IS NULL
CREATE TABLE dbo.ItemClassification (
    ItemId INT NOT NULL CONSTRAINT FK_ItemClassification_Item REFERENCES dbo.Item (ItemId),
    ClassificationCategoryId INT NOT NULL CONSTRAINT FK_ItemClassification_Category REFERENCES dbo.ClassificationCategory (ClassificationCategoryId),
    CONSTRAINT PK_ItemClassification PRIMARY KEY (ItemId, ClassificationCategoryId)
);
GO

IF OBJECT_ID('dbo.Lot', 'U') IS NULL
CREATE TABLE dbo.Lot (
    LotId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Lot PRIMARY KEY,
    ItemId INT NOT NULL CONSTRAINT FK_Lot_Item REFERENCES dbo.Item (ItemId),
    LotNumber VARCHAR(40) NOT NULL,
    ManufacturedOn DATE NULL,
    BestBefore DATE NULL,
    CONSTRAINT UQ_Lot UNIQUE (ItemId, LotNumber),
    CONSTRAINT CK_Lot_Dates CHECK (BestBefore IS NULL OR ManufacturedOn IS NULL OR BestBefore >= ManufacturedOn)
);
GO

IF OBJECT_ID('dbo.SerialNumber', 'U') IS NULL
CREATE TABLE dbo.SerialNumber (
    SerialNumberId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SerialNumber PRIMARY KEY,
    ItemId INT NOT NULL CONSTRAINT FK_SerialNumber_Item REFERENCES dbo.Item (ItemId),
    LotId INT NULL CONSTRAINT FK_SerialNumber_Lot REFERENCES dbo.Lot (LotId),
    Serial VARCHAR(60) NOT NULL,
    Status VARCHAR(10) NOT NULL CONSTRAINT DF_SerialNumber_Status DEFAULT 'in-stock' CONSTRAINT CK_SerialNumber_Status CHECK (Status IN ('in-stock', 'sold', 'returned', 'scrapped')),
    SalesInvoiceLineId INT NULL CONSTRAINT FK_SerialNumber_InvoiceLine REFERENCES dbo.SalesInvoiceLine (LineId),
    CONSTRAINT UQ_SerialNumber UNIQUE (ItemId, Serial)
);
GO

-- Stock per location and its ledger
IF OBJECT_ID('dbo.StorageLocation', 'U') IS NULL
CREATE TABLE dbo.StorageLocation (
    StorageLocationId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StorageLocation PRIMARY KEY,
    WarehouseId INT NOT NULL CONSTRAINT FK_StorageLocation_Warehouse REFERENCES dbo.Warehouse (WarehouseId),
    ParentStorageLocationId INT NULL CONSTRAINT FK_StorageLocation_Parent REFERENCES dbo.StorageLocation (StorageLocationId),
    Code VARCHAR(20) NOT NULL,
    Name VARCHAR(80) NOT NULL,
    Latitude DECIMAL(9,6) NULL CONSTRAINT CK_StorageLocation_Latitude CHECK (Latitude IS NULL OR Latitude BETWEEN -90 AND 90),
    Longitude DECIMAL(9,6) NULL CONSTRAINT CK_StorageLocation_Longitude CHECK (Longitude IS NULL OR Longitude BETWEEN -180 AND 180),
    IsActive BIT NOT NULL CONSTRAINT DF_StorageLocation_IsActive DEFAULT 1,
    CONSTRAINT UQ_StorageLocation UNIQUE (WarehouseId, Code)
);
GO

IF OBJECT_ID('dbo.ItemLocationStock', 'U') IS NULL
CREATE TABLE dbo.ItemLocationStock (
    ItemLocationStockId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ItemLocationStock PRIMARY KEY,
    ItemId INT NOT NULL CONSTRAINT FK_ItemLocationStock_Item REFERENCES dbo.Item (ItemId),
    StorageLocationId INT NOT NULL CONSTRAINT FK_ItemLocationStock_Location REFERENCES dbo.StorageLocation (StorageLocationId),
    OnHand INT NOT NULL CONSTRAINT DF_ItemLocationStock_OnHand DEFAULT 0,
    Reserved INT NOT NULL CONSTRAINT DF_ItemLocationStock_Reserved DEFAULT 0,
    MinQuantity INT NOT NULL CONSTRAINT DF_ItemLocationStock_Min DEFAULT 0,
    MaxQuantity INT NULL,
    ReorderQuantity INT NOT NULL CONSTRAINT DF_ItemLocationStock_Reorder DEFAULT 0,
    LeadTimeDays INT NULL,
    CONSTRAINT UQ_ItemLocationStock UNIQUE (ItemId, StorageLocationId),
    CONSTRAINT CK_ItemLocationStock_Available CHECK (OnHand - Reserved >= 0),
    CONSTRAINT CK_ItemLocationStock_Range CHECK (MaxQuantity IS NULL OR MaxQuantity >= MinQuantity)
);
GO

IF OBJECT_ID('dbo.LocationStockLedger', 'U') IS NULL
CREATE TABLE dbo.LocationStockLedger (
    LocationStockLedgerId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LocationStockLedger PRIMARY KEY,
    ItemId INT NOT NULL CONSTRAINT FK_LocationStockLedger_Item REFERENCES dbo.Item (ItemId),
    StorageLocationId INT NOT NULL CONSTRAINT FK_LocationStockLedger_Location REFERENCES dbo.StorageLocation (StorageLocationId),
    LotId INT NULL CONSTRAINT FK_LocationStockLedger_Lot REFERENCES dbo.Lot (LotId),
    Kind VARCHAR(12) NOT NULL CONSTRAINT CK_LocationStockLedger_Kind CHECK (Kind IN ('receipt', 'sale', 'return', 'adjustment', 'transfer-out', 'transfer-in')),
    Quantity INT NOT NULL CONSTRAINT CK_LocationStockLedger_Quantity CHECK (Quantity <> 0),
    TransferGroup UNIQUEIDENTIFIER NULL,
    Reference VARCHAR(60) NULL,
    OccurredOn DATETIME NOT NULL CONSTRAINT DF_LocationStockLedger_OccurredOn DEFAULT GETDATE()
);
GO

-- Packing: a unit holds units and goods; transport: legs, tracking events, endorsements
IF OBJECT_ID('dbo.HandlingUnit', 'U') IS NULL
CREATE TABLE dbo.HandlingUnit (
    HandlingUnitId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HandlingUnit PRIMARY KEY,
    ShipmentId INT NULL CONSTRAINT FK_HandlingUnit_Shipment REFERENCES dbo.Shipment (ShipmentId),
    ParentHandlingUnitId INT NULL CONSTRAINT FK_HandlingUnit_Parent REFERENCES dbo.HandlingUnit (HandlingUnitId),
    UnitType VARCHAR(10) NOT NULL CONSTRAINT CK_HandlingUnit_Type CHECK (UnitType IN ('pallet', 'carton', 'box', 'container')),
    Code VARCHAR(30) NOT NULL CONSTRAINT UQ_HandlingUnit_Code UNIQUE,
    LengthCm DECIMAL(8,2) NULL,
    WidthCm DECIMAL(8,2) NULL,
    HeightCm DECIMAL(8,2) NULL,
    WeightKg DECIMAL(10,3) NULL CONSTRAINT CK_HandlingUnit_Weight CHECK (WeightKg IS NULL OR WeightKg >= 0),
    SealNumber VARCHAR(30) NULL,
    AllowsHazardous BIT NOT NULL CONSTRAINT DF_HandlingUnit_AllowsHazardous DEFAULT 1,
    CONSTRAINT CK_HandlingUnit_Parent CHECK (ParentHandlingUnitId IS NULL OR ParentHandlingUnitId <> HandlingUnitId)
);
GO

IF OBJECT_ID('dbo.HandlingUnitItem', 'U') IS NULL
CREATE TABLE dbo.HandlingUnitItem (
    HandlingUnitItemId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HandlingUnitItem PRIMARY KEY,
    HandlingUnitId INT NOT NULL CONSTRAINT FK_HandlingUnitItem_Unit REFERENCES dbo.HandlingUnit (HandlingUnitId),
    ShipmentItemId INT NOT NULL CONSTRAINT FK_HandlingUnitItem_ShipmentItem REFERENCES dbo.ShipmentItem (ShipmentItemId),
    Quantity INT NOT NULL CONSTRAINT CK_HandlingUnitItem_Quantity CHECK (Quantity > 0),
    IsHazardous BIT NOT NULL CONSTRAINT DF_HandlingUnitItem_IsHazardous DEFAULT 0,
    StorageTempMinC DECIMAL(5,1) NULL,
    StorageTempMaxC DECIMAL(5,1) NULL,
    CONSTRAINT UQ_HandlingUnitItem UNIQUE (HandlingUnitId, ShipmentItemId),
    CONSTRAINT CK_HandlingUnitItem_Temperature CHECK (StorageTempMinC IS NULL OR StorageTempMaxC IS NULL OR StorageTempMinC <= StorageTempMaxC)
);
GO

IF OBJECT_ID('dbo.TransportMode', 'U') IS NULL
CREATE TABLE dbo.TransportMode (
    TransportModeId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TransportMode PRIMARY KEY,
    Code VARCHAR(10) NOT NULL CONSTRAINT UQ_TransportMode_Code UNIQUE,
    Name VARCHAR(40) NOT NULL,
    VehicleLabel VARCHAR(30) NULL
);
GO

IF OBJECT_ID('dbo.Consignment', 'U') IS NULL
CREATE TABLE dbo.Consignment (
    ConsignmentId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Consignment PRIMARY KEY,
    ShipmentId INT NOT NULL CONSTRAINT FK_Consignment_Shipment REFERENCES dbo.Shipment (ShipmentId),
    CarrierPartyId INT NULL CONSTRAINT FK_Consignment_Carrier REFERENCES dbo.Party (PartyId),
    Reference VARCHAR(40) NOT NULL CONSTRAINT UQ_Consignment_Reference UNIQUE,
    Status VARCHAR(12) NOT NULL CONSTRAINT DF_Consignment_Status DEFAULT 'planned' CONSTRAINT CK_Consignment_Status CHECK (Status IN ('planned', 'in-transit', 'delivered', 'cancelled'))
);
GO

IF OBJECT_ID('dbo.TransportLeg', 'U') IS NULL
CREATE TABLE dbo.TransportLeg (
    TransportLegId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TransportLeg PRIMARY KEY,
    ConsignmentId INT NOT NULL CONSTRAINT FK_TransportLeg_Consignment REFERENCES dbo.Consignment (ConsignmentId),
    LegNo INT NOT NULL CONSTRAINT CK_TransportLeg_LegNo CHECK (LegNo > 0),
    TransportModeId INT NOT NULL CONSTRAINT FK_TransportLeg_Mode REFERENCES dbo.TransportMode (TransportModeId),
    FromPlace VARCHAR(100) NOT NULL,
    ToPlace VARCHAR(100) NOT NULL,
    PlannedDeparture DATETIME NOT NULL,
    PlannedArrival DATETIME NOT NULL,
    ActualDeparture DATETIME NULL,
    ActualArrival DATETIME NULL,
    VehicleReference VARCHAR(40) NULL,
    CONSTRAINT UQ_TransportLeg UNIQUE (ConsignmentId, LegNo),
    CONSTRAINT CK_TransportLeg_Planned CHECK (PlannedArrival >= PlannedDeparture),
    CONSTRAINT CK_TransportLeg_Actual CHECK (ActualDeparture IS NULL OR ActualArrival IS NULL OR ActualArrival >= ActualDeparture)
);
GO

IF OBJECT_ID('dbo.TransportEvent', 'U') IS NULL
CREATE TABLE dbo.TransportEvent (
    TransportEventId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TransportEvent PRIMARY KEY,
    ConsignmentId INT NOT NULL CONSTRAINT FK_TransportEvent_Consignment REFERENCES dbo.Consignment (ConsignmentId),
    TransportLegId INT NULL CONSTRAINT FK_TransportEvent_Leg REFERENCES dbo.TransportLeg (TransportLegId),
    EventCode VARCHAR(14) NOT NULL CONSTRAINT CK_TransportEvent_Code CHECK (EventCode IN ('departed', 'arrived', 'delayed', 'delivered', 'customs-hold', 'customs-clear')),
    OccurredOn DATETIME NOT NULL,
    Place VARCHAR(100) NULL,
    Note VARCHAR(300) NULL
);
GO

IF OBJECT_ID('dbo.ConsignmentEndorsement', 'U') IS NULL
CREATE TABLE dbo.ConsignmentEndorsement (
    ConsignmentEndorsementId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ConsignmentEndorsement PRIMARY KEY,
    ConsignmentId INT NOT NULL CONSTRAINT FK_ConsignmentEndorsement_Consignment REFERENCES dbo.Consignment (ConsignmentId),
    PartyId INT NOT NULL CONSTRAINT FK_ConsignmentEndorsement_Party REFERENCES dbo.Party (PartyId),
    SequenceNo INT NOT NULL CONSTRAINT CK_ConsignmentEndorsement_Sequence CHECK (SequenceNo > 0),
    EndorsedOn DATE NOT NULL,
    CONSTRAINT UQ_ConsignmentEndorsement UNIQUE (ConsignmentId, SequenceNo)
);
GO

-- Requested, promised and actual delivery dates, and the history of the promise
IF OBJECT_ID('dbo.DeliverySchedule', 'U') IS NULL
CREATE TABLE dbo.DeliverySchedule (
    DeliveryScheduleId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DeliverySchedule PRIMARY KEY,
    SalesOrderLineId INT NOT NULL CONSTRAINT FK_DeliverySchedule_Line REFERENCES dbo.SalesOrderLine (SalesOrderLineId) CONSTRAINT UQ_DeliverySchedule_Line UNIQUE,
    RequestedFrom DATE NOT NULL,
    RequestedTo DATE NOT NULL,
    PromisedFrom DATE NULL,
    PromisedTo DATE NULL,
    ActualOn DATE NULL,
    DaysLate AS (CASE WHEN PromisedTo IS NULL OR ActualOn IS NULL THEN NULL ELSE DATEDIFF(DAY, PromisedTo, ActualOn) END) PERSISTED,
    CONSTRAINT CK_DeliverySchedule_Requested CHECK (RequestedTo >= RequestedFrom),
    CONSTRAINT CK_DeliverySchedule_Promised CHECK (PromisedFrom IS NULL OR PromisedTo IS NULL OR PromisedTo >= PromisedFrom)
);
GO

IF OBJECT_ID('dbo.DeliveryPromise', 'U') IS NULL
CREATE TABLE dbo.DeliveryPromise (
    DeliveryPromiseId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DeliveryPromise PRIMARY KEY,
    DeliveryScheduleId INT NOT NULL CONSTRAINT FK_DeliveryPromise_Schedule REFERENCES dbo.DeliverySchedule (DeliveryScheduleId),
    PromisedFrom DATE NOT NULL,
    PromisedTo DATE NOT NULL,
    ChangedOn DATETIME NOT NULL CONSTRAINT DF_DeliveryPromise_ChangedOn DEFAULT GETDATE(),
    Reason VARCHAR(200) NULL,
    CONSTRAINT CK_DeliveryPromise_Period CHECK (PromisedTo >= PromisedFrom)
);
GO

-- Contracts and trading terms
IF OBJECT_ID('dbo.DeliveryTerm', 'U') IS NULL
CREATE TABLE dbo.DeliveryTerm (
    DeliveryTermId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DeliveryTerm PRIMARY KEY,
    Incoterm CHAR(3) NOT NULL CONSTRAINT UQ_DeliveryTerm_Incoterm UNIQUE,
    Name VARCHAR(80) NOT NULL,
    NeedsNamedPlace BIT NOT NULL CONSTRAINT DF_DeliveryTerm_NeedsPlace DEFAULT 1
);
GO

IF OBJECT_ID('dbo.Contract', 'U') IS NULL
CREATE TABLE dbo.Contract (
    ContractId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Contract PRIMARY KEY,
    ContractNumber VARCHAR(30) NOT NULL CONSTRAINT UQ_Contract_Number UNIQUE,
    PartyId INT NOT NULL CONSTRAINT FK_Contract_Party REFERENCES dbo.Party (PartyId),
    Title VARCHAR(150) NOT NULL,
    Status VARCHAR(10) NOT NULL CONSTRAINT DF_Contract_Status DEFAULT 'draft' CONSTRAINT CK_Contract_Status CHECK (Status IN ('draft', 'active', 'expired', 'terminated')),
    ValidFrom DATE NOT NULL,
    ValidTo DATE NULL,
    RenewalMode VARCHAR(8) NOT NULL CONSTRAINT DF_Contract_Renewal DEFAULT 'none' CONSTRAINT CK_Contract_Renewal CHECK (RenewalMode IN ('none', 'manual', 'auto')),
    PriceListId INT NULL CONSTRAINT FK_Contract_PriceList REFERENCES dbo.PriceList (PriceListId),
    PaymentTermsId INT NULL CONSTRAINT FK_Contract_Terms REFERENCES dbo.PaymentTerms (PaymentTermsId),
    DeliveryTermId INT NULL CONSTRAINT FK_Contract_DeliveryTerm REFERENCES dbo.DeliveryTerm (DeliveryTermId),
    DeliveryNamedPlace VARCHAR(100) NULL,
    CONSTRAINT CK_Contract_Period CHECK (ValidTo IS NULL OR ValidTo >= ValidFrom)
);
GO

IF OBJECT_ID('dbo.ContractCondition', 'U') IS NULL
CREATE TABLE dbo.ContractCondition (
    ContractConditionId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ContractCondition PRIMARY KEY,
    ContractId INT NOT NULL CONSTRAINT FK_ContractCondition_Contract REFERENCES dbo.Contract (ContractId),
    ConditionCode VARCHAR(20) NOT NULL,
    Description VARCHAR(300) NOT NULL,
    CONSTRAINT UQ_ContractCondition UNIQUE (ContractId, ConditionCode)
);
GO

-- Statements, reminders and remittance advice built from the ledger
IF OBJECT_ID('dbo.CustomerStatement', 'U') IS NULL
CREATE TABLE dbo.CustomerStatement (
    CustomerStatementId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerStatement PRIMARY KEY,
    CustomerId INT NOT NULL CONSTRAINT FK_CustomerStatement_Customer REFERENCES dbo.Customer (CustomerId),
    PeriodFrom DATE NOT NULL,
    PeriodTo DATE NOT NULL,
    OpeningBalance DECIMAL(12,2) NOT NULL,
    ClosingBalance DECIMAL(12,2) NOT NULL,
    IssuedOn DATETIME NOT NULL CONSTRAINT DF_CustomerStatement_IssuedOn DEFAULT GETDATE(),
    CONSTRAINT CK_CustomerStatement_Period CHECK (PeriodTo >= PeriodFrom),
    CONSTRAINT UQ_CustomerStatement UNIQUE (CustomerId, PeriodFrom, PeriodTo)
);
GO

IF OBJECT_ID('dbo.CustomerStatementLine', 'U') IS NULL
CREATE TABLE dbo.CustomerStatementLine (
    CustomerStatementLineId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerStatementLine PRIMARY KEY,
    CustomerStatementId INT NOT NULL CONSTRAINT FK_CustomerStatementLine_Statement REFERENCES dbo.CustomerStatement (CustomerStatementId),
    LineNumber INT NOT NULL CONSTRAINT CK_CustomerStatementLine_LineNumber CHECK (LineNumber > 0),
    LineDate DATE NOT NULL,
    Description VARCHAR(200) NOT NULL,
    SalesInvoiceId INT NULL CONSTRAINT FK_CustomerStatementLine_Invoice REFERENCES dbo.SalesInvoice (SalesInvoiceId),
    DebitAmount DECIMAL(12,2) NOT NULL CONSTRAINT DF_CustomerStatementLine_Debit DEFAULT 0 CONSTRAINT CK_CustomerStatementLine_Debit CHECK (DebitAmount >= 0),
    CreditAmount DECIMAL(12,2) NOT NULL CONSTRAINT DF_CustomerStatementLine_Credit DEFAULT 0 CONSTRAINT CK_CustomerStatementLine_Credit CHECK (CreditAmount >= 0),
    RunningBalance DECIMAL(12,2) NOT NULL,
    CONSTRAINT UQ_CustomerStatementLine UNIQUE (CustomerStatementId, LineNumber)
);
GO

IF OBJECT_ID('dbo.PaymentReminder', 'U') IS NULL
CREATE TABLE dbo.PaymentReminder (
    PaymentReminderId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentReminder PRIMARY KEY,
    SalesInvoiceId INT NOT NULL CONSTRAINT FK_PaymentReminder_Invoice REFERENCES dbo.SalesInvoice (SalesInvoiceId),
    ReminderLevel TINYINT NOT NULL CONSTRAINT CK_PaymentReminder_Level CHECK (ReminderLevel BETWEEN 1 AND 3),
    DaysOverdue INT NOT NULL CONSTRAINT CK_PaymentReminder_Days CHECK (DaysOverdue >= 0),
    FeeAmount DECIMAL(12,2) NOT NULL CONSTRAINT DF_PaymentReminder_Fee DEFAULT 0 CONSTRAINT CK_PaymentReminder_Fee CHECK (FeeAmount >= 0),
    IssuedOn DATETIME NOT NULL CONSTRAINT DF_PaymentReminder_IssuedOn DEFAULT GETDATE(),
    SentOn DATETIME NULL,
    CONSTRAINT UQ_PaymentReminder UNIQUE (SalesInvoiceId, ReminderLevel)
);
GO

IF OBJECT_ID('dbo.RemittanceAdvice', 'U') IS NULL
CREATE TABLE dbo.RemittanceAdvice (
    RemittanceAdviceId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RemittanceAdvice PRIMARY KEY,
    PayerPartyId INT NOT NULL CONSTRAINT FK_RemittanceAdvice_Payer REFERENCES dbo.Party (PartyId),
    PaymentReference VARCHAR(60) NOT NULL CONSTRAINT UQ_RemittanceAdvice_Reference UNIQUE,
    PaidOn DATE NOT NULL,
    CurrencyCode CHAR(3) NOT NULL CONSTRAINT DF_RemittanceAdvice_Currency DEFAULT 'USD',
    TotalAmount DECIMAL(12,2) NOT NULL CONSTRAINT CK_RemittanceAdvice_Total CHECK (TotalAmount > 0)
);
GO

IF OBJECT_ID('dbo.RemittanceAdviceLine', 'U') IS NULL
CREATE TABLE dbo.RemittanceAdviceLine (
    RemittanceAdviceLineId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RemittanceAdviceLine PRIMARY KEY,
    RemittanceAdviceId INT NOT NULL CONSTRAINT FK_RemittanceAdviceLine_Advice REFERENCES dbo.RemittanceAdvice (RemittanceAdviceId),
    SalesInvoiceId INT NOT NULL CONSTRAINT FK_RemittanceAdviceLine_Invoice REFERENCES dbo.SalesInvoice (SalesInvoiceId),
    AmountPaid DECIMAL(12,2) NOT NULL CONSTRAINT CK_RemittanceAdviceLine_Amount CHECK (AmountPaid > 0),
    CONSTRAINT UQ_RemittanceAdviceLine UNIQUE (RemittanceAdviceId, SalesInvoiceId)
);
GO

-- File integrity and who was sent a copy of a document
IF OBJECT_ID('dbo.AssetFileHash', 'U') IS NULL
CREATE TABLE dbo.AssetFileHash (
    AssetFileHashId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AssetFileHash PRIMARY KEY,
    AssetId INT NOT NULL CONSTRAINT FK_AssetFileHash_Asset REFERENCES dbo.Asset (AssetId),
    Algorithm VARCHAR(10) NOT NULL CONSTRAINT DF_AssetFileHash_Algorithm DEFAULT 'sha256' CONSTRAINT CK_AssetFileHash_Algorithm CHECK (Algorithm IN ('sha256', 'sha512')),
    HashValue VARCHAR(128) NOT NULL,
    ExternalUri VARCHAR(500) NULL,
    ComputedOn DATETIME NOT NULL CONSTRAINT DF_AssetFileHash_ComputedOn DEFAULT GETDATE(),
    VerifiedOn DATETIME NULL,
    VerifyStatus VARCHAR(10) NOT NULL CONSTRAINT DF_AssetFileHash_Status DEFAULT 'unverified' CONSTRAINT CK_AssetFileHash_Status CHECK (VerifyStatus IN ('unverified', 'ok', 'mismatch', 'missing')),
    CONSTRAINT UQ_AssetFileHash UNIQUE (AssetId, Algorithm)
);
GO

IF OBJECT_ID('dbo.DocumentDistribution', 'U') IS NULL
CREATE TABLE dbo.DocumentDistribution (
    DocumentDistributionId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DocumentDistribution PRIMARY KEY,
    DocumentType VARCHAR(20) NOT NULL CONSTRAINT CK_DocumentDistribution_Type CHECK (DocumentType IN ('SalesOrder', 'SalesInvoice', 'CreditNote', 'CustomerStatement', 'PaymentReminder', 'Quotation')),
    DocumentId INT NOT NULL,
    PartyId INT NOT NULL CONSTRAINT FK_DocumentDistribution_Party REFERENCES dbo.Party (PartyId),
    Channel VARCHAR(10) NOT NULL CONSTRAINT CK_DocumentDistribution_Channel CHECK (Channel IN ('email', 'post', 'portal', 'edi')),
    SentOn DATETIME NULL,
    Status VARCHAR(10) NOT NULL CONSTRAINT DF_DocumentDistribution_Status DEFAULT 'queued' CONSTRAINT CK_DocumentDistribution_Status CHECK (Status IN ('queued', 'sent', 'failed', 'read'))
);
GO

-- Indexes: the foreign keys that no unique constraint already leads with, and the lookups the rules use
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PartyCommunication_Preferred' AND object_id = OBJECT_ID('dbo.PartyCommunication'))
    CREATE UNIQUE INDEX UX_PartyCommunication_Preferred ON dbo.PartyCommunication (PartyId, Channel) WHERE IsPreferred = 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PartyBankAccount_Default' AND object_id = OBJECT_ID('dbo.PartyBankAccount'))
    CREATE UNIQUE INDEX UX_PartyBankAccount_Default ON dbo.PartyBankAccount (PartyId) WHERE IsDefault = 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Quotation_Accepted' AND object_id = OBJECT_ID('dbo.Quotation'))
    CREATE UNIQUE INDEX UX_Quotation_Accepted ON dbo.Quotation (RequestForQuotationId) WHERE Status = 'accepted';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_TaxCategory_Open' AND object_id = OBJECT_ID('dbo.TaxCategory'))
    CREATE UNIQUE INDEX UX_TaxCategory_Open ON dbo.TaxCategory (Code) WHERE ValidTo IS NULL;
GO

DECLARE @ix TABLE (IndexName SYSNAME, TableName SYSNAME, Cols NVARCHAR(200));
INSERT INTO @ix (IndexName, TableName, Cols) VALUES
    ('IX_SalesOrderTaxSubtotal_Category', 'SalesOrderTaxSubtotal', 'TaxCategoryId'),
    ('IX_AllowanceCharge_Order', 'AllowanceCharge', 'SalesOrderId'),
    ('IX_AllowanceCharge_Line', 'AllowanceCharge', 'SalesOrderLineId'),
    ('IX_PriceListLine_Item', 'PriceListLine', 'ItemId, MinQuantity'),
    ('IX_PriceList_Currency', 'PriceList', 'CurrencyId'),
    ('IX_ExchangeRate_To', 'ExchangeRate', 'ToCurrencyId, RateDate'),
    ('IX_Party_Customer', 'Party', 'CustomerId'),
    ('IX_Party_LegalName', 'Party', 'LegalName'),
    ('IX_PartyIdentification_Party', 'PartyIdentification', 'PartyId'),
    ('IX_PartyTaxRegistration_Party', 'PartyTaxRegistration', 'PartyId'),
    ('IX_PartyContact_Party', 'PartyContact', 'PartyId'),
    ('IX_PartyCommunication_Party', 'PartyCommunication', 'PartyId'),
    ('IX_PartyCommunication_Contact', 'PartyCommunication', 'PartyContactId'),
    ('IX_PartyBankAccount_Party', 'PartyBankAccount', 'PartyId'),
    ('IX_PaymentMeans_Order', 'PaymentMeans', 'SalesOrderId'),
    ('IX_PaymentMeans_Terms', 'PaymentMeans', 'PaymentTermsId'),
    ('IX_PaymentMeans_Account', 'PaymentMeans', 'PayeeBankAccountId'),
    ('IX_PaymentInstalment_Due', 'PaymentInstalment', 'DueDate'),
    ('IX_GoodsReceipt_Order', 'GoodsReceipt', 'SalesOrderId'),
    ('IX_GoodsReceiptLine_Line', 'GoodsReceiptLine', 'SalesOrderLineId'),
    ('IX_CreditNote_Invoice', 'CreditNote', 'SalesInvoiceId'),
    ('IX_CreditNoteLine_Note', 'CreditNoteLine', 'CreditNoteId'),
    ('IX_CreditNoteLine_InvoiceLine', 'CreditNoteLine', 'SalesInvoiceLineId'),
    ('IX_DocumentReference_To', 'DocumentReference', 'ToDocumentType, ToDocumentId'),
    ('IX_DocumentResponse_Order', 'DocumentResponse', 'SalesOrderId'),
    ('IX_DocumentResponse_Party', 'DocumentResponse', 'RespondedByPartyId'),
    ('IX_LineResponse_Line', 'LineResponse', 'SalesOrderLineId'),
    ('IX_RequestForQuotationLine_Item', 'RequestForQuotationLine', 'ItemId'),
    ('IX_Quotation_Supplier', 'Quotation', 'SupplierPartyId'),
    ('IX_QuotationLine_RequestLine', 'QuotationLine', 'RequestForQuotationLineId'),
    ('IX_ItemIdentifier_Item', 'ItemIdentifier', 'ItemId'),
    ('IX_ItemIdentifier_Party', 'ItemIdentifier', 'PartyId'),
    ('IX_ItemRelation_Related', 'ItemRelation', 'RelatedItemId'),
    ('IX_ClassificationCategory_Parent', 'ClassificationCategory', 'ParentCategoryId'),
    ('IX_ItemClassification_Category', 'ItemClassification', 'ClassificationCategoryId'),
    ('IX_Lot_BestBefore', 'Lot', 'BestBefore'),
    ('IX_SerialNumber_Lot', 'SerialNumber', 'LotId'),
    ('IX_SerialNumber_InvoiceLine', 'SerialNumber', 'SalesInvoiceLineId'),
    ('IX_StorageLocation_Parent', 'StorageLocation', 'ParentStorageLocationId'),
    ('IX_ItemLocationStock_Location', 'ItemLocationStock', 'StorageLocationId'),
    ('IX_LocationStockLedger_Item', 'LocationStockLedger', 'ItemId, StorageLocationId, OccurredOn'),
    ('IX_LocationStockLedger_Location', 'LocationStockLedger', 'StorageLocationId'),
    ('IX_LocationStockLedger_Lot', 'LocationStockLedger', 'LotId'),
    ('IX_LocationStockLedger_Transfer', 'LocationStockLedger', 'TransferGroup'),
    ('IX_HandlingUnit_Shipment', 'HandlingUnit', 'ShipmentId'),
    ('IX_HandlingUnit_Parent', 'HandlingUnit', 'ParentHandlingUnitId'),
    ('IX_HandlingUnitItem_ShipmentItem', 'HandlingUnitItem', 'ShipmentItemId'),
    ('IX_Consignment_Shipment', 'Consignment', 'ShipmentId'),
    ('IX_Consignment_Carrier', 'Consignment', 'CarrierPartyId'),
    ('IX_TransportLeg_Mode', 'TransportLeg', 'TransportModeId'),
    ('IX_TransportEvent_Consignment', 'TransportEvent', 'ConsignmentId, OccurredOn'),
    ('IX_TransportEvent_Leg', 'TransportEvent', 'TransportLegId'),
    ('IX_ConsignmentEndorsement_Party', 'ConsignmentEndorsement', 'PartyId'),
    ('IX_DeliveryPromise_Schedule', 'DeliveryPromise', 'DeliveryScheduleId, ChangedOn'),
    ('IX_Contract_Party', 'Contract', 'PartyId'),
    ('IX_Contract_PriceList', 'Contract', 'PriceListId'),
    ('IX_Contract_Terms', 'Contract', 'PaymentTermsId'),
    ('IX_Contract_DeliveryTerm', 'Contract', 'DeliveryTermId'),
    ('IX_Contract_ValidTo', 'Contract', 'ValidTo'),
    ('IX_CustomerStatementLine_Invoice', 'CustomerStatementLine', 'SalesInvoiceId'),
    ('IX_RemittanceAdvice_Payer', 'RemittanceAdvice', 'PayerPartyId'),
    ('IX_RemittanceAdviceLine_Invoice', 'RemittanceAdviceLine', 'SalesInvoiceId'),
    ('IX_DocumentDistribution_Document', 'DocumentDistribution', 'DocumentType, DocumentId'),
    ('IX_DocumentDistribution_Party', 'DocumentDistribution', 'PartyId');

DECLARE @sql NVARCHAR(MAX) = (
    SELECT STRING_AGG(CONVERT(NVARCHAR(MAX), 'CREATE INDEX ' + QUOTENAME(i.IndexName) + ' ON dbo.' + QUOTENAME(i.TableName) + ' (' + i.Cols + ');'), CHAR(10))
    FROM @ix i
    WHERE NOT EXISTS (SELECT 1 FROM sys.indexes x WHERE x.name = i.IndexName AND x.object_id = OBJECT_ID('dbo.' + i.TableName)));
IF @sql IS NOT NULL EXEC sys.sp_executesql @sql;
GO
