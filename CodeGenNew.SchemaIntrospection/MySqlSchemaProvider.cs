using System.Data.Common;
using CodeGenNew.Connections;
using CodeGenNew.Core;
using MySqlConnector;

namespace CodeGenNew.SchemaIntrospection;

/// <summary> Builds a fully-populated TableModel for one table by reading live schema metadata from MySQL (information_schema: TABLES, COLUMNS,
/// STATISTICS for keys and unique indexes, KEY_COLUMN_USAGE for foreign keys). Everything is read-only. In MySQL a database and a schema are the same
/// thing: SchemaName is the database name, and a table is listed under the database the connection names. </summary>
public class MySqlSchemaProvider : SchemaProviderBase
{
    private readonly ConnectionRequest _connectionRequest;

    public MySqlSchemaProvider(ConnectionRequest connectionRequest, string specialLogicColumnsConfigPath, NamingStyle naming = NamingStyle.AsIs, IReadOnlyCollection<string>? acronyms = null) : base(specialLogicColumnsConfigPath, naming, acronyms)
    {
        _connectionRequest = connectionRequest;
    }

    protected override SqlDialect Dialect => SqlDialect.MySql;

    protected override async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = _connectionRequest.CreateMySqlConnection();
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    protected override string Quote(string name) => "`" + name.Replace("`", "``") + "`";

    // =============== type vocabulary ===============

    /// <summary> A MySQL column type in the SQL Server vocabulary the generator classifies by, plus the MySQL spelling of the declaration (for procedure parameters).
    /// <paramref name="columnType"/> is information_schema's COLUMN_TYPE ("int unsigned", "varchar(50)", "tinyint(1)", "enum('a','b')"). Length -1 means unbounded
    /// (the text and blob families, json). A type with no equivalent (geometry ...) maps to sql_variant, which the generators treat as an unsupported "object" column. </summary>
    public static (string SqlTypeName, int MaxLength, int Precision, int Scale, string Declaration) MapType(
        string dataType, string columnType, long? characterMaximumLength, int? numericPrecision, int? numericScale)
    {
        string t = dataType.ToLowerInvariant();
        string ct = columnType.ToLowerInvariant();
        bool unsigned = ct.Contains("unsigned");
        int length = characterMaximumLength is > 0 and < int.MaxValue ? (int)characterMaximumLength.Value : -1;
        switch (t)
        {
            case "tinyint":
                if (ct.StartsWith("tinyint(1)")) return ("bit", 0, 0, 0, "tinyint(1)"); // MySQL's boolean
                return unsigned ? ("tinyint", 0, 0, 0, "tinyint unsigned") : ("smallint", 0, 0, 0, "tinyint"); // a signed tinyint (-128..127) does not fit SQL Server's byte
            case "smallint": return unsigned ? ("int", 0, 0, 0, "smallint unsigned") : ("smallint", 0, 0, 0, "smallint");
            case "mediumint": return ("int", 0, 0, 0, unsigned ? "mediumint unsigned" : "mediumint");
            case "int": case "integer": return unsigned ? ("bigint", 0, 0, 0, "int unsigned") : ("int", 0, 0, 0, "int");
            case "bigint": return unsigned ? ("decimal", 0, 20, 0, "bigint unsigned") : ("bigint", 0, 0, 0, "bigint");
            case "bit": return numericPrecision == 1 ? ("bit", 0, 0, 0, "bit(1)") : ("varbinary", numericPrecision is > 0 ? (numericPrecision.Value + 7) / 8 : 8, 0, 0, ct);
            case "decimal": case "numeric":
            {
                int precision = numericPrecision ?? 10;
                int scale = numericScale ?? 0;
                return ("decimal", 0, precision, scale, $"decimal({precision},{scale})");
            }
            case "float": return ("real", 0, 0, 0, "float");
            case "double": case "real": return ("float", 0, 0, 0, "double");
            case "year": return ("smallint", 0, 0, 0, "year");
            case "date": return ("date", 0, 0, 0, "date");
            case "datetime": return ("datetime2", 0, 0, 0, ct);
            case "timestamp": return ("datetime2", 0, 0, 0, ct);
            case "time": return ("time", 0, 0, 0, ct);
            case "char": return ("char", length, 0, 0, $"char({length})");
            case "varchar": return ("varchar", length, 0, 0, $"varchar({length})");
            case "tinytext": case "text": case "mediumtext": case "longtext": return ("varchar", -1, 0, 0, t);
            // a value from a list: a string as long as the longest listed value (the form shows an enum as a drop-down); a set holds several values at once, so it stays free text
            case "enum": { int longest = ParseEnumValues(columnType)?.Max(v => v.Length) ?? 255; return ("varchar", longest, 0, 0, $"varchar({longest})"); }
            case "set": return ("varchar", 255, 0, 0, "varchar(255)");
            case "json": return ("varchar", -1, 0, 0, "json");
            case "binary": case "varbinary": return ("varbinary", length, 0, 0, $"{t}({length})");
            case "tinyblob": case "blob": case "mediumblob": case "longblob": return ("varbinary", -1, 0, 0, t);
            default: return ("sql_variant", 0, 0, 0, ct);
        }
    }

    // A default as information_schema gives it (a literal without quotes, or an expression when EXTRA says DEFAULT_GENERATED) in the form the C# default-value
    // resolver expects a SQL Server one: functions by their SQL Server name, a string literal quoted.
    /// <summary> The values of an <c>enum('a','b')</c> column type (a quote inside a value is doubled: <c>'it''s'</c>); null for any other type, including <c>set</c>
    /// (a set holds several values at once, which a drop-down cannot express). </summary>
    public static List<string>? ParseEnumValues(string columnType)
    {
        if (!columnType.StartsWith("enum(", StringComparison.OrdinalIgnoreCase) || !columnType.EndsWith(')'))
            return null;

        var values = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inside = false;
        string body = columnType[5..^1];
        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            if (!inside)
            {
                if (c == '\'')
                    inside = true;
            }
            else if (c == '\'' && i + 1 < body.Length && body[i + 1] == '\'')
            {
                current.Append('\'');
                i++;
            }
            else if (c == '\'')
            {
                values.Add(current.ToString());
                current.Clear();
                inside = false;
            }
            else
            {
                current.Append(c);
            }
        }
        return values.Count > 0 ? values : null;
    }

    public static string? NormalizeDefault(string? columnDefault, string extra, bool isString)
    {
        if (columnDefault is null)
            return null;

        string d = columnDefault.Trim();
        if (extra.Contains("DEFAULT_GENERATED", StringComparison.OrdinalIgnoreCase))
        {
            string lower = d.ToLowerInvariant();
            if (lower.StartsWith("current_timestamp") || lower.StartsWith("now(") || lower.StartsWith("localtimestamp")) return "getdate()";
            if (lower.StartsWith("utc_timestamp")) return "getutcdate()";
            if (lower.StartsWith("uuid(") || lower.StartsWith("(uuid(")) return "newid()";
            return d;
        }

        return isString ? "'" + d.Replace("'", "''") + "'" : d;
    }

    private static PrimaryKeyShape ClassifyPrimaryKeyShape(int pkColumnCount, string? pkDataType, string? pkColumnType) => pkColumnCount switch
    {
        0 => PrimaryKeyShape.None,
        > 1 => PrimaryKeyShape.Composite,
        _ => MapType(pkDataType ?? "", pkColumnType ?? "", null, null, null).SqlTypeName switch
        {
            "uniqueidentifier" => PrimaryKeyShape.SingleUniqueIdentifier,
            "int" or "bigint" or "smallint" or "tinyint" => PrimaryKeyShape.SingleInt,
            _ => PrimaryKeyShape.SingleOther
        }
    };

    // =============== the table list ===============

    private const string ListTablesQuery = """
        SELECT t.TABLE_SCHEMA, t.TABLE_NAME,
               EXISTS (SELECT 1 FROM information_schema.STATISTICS s WHERE s.TABLE_SCHEMA = t.TABLE_SCHEMA AND s.TABLE_NAME = t.TABLE_NAME AND s.INDEX_NAME = 'PRIMARY') AS has_primary_key,
               EXISTS (SELECT 1 FROM information_schema.STATISTICS s WHERE s.TABLE_SCHEMA = t.TABLE_SCHEMA AND s.TABLE_NAME = t.TABLE_NAME AND s.NON_UNIQUE = 0) AS has_unique_index,
               (SELECT COUNT(*) FROM information_schema.STATISTICS s WHERE s.TABLE_SCHEMA = t.TABLE_SCHEMA AND s.TABLE_NAME = t.TABLE_NAME AND s.INDEX_NAME = 'PRIMARY') AS pk_column_count,
               (SELECT c.DATA_TYPE FROM information_schema.STATISTICS s JOIN information_schema.COLUMNS c ON c.TABLE_SCHEMA = s.TABLE_SCHEMA AND c.TABLE_NAME = s.TABLE_NAME AND c.COLUMN_NAME = s.COLUMN_NAME
                 WHERE s.TABLE_SCHEMA = t.TABLE_SCHEMA AND s.TABLE_NAME = t.TABLE_NAME AND s.INDEX_NAME = 'PRIMARY' AND s.SEQ_IN_INDEX = 1) AS pk_data_type,
               (SELECT c.COLUMN_TYPE FROM information_schema.STATISTICS s JOIN information_schema.COLUMNS c ON c.TABLE_SCHEMA = s.TABLE_SCHEMA AND c.TABLE_NAME = s.TABLE_NAME AND c.COLUMN_NAME = s.COLUMN_NAME
                 WHERE s.TABLE_SCHEMA = t.TABLE_SCHEMA AND s.TABLE_NAME = t.TABLE_NAME AND s.INDEX_NAME = 'PRIMARY' AND s.SEQ_IN_INDEX = 1) AS pk_column_type
        FROM information_schema.TABLES t
        WHERE t.TABLE_TYPE = 'BASE TABLE' AND t.TABLE_SCHEMA = DATABASE()
        ORDER BY t.TABLE_NAME;
        """;

    private const string AllColumnsQuery = """
        SELECT c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME, (c.EXTRA LIKE '%auto_increment%') AS is_identity, (c.COLUMN_KEY = 'PRI') AS is_primary_key,
               (c.EXTRA LIKE '%GENERATED%' AND c.EXTRA NOT LIKE '%DEFAULT_GENERATED%') AS is_computed, c.DATA_TYPE, (c.IS_NULLABLE = 'YES') AS is_nullable
        FROM information_schema.COLUMNS c
        JOIN information_schema.TABLES t ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME AND t.TABLE_TYPE = 'BASE TABLE'
        WHERE c.TABLE_SCHEMA = DATABASE();
        """;

    private const string SingleColumnForeignKeysQuery = """
        SELECT k.TABLE_SCHEMA, k.TABLE_NAME, k.COLUMN_NAME
        FROM information_schema.KEY_COLUMN_USAGE k
        WHERE k.TABLE_SCHEMA = DATABASE() AND k.REFERENCED_TABLE_NAME IS NOT NULL
          AND (SELECT COUNT(*) FROM information_schema.KEY_COLUMN_USAGE k2 WHERE k2.CONSTRAINT_SCHEMA = k.CONSTRAINT_SCHEMA AND k2.CONSTRAINT_NAME = k.CONSTRAINT_NAME AND k2.TABLE_NAME = k.TABLE_NAME) = 1;
        """;

    private const string ReferencedTablesQuery = """
        SELECT DISTINCT k.REFERENCED_TABLE_SCHEMA, k.REFERENCED_TABLE_NAME
        FROM information_schema.KEY_COLUMN_USAGE k
        WHERE k.TABLE_SCHEMA = DATABASE() AND k.REFERENCED_TABLE_NAME IS NOT NULL;
        """;

    public override async Task<List<TableSummary>> ListTablesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionRequest.CreateMySqlConnection();
        await connection.OpenAsync(cancellationToken);

        var columnsByTable = new Dictionary<(string Schema, string Table), List<(string Name, bool IsIdentity, bool IsPrimaryKey, bool IsComputed, string DataType, bool IsNullable)>>();
        await using (var command = new MySqlCommand(AllColumnsQuery, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var key = (reader.GetString(0), reader.GetString(1));
                if (!columnsByTable.TryGetValue(key, out var list))
                    columnsByTable[key] = list = [];
                list.Add((reader.GetString(2), reader.GetBoolean(3), reader.GetBoolean(4), reader.GetBoolean(5), reader.GetString(6), reader.GetBoolean(7)));
            }
        }

        var singleColumnFks = new Dictionary<(string Schema, string Table), HashSet<string>>();
        await using (var command = new MySqlCommand(SingleColumnForeignKeysQuery, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var key = (reader.GetString(0), reader.GetString(1));
                if (!singleColumnFks.TryGetValue(key, out var set))
                    singleColumnFks[key] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add(reader.GetString(2));
            }
        }

        var referenced = new HashSet<(string Schema, string Table)>(new TableKeyComparer());
        await using (var command = new MySqlCommand(ReferencedTablesQuery, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                referenced.Add((reader.GetString(0), reader.GetString(1)));
        }

        var results = new List<TableSummary>();
        await using var tablesCommand = new MySqlCommand(ListTablesQuery, connection);
        await using var tables = await tablesCommand.ExecuteReaderAsync(cancellationToken);
        while (await tables.ReadAsync(cancellationToken))
        {
            string schemaName = tables.GetString(0);
            string tableName = tables.GetString(1);
            if (Named(tableName).IsSystemTable())
                continue;

            var key = (schemaName, tableName);
            columnsByTable.TryGetValue(key, out var columns);
            columns ??= [];

            // a junction table: exactly two non-audit, non-computed columns (the identity key excluded) and both are single-column foreign keys
            var candidates = columns.Where(c => !c.IsComputed && !Named(c.Name).IsAuditColumn() && !(c.IsIdentity && c.IsPrimaryKey)).ToList();
            bool junction = candidates.Count == 2 && singleColumnFks.TryGetValue(key, out var fkColumns) && candidates.All(c => fkColumns.Contains(c.Name));

            // a NOT NULL text Name and a NOT NULL boolean IsActive
            bool nameActive = columns.Any(c => Named(c.Name) == "Name" && !c.IsNullable && c.DataType is "varchar" or "char" or "text")
                              && columns.Any(c => Named(c.Name) == "IsActive" && !c.IsNullable && c.DataType == "tinyint");

            int pkOrdinal = tables.GetOrdinal("pk_data_type");
            results.Add(new TableSummary
            {
                SchemaName = schemaName,
                TableName = tableName,
                HasPrimaryKey = Convert.ToBoolean(tables.GetValue(tables.GetOrdinal("has_primary_key"))),
                HasUniqueIndex = Convert.ToBoolean(tables.GetValue(tables.GetOrdinal("has_unique_index"))),
                IsJunctionTable = junction,
                HasChildForeignKeys = referenced.Contains(key),
                PrimaryKeyShape = ClassifyPrimaryKeyShape(Convert.ToInt32(tables.GetValue(tables.GetOrdinal("pk_column_count"))),
                    tables.IsDBNull(pkOrdinal) ? null : tables.GetString(pkOrdinal),
                    tables.IsDBNull(tables.GetOrdinal("pk_column_type")) ? null : tables.GetString(tables.GetOrdinal("pk_column_type"))),
                IsNameActiveTable = nameActive,
                IsAuditTable = AuditTableShape.IsAuditTable(columns.Select(c => Named(c.Name))),
                IsReservedWordName = tableName.IsSqlReservedWord(),
                IsCSharpReservedWordName = Named(tableName).IsCSharpReservedWord()
            });
        }

        return results;
    }

    // information_schema reports a table's name as the server stores it, and the case of a name that was typed another way can differ by server setting.
    private sealed class TableKeyComparer : IEqualityComparer<(string Schema, string Table)>
    {
        public bool Equals((string Schema, string Table) x, (string Schema, string Table) y) =>
            string.Equals(x.Schema, y.Schema, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string Schema, string Table) obj) =>
            HashCode.Combine(obj.Schema.ToLowerInvariant(), obj.Table.ToLowerInvariant());
    }

    // =============== columns ===============

    private const string ColumnsQuery = """
        SELECT c.COLUMN_NAME, c.ORDINAL_POSITION, c.DATA_TYPE, c.COLUMN_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.NUMERIC_PRECISION, c.NUMERIC_SCALE,
               (c.IS_NULLABLE = 'YES') AS is_nullable, c.COLUMN_DEFAULT, c.EXTRA, c.GENERATION_EXPRESSION, (c.COLUMN_KEY = 'PRI') AS is_primary_key,
               EXISTS (SELECT 1 FROM information_schema.STATISTICS s
                        WHERE s.TABLE_SCHEMA = c.TABLE_SCHEMA AND s.TABLE_NAME = c.TABLE_NAME AND s.COLUMN_NAME = c.COLUMN_NAME
                          AND s.NON_UNIQUE = 0 AND s.INDEX_NAME <> 'PRIMARY') AS is_in_unique_index
        FROM information_schema.COLUMNS c
        WHERE c.TABLE_SCHEMA = @schema AND c.TABLE_NAME = @table
        ORDER BY c.ORDINAL_POSITION;
        """;

    // CHECK_CONSTRAINTS exists from MySQL 8.0.16; an older server has none to read.
    private const string CheckConstraintsQuery = """
        SELECT cc.CHECK_CLAUSE
        FROM information_schema.CHECK_CONSTRAINTS cc
        JOIN information_schema.TABLE_CONSTRAINTS tc ON tc.CONSTRAINT_SCHEMA = cc.CONSTRAINT_SCHEMA AND tc.CONSTRAINT_NAME = cc.CONSTRAINT_NAME
        WHERE tc.CONSTRAINT_TYPE = 'CHECK' AND tc.TABLE_SCHEMA = @schema AND tc.TABLE_NAME = @table
        """;

    protected override async Task<string?> ReadTableDescriptionAsync(DbConnection connection, string schemaName, string tableName, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand("SELECT TABLE_COMMENT FROM information_schema.TABLES WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table", (MySqlConnection)connection);
        command.Parameters.AddWithValue("@schema", schemaName);
        command.Parameters.AddWithValue("@table", tableName);
        return await command.ExecuteScalarAsync(cancellationToken) is string text && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
    }

    protected override async Task<List<RawColumn>> ReadColumnsAsync(DbConnection connection, string schemaName, string tableName, CancellationToken cancellationToken)
    {
        var columns = await ReadRawColumnsAsync(connection, schemaName, tableName, cancellationToken);
        var descriptions = new List<(string, string)>();
        await using (var describe = new MySqlCommand("SELECT COLUMN_NAME, COLUMN_COMMENT FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table AND COLUMN_COMMENT <> ''", (MySqlConnection)connection))
        {
            describe.Parameters.AddWithValue("@schema", schemaName);
            describe.Parameters.AddWithValue("@table", tableName);
            await using var described = await describe.ExecuteReaderAsync(cancellationToken);
            while (await described.ReadAsync(cancellationToken))
                descriptions.Add((described.GetString(0), described.GetString(1)));
        }
        columns = WithDescriptions(columns, descriptions);
        var checks = new List<(string?, string)>();
        try
        {
            await using var command = new MySqlCommand(CheckConstraintsQuery, (MySqlConnection)connection);
            command.Parameters.AddWithValue("@schema", schemaName);
            command.Parameters.AddWithValue("@table", tableName);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                checks.Add((null, reader.GetString(0)));
        }
        catch (MySqlException)
        {
            // a server without information_schema.CHECK_CONSTRAINTS: no checks to read
        }
        return WithChecks(columns, checks);
    }

    private async Task<List<RawColumn>> ReadRawColumnsAsync(DbConnection connection, string schemaName, string tableName, CancellationToken cancellationToken)
    {
        var results = new List<RawColumn>();
        await using var command = new MySqlCommand(ColumnsQuery, (MySqlConnection)connection);
        command.Parameters.AddWithValue("@schema", schemaName);
        command.Parameters.AddWithValue("@table", tableName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string columnName = reader.GetString(0);
            if (columnName.IsSystemColumn())
                continue;

            long? Long(int o) => reader.IsDBNull(o) ? null : Convert.ToInt64(reader.GetValue(o));
            string? Text(int o) => reader.IsDBNull(o) ? null : reader.GetString(o);

            string dataType = reader.GetString(2);
            string columnType = reader.GetString(3);
            string extra = Text(9) ?? "";
            var (sqlType, maxLength, precision, scale, declaration) = MapType(dataType, columnType, Long(4), (int?)Long(5), (int?)Long(6));
            if (sqlType == "decimal" && dataType.StartsWith("bigint", StringComparison.OrdinalIgnoreCase)) { precision = 20; scale = 0; }
            bool isString = sqlType is "varchar" or "char";
            string? columnDefault = Text(8);
            bool identity = extra.Contains("auto_increment", StringComparison.OrdinalIgnoreCase);
            bool generated = extra.Contains("GENERATED", StringComparison.OrdinalIgnoreCase) && !extra.Contains("DEFAULT_GENERATED", StringComparison.OrdinalIgnoreCase);

            results.Add(new RawColumn(
                Name: columnName,
                OrdinalPosition: Convert.ToInt32(reader.GetValue(1)),
                SqlTypeName: sqlType,
                MaxLength: maxLength,
                Precision: precision,
                Scale: scale,
                IsNullable: reader.GetBoolean(7),
                IsIdentity: identity,
                IdentitySeed: identity ? 1 : null,
                IdentityIncrement: identity ? 1 : null,
                ComputedDefinition: generated ? Text(10) ?? "" : null,
                DefaultDefinition: columnDefault,
                IsPrimaryKey: reader.GetBoolean(11),
                IsInUniqueIndex: reader.GetBoolean(12),
                DeclarationOverride: declaration,
                DefaultForCSharp: NormalizeDefault(columnDefault, extra, isString),
                Choices: ParseEnumValues(columnType)));
        }

        return results;
    }

    private const string ColumnSummariesQuery = """
        SELECT c.COLUMN_NAME, c.DATA_TYPE, (c.IS_NULLABLE = 'YES') AS is_nullable, (c.COLUMN_KEY = 'PRI') AS is_primary_key
        FROM information_schema.COLUMNS c
        WHERE c.TABLE_SCHEMA = @schema AND c.TABLE_NAME = @table
        ORDER BY c.ORDINAL_POSITION;
        """;

    public override async Task<List<ColumnSummary>> ListColumnSummariesAsync(string schemaName, string tableName, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionRequest.CreateMySqlConnection();
        await connection.OpenAsync(cancellationToken);

        var results = new List<ColumnSummary>();
        await using var command = new MySqlCommand(ColumnSummariesQuery, connection);
        command.Parameters.AddWithValue("@schema", schemaName);
        command.Parameters.AddWithValue("@table", tableName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string columnName = reader.GetString(0);
            if (columnName.IsSystemColumn())
                continue;

            results.Add(new ColumnSummary
            {
                Name = columnName,
                SqlTypeName = reader.GetString(1),
                IsNullable = reader.GetBoolean(2),
                IsPrimaryKey = reader.GetBoolean(3)
            });
        }

        return results;
    }

    // =============== foreign keys  ===============

    private const string ForeignKeysQuery = """
        SELECT k.CONSTRAINT_NAME AS ConstraintName, k.REFERENCED_TABLE_SCHEMA AS OtherSchema, k.REFERENCED_TABLE_NAME AS OtherTable,
               k.COLUMN_NAME AS ReferencingColumn, k.REFERENCED_COLUMN_NAME AS ReferencedColumn
        FROM information_schema.KEY_COLUMN_USAGE k
        WHERE k.TABLE_SCHEMA = @schema AND k.TABLE_NAME = @table AND k.REFERENCED_TABLE_NAME IS NOT NULL
        ORDER BY k.CONSTRAINT_NAME, k.ORDINAL_POSITION;
        """;

    private const string ChildForeignKeysQuery = """
        SELECT k.CONSTRAINT_NAME AS ConstraintName, k.TABLE_SCHEMA AS OtherSchema, k.TABLE_NAME AS OtherTable,
               k.COLUMN_NAME AS ReferencingColumn, k.REFERENCED_COLUMN_NAME AS ReferencedColumn
        FROM information_schema.KEY_COLUMN_USAGE k
        WHERE k.REFERENCED_TABLE_SCHEMA = @schema AND k.REFERENCED_TABLE_NAME = @table
        ORDER BY k.CONSTRAINT_NAME, k.ORDINAL_POSITION;
        """;

    protected override async Task<List<ForeignKeyRow>> ReadForeignKeyRowsAsync(
        DbConnection connection, string schemaName, string tableName, bool children, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(children ? ChildForeignKeysQuery : ForeignKeysQuery, (MySqlConnection)connection);
        command.Parameters.AddWithValue("@schema", schemaName);
        command.Parameters.AddWithValue("@table", tableName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await GroupForeignKeyRowsAsync(reader, cancellationToken);
    }

    private const string IndexesQuery = """
        SELECT INDEX_NAME, NON_UNIQUE = 0, COLUMN_NAME
        FROM information_schema.STATISTICS
        WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table AND COLUMN_NAME IS NOT NULL AND INDEX_TYPE IN ('BTREE', 'HASH')
        ORDER BY INDEX_NAME, SEQ_IN_INDEX;
        """;

    protected override async Task<List<IndexRow>> ReadIndexRowsAsync(DbConnection connection, string schemaName, string tableName, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(IndexesQuery, (MySqlConnection)connection);
        command.Parameters.AddWithValue("@schema", schemaName);
        command.Parameters.AddWithValue("@table", tableName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await GroupIndexRowsAsync(reader, cancellationToken);
    }

    // =============== row count and row data  ===============

    protected override async Task<long> ReadRowCountAsync(DbConnection connection, string schemaName, string tableName, CancellationToken cancellationToken)
    {
        // A bounded count (it stops at 10,001 rows): TABLE_ROWS is only an estimate for InnoDB.
        await using var count = new MySqlCommand($"SELECT COUNT(*) FROM (SELECT 1 FROM {QuotedTable(schemaName, tableName)} LIMIT 10001) AS t;", (MySqlConnection)connection);
        return Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken));
    }

    protected override async Task<List<object?[]>> ReadRowsAsync(
        DbConnection connection, string schemaName, string tableName, List<ColumnModel> columns, List<ColumnModel> primaryKeyColumns,
        CancellationToken cancellationToken)
    {
        string fullTableName = QuotedTable(schemaName, tableName);
        var readIndexes = Enumerable.Range(0, columns.Count).Where(i => !columns[i].IsComputed).ToList();
        string columnList = string.Join(", ", readIndexes.Select(i => Quote(columns[i].DbName)));
        string orderBy = primaryKeyColumns.Count > 0 ? " ORDER BY " + string.Join(", ", primaryKeyColumns.Select(c => Quote(c.DbName))) : "";
        string sql = $"SELECT {columnList} FROM {fullTableName}{orderBy} LIMIT @maxRows;";

        var rows = new List<object?[]>();
        await using var command = new MySqlCommand(sql, (MySqlConnection)connection);
        command.Parameters.AddWithValue("@maxRows", MaxRowDataRows + 1);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (rows.Count == MaxRowDataRows)
                throw new InvalidOperationException(
                    $"{fullTableName} has more than {MaxRowDataRows} rows. Loading row data is meant for small reference/seed tables.");

            var values = new object?[columns.Count];
            for (int ordinal = 0; ordinal < readIndexes.Count; ordinal++)
                values[readIndexes[ordinal]] = reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal);
            rows.Add(values);
        }

        return rows;
    }
}
