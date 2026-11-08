-- The Asset table of assets.sql for PostgreSQL (AssetTables=Asset:StorageKey:FileName:MimeType:SizeBytes:PublicUrl)
CREATE TABLE IF NOT EXISTS "Asset" (
    "AssetId" SERIAL PRIMARY KEY,
    "FileName" VARCHAR(200) NOT NULL,
    "MimeType" VARCHAR(100) NOT NULL,
    "SizeBytes" BIGINT NOT NULL,
    "StorageKey" VARCHAR(300) NOT NULL,
    "PublicUrl" VARCHAR(500) NULL,
    "UploadedOn" TIMESTAMP NOT NULL DEFAULT NOW(),
    "Caption" VARCHAR(200) NULL,
    CONSTRAINT "UX_Asset_StorageKey" UNIQUE ("StorageKey")
);
