-- A post for the MySQL copy of the InvoiceSystem sample. Settings: ContentTables=Post, OnlyTables=Post
-- The same table as Schemas/content.sql (and the SQL Server copy); the unique slug is the rule the server's numbering keeps.
CREATE TABLE IF NOT EXISTS Post (
    PostId int NOT NULL AUTO_INCREMENT PRIMARY KEY,
    Title varchar(150) NOT NULL,
    Slug varchar(150) NOT NULL,
    Body text NOT NULL,
    Status varchar(12) NOT NULL DEFAULT 'draft',
    PublishedOn datetime NULL,
    CreatedOn datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY UX_Post_Slug (Slug)
);
