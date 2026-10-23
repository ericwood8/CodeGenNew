using System.Text;
using System.Text.RegularExpressions;

namespace CodeGenNew.Core;

/// <summary> A create script written from what the database reported about its tables: the tables with their columns (type, identity, computed expression, null rule, default), primary keys, unique and ordinary indexes and
/// foreign keys with their delete rule, in the dialect the model was read from (SQL Server, PostgreSQL or MySQL). It lets a test container be filled without a script of the project's own. The model does not carry check
/// constraints, filtered or partial indexes, included columns, triggers, views or routines, so those are not in it: a container made from this script accepts a few values the real database refuses. </summary>
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
        script.AppendLine($"-- Create script of {database.DatabaseName}, written from its catalog: tables, keys, indexes and foreign keys (no checks, filtered indexes, triggers or routines).");

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

        return script.ToString();
    }

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
