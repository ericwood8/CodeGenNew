using System.Text;

namespace CodeGenNew.Core;

/// <summary> The script that keeps the tables of <c>ArchiveTables</c> small, in the dialect of the source database: it creates the archive table when the database has none, then a procedure that moves the rows older than the
/// days to keep to the archive table (or deletes them) in batches, so the log never holds a lock for long. SQL Server gets an Agent job, PostgreSQL a procedure for pg_cron, MySQL an event, SQLite a plain script.
/// The scheduling part is commented out. Written for the project to review and run; the application never runs it. </summary>
public static class ArchiveScript
{
    public static string ProcedureName(ArchiveJob job) => "Archive" + job.Table.TableName;

    /// <summary> A row that another table's foreign key refers to stays where it is: the delete would be refused and roll the whole batch back. It moves in a later run, once the rows that refer to it are gone.
    /// A table that refers to itself is not covered. </summary>
    private static string NotReferenced(DialectInfo di, ArchiveJob job)
    {
        var o = new StringBuilder();
        string source = di.Quote(job.Table.DbTableName);
        foreach (var fk in job.Table.ChildForeignKeys.Where(f => f.ReferencingColumns.Count > 0 && f.ReferencingColumns.Count == f.ReferencedColumns.Count
            && !f.ReferencingTable.EqualsIgnoreCase(job.Table.TableName) && !f.ReferencingTable.EqualsIgnoreCase(job.ArchiveName)))
        {
            string match = string.Join(" AND ", fk.ReferencingColumns.Select((column, i) =>
                $"{di.Quote(ReferencingAlias)}.{di.Quote(column)} = {source}.{di.Quote(job.Table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(fk.ReferencedColumns[i]))?.DbName ?? fk.ReferencedColumns[i])}"));
            o.Append($" AND NOT EXISTS (SELECT 1 FROM {di.QuoteTable(fk.ReferencingSchema, fk.ReferencingDbTable)} AS {di.Quote(ReferencingAlias)} WHERE {match})");
        }
        return o.ToString();
    }

    private const string ReferencingAlias = "refs";

    public static string Text(DatabaseModel database, ProjectSettings project)
    {
        var resolution = ArchivePlan.Resolve(database, project);
        var o = new StringBuilder();
        foreach (var job in resolution.Jobs.OrderBy(j => j.Table.TableName, StringComparer.OrdinalIgnoreCase))
            o.Append(For(database, job, project));
        return o.ToString();
    }

    public static string For(DatabaseModel database, ArchiveJob job, ProjectSettings project)
    {
        int batchSize = project.ArchiveBatchSize;
        var runAt = project.ArchiveRunTime ?? TimeOnly.MinValue;
        var di = DialectInfo.For(database.Dialect);
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');
        string what = job.Delete ? $"deletes the rows of {job.Table.TableName}" : $"moves the rows of {job.Table.TableName} to {job.ArchiveName}";
        string finished = job.Queue is null ? "" : $", finished ones only ({job.Queue.Status.DbName} = '{QueueStatus.Done}')";
        L($"-- {what} that are older than {job.Days} days by {job.Age.DbName}{finished}{(database.Dialect == SqlDialect.Sqlite ? "" : $", {batchSize} rows at a time")}");
        L("-- Review it, run it yourself and schedule it; the application never runs it.");
        string source = di.QuoteTable(job.Table.SchemaName, job.Table.DbTableName);
        string archive = di.QuoteTable(job.Table.SchemaName, job.ArchiveName);
        string age = di.Quote(job.Age.DbName);
        string statusTest = (job.Queue is null ? "" : $" AND {di.Quote(job.Queue.Status.DbName)} = '{QueueStatus.Done}'") + NotReferenced(di, job);
        var columns = job.Copied.Select(c => di.Quote(c.DbName)).ToList();
        string columnList = string.Join(", ", columns);
        string insertColumns = columnList + (job.Archive is null || job.ArchivedOn is not null ? ", " + di.Quote(ArchivePlan.ArchivedOnName) : "");
        string key = string.Join(", ", job.Table.PrimaryKeyColumns.Select(c => di.Quote(c.DbName)));
        string procedure = di.Quote(ProcedureName(job));
        string stamp = database.Dialect switch { SqlDialect.SqlServer => "datetime2", SqlDialect.MySql => "datetime", _ => "timestamp" };
        bool withArchivedOn = job.Archive is null || job.ArchivedOn is not null;

        if (!job.Delete && job.Archive is null)
        {
            var lines = job.Copied.Select(c => $"    {di.Quote(c.DbName)} {c.SqlTypeDeclaration} {(c.IsNullable ? "NULL" : "NOT NULL")}").ToList();
            lines.Add($"    {di.Quote(ArchivePlan.ArchivedOnName)} {stamp} NOT NULL");
            lines.Add($"    PRIMARY KEY ({key})");
            string create = $"CREATE TABLE {(database.Dialect == SqlDialect.SqlServer ? "" : "IF NOT EXISTS ")}{archive} (\n{string.Join(",\n", lines)}\n);\n";
            if (database.Dialect == SqlDialect.SqlServer)
                L($"IF OBJECT_ID(N'{job.Table.SchemaName}.{job.ArchiveName}', N'U') IS NULL\n{create.TrimEnd('\n')}\nGO");
            else
                o.Append(create);
        }

        switch (database.Dialect)
        {
            case SqlDialect.SqlServer:
                L($"CREATE OR ALTER PROCEDURE {di.QuoteTable(job.Table.SchemaName, ProcedureName(job))} @Days int = {job.Days}, @BatchSize int = {batchSize}");
                L("AS");
                L("BEGIN");
                L("    SET NOCOUNT ON;");
                L("    DECLARE @cutoff datetime2 = DATEADD(DAY, -@Days, GETDATE());");
                L("    DECLARE @moved int = @BatchSize;");
                L("    WHILE @moved >= @BatchSize");
                L("    BEGIN");
                if (job.Delete)
                {
                    L($"        DELETE TOP (@BatchSize) FROM {source} WHERE {age} < @cutoff{statusTest};");
                }
                else
                {
                    L($"        DELETE TOP (@BatchSize) FROM {source}");
                    L($"        OUTPUT {string.Join(", ", job.Copied.Select(c => "deleted." + di.Quote(c.DbName)))}{(withArchivedOn ? ", GETDATE()" : "")} INTO {archive} ({insertColumns})");
                    L($"        WHERE {age} < @cutoff{statusTest};");
                }
                L("        SET @moved = @@ROWCOUNT;");
                L("    END");
                L("END");
                L("GO");
                L();
                L("/* Schedule it with an Agent job: remove these comment marks, set the time and run it in msdb.");
                L("USE msdb;");
                L($"EXEC dbo.sp_add_job @job_name = N'{ProcedureName(job)}';");
                L($"EXEC dbo.sp_add_jobstep @job_name = N'{ProcedureName(job)}', @step_name = N'Run', @subsystem = N'TSQL', @database_name = N'{database.DatabaseName}', @command = N'EXEC {job.Table.SchemaName}.{ProcedureName(job)};';");
                L($"EXEC dbo.sp_add_schedule @schedule_name = N'{ProcedureName(job)} daily', @freq_type = 4, @freq_interval = 1, @active_start_time = {runAt:HHmm}00;");
                L($"EXEC dbo.sp_attach_schedule @job_name = N'{ProcedureName(job)}', @schedule_name = N'{ProcedureName(job)} daily';");
                L($"EXEC dbo.sp_add_jobserver @job_name = N'{ProcedureName(job)}';");
                L("*/");
                break;
            case SqlDialect.PostgreSql:
                L($"CREATE OR REPLACE PROCEDURE {di.QuoteTable(job.Table.SchemaName, ProcedureName(job))}(p_days integer DEFAULT {job.Days}, p_batch integer DEFAULT {batchSize})");
                L("LANGUAGE plpgsql AS $$");
                L("DECLARE");
                L("    v_moved integer;");
                L("BEGIN");
                L("    LOOP");
                string pick = $"SELECT ctid FROM {source} WHERE {age} < LOCALTIMESTAMP - make_interval(days => p_days){statusTest} ORDER BY {age} LIMIT p_batch FOR UPDATE SKIP LOCKED";
                if (job.Delete)
                {
                    L($"        DELETE FROM {source} WHERE ctid IN ({pick});");
                }
                else
                {
                    L("        WITH gone AS (");
                    L($"            DELETE FROM {source} WHERE ctid IN ({pick})");
                    L($"            RETURNING {columnList}");
                    L("        )");
                    L($"        INSERT INTO {archive} ({insertColumns}) SELECT {columnList}{(withArchivedOn ? ", LOCALTIMESTAMP" : "")} FROM gone;");
                }
                L("        GET DIAGNOSTICS v_moved = ROW_COUNT;");
                L("        COMMIT;");
                L("        EXIT WHEN v_moved < p_batch;");
                L("    END LOOP;");
                L("END $$;");
                L();
                L($"-- Schedule it with pg_cron, for example: SELECT cron.schedule('{ProcedureName(job)}', '{runAt.Minute} {runAt.Hour} * * *', 'CALL {di.QuoteTable(job.Table.SchemaName, ProcedureName(job)).Replace("'", "''")}()');");
                break;
            case SqlDialect.MySql:
                L($"DROP PROCEDURE IF EXISTS {procedure};");
                L("DELIMITER $$");
                L($"CREATE PROCEDURE {procedure}(IN p_days INT, IN p_batch INT)");
                L("BEGIN");
                L("    DECLARE v_cutoff DATETIME DEFAULT NOW() - INTERVAL p_days DAY;");
                L("    DECLARE v_moved INT DEFAULT p_batch;");
                L("    WHILE v_moved >= p_batch DO");
                L("        START TRANSACTION;");
                string where = $"WHERE {age} < v_cutoff{statusTest} ORDER BY {key} LIMIT p_batch";
                if (!job.Delete)
                    L($"        INSERT INTO {archive} ({insertColumns}) SELECT {columnList}{(withArchivedOn ? ", NOW()" : "")} FROM {source} {where};");
                L($"        DELETE FROM {source} {where};");
                L("        SET v_moved = ROW_COUNT();");
                L("        COMMIT;");
                L("    END WHILE;");
                L("END$$");
                L("DELIMITER ;");
                L();
                L("-- Schedule it with an event (the server needs SET GLOBAL event_scheduler = ON):");
                L($"-- CREATE EVENT {di.Quote(ProcedureName(job) + "Daily")} ON SCHEDULE EVERY 1 DAY STARTS (CURRENT_DATE + INTERVAL 1 DAY + INTERVAL {runAt.Hour} HOUR + INTERVAL {runAt.Minute} MINUTE) DO CALL {procedure}({job.Days}, {batchSize});");
                break;
            default:
                L("BEGIN;");
                string cutoff = $"datetime('now', 'localtime', '-{job.Days} days')";
                if (!job.Delete)
                    L($"INSERT INTO {archive} ({insertColumns}) SELECT {columnList}{(withArchivedOn ? ", datetime('now', 'localtime')" : "")} FROM {source} WHERE {age} < {cutoff}{statusTest};");
                L($"DELETE FROM {source} WHERE {age} < {cutoff}{statusTest};");
                L("COMMIT;");
                L("-- SQLite has no scheduler: run this file from the operating system's (cron, Task Scheduler).");
                break;
        }
        L();
        return o.ToString();
    }
}
