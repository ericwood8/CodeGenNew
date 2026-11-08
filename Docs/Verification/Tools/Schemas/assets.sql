-- A table that points at a stored file (Asset, with a download address) and one without the address (Document), for AssetTables=Asset,Document
CREATE TABLE Asset (
    AssetId INTEGER PRIMARY KEY AUTOINCREMENT,
    FileName VARCHAR(200) NOT NULL,
    MimeType VARCHAR(100) NOT NULL,
    SizeBytes BIGINT NOT NULL,
    StorageKey VARCHAR(300) NOT NULL,
    PublicUrl VARCHAR(500) NULL,
    UploadedOn DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    Caption VARCHAR(200) NULL
);
CREATE UNIQUE INDEX UX_Asset_StorageKey ON Asset (StorageKey);

CREATE TABLE Document (
    DocumentId INTEGER PRIMARY KEY AUTOINCREMENT,
    Name VARCHAR(120) NOT NULL,
    ContentType VARCHAR(80) NULL,
    FileSize INTEGER NOT NULL,
    FileKey VARCHAR(100) NOT NULL,
    Notes VARCHAR(300) NULL
);
CREATE UNIQUE INDEX UX_Document_FileKey ON Document (FileKey);
