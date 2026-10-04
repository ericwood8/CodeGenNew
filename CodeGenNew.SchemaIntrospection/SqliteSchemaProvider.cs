using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using CodeGenNew.Connections;
using CodeGenNew.Core;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.SchemaIntrospection;

/// <summary> Builds a TableModel from a SQLite database file, read-only: <c>sqlite_master</c> for the tables and their <c>CREATE</c> text, the <c>pragma_*</c> table functions for columns, keys,
/// foreign keys and indexes. SQLite has no schemas (the schema is always <c>main</c>), no routines, no sequences and no declared types that mean anything to the engine, so the declared type
/// name is read the way ORMs read it and put in the SQL Server vocabulary the rest of the generator classifies by. Everything the pragmas cannot say (CHECK constraints) comes from the
/// statement text (<see cref="SqliteDdl"/>). </summary>
public class SqliteSchemaProvider : SchemaProviderBase
{
    /// <summary> The only schema a SQLite connection has. </summary>
    public const string MainSchema = "main";

    private readonly ConnectionRequest _connectionRequest;

    public SqliteSchemaProvider(ConnectionRequest connectionRequest, string specialLogicColumnsConfigPath, NamingStyle naming = NamingStyle.AsIs, IReadOnlyCollection<string>? acronyms = null)
        : base(specialLogicColumnsConfigPath, naming, acronyms)
    {
        _connectionRequest = connectionRequest;
    }

    protected override SqlDialect Dialect => SqlDialect.Sqlite;

    protected override async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = _connectionRequest.CreateSqliteConnection();
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    protected override string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

    // =============== type vocabulary ===============

    /// <summary> A declared column type in the SQL Server vocabulary. SQLite keeps whatever name the author wrote, so the name is read the way the engine decides its affinity (anything with INT is an
    /// integer, CHAR / CLOB / TEXT is text, REAL / FLOA / DOUB is a floating-point number, BLOB or no type is binary, the rest numeric) after the names ORMs and people use are recognised first
    /// (BOOLEAN, DATETIME, DECIMAL(10,2), VARCHAR(50), UUID). An <c>INTEGER</c> is read as <c>int</c>, not <c>long</c>: the engine's integer is 64 bits, but a table keyed by one is the common case and every
    /// generated key is an <c>int</c>; declare a column <c>BIGINT</c> to get a <c>long</c>. A column with no declared type, or a type that is none of the above, is <c>sql_variant</c> (unsupported;
    /// list it in IgnoredColumns). </summary>
    public static (string SqlTypeName, int MaxLength, int Precision, int Scale, string Declaration) MapType(string? declaredType)
    {
        string declared = (declaredType ?? "").Trim();
        string upper = declared.ToUpperInvariant();
        string baseName = Regex.Replace(upper, @"\(.*\)", "").Trim();
        var arguments = Regex.Match(upper, @"\(\s*(\d+)\s*(?:,\s*(\d+)\s*)?\)");
        int first = arguments.Success ? int.Parse(arguments.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        int second = arguments.Success && arguments.Groups[2].Success ? int.Parse(arguments.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
        string text = declared.Length == 0 ? "" : declared;

        switch (baseName)
        {
            case "BOOLEAN": case "BOOL": case "BIT": return ("bit", 0, 0, 0, text);
            case "TINYINT": case "SMALLINT": case "INT2": return ("smallint", 0, 0, 0, text);
            case "BIGINT": case "INT8": case "UNSIGNED BIG INT": return ("bigint", 0, 0, 0, text);
            case "DATE": return ("date", 0, 0, 0, text);
            case "DATETIME": case "DATETIME2": case "TIMESTAMP": return ("datetime2", 0, 0, 0, text);
            case "TIME": return ("time", 0, 0, 0, text);
            case "UUID": case "GUID": case "UNIQUEIDENTIFIER": return ("uniqueidentifier", 0, 0, 0, text);
            case "DECIMAL": case "NUMERIC": return arguments.Success ? ("decimal", 0, first, second, text) : ("decimal", 0, 38, 4, text);
            case "MONEY": case "CURRENCY": return ("decimal", 0, 19, 4, text);
            case "DOUBLE": case "DOUBLE PRECISION": case "FLOAT": return ("float", 0, 0, 0, text);
            case "REAL": return ("real", 0, 0, 0, text);
            case "VARCHAR": case "NVARCHAR": case "CHARACTER": case "VARYING CHARACTER": case "NATIVE CHARACTER": case "NCHAR": case "CHAR":
                return (baseName is "CHAR" or "NCHAR" or "CHARACTER" && first > 0 ? "char" : "varchar", first > 0 ? first : -1, 0, 0, text);
        }

        // the affinity rules, in the order SQLite applies them
        if (upper.Contains("INT")) return ("int", 0, 0, 0, text);
        if (upper.Contains("CHAR") || upper.Contains("CLOB") || upper.Contains("TEXT") || upper == "STRING") return ("varchar", first > 0 ? first : -1, 0, 0, text);
        if (upper.Contains("BLOB")) return ("varbinary", -1, 0, 0, text);
        if (upper.Contains("REAL") || upper.Contains("FLOA") || upper.Contains("DOUB")) return ("float", 0, 0, 0, text);
        if (declared.Length == 0) return ("sql_variant", 0, 0, 0, "");
        return ("decimal", 0, 38, 4, text);   // numeric affinity: a number of unknown shape
    }

    /// <summary> A column default as SQLite reports it (<c>dflt_value</c>: a literal, or an expression as written) in the form the C# default resolver expects of a SQL Server default. </summary>
    public static string? NormalizeDefault(string? defaultValue)
    {
        if (defaultValue is null)
            return null;

        string d = defaultValue.Trim();
        string lower = d.ToLowerInvariant();
        while (lower.Length > 1 && lower[0] == '(' && lower[^1] == ')')
            lower = lower[1..^1].Trim();
        if (lower is "current_timestamp" or "datetime('now')" or "datetime('now','localtime')" or "datetime('now', 'localtime')" or "date('now')") return "getdate()";
        if (lower is "current_date") return "getdate()";
        if (lower is "true") return "1";
        if (lower is "false") return "0";
        return d;
    }

    private static PrimaryKeyShape ShapeOf(int primaryKeyColumns, string? declaredType) => primaryKeyColumns switch
    {
        0 => PrimaryKeyShape.None,
        > 1 => PrimaryKeyShape.Composite,
        _ => MapType(declaredType).SqlTypeName switch
        {
            "uniqueidentifier" => PrimaryKeyShape.SingleUniqueIdentifier,
            "int" or "bigint" or "smallint" or "tinyint" => PrimaryKeyShape.SingleInt,
            _ => PrimaryKeyShape.SingleOther
        }
    };

    private static readonly Regex FlagName = new(@"^(is|has|can|should)[_A-Z0-9]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // =============== reading ===============

    private sealed record RawColumnInfo(int Cid, string Name, string? Type, bool NotNull, string? Default, int PrimaryKeyPosition, int Hidden);

    private static async Task<List<RawColumnInfo>> ReadColumnInfoAsync(DbConnection connection, string table, CancellationToken cancellationToken)
    {
        var result = new List<RawColumnInfo>();
        await using var command = (SqliteCommand)connection.CreateCommand();
        command.CommandText = "SELECT cid, name, type, \"notnull\", dflt_value, pk, hidden FROM pragma_table_xinfo(@t) WHERE hidden IN (0, 2, 3) ORDER BY cid";
        command.Parameters.AddWithValue("@t", table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new RawColumnInfo(reader.GetInt32(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetInt32(3) != 0,
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetInt32(5), reader.GetInt32(6)));
        return result;
    }

    private static async Task<string?> ReadCreateSqlAsync(DbConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = (SqliteCommand)connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = @t COLLATE NOCASE";
        command.Parameters.AddWithValue("@t", table);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private sealed record IndexInfo(string Name, bool IsUnique, string Origin, bool IsPartial, List<string> Columns);

    /// <summary> The indexes of a table with their key columns; an index on an expression has no column name there and is left out. </summary>
    private static async Task<List<IndexInfo>> ReadIndexInfoAsync(DbConnection connection, string table, CancellationToken cancellationToken)
    {
        var indexes = new List<IndexInfo>();
        await using (var command = (SqliteCommand)connection.CreateCommand())
        {
            command.CommandText = "SELECT name, \"unique\", origin, partial FROM pragma_index_list(@t)";
            command.Parameters.AddWithValue("@t", table);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                indexes.Add(new IndexInfo(reader.GetString(0), reader.GetInt32(1) != 0, reader.GetString(2), reader.GetInt32(3) != 0, []));
        }

        foreach (var index in indexes)
        {
            await using var command = (SqliteCommand)connection.CreateCommand();
            command.CommandText = "SELECT name FROM pragma_index_xinfo(@i) WHERE key = 1 ORDER BY seqno";
            command.Parameters.AddWithValue("@i", index.Name);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                index.Columns.Add(reader.IsDBNull(0) ? "" : reader.GetString(0));
        }

        return indexes.Where(i => i.Columns.All(c => c.Length > 0)).ToList();
    }

    protected override async Task<List<RawColumn>> ReadColumnsAsync(DbConnection connection, string schemaName, string tableName, CancellationToken cancellationToken)
    {
        var info = await ReadColumnInfoAsync(connection, tableName, cancellationToken);
        var definition = SqliteDdl.Parse(await ReadCreateSqlAsync(connection, tableName, cancellationToken));
        var indexes = await ReadIndexInfoAsync(connection, tableName, cancellationToken);
        var uniqueColumns = indexes.Where(i => i.IsUnique && i.Origin != "pk" && !i.IsPartial).SelectMany(i => i.Columns).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var primaryKey = info.Where(c => c.PrimaryKeyPosition > 0).ToList();
        // INTEGER PRIMARY KEY is an alias of the row id: the database fills it in (a WITHOUT ROWID table has no row id)
        bool rowIdAlias = !definition.WithoutRowId && primaryKey.Count == 1 && string.Equals(primaryKey[0].Type?.Trim(), "INTEGER", StringComparison.OrdinalIgnoreCase);

        var checks = definition.Checks.Select(c => (c.Column ?? ColumnMentionedBy(c.Expression, info), c.Expression))
            .Where(c => c.Item1 is not null).Select(c => (c.Item1, c.Expression)).ToList();

        var columns = new List<RawColumn>();
        foreach (var c in info)
        {
            var (sqlType, maxLength, precision, scale, declaration) = MapType(c.Type);
            var columnChecks = checks.Where(k => string.Equals(k.Item1, c.Name, StringComparison.OrdinalIgnoreCase)).Select(k => k.Expression).ToList();
            if (sqlType is "int" or "smallint" or "bigint" && c.PrimaryKeyPosition == 0 && IsFlag(c.Name, columnChecks))
                sqlType = "bit";

            bool identity = rowIdAlias && c.PrimaryKeyPosition > 0;
            bool generated = c.Hidden is 2 or 3;
            columns.Add(new RawColumn(
                Name: c.Name,
                OrdinalPosition: c.Cid + 1,
                SqlTypeName: sqlType,
                MaxLength: maxLength,
                Precision: precision,
                Scale: scale,
                IsNullable: !c.NotNull && c.PrimaryKeyPosition == 0,
                IsIdentity: identity,
                IdentitySeed: identity ? 1 : null,
                IdentityIncrement: identity ? 1 : null,
                ComputedDefinition: generated ? "" : null,
                DefaultDefinition: c.Default,
                IsPrimaryKey: c.PrimaryKeyPosition > 0,
                IsInUniqueIndex: uniqueColumns.Contains(c.Name),
                DeclarationOverride: string.IsNullOrEmpty(declaration) ? null : declaration,
                DefaultForCSharp: NormalizeDefault(c.Default),
                Choices: null));
        }

        return WithChecks(columns, checks.Select(k => (k.Item1, k.Expression)));
    }

    /// <summary> An integer column is a flag when its name says so (IsActive, has_photo) or its only allowed values are 0 and 1: SQLite has no boolean type, so a flag is an integer by another name. </summary>
    private static bool IsFlag(string name, List<string> checks)
    {
        if (FlagName.IsMatch(name))
            return true;
        return checks.Any(expression => CheckConstraintParser.ParseRange(expression, name) is { Min: 0, Max: 1 });
    }

    /// <summary> The one column a table-level CHECK mentions, by word; null when it mentions none or several. </summary>
    private static string? ColumnMentionedBy(string expression, List<RawColumnInfo> columns)
    {
        string withoutLiterals = Regex.Replace(expression, "'(?:[^']|'')*'", "''");
        var mentioned = columns.Where(c => Regex.IsMatch(withoutLiterals, @"(?<![A-Za-z0-9_])[""`\[]?" + Regex.Escape(c.Name) + @"[""`\]]?(?![A-Za-z0-9_])", RegexOptions.IgnoreCase)).ToList();
        return mentioned.Count == 1 ? mentioned[0].Name : null;
    }

    protected override Task<long> ReadRowCountAsync(DbConnection connection, string schemaName, string tableName, CancellationToken cancellationToken) =>
        CountAsync(connection, tableName, cancellationToken);

    private async Task<long> CountAsync(DbConnection connection, string tableName, CancellationToken cancellationToken)
    {
        await using var command = (SqliteCommand)connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM (SELECT 1 FROM {Quote(tableName)} LIMIT 10001)";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    protected override async Task<List<object?[]>> ReadRowsAsync(DbConnection connection, string schemaName, string tableName, List<ColumnModel> columns,
        List<ColumnModel> primaryKeyColumns, CancellationToken cancellationToken)
    {
        var readIndexes = Enumerable.Range(0, columns.Count).Where(i => !columns[i].IsComputed).ToList();
        string columnList = string.Join(", ", readIndexes.Select(i => Quote(columns[i].DbName)));
        string orderBy = primaryKeyColumns.Count > 0 ? " ORDER BY " + string.Join(", ", primaryKeyColumns.Select(c => Quote(c.DbName))) : "";

        var rows = new List<object?[]>();
        await using var command = (SqliteCommand)connection.CreateCommand();
        command.CommandText = $"SELECT {columnList} FROM {Quote(tableName)}{orderBy} LIMIT @maxRows";
        command.Parameters.AddWithValue("@maxRows", MaxRowDataRows + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (rows.Count == MaxRowDataRows)
                throw new InvalidOperationException($"{Quote(tableName)} has more than {MaxRowDataRows} rows. Loading row data is meant for small reference/seed tables.");

            var values = new object?[columns.Count];
            for (int ordinal = 0; ordinal < readIndexes.Count; ordinal++)
                values[readIndexes[ordinal]] = reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal);
            rows.Add(values);
        }
        return rows;
    }

    // =============== foreign keys ===============

    protected override async Task<List<ForeignKeyRow>> ReadForeignKeyRowsAsync(DbConnection connection, string schemaName, string tableName, bool children, CancellationToken cancellationToken)
    {
        var rows = new List<(string Table, int Id, int Seq, string From, string? To, string ReferencedTable)>();
        await using (var command = (SqliteCommand)connection.CreateCommand())
        {
            command.CommandText = children
                ? "SELECT m.name, f.id, f.seq, f.\"from\", f.\"to\", f.\"table\" FROM sqlite_master m, pragma_foreign_key_list(m.name) f " +
                  "WHERE m.type = 'table' AND m.name NOT LIKE 'sqlite_%' AND f.\"table\" = @t COLLATE NOCASE ORDER BY m.name, f.id, f.seq"
                : "SELECT @t, f.id, f.seq, f.\"from\", f.\"to\", f.\"table\" FROM pragma_foreign_key_list(@t) f ORDER BY f.id, f.seq";
            command.Parameters.AddWithValue("@t", tableName);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                rows.Add((reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5)));
        }

        // a foreign key written without target columns points at the other table's primary key
        var primaryKeys = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        async Task<List<string>> KeyOf(string table)
        {
            if (!primaryKeys.TryGetValue(table, out var key))
                primaryKeys[table] = key = (await ReadColumnInfoAsync(connection, table, cancellationToken)).Where(c => c.PrimaryKeyPosition > 0).OrderBy(c => c.PrimaryKeyPosition).Select(c => c.Name).ToList();
            return key;
        }

        var grouped = new List<ForeignKeyRow>();
        foreach (var group in rows.GroupBy(r => (r.Table, r.Id)))
        {
            var list = group.OrderBy(r => r.Seq).ToList();
            var targetKey = list.Any(r => r.To is null) ? await KeyOf(list[0].ReferencedTable) : [];
            var referenced = list.Select((r, i) => r.To ?? (i < targetKey.Count ? targetKey[i] : "")).ToList();
            string other = children ? group.Key.Table : list[0].ReferencedTable;
            string constraint = $"FK_{group.Key.Table}_{group.Key.Id}";
            grouped.Add(new ForeignKeyRow(constraint, MainSchema, other, list.Select(r => r.From).ToList(), referenced));
        }
        return grouped;
    }

    // =============== indexes ===============

    protected override async Task<List<IndexRow>> ReadIndexRowsAsync(DbConnection connection, string schemaName, string tableName, CancellationToken cancellationToken)
    {
        var indexes = await ReadIndexInfoAsync(connection, tableName, cancellationToken);
        var result = indexes.Where(i => !i.IsPartial).Select(i => new IndexRow(i.Name, i.IsUnique, i.Columns)).ToList();

        // the row id alias is looked up by its own storage and has no index entry: say so, so a key counts as indexed
        var info = await ReadColumnInfoAsync(connection, tableName, cancellationToken);
        var key = info.Where(c => c.PrimaryKeyPosition > 0).OrderBy(c => c.PrimaryKeyPosition).ToList();
        if (key.Count > 0 && !result.Any(i => i.IsUnique && i.Columns.SequenceEqual(key.Select(k => k.Name))))
            result.Insert(0, new IndexRow("PRIMARY KEY", true, key.Select(k => k.Name).ToList()));
        return result;
    }

    // =============== the table list ===============

    public override async Task<List<TableSummary>> ListTablesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionRequest.CreateSqliteConnection();
        await connection.OpenAsync(cancellationToken);

        var names = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                names.Add(reader.GetString(0));
        }

        var infoByTable = new Dictionary<string, List<RawColumnInfo>>(StringComparer.OrdinalIgnoreCase);
        var singleColumnForeignKeys = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
        {
            infoByTable[name] = await ReadColumnInfoAsync(connection, name, cancellationToken);
            var foreignKeys = await ReadForeignKeyRowsAsync(connection, MainSchema, name, children: false, cancellationToken);
            singleColumnForeignKeys[name] = foreignKeys.Where(f => f.ReferencingColumns.Count == 1).Select(f => f.ReferencingColumns[0]).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var fk in foreignKeys)
                referenced.Add(fk.OtherTable);
        }

        var results = new List<TableSummary>();
        foreach (string name in names)
        {
            if (Named(name).IsSystemTable())
                continue;

            var columns = infoByTable[name];
            var definition = SqliteDdl.Parse(await ReadCreateSqlAsync(connection, name, cancellationToken));
            var primaryKey = columns.Where(c => c.PrimaryKeyPosition > 0).OrderBy(c => c.PrimaryKeyPosition).ToList();
            bool identityKey = !definition.WithoutRowId && primaryKey.Count == 1 && string.Equals(primaryKey[0].Type?.Trim(), "INTEGER", StringComparison.OrdinalIgnoreCase);
            var indexes = await ReadIndexInfoAsync(connection, name, cancellationToken);

            var candidates = columns.Where(c => c.Hidden == 0 && !Named(c.Name).IsAuditColumn() && !(identityKey && c.PrimaryKeyPosition > 0)).ToList();
            bool junction = candidates.Count == 2 && candidates.All(c => singleColumnForeignKeys[name].Contains(c.Name));
            bool nameActive = columns.Any(c => Named(c.Name) == "Name" && c.NotNull && MapType(c.Type).SqlTypeName is "varchar" or "char")
                              && columns.Any(c => Named(c.Name) == "IsActive" && c.NotNull && MapType(c.Type).SqlTypeName is "bit" or "int" or "smallint");

            results.Add(new TableSummary
            {
                SchemaName = MainSchema,
                TableName = name,
                HasPrimaryKey = primaryKey.Count > 0,
                HasUniqueIndex = primaryKey.Count > 0 || indexes.Any(i => i.IsUnique),
                IsJunctionTable = junction,
                HasChildForeignKeys = referenced.Contains(name),
                PrimaryKeyShape = ShapeOf(primaryKey.Count, primaryKey.FirstOrDefault()?.Type),
                IsNameActiveTable = nameActive,
                IsReservedWordName = name.IsSqlReservedWord(),
                IsCSharpReservedWordName = Named(name).IsCSharpReservedWord()
            });
        }

        return results;
    }

    public override async Task<List<ColumnSummary>> ListColumnSummariesAsync(string schemaName, string tableName, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionRequest.CreateSqliteConnection();
        await connection.OpenAsync(cancellationToken);

        return (await ReadColumnInfoAsync(connection, tableName, cancellationToken))
            .Where(c => !c.Name.IsSystemColumn())
            .Select(c => new ColumnSummary { Name = c.Name, SqlTypeName = MapType(c.Type).SqlTypeName, IsNullable = !c.NotNull && c.PrimaryKeyPosition == 0, IsPrimaryKey = c.PrimaryKeyPosition > 0 })
            .ToList();
    }
}
