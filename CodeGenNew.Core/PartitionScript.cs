using System.Text;

namespace CodeGenNew.Core;

/// <summary> The script that splits the tables of <c>PartitionTables</c> into one partition for each month of their date column, in the dialect of the source database: it converts the table (SQL Server and MySQL in place,
/// PostgreSQL by copying into a partitioned table and leaving the old one for you to drop) and creates a procedure that adds the coming months and drops the months older than <c>PartitionKeepMonths</c>. The scheduling part is
/// commented out. Written for the project to review and run; the application never runs it. </summary>
public static class PartitionScript
{
    public static string Text(DatabaseModel database, ProjectSettings project)
    {
        var o = new StringBuilder();
        foreach (var job in PartitionPlan.Resolve(database, project).Jobs.OrderBy(j => j.Table.TableName, StringComparer.OrdinalIgnoreCase))
            o.Append(For(database, job, project));
        return o.ToString();
    }

    public static string For(DatabaseModel database, PartitionJob job, ProjectSettings project)
    {
        var di = DialectInfo.For(database.Dialect);
        string Q(string name) => di.Quote(name);
        string columns(IEnumerable<string> names) => string.Join(", ", names.Select(Q));
        string table = job.Table.DbTableName, schema = job.Table.SchemaName;
        string loose = job.LooseUnique.Count == 0 ? "" : $" The unique index {string.Join(", ", job.LooseUnique)} lacks {job.Date.DbName}, which no partitioned table can enforce, so it becomes a plain index: the same row can be stored twice if two requests arrive together.";
        string header = $"-- {table}: one partition for each month of {job.Date.DbName}. The key becomes ({columns(job.Key.Select(k => k.DbName))}, {Q(job.Date.DbName)}).{loose}\n" +
            "-- Review it, run it yourself and schedule the procedure; the application never runs it. Running it again leaves a table that is partitioned already alone.\n";
        var values = new (string, string)[]
        {
            ("§TABLEQ", di.QuoteTable(schema, table)), ("§TABLEOBJ", schema + "." + table), ("§TABLEOLDQ", di.QuoteTable(schema, table + "_unpartitioned")), ("§TABLEOLD", table + "_unpartitioned"),
            ("§TABLE", table), ("§SCHEMA", schema), ("§DATEQ", Q(job.Date.DbName)), ("§DATETYPE", job.Date.SqlTypeDeclaration), ("§KEYDATE", columns(job.Key.Select(k => k.DbName).Append(job.Date.DbName))),
            ("§PROCQ", di.QuoteTable(schema, job.ProcedureName)), ("§PROCBARE", Q(job.ProcedureName)), ("§CONVQ", Q(job.ProcedureName + "Convert")), ("§PROC", job.ProcedureName),
            ("§AHEAD", project.PartitionMonthsAhead.ToString()), ("§KEEP", project.PartitionKeepMonths.ToString()),
            ("§FUNCTION", $"pf_{table}_Month"), ("§SCHEME", $"ps_{table}_Month"), ("§DEFAULTQ", di.QuoteTable(schema, table + "_default"))
        };
        string body = database.Dialect switch
        {
            SqlDialect.SqlServer => SqlServer(job, di),
            SqlDialect.PostgreSql => PostgreSql(job, di),
            _ => MySql(job, di)
        };
        return header + TemplateText.FillAll(body, values) + "\n";
    }

    private static string SqlServer(PartitionJob job, DialectInfo di)
    {
        var drop = new StringBuilder();
        var create = new StringBuilder();
        foreach (var index in job.Indexes)
        {
            string name = di.Quote(index.Name);
            drop.Append($"    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'{index.Name}' AND parent_object_id = OBJECT_ID(N'§TABLEOBJ'))\n        ALTER TABLE §TABLEQ DROP CONSTRAINT {name};\n    ELSE\n        DROP INDEX IF EXISTS {name} ON §TABLEQ;\n");
            bool unique = index.IsUnique && !job.LooseUnique.Contains(index.Name);
            create.Append($"    CREATE {(unique ? "UNIQUE " : "")}INDEX {name} ON §TABLEQ ({string.Join(", ", index.Columns.Select(di.Quote))}) ON §SCHEME (§DATEQ);\n");
        }
        return SqlServerText.Replace("§DROPINDEXES", drop.ToString()).Replace("§CREATEINDEXES", create.ToString().TrimEnd('\n'));
    }

    private static string PostgreSql(PartitionJob job, DialectInfo di)
    {
        var indexes = new StringBuilder();
        foreach (var index in job.Indexes)
        {
            bool unique = index.IsUnique && !job.LooseUnique.Contains(index.Name);
            indexes.Append($"    CREATE {(unique ? "UNIQUE " : "")}INDEX {di.Quote(index.Name)} ON §TABLEQ ({string.Join(", ", index.Columns.Select(di.Quote))});\n");
        }
        string overriding = job.Table.Columns.Any(c => c.IsIdentity) ? " OVERRIDING SYSTEM VALUE" : "";
        return PostgreSqlText.Replace("§INDEXES", indexes.ToString()).Replace("§OVERRIDING", overriding);
    }

    private static string MySql(PartitionJob job, DialectInfo di)
    {
        var changes = new StringBuilder();
        foreach (string name in job.LooseUnique)
        {
            var index = job.Indexes.First(i => i.Name == name);
            changes.Append($", DROP INDEX {di.Quote(name)}, ADD INDEX {di.Quote(name)} ({string.Join(", ", index.Columns.Select(di.Quote))})");
        }
        return MySqlText.Replace("§INDEXCHANGES", changes.ToString());
    }

    private const string SqlServerText = """
IF NOT EXISTS (SELECT 1 FROM sys.partition_functions WHERE name = N'§FUNCTION')
BEGIN
    DECLARE @this date = DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1);
    DECLARE @first date = ISNULL((SELECT DATEFROMPARTS(YEAR(MIN(§DATEQ)), MONTH(MIN(§DATEQ)), 1) FROM §TABLEQ), @this);
    DECLARE @until date = DATEADD(MONTH, §AHEAD, @this);
    DECLARE @values nvarchar(max) = N'';
    WHILE @first <= @until
    BEGIN
        SET @values += CASE WHEN @values = N'' THEN N'' ELSE N', ' END + N'''' + CONVERT(nvarchar(10), @first, 23) + N'''';
        SET @first = DATEADD(MONTH, 1, @first);
    END;
    EXEC (N'CREATE PARTITION FUNCTION §FUNCTION (§DATETYPE) AS RANGE RIGHT FOR VALUES (' + @values + N')');
    EXEC (N'CREATE PARTITION SCHEME §SCHEME AS PARTITION §FUNCTION ALL TO ([PRIMARY])');
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'§TABLEOBJ') AND index_id <= 1 AND data_space_id = (SELECT data_space_id FROM sys.partition_schemes WHERE name = N'§SCHEME'))
    PRINT N'§TABLE is partitioned already.';
ELSE
BEGIN
§DROPINDEXES    DECLARE @pk sysname = (SELECT name FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'§TABLEOBJ') AND type = N'PK');
    DECLARE @sql nvarchar(max) = N'ALTER TABLE §TABLEQ DROP CONSTRAINT ' + QUOTENAME(@pk);
    EXEC (@sql);
    SET @sql = N'ALTER TABLE §TABLEQ ADD CONSTRAINT ' + QUOTENAME(@pk) + N' PRIMARY KEY CLUSTERED (§KEYDATE) ON §SCHEME (§DATEQ)';
    EXEC (@sql);
§CREATEINDEXES
END
GO

CREATE OR ALTER PROCEDURE §PROCQ @MonthsAhead int = §AHEAD, @KeepMonths int = §KEEP
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @this date = DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1);
    DECLARE @boundary date = (SELECT MAX(CONVERT(date, CONVERT(datetime2, v.value))) FROM sys.partition_range_values v JOIN sys.partition_functions f ON f.function_id = v.function_id WHERE f.name = N'§FUNCTION');
    WHILE @boundary < DATEADD(MONTH, @MonthsAhead, @this)
    BEGIN
        SET @boundary = DATEADD(MONTH, 1, @boundary);
        ALTER PARTITION SCHEME §SCHEME NEXT USED [PRIMARY];
        ALTER PARTITION FUNCTION §FUNCTION() SPLIT RANGE (@boundary);
    END;
    IF @KeepMonths > 0
    BEGIN
        DECLARE @cutoff date = DATEADD(MONTH, -@KeepMonths, @this);
        DECLARE @oldest date, @following date;
        WHILE 1 = 1
        BEGIN
            SET @oldest = (SELECT MIN(CONVERT(date, CONVERT(datetime2, v.value))) FROM sys.partition_range_values v JOIN sys.partition_functions f ON f.function_id = v.function_id WHERE f.name = N'§FUNCTION');
            SET @following = (SELECT MIN(CONVERT(date, CONVERT(datetime2, v.value))) FROM sys.partition_range_values v JOIN sys.partition_functions f ON f.function_id = v.function_id WHERE f.name = N'§FUNCTION' AND CONVERT(date, CONVERT(datetime2, v.value)) > @oldest);
            IF @following IS NULL OR @following > @cutoff
                BREAK;
            TRUNCATE TABLE §TABLEQ WITH (PARTITIONS (2));
            ALTER PARTITION FUNCTION §FUNCTION() MERGE RANGE (@oldest);
        END;
    END;
END
GO

/* Schedule it on the first of each month with an Agent job: remove these comment marks, set the time and run it in msdb.
USE msdb;
EXEC dbo.sp_add_job @job_name = N'§PROC';
EXEC dbo.sp_add_jobstep @job_name = N'§PROC', @step_name = N'Run', @subsystem = N'TSQL', @command = N'EXEC §SCHEMA.§PROC;';
EXEC dbo.sp_add_schedule @schedule_name = N'§PROC monthly', @freq_type = 16, @freq_interval = 1, @active_start_time = 010000;
EXEC dbo.sp_attach_schedule @job_name = N'§PROC', @schedule_name = N'§PROC monthly';
EXEC dbo.sp_add_jobserver @job_name = N'§PROC';
*/
""";

    private const string PostgreSqlText = """
CREATE OR REPLACE PROCEDURE §PROCQ(p_ahead integer DEFAULT §AHEAD, p_keep integer DEFAULT §KEEP, p_from date DEFAULT NULL)
LANGUAGE plpgsql AS $$
DECLARE
    v_month date := date_trunc('month', COALESCE(p_from, CURRENT_DATE))::date;
    v_last date := (date_trunc('month', CURRENT_DATE) + make_interval(months => p_ahead))::date;
    v_cutoff date;
    v_name text;
BEGIN
    WHILE v_month <= v_last LOOP
        EXECUTE format('CREATE TABLE IF NOT EXISTS %I.%I PARTITION OF %I.%I FOR VALUES FROM (%L) TO (%L)', '§SCHEMA', '§TABLE_' || to_char(v_month, 'YYYY_MM'), '§SCHEMA', '§TABLE', v_month, (v_month + interval '1 month')::date);
        v_month := (v_month + interval '1 month')::date;
    END LOOP;
    IF p_keep > 0 THEN
        v_cutoff := (date_trunc('month', CURRENT_DATE) - make_interval(months => p_keep))::date;
        FOR v_name IN
            SELECT c.relname FROM pg_inherits i JOIN pg_class c ON c.oid = i.inhrelid JOIN pg_class p ON p.oid = i.inhparent JOIN pg_namespace n ON n.oid = p.relnamespace
            WHERE n.nspname = '§SCHEMA' AND p.relname = '§TABLE' AND c.relname ~ ('^' || '§TABLE' || '_[0-9]{4}_[0-9]{2}$') AND to_date(right(c.relname, 7), 'YYYY_MM') < v_cutoff
        LOOP
            EXECUTE format('DROP TABLE %I.%I', '§SCHEMA', v_name);
        END LOOP;
    END IF;
END $$;

DO $$
DECLARE
    r record;
    v_from date;
BEGIN
    IF EXISTS (SELECT 1 FROM pg_partitioned_table WHERE partrelid = '§TABLEQ'::regclass) THEN
        RAISE NOTICE '§TABLE is partitioned already.';
        RETURN;
    END IF;
    ALTER TABLE §TABLEQ RENAME TO "§TABLEOLD";
    FOR r IN SELECT indexname FROM pg_indexes WHERE schemaname = '§SCHEMA' AND tablename = '§TABLEOLD' LOOP
        EXECUTE format('ALTER INDEX %I.%I RENAME TO %I', '§SCHEMA', r.indexname, r.indexname || '_old');
    END LOOP;
    CREATE TABLE §TABLEQ (LIKE §TABLEOLDQ INCLUDING DEFAULTS INCLUDING IDENTITY INCLUDING GENERATED INCLUDING CONSTRAINTS INCLUDING COMMENTS) PARTITION BY RANGE (§DATEQ);
    ALTER TABLE §TABLEQ ADD PRIMARY KEY (§KEYDATE);
    CREATE TABLE §DEFAULTQ PARTITION OF §TABLEQ DEFAULT;
§INDEXES    SELECT MIN(§DATEQ)::date INTO v_from FROM §TABLEOLDQ;
    CALL §PROCQ(p_from => v_from);
    INSERT INTO §TABLEQ§OVERRIDING SELECT * FROM §TABLEOLDQ;
    FOR r IN SELECT a.attname FROM pg_attribute a WHERE a.attrelid = '§TABLEOLDQ'::regclass AND a.attnum > 0 AND NOT a.attisdropped AND a.attidentity = '' AND pg_get_serial_sequence('§TABLEOLDQ', a.attname) IS NOT NULL LOOP
        EXECUTE format('ALTER SEQUENCE %s OWNED BY %s.%I', pg_get_serial_sequence('§TABLEOLDQ', r.attname), '§TABLEQ', r.attname);
    END LOOP;
    FOR r IN SELECT a.attname FROM pg_attribute a WHERE a.attrelid = '§TABLEQ'::regclass AND a.attnum > 0 AND NOT a.attisdropped AND a.attidentity <> '' LOOP
        EXECUTE format('SELECT setval(pg_get_serial_sequence(%L, %L), COALESCE((SELECT MAX(%I) FROM %s), 0) + 1, false)', '§TABLEQ', r.attname, r.attname, '§TABLEQ');
    END LOOP;
END $$;

-- The old table is left for you: check the copy, then DROP TABLE §TABLEOLDQ;
-- Rows of a month with no partition go to §DEFAULTQ. A new month cannot be added while that table holds rows of it, so keep the procedure scheduled ahead of the months.
-- Schedule it with pg_cron, for example: SELECT cron.schedule('§PROC', '0 1 1 * *', 'CALL §PROCQ()');
""";

    private const string MySqlText = """
DROP PROCEDURE IF EXISTS §PROCQ;
DROP PROCEDURE IF EXISTS §CONVQ;
DELIMITER $$
CREATE PROCEDURE §PROCQ(IN p_ahead INT, IN p_keep INT)
BEGIN
    DECLARE v_month DATE;
    DECLARE v_last DATE DEFAULT DATE_ADD(DATE_FORMAT(CURDATE(), '%Y-%m-01'), INTERVAL p_ahead MONTH);
    DECLARE v_cutoff VARCHAR(8);
    DECLARE v_name VARCHAR(64);
    SELECT STR_TO_DATE(CONCAT(SUBSTRING(MAX(PARTITION_NAME), 2), '01'), '%Y%m%d') INTO v_month
        FROM information_schema.PARTITIONS WHERE TABLE_SCHEMA = DATABASE() AND LOWER(TABLE_NAME) = LOWER('§TABLE') AND PARTITION_NAME LIKE 'p______';
    WHILE v_month < v_last DO
        SET v_month = DATE_ADD(v_month, INTERVAL 1 MONTH);
        SET @partition_sql = CONCAT('ALTER TABLE §TABLEQ REORGANIZE PARTITION pmax INTO (PARTITION p', DATE_FORMAT(v_month, '%Y%m'), ' VALUES LESS THAN (''', DATE_FORMAT(DATE_ADD(v_month, INTERVAL 1 MONTH), '%Y-%m-%d'), '''), PARTITION pmax VALUES LESS THAN (MAXVALUE))');
        PREPARE partition_statement FROM @partition_sql;
        EXECUTE partition_statement;
        DEALLOCATE PREPARE partition_statement;
    END WHILE;
    IF p_keep > 0 THEN
        SET v_cutoff = CONCAT('p', DATE_FORMAT(DATE_SUB(DATE_FORMAT(CURDATE(), '%Y-%m-01'), INTERVAL p_keep MONTH), '%Y%m'));
        SET v_name = (SELECT MIN(PARTITION_NAME) FROM information_schema.PARTITIONS WHERE TABLE_SCHEMA = DATABASE() AND LOWER(TABLE_NAME) = LOWER('§TABLE') AND PARTITION_NAME LIKE 'p______' AND PARTITION_NAME < v_cutoff);
        WHILE v_name IS NOT NULL DO
            SET @partition_sql = CONCAT('ALTER TABLE §TABLEQ DROP PARTITION ', v_name);
            PREPARE partition_statement FROM @partition_sql;
            EXECUTE partition_statement;
            DEALLOCATE PREPARE partition_statement;
            SET v_name = (SELECT MIN(PARTITION_NAME) FROM information_schema.PARTITIONS WHERE TABLE_SCHEMA = DATABASE() AND LOWER(TABLE_NAME) = LOWER('§TABLE') AND PARTITION_NAME LIKE 'p______' AND PARTITION_NAME < v_cutoff);
        END WHILE;
    END IF;
END$$

CREATE PROCEDURE §CONVQ(IN p_ahead INT)
BEGIN
    DECLARE v_month DATE;
    DECLARE v_last DATE DEFAULT DATE_ADD(DATE_FORMAT(CURDATE(), '%Y-%m-01'), INTERVAL p_ahead MONTH);
    DECLARE v_list TEXT DEFAULT '';
    IF (SELECT COUNT(*) FROM information_schema.PARTITIONS WHERE TABLE_SCHEMA = DATABASE() AND LOWER(TABLE_NAME) = LOWER('§TABLE') AND PARTITION_NAME IS NOT NULL) = 0 THEN
        SET v_month = DATE_FORMAT(COALESCE((SELECT MIN(§DATEQ) FROM §TABLEQ), CURDATE()), '%Y-%m-01');
        WHILE v_month <= v_last DO
            SET v_list = CONCAT(v_list, 'PARTITION p', DATE_FORMAT(v_month, '%Y%m'), ' VALUES LESS THAN (''', DATE_FORMAT(DATE_ADD(v_month, INTERVAL 1 MONTH), '%Y-%m-%d'), '''), ');
            SET v_month = DATE_ADD(v_month, INTERVAL 1 MONTH);
        END WHILE;
        SET @partition_sql = CONCAT('ALTER TABLE §TABLEQ DROP PRIMARY KEY, ADD PRIMARY KEY (§KEYDATE)§INDEXCHANGES PARTITION BY RANGE COLUMNS (§DATEQ) (', v_list, 'PARTITION pmax VALUES LESS THAN (MAXVALUE))');
        PREPARE partition_statement FROM @partition_sql;
        EXECUTE partition_statement;
        DEALLOCATE PREPARE partition_statement;
    END IF;
END$$
DELIMITER ;
CALL §CONVQ(§AHEAD);
DROP PROCEDURE §CONVQ;

-- Schedule it on the first of each month with an event (the server needs SET GLOBAL event_scheduler = ON):
-- CREATE EVENT `§PROC Monthly` ON SCHEDULE EVERY 1 MONTH STARTS (DATE_FORMAT(CURDATE() + INTERVAL 1 MONTH, '%Y-%m-01') + INTERVAL 1 HOUR) DO CALL §PROCQ(§AHEAD, §KEEP);
""";
}
