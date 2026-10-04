using System.Data.Common;
using System.Text.RegularExpressions;
using CodeGenNew.Connections;
using CodeGenNew.Core;
using Npgsql;

namespace CodeGenNew.SchemaIntrospection;

/// <summary> Builds a fully-populated TableModel for one table by reading live schema metadata from PostgreSQL.
/// Table and column facts come from information_schema (tables, columns); keys, indexes and foreign keys come from pg_catalog
/// (pg_class, pg_namespace, pg_index, pg_constraint, pg_attribute), because information_schema cannot say which index is unique
/// or give a composite foreign key's column pairs in a form that is easy to join. Everything is read-only. </summary>
public class PostgresSchemaProvider : SchemaProviderBase
{
    private readonly ConnectionRequest _connectionRequest;

    public PostgresSchemaProvider(ConnectionRequest connectionRequest, string specialLogicColumnsConfigPath, NamingStyle naming = NamingStyle.AsIs, IReadOnlyCollection<string>? acronyms = null) : base(specialLogicColumnsConfigPath, naming, acronyms)
    {
        _connectionRequest = connectionRequest;
    }

    protected override SqlDialect Dialect => SqlDialect.PostgreSql;

    protected override async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = _connectionRequest.CreatePostgresConnection();
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    protected override string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

    // ========== type vocabulary =============

    /// <summary> A PostgreSQL type (information_schema udt_name) in the SQL Server vocabulary the generator classifies by, plus the
    /// PostgreSQL spelling of the declaration. Length -1 means unbounded (text, bytea, json). A type with no equivalent
    /// (arrays, enums, geometry ...) maps to sql_variant, which the generators treat as an unsupported "object" column. </summary>
    public static (string SqlTypeName, int MaxLength, int Precision, int Scale, string Declaration) MapType(
        string udtName, int? characterMaximumLength, int? numericPrecision, int? numericScale, IReadOnlyList<string>? enumLabels = null)
    {
        // An enum type is a string as long as its longest label (the form shows it as a drop-down); the SQL text calls it varchar so a function can take it as text.
        if (enumLabels is { Count: > 0 })
        {
            int longest = enumLabels.Max(v => v.Length);
            return ("varchar", longest, 0, 0, $"varchar({longest})");
        }
        int length = characterMaximumLength ?? -1;
        switch (udtName.ToLowerInvariant())
        {
            case "int2": return ("smallint", 0, 0, 0, "smallint");
            case "int4": return ("int", 0, 0, 0, "integer");
            case "int8": return ("bigint", 0, 0, 0, "bigint");
            case "bool": return ("bit", 0, 0, 0, "boolean");
            case "numeric":
            {
                // A bare "numeric" holds any number of places; a form needs a number, so it reads as 38 digits with 4 places (what money holds). The SQL keeps "numeric" and the
                // entity writes no [Precision], so nothing is rounded or re-typed by the generated code on the way to the database.
                int precision = numericPrecision ?? 38;
                int scale = numericScale ?? 4;
                return ("decimal", 0, precision, scale, numericPrecision is null ? "numeric" : $"numeric({precision},{scale})");
            }
            case "money": return ("money", 0, 0, 0, "money");
            case "float4": return ("real", 0, 0, 0, "real");
            case "float8": return ("float", 0, 0, 0, "double precision");
            case "date": return ("date", 0, 0, 0, "date");
            case "timestamp": return ("datetime2", 0, 0, 0, "timestamp");
            case "timestamptz": return ("datetimeoffset", 0, 0, 0, "timestamptz");
            case "time": case "timetz": return ("time", 0, 0, 0, "time");
            case "bpchar": return ("char", length, 0, 0, characterMaximumLength is null ? "char" : $"char({length})");
            case "varchar": return ("varchar", length, 0, 0, characterMaximumLength is null ? "varchar" : $"varchar({length})");
            case "text": case "name": case "citext": return ("varchar", -1, 0, 0, "text");
            case "json": case "jsonb": return ("varchar", -1, 0, 0, udtName.ToLowerInvariant());
            case "xml": return ("xml", 0, 0, 0, "xml");
            case "bytea": return ("varbinary", -1, 0, 0, "bytea");
            case "uuid": return ("uniqueidentifier", 0, 0, 0, "uuid");
            default: return ("sql_variant", 0, 0, 0, udtName);
        }
    }

    // Reads a default expression the way the C# default-value resolver expects a SQL Server one: functions by their SQL Server
    // name, booleans as bit literals, casts removed. Anything else is left as it is (the resolver then suggests nothing).
    private static readonly Regex TrailingCast = new(@"(::[a-zA-Z_][a-zA-Z0-9_ ]*(\[\])?)+$", RegexOptions.Compiled);

    public static string? NormalizeDefault(string? pgDefault)
    {
        if (string.IsNullOrWhiteSpace(pgDefault))
            return null;

        string expr = TrailingCast.Replace(pgDefault.Trim(), "").Trim();
        while (expr.StartsWith('(') && expr.EndsWith(')') && !expr.EndsWith("()"))
            expr = expr[1..^1].Trim();

        string lower = expr.ToLowerInvariant();
        if (lower is "now()" or "current_timestamp" or "localtimestamp" or "current_date" or "transaction_timestamp()" or "statement_timestamp()")
            return "getdate()";
        if (lower.StartsWith("timezone('utc'") || lower.StartsWith("timezone('utc'::text"))
            return "getutcdate()";
        if (lower is "gen_random_uuid()" or "uuid_generate_v4()")
            return "newid()";
        if (lower == "true") return "1";
        if (lower == "false") return "0";
        return expr;
    }

    private static readonly Regex StringLiteral = new("'((?:[^']|'')*)'", RegexOptions.Compiled);

    /// <summary> The values a single-column CHECK constraint lists (<c>CHECK (status IN ('Open','Closed'))</c>, which PostgreSQL stores as
    /// <c>= ANY (ARRAY['Open'::text, 'Closed'::text])</c>); null for any other check (a range, an OR, a NOT, a comparison with a column). </summary>
    public static List<string>? ParseCheckValues(string constraintDefinition)
    {
        // Quoted values hold anything, so judge the shape on the definition with every value emptied out.
        string shape = StringLiteral.Replace(constraintDefinition, "''");
        if (!shape.Contains("= ANY", StringComparison.OrdinalIgnoreCase) || !shape.Contains("ARRAY[", StringComparison.OrdinalIgnoreCase))
            return null;
        if (Regex.IsMatch(shape, @"\b(OR|AND|NOT)\b|<>|!=|<|>", RegexOptions.IgnoreCase))
            return null;

        int open = shape.IndexOf("ARRAY[", StringComparison.OrdinalIgnoreCase) + 6;
        int close = shape.IndexOf(']', open);
        if (close < 0 || Regex.Replace(shape[open..close], @"''(::[a-zA-Z_ ]+)?|,|\s", "").Length > 0)
            return null;                                           // something other than listed values inside the array

        var values = StringLiteral.Matches(constraintDefinition).Select(m => m.Groups[1].Value.Replace("''", "'")).ToList();
        return values.Count > 0 ? values : null;
    }

    private static bool IsSerialDefault(string? columnDefault) => columnDefault is not null && columnDefault.StartsWith("nextval(", StringComparison.OrdinalIgnoreCase);

    // A single primary key column's PostgreSQL type -> PrimaryKeyShape, matching TableModel.PrimaryKeyShape's own rule.
    private static PrimaryKeyShape ClassifyPrimaryKeyShape(int pkColumnCount, string? pkUdtName) => pkColumnCount switch
    {
        0 => PrimaryKeyShape.None,
        > 1 => PrimaryKeyShape.Composite,
        _ => MapType(pkUdtName ?? "", null, null, null).SqlTypeName switch
        {
            "uniqueidentifier" => PrimaryKeyShape.SingleUniqueIdentifier,
            "int" or "bigint" or "smallint" or "tinyint" => PrimaryKeyShape.SingleInt,
            _ => PrimaryKeyShape.SingleOther
        }
    };

    // ========== the table list ==============

    private const string ListTablesQuery = """
        SELECT t.table_schema AS schema_name, t.table_name AS table_name,
               c.oid AS table_oid,
               EXISTS (SELECT 1 FROM pg_index i WHERE i.indrelid = c.oid AND i.indisprimary) AS has_primary_key,
               EXISTS (SELECT 1 FROM pg_index i WHERE i.indrelid = c.oid AND i.indisunique) AS has_unique_index,
               COALESCE((SELECT i.indnkeyatts FROM pg_index i WHERE i.indrelid = c.oid AND i.indisprimary), 0)::int AS pk_column_count,
               (SELECT ty.typname FROM pg_index i
                  JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = i.indkey[0]
                  JOIN pg_type ty ON ty.oid = a.atttypid
                 WHERE i.indrelid = c.oid AND i.indisprimary) AS pk_type_name
        FROM information_schema.tables t
        JOIN pg_namespace n ON n.nspname = t.table_schema
        JOIN pg_class c ON c.relnamespace = n.oid AND c.relname = t.table_name
        WHERE t.table_type = 'BASE TABLE'
          AND t.table_schema NOT IN ('pg_catalog', 'information_schema')
          AND t.table_schema NOT LIKE 'pg\_%'
        ORDER BY t.table_schema, t.table_name;
        """;

    // For every table: its columns with identity/primary-key/generated flags (the junction-table rule), and every table that is the parent in a foreign key.
    private const string AllColumnsForJunctionCheckQuery = """
        SELECT c.table_schema AS schema_name, c.table_name AS table_name, c.column_name AS column_name,
               (c.is_identity = 'YES' OR COALESCE(c.column_default LIKE 'nextval(%', false)) AS is_identity,
               EXISTS (SELECT 1 FROM pg_index i
                         JOIN pg_class t ON t.oid = i.indrelid
                         JOIN pg_namespace n ON n.oid = t.relnamespace
                         JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = ANY (i.indkey)
                        WHERE i.indisprimary AND n.nspname = c.table_schema AND t.relname = c.table_name AND a.attname = c.column_name) AS is_primary_key,
               (c.is_generated = 'ALWAYS') AS is_computed
        FROM information_schema.columns c
        JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name AND t.table_type = 'BASE TABLE'
        WHERE c.table_schema NOT IN ('pg_catalog', 'information_schema');
        """;

    private const string AllSingleColumnForeignKeysQuery = """
        SELECT pn.nspname AS schema_name, pc.relname AS table_name, a.attname AS column_name
        FROM pg_constraint con
        JOIN pg_class pc ON pc.oid = con.conrelid
        JOIN pg_namespace pn ON pn.oid = pc.relnamespace
        JOIN pg_attribute a ON a.attrelid = con.conrelid AND a.attnum = con.conkey[1]
        WHERE con.contype = 'f' AND array_length(con.conkey, 1) = 1;
        """;

    private const string AllReferencedTablesQuery = """
        SELECT DISTINCT rn.nspname AS schema_name, rc.relname AS table_name
        FROM pg_constraint con
        JOIN pg_class rc ON rc.oid = con.confrelid
        JOIN pg_namespace rn ON rn.oid = rc.relnamespace
        WHERE con.contype = 'f';
        """;

    private const string AllNameActiveCandidateColumnsQuery = """
        SELECT c.table_schema AS schema_name, c.table_name AS table_name, c.column_name AS column_name,
               c.udt_name AS udt_name, (c.is_nullable = 'YES') AS is_nullable
        FROM information_schema.columns c
        JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name AND t.table_type = 'BASE TABLE'
        WHERE lower(replace(c.column_name, '_', '')) IN ('name', 'isactive');
        """;

    public override async Task<List<TableSummary>> ListTablesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionRequest.CreatePostgresConnection();
        await connection.OpenAsync(cancellationToken);

        var junctionTables = await DetermineJunctionTablesAsync(connection, cancellationToken);
        var tablesWithChildren = await ReadPairsAsync(connection, AllReferencedTablesQuery, cancellationToken);
        var nameActiveTables = await DetermineNameActiveTablesAsync(connection, cancellationToken);

        var results = new List<TableSummary>();
        await using var command = new NpgsqlCommand(ListTablesQuery, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string tableName = reader.GetString(reader.GetOrdinal("table_name"));
            if (tableName.IsSystemTable())
                continue;

            string schemaName = reader.GetString(reader.GetOrdinal("schema_name"));
            int pkOrdinal = reader.GetOrdinal("pk_type_name");
            results.Add(new TableSummary
            {
                SchemaName = schemaName,
                TableName = tableName,
                HasPrimaryKey = reader.GetBoolean(reader.GetOrdinal("has_primary_key")),
                HasUniqueIndex = reader.GetBoolean(reader.GetOrdinal("has_unique_index")),
                IsJunctionTable = junctionTables.Contains((schemaName, tableName)),
                HasChildForeignKeys = tablesWithChildren.Contains((schemaName, tableName)),
                PrimaryKeyShape = ClassifyPrimaryKeyShape(reader.GetInt32(reader.GetOrdinal("pk_column_count")), reader.IsDBNull(pkOrdinal) ? null : reader.GetString(pkOrdinal)),
                IsNameActiveTable = nameActiveTables.Contains((schemaName, tableName)),
                IsReservedWordName = tableName.IsSqlReservedWord(),
                IsCSharpReservedWordName = tableName.IsCSharpReservedWord()
            });
        }

        return results;
    }

    private static async Task<HashSet<(string Schema, string Table)>> ReadPairsAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        var pairs = new HashSet<(string, string)>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            pairs.Add((reader.GetString(0), reader.GetString(1)));
        return pairs;
    }

    private async Task<HashSet<(string Schema, string Table)>> DetermineJunctionTablesAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var columnsByTable = new Dictionary<(string Schema, string Table), List<(string Name, bool IsIdentity, bool IsPrimaryKey, bool IsComputed)>>();
        await using (var command = new NpgsqlCommand(AllColumnsForJunctionCheckQuery, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var key = (reader.GetString(0), reader.GetString(1));
                if (!columnsByTable.TryGetValue(key, out var list))
                    columnsByTable[key] = list = [];
                list.Add((reader.GetString(2), reader.GetBoolean(3), reader.GetBoolean(4), reader.GetBoolean(5)));
            }
        }

        var singleColumnFkColumnsByTable = new Dictionary<(string Schema, string Table), HashSet<string>>();
        await using (var command = new NpgsqlCommand(AllSingleColumnForeignKeysQuery, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var key = (reader.GetString(0), reader.GetString(1));
                if (!singleColumnFkColumnsByTable.TryGetValue(key, out var set))
                    singleColumnFkColumnsByTable[key] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add(reader.GetString(2));
            }
        }

        var junctionTables = new HashSet<(string Schema, string Table)>();
        foreach (var (key, columns) in columnsByTable)
        {
            var candidates = columns
                .Where(c => !c.IsComputed && !Named(c.Name).IsAuditColumn() && !(c.IsIdentity && c.IsPrimaryKey))
                .ToList();
            if (candidates.Count != 2)
                continue;

            if (singleColumnFkColumnsByTable.TryGetValue(key, out var fkColumns) &&
                candidates.All(c => fkColumns.Contains(c.Name)))
                junctionTables.Add(key);
        }

        return junctionTables;
    }

    // A NOT NULL text column named exactly "Name" and a NOT NULL boolean column named exactly "IsActive" (TableModel.IsNameActiveTable's bulk form).
    private async Task<HashSet<(string Schema, string Table)>> DetermineNameActiveTablesAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var hasNotNullName = new HashSet<(string Schema, string Table)>();
        var hasNotNullIsActive = new HashSet<(string Schema, string Table)>();

        await using var command = new NpgsqlCommand(AllNameActiveCandidateColumnsQuery, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.GetBoolean(4))
                continue;

            var key = (reader.GetString(0), reader.GetString(1));
            string columnName = reader.GetString(2);
            string udt = reader.GetString(3);
            if (Named(columnName) == "Name" && udt is "varchar" or "text" or "bpchar")
                hasNotNullName.Add(key);
            else if (Named(columnName) == "IsActive" && udt == "bool")
                hasNotNullIsActive.Add(key);
        }

        hasNotNullName.IntersectWith(hasNotNullIsActive);
        return hasNotNullName;
    }

    // ========== columns ==========

    private const string ColumnsQuery = """
        SELECT c.column_name, c.ordinal_position, c.udt_schema, c.udt_name, c.character_maximum_length, c.numeric_precision, c.numeric_scale,
               (c.is_nullable = 'YES') AS is_nullable, c.is_identity, c.identity_start, c.identity_increment,
               c.column_default, c.is_generated, c.generation_expression,
               EXISTS (SELECT 1 FROM pg_index i
                         JOIN pg_class t ON t.oid = i.indrelid
                         JOIN pg_namespace n ON n.oid = t.relnamespace
                         JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = ANY (i.indkey)
                        WHERE i.indisprimary AND n.nspname = c.table_schema AND t.relname = c.table_name AND a.attname = c.column_name) AS is_primary_key,
               EXISTS (SELECT 1 FROM pg_index i
                         JOIN pg_class t ON t.oid = i.indrelid
                         JOIN pg_namespace n ON n.oid = t.relnamespace
                         JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = ANY ((i.indkey::int2[])[0:i.indnkeyatts - 1])
                        WHERE i.indisunique AND NOT i.indisprimary AND n.nspname = c.table_schema AND t.relname = c.table_name AND a.attname = c.column_name) AS is_in_unique_index,
               (SELECT array_agg(e.enumlabel::text ORDER BY e.enumsortorder) FROM pg_type ty
                         JOIN pg_namespace tn ON tn.oid = ty.typnamespace
                         JOIN pg_enum e ON e.enumtypid = ty.oid
                        WHERE ty.typname = c.udt_name AND tn.nspname = c.udt_schema) AS enum_labels,
               (SELECT array_agg(pg_get_constraintdef(con.oid)) FROM pg_constraint con
                         JOIN pg_class t ON t.oid = con.conrelid
                         JOIN pg_namespace n ON n.oid = t.relnamespace
                         JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = con.conkey[1]
                        WHERE con.contype = 'c' AND array_length(con.conkey, 1) = 1
                          AND n.nspname = c.table_schema AND t.relname = c.table_name AND a.attname = c.column_name) AS check_definitions
        FROM information_schema.columns c
        WHERE c.table_schema = @schema AND c.table_name = @table
        ORDER BY c.ordinal_position;
        """;

    protected override async Task<List<RawColumn>> ReadColumnsAsync(DbConnection connection, string schemaName, string tableName, CancellationToken cancellationToken)
    {
        var results = new List<RawColumn>();
        await using var command = new NpgsqlCommand(ColumnsQuery, (NpgsqlConnection)connection);
        command.Parameters.AddWithValue("schema", schemaName);
        command.Parameters.AddWithValue("table", tableName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string columnName = reader.GetString(reader.GetOrdinal("column_name"));
            if (columnName.IsSystemColumn())
                continue;

            int? Int(string name) { int o = reader.GetOrdinal(name); return reader.IsDBNull(o) ? null : Convert.ToInt32(reader.GetValue(o)); }
            string? Text(string name) { int o = reader.GetOrdinal(name); return reader.IsDBNull(o) ? null : reader.GetString(o); }

            string udtName = reader.GetString(reader.GetOrdinal("udt_name"));
            int enumOrdinal = reader.GetOrdinal("enum_labels"), checkOrdinal = reader.GetOrdinal("check_definitions");
            var enumLabels = reader.IsDBNull(enumOrdinal) ? null : ((string[])reader.GetValue(enumOrdinal)).ToList();
            var (sqlType, maxLength, precision, scale, declaration) = MapType(
                udtName, Int("character_maximum_length"), Int("numeric_precision"), Int("numeric_scale"), enumLabels);

            // A value from a list: a native enum type, or a text column a CHECK constraint limits to listed values. The form shows either as a drop-down.
            var choices = enumLabels;
            if (choices is null && sqlType == "varchar" && !reader.IsDBNull(checkOrdinal))
            {
                choices = ((string[])reader.GetValue(checkOrdinal)).Select(ParseCheckValues).FirstOrDefault(v => v is not null);
                // an unbounded text column has no length to size the control by: it is as long as its longest listed value
                if (choices is not null && maxLength <= 0)
                    maxLength = choices.Max(v => v.Length);
            }

            string? columnDefault = Text("column_default");
            bool serial = IsSerialDefault(columnDefault);
            bool identity = Text("is_identity") == "YES" || serial;
            string? generation = Text("generation_expression");

            results.Add(new RawColumn(
                Name: columnName,
                OrdinalPosition: reader.GetInt32(reader.GetOrdinal("ordinal_position")),
                SqlTypeName: sqlType,
                MaxLength: maxLength,
                Precision: precision,
                Scale: scale,
                IsNullable: reader.GetBoolean(reader.GetOrdinal("is_nullable")),
                IsIdentity: identity,
                IdentitySeed: identity ? Int("identity_start") ?? 1 : null,
                IdentityIncrement: identity ? Int("identity_increment") ?? 1 : null,
                ComputedDefinition: Text("is_generated") == "ALWAYS" ? generation ?? "" : null,
                DefaultDefinition: serial ? null : columnDefault,
                IsPrimaryKey: reader.GetBoolean(reader.GetOrdinal("is_primary_key")),
                IsInUniqueIndex: reader.GetBoolean(reader.GetOrdinal("is_in_unique_index")),
                DeclarationOverride: declaration,
                DefaultForCSharp: serial ? null : NormalizeDefault(columnDefault),
                Choices: choices,
                EnumType: enumLabels is null ? null : reader.GetString(reader.GetOrdinal("udt_schema")) + "." + udtName,
                CheckDefinitions: reader.IsDBNull(checkOrdinal) ? null : ((string[])reader.GetValue(checkOrdinal)).ToList()));
        }

        return results;
    }

    private const string ColumnSummariesQuery = """
        SELECT c.column_name, c.udt_name, (c.is_nullable = 'YES') AS is_nullable,
               EXISTS (SELECT 1 FROM pg_index i
                         JOIN pg_class t ON t.oid = i.indrelid
                         JOIN pg_namespace n ON n.oid = t.relnamespace
                         JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = ANY (i.indkey)
                        WHERE i.indisprimary AND n.nspname = c.table_schema AND t.relname = c.table_name AND a.attname = c.column_name) AS is_primary_key
        FROM information_schema.columns c
        WHERE c.table_schema = @schema AND c.table_name = @table
        ORDER BY c.ordinal_position;
        """;

    public override async Task<List<ColumnSummary>> ListColumnSummariesAsync(string schemaName, string tableName, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionRequest.CreatePostgresConnection();
        await connection.OpenAsync(cancellationToken);

        var results = new List<ColumnSummary>();
        await using var command = new NpgsqlCommand(ColumnSummariesQuery, connection);
        command.Parameters.AddWithValue("schema", schemaName);
        command.Parameters.AddWithValue("table", tableName);

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

    // ========== foreign keys ==========

    // pg_constraint keeps the two column lists as parallel arrays (conkey, confkey); unnest pairs them in order.
    private const string ForeignKeysQueryTemplate = """
        SELECT con.conname AS "ConstraintName", {other}n.nspname AS "OtherSchema", {other}c.relname AS "OtherTable",
               pa.attname AS "ReferencingColumn", ra.attname AS "ReferencedColumn"
        FROM pg_constraint con
        JOIN pg_class pc ON pc.oid = con.conrelid
        JOIN pg_namespace pn ON pn.oid = pc.relnamespace
        JOIN pg_class rc ON rc.oid = con.confrelid
        JOIN pg_namespace rn ON rn.oid = rc.relnamespace
        CROSS JOIN LATERAL unnest(con.conkey, con.confkey) WITH ORDINALITY AS k(attnum, refattnum, ord)
        JOIN pg_attribute pa ON pa.attrelid = con.conrelid AND pa.attnum = k.attnum
        JOIN pg_attribute ra ON ra.attrelid = con.confrelid AND ra.attnum = k.refattnum
        WHERE con.contype = 'f' AND {this}n.nspname = @schema AND {this}c.relname = @table
        ORDER BY con.conname, k.ord;
        """;

    protected override async Task<List<ForeignKeyRow>> ReadForeignKeyRowsAsync(
        DbConnection connection, string schemaName, string tableName, bool children, CancellationToken cancellationToken)
    {
        // "this" table is the referencing side (p) for the keys it has, the referenced side (r) for the keys that point at it.
        string sql = ForeignKeysQueryTemplate
            .Replace("{other}n.", children ? "p" + "n." : "rn.").Replace("{other}c.", children ? "pc." : "rc.")
            .Replace("{this}n.", children ? "rn." : "pn.").Replace("{this}c.", children ? "rc." : "pc.");

        await using var command = new NpgsqlCommand(sql, (NpgsqlConnection)connection);
        command.Parameters.AddWithValue("schema", schemaName);
        command.Parameters.AddWithValue("table", tableName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await GroupForeignKeyRowsAsync(reader, cancellationToken);
    }

    // ========== row count and row data ==========

    protected override async Task<long> ReadRowCountAsync(DbConnection connection, string schemaName, string tableName, CancellationToken cancellationToken)
    {
        // The catalog estimate (reltuples) is -1 until a table is first analyzed and stale after a bulk load, so a small or unknown table
        // is counted for real (bounded: the count stops at 10,001 rows); only a table the estimate says is big keeps the estimate.
        await using var estimate = new NpgsqlCommand(
            "SELECT c.reltuples::bigint FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = @schema AND c.relname = @table;", (NpgsqlConnection)connection);
        estimate.Parameters.AddWithValue("schema", schemaName);
        estimate.Parameters.AddWithValue("table", tableName);
        long reltuples = Convert.ToInt64(await estimate.ExecuteScalarAsync(cancellationToken) ?? -1L);
        if (reltuples > 10000)
            return reltuples;

        await using var count = new NpgsqlCommand($"SELECT count(*) FROM (SELECT 1 FROM {QuotedTable(schemaName, tableName)} LIMIT 10001) AS t;", (NpgsqlConnection)connection);
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
        await using var command = new NpgsqlCommand(sql, (NpgsqlConnection)connection);
        command.Parameters.AddWithValue("maxRows", MaxRowDataRows + 1);

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
