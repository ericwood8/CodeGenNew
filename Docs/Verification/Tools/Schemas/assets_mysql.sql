-- The Asset table of assets.sql for MySQL (AssetTables=Asset:StorageKey:FileName:MimeType:SizeBytes:PublicUrl)
CREATE TABLE IF NOT EXISTS asset (
    AssetId INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    FileName VARCHAR(200) NOT NULL,
    MimeType VARCHAR(100) NOT NULL,
    SizeBytes BIGINT NOT NULL,
    StorageKey VARCHAR(300) NOT NULL,
    PublicUrl VARCHAR(500) NULL,
    UploadedOn DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    Caption VARCHAR(200) NULL,
    UNIQUE KEY UX_Asset_StorageKey (StorageKey)
);
