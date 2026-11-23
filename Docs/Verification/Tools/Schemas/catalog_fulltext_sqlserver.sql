-- Full-text indexes for FacetFullText=true on SQL Server: the keyword of the catalog routes is then a CONTAINS condition (each word as a prefix) on the product's text columns and on the attribute values.
-- sqlcmd -S <server> -E -C -I -b -d InvoiceSystem -i catalog_fulltext_sqlserver.sql      Can be run again. It does nothing, and says so, on an instance without the Full-Text Search feature
-- (SELECT FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') answers 0): add the feature to the SQL Server setup first. The tables of the schemas cat and dbo are indexed when they exist (catalog_sqlserver.sql, InvoiceSystemTableShapes.sql).
SET NOCOUNT ON;
IF FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') <> 1
BEGIN
    PRINT 'Full-Text Search is not installed on this SQL Server instance: no full-text index was created.';
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'CatalogSearch')
        EXEC ('CREATE FULLTEXT CATALOG CatalogSearch');
    DECLARE @sql NVARCHAR(MAX) = N'';
    SELECT @sql += N'CREATE FULLTEXT INDEX ON ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N' (' + c.columns + N') KEY INDEX ' + QUOTENAME(k.name) + N' ON CatalogSearch WITH CHANGE_TRACKING AUTO;' + CHAR(10)
    FROM sys.tables t
    JOIN sys.schemas s ON s.schema_id = t.schema_id
    CROSS APPLY (SELECT TOP 1 i.name FROM sys.indexes i WHERE i.object_id = t.object_id AND i.is_primary_key = 1) k
    CROSS APPLY (SELECT STUFF((SELECT N', ' + QUOTENAME(x.name) FROM sys.columns x JOIN sys.types ty ON ty.user_type_id = x.user_type_id
                               WHERE x.object_id = t.object_id AND ty.name IN ('varchar', 'nvarchar', 'char', 'nchar') AND x.max_length BETWEEN 1 AND 800 AND (t.name = 'Product' OR x.name = 'Value')
                               FOR XML PATH('')), 1, 2, N'') AS columns) c
    WHERE s.name IN ('cat', 'dbo') AND t.name IN ('Product', 'ProductAttribute') AND c.columns IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes f WHERE f.object_id = t.object_id);
    IF LEN(@sql) > 0
        EXEC sys.sp_executesql @sql;
    PRINT 'Full-text indexes are in place.';
END
