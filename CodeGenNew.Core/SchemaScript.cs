using System.Text;
using System.Text.RegularExpressions;

namespace CodeGenNew.Core;

/// <summary> A create script written from what the database reported about its tables: the tables with their columns (type, identity, computed expression, null rule, default), primary keys, unique and ordinary indexes and
/// foreign keys with their delete rule, check constraints, filtered or partial indexes with their included columns, views and triggers, in the dialect the model was read from (SQL Server, PostgreSQL or MySQL). It lets a
/// test container be filled without a script of the project's own. Stored routines are not in it, so a container made from this script lacks the routines a trigger or view might call. SQL Server batches are
/// separated by GO and MySQL trigger bodies by DELIMITER, so run the script with sqlcmd or the mysql client, which read both. </summary>
public static partial class SchemaScript
{
    /// <summary> Whether a script can be written for the dialect (SQLite has no container to fill). </summary>
    public static bool Supports(SqlDialect dialect) => dialect is SqlDialect.SqlServer or SqlDialect.PostgreSql or SqlDialect.MySql;

    public static string For(DatabaseModel database)
    {
        if (!Supports(database.Dialect))
            return "";

        var dialect = database.Dialect;
        var tables = database.Tables.OrderBy(t => t.SchemaName, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.DatabaseTableName ?? t.TableName, StringComparer.OrdinalIgnoreCase).ToList();
        var script = new StringBuilder();
        script.AppendLine($"-- Create script of {database.DatabaseName}, written from its catalog: tables, keys, indexes, foreign keys, checks, views and triggers (no routines).");

        if (dialect != SqlDialect.MySql)
            foreach (string schema in tables.Select(t => t.SchemaName).Where(s => !s.EqualsIgnoreCase("dbo") && !s.EqualsIgnoreCase("public")).Distinct(StringComparer.OrdinalIgnoreCase))
                script.AppendLine(dialect == SqlDialect.PostgreSql ? $"CREATE SCHEMA IF NOT EXISTS \"{schema}\";" : $"CREATE SCHEMA [{schema}];");

        if (dialect == SqlDialect.PostgreSql)
            foreach (var type in tables.SelectMany(t => t.Columns).Where(c => c.DbEnumType is not null && c.Choices is { Count: > 0 }).GroupBy(c => c.DbEnumType!, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
                script.AppendLine($"CREATE TYPE {type.Key} AS ENUM ({string.Join(", ", type.First().Choices!.Select(v => "'" + v.Replace("'", "''") + "'"))});");

        foreach (var table in tables)
        {
            script.AppendLine();
            script.AppendLine($"CREATE TABLE {TableName(table, dialect)} (");
            var lines = table.Columns.OrderBy(c => c.OrdinalPosition).Select(c => "    " + ColumnText(c, dialect)).ToList();
            if (table.PrimaryKeyColumns.Count > 0)
                lines.Add($"    CONSTRAINT {Quote("PK_" + (table.DatabaseTableName ?? table.TableName), dialect)} PRIMARY KEY ({string.Join(", ", table.PrimaryKeyColumns.Select(c => Quote(c.DbName, dialect)))})");
            script.AppendLine(string.Join("," + Environment.NewLine, lines));
            script.AppendLine(");");
        }

        var usedIndexNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
        {
            var key = table.PrimaryKeyColumns.Select(c => c.DbName).ToList();
            foreach (var index in table.Indexes)
            {
                if (index.IsUnique && index.Columns.SequenceEqual(key, StringComparer.OrdinalIgnoreCase))
                    continue;   // the primary key's own index
                string name = index.Name;
                for (int n = 2; !usedIndexNames.Add(dialect == SqlDialect.MySql ? table.TableName + "." + name : name); n++)
                    name = index.Name + "_" + n;   // PostgreSQL index names are per schema
                script.AppendLine($"CREATE {(index.IsUnique ? "UNIQUE " : "")}INDEX {Quote(name, dialect)} ON {TableName(table, dialect)} ({string.Join(", ", index.Columns.Select(c => Quote(c, dialect)))});");
            }

            foreach (var index in table.PartialIndexes)
            {
                string name = index.Name;
                for (int n = 2; !usedIndexNames.Add(dialect == SqlDialect.MySql ? table.TableName + "." + name : name); n++)
                    name = index.Name + "_" + n;
                string included = index.IncludedColumns.Count == 0 ? "" : $" INCLUDE ({string.Join(", ", index.IncludedColumns.Select(c => Quote(c, dialect)))})";
                script.AppendLine($"CREATE {(index.IsUnique ? "UNIQUE " : "")}INDEX {Quote(name, dialect)} ON {TableName(table, dialect)} ({string.Join(", ", index.Columns.Select(c => Quote(c, dialect)))}){included} WHERE {index.Filter};");
            }
        }

        foreach (var table in tables)
            foreach (var foreignKey in table.ForeignKeys.OrderBy(f => f.ConstraintName, StringComparer.OrdinalIgnoreCase))
            {
                var parent = database.Tables.FirstOrDefault(t => t.SchemaName.EqualsIgnoreCase(foreignKey.ReferencedSchema) && (t.DatabaseTableName ?? t.TableName).EqualsIgnoreCase(foreignKey.ReferencedDbTable));
                if (parent is null)
                    continue;   // refers to a table the model does not hold
                script.AppendLine($"ALTER TABLE {TableName(table, dialect)} ADD CONSTRAINT {Quote(foreignKey.ConstraintName, dialect)} FOREIGN KEY ({string.Join(", ", foreignKey.ReferencingDbColumns.Select(c => Quote(c, dialect)))}) "
                    + $"REFERENCES {TableName(parent, dialect)} ({string.Join(", ", foreignKey.ReferencedDbColumns.Select(c => Quote(c, dialect)))}){DeleteClause(foreignKey.DeleteRule)};");
            }

        foreach (var table in tables)
            foreach (var check in table.CheckConstraints)
                script.AppendLine($"ALTER TABLE {TableName(table, dialect)} ADD CONSTRAINT {Quote(check.Name, dialect)} {CheckDefinition(check.Definition, dialect)};");

        foreach (var view in InDependencyOrder(database.Views))
            AppendStatement(script, view.Definition, dialect, delimited: false);

        foreach (var table in tables)
            foreach (var trigger in table.Triggers)
            {
                AppendStatement(script, trigger.Definition, dialect, delimited: true);
                if (trigger.IsDisabled)
                    script.AppendLine(dialect switch
                    {
                        SqlDialect.SqlServer => $"DISABLE TRIGGER {Quote(table.SchemaName, dialect)}.{Quote(trigger.Name, dialect)} ON {TableName(table, dialect)};",
                        SqlDialect.PostgreSql => $"ALTER TABLE {TableName(table, dialect)} DISABLE TRIGGER {Quote(trigger.Name, dialect)};",
                        _ => ""
                    });
            }

        return script.ToString();
    }

    /// <summary> One create statement. SQL Server wants a view or trigger first in its batch, so GO goes before and after; a MySQL trigger body holds semicolons, so it is written between DELIMITER lines. </summary>
    private static void AppendStatement(StringBuilder script, string definition, SqlDialect dialect, bool delimited)
    {
        script.AppendLine();
        switch (dialect)
        {
            case SqlDialect.SqlServer:
                script.AppendLine("GO").AppendLine(definition.Trim()).AppendLine("GO");
                break;
            case SqlDialect.MySql when delimited:
                script.AppendLine("DELIMITER $$").AppendLine(definition.Trim() + "$$").AppendLine("DELIMITER ;");
                break;
            default:
                script.AppendLine(definition.Trim().TrimEnd(';') + ";");
                break;
        }
    }

    /// <summary> The views with each after the views its text names, so a view over a view is created second; views that name each other, or neither, stay in name order. </summary>
    private static List<ViewModel> InDependencyOrder(IReadOnlyList<ViewModel> views)
    {
        var remaining = views.OrderBy(v => v.SchemaName, StringComparer.OrdinalIgnoreCase).ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var ordered = new List<ViewModel>();
        while (remaining.Count > 0)
        {
            var next = remaining.FirstOrDefault(v => !remaining.Any(other => other != v && Names(v.Definition, other.Name))) ?? remaining[0];
            ordered.Add(next);
            remaining.Remove(next);
        }
        return ordered;
    }

    private static bool Names(string text, string name) => Regex.IsMatch(text, @"(?<![\w])" + Regex.Escape(name) + @"(?![\w])", RegexOptions.IgnoreCase);

    /// <summary> The check as a statement can hold it. MySQL reports the text of a check the way it shows a string, with each quote escaped (<c>\'open\'</c>) and a character set introducer
    /// in front of each literal (<c>_cp850'open'</c>); neither is valid when the text is run as SQL, and the introducer names a character set the container may not have. </summary>
    public static string CheckDefinition(string definition, SqlDialect dialect) => dialect == SqlDialect.MySql
        ? CharacterSetIntroducer().Replace(definition.Replace("\\'", "'"), "")
        : definition;

    [GeneratedRegex("(?<![A-Za-z0-9_`])_[A-Za-z0-9]+(?=')")]
    private static partial Regex CharacterSetIntroducer();

    /// <summary> The table as the script names it: schema and name, but for MySQL the name alone, since its schema is the database it was read from, which a container does not have. </summary>
    private static string TableName(TableModel table, SqlDialect dialect) => dialect == SqlDialect.MySql ? Quote(table.DatabaseTableName ?? table.TableName, dialect) : Quote(table.SchemaName, dialect) + "." + Quote(table.DatabaseTableName ?? table.TableName, dialect);

    private static string Quote(string name, SqlDialect dialect) => dialect switch
    {
        SqlDialect.SqlServer => "[" + name.Replace("]", "]]") + "]",
        SqlDialect.PostgreSql => "\"" + name.Replace("\"", "\"\"") + "\"",
        _ => "`" + name.Replace("`", "``") + "`"
    };

    private static string DeleteClause(ForeignKeyDeleteRule rule) => rule switch
    {
        ForeignKeyDeleteRule.Cascade => " ON DELETE CASCADE",
        ForeignKeyDeleteRule.SetNull => " ON DELETE SET NULL",
        ForeignKeyDeleteRule.SetDefault => " ON DELETE SET DEFAULT",
        ForeignKeyDeleteRule.Restrict => " ON DELETE RESTRICT",
        _ => ""
    };

    private static string ColumnText(ColumnModel column, SqlDialect dialect)
    {
        var text = new StringBuilder(Quote(column.DbName, dialect));
        if (column.IsComputed && !string.IsNullOrWhiteSpace(column.ComputedDefinition))
        {
            if (dialect == SqlDialect.SqlServer)
                return text.Append(" AS ").Append(column.ComputedDefinition).ToString();
            return text.Append(' ').Append(column.SqlTypeDeclaration).Append(" GENERATED ALWAYS AS (").Append(column.ComputedDefinition).Append(") STORED").ToString();
        }

        text.Append(' ').Append(column.SqlTypeDeclaration);
        if (column.IsIdentity)
            text.Append(dialect switch
            {
                SqlDialect.SqlServer => $" IDENTITY({column.IdentitySeed ?? 1},{column.IdentityIncrement ?? 1})",
                SqlDialect.PostgreSql => " GENERATED BY DEFAULT AS IDENTITY",
                _ => " AUTO_INCREMENT"
            });
        text.Append(column.IsNullable ? " NULL" : " NOT NULL");
        if (!column.IsIdentity && DefaultText(column, dialect) is { } value)
            text.Append(" DEFAULT ").Append(value);
        return text.ToString();
    }

    /// <summary> The default as the database wrote it. SQL Server and PostgreSQL keep it as it was read; MySQL reports a literal without quotes, so a text or date literal is quoted and a function call is put in parentheses. </summary>
    private static string? DefaultText(ColumnModel column, SqlDialect dialect)
    {
        string? raw = column.DatabaseDefaultSql?.Trim();
        if (string.IsNullOrEmpty(raw) || raw.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            return null;
        if (dialect != SqlDialect.MySql)
            return raw;

        if (raw.StartsWith("current_timestamp", StringComparison.OrdinalIgnoreCase) && column.IsDateColumn)
            return raw;
        if (raw.StartsWith('(') || Function().IsMatch(raw))
            return raw.StartsWith('(') ? raw : "(" + raw + ")";
        if (column.IsStringColumn || column.IsDateColumn)
            return "'" + raw.Replace("\\", "\\\\").Replace("'", "''") + "'";
        return raw;
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z_0-9]*\(.*\)$", RegexOptions.Singleline)]
    private static partial Regex Function();
}
