using System.Data;
using System.Data.Common;
using CodeGenNew.Connections;
using CodeGenNew.Core;
using Microsoft.Data.SqlClient;

namespace CodeGenNew.SchemaIntrospection;

/// <summary> Builds a fully-populated TableModel for one table by reading live schema metadata from SQL Server. </summary>
public class SqlServerSchemaProvider : SchemaProviderBase
{
    private readonly ConnectionRequest _connectionRequest;

    public SqlServerSchemaProvider(ConnectionRequest connectionRequest, string specialLogicColumnsConfigPath, NamingStyle naming = NamingStyle.AsIs, IReadOnlyCollection<string>? acronyms = null) : base(specialLogicColumnsConfigPath, naming, acronyms)
    {
        _connectionRequest = connectionRequest;
    }

    protected override SqlDialect Dialect => SqlDialect.SqlServer;

    protected override async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = _connectionRequest.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    protected override string Quote(string name) => "[" + name.Replace("]", "]]") + "]";

    private const string ListTablesQuery = """
        SELECT
            s.name AS SchemaName,
            t.name AS TableName,
            CASE WHEN EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id = t.object_id AND i.is_primary_key = 1) THEN 1 ELSE 0 END AS HasPrimaryKey,
            CASE WHEN EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id = t.object_id AND i.is_unique = 1) THEN 1 ELSE 0 END AS HasUniqueIndex,
            (SELECT COUNT(*) FROM sys.index_columns pkc
             INNER JOIN sys.indexes pk ON pk.object_id = pkc.object_id AND pk.index_id = pkc.index_id
             WHERE pk.object_id = t.object_id AND pk.is_primary_key = 1) AS PrimaryKeyColumnCount,
            (SELECT TOP 1 ty.name FROM sys.index_columns pkc
             INNER JOIN sys.indexes pk ON pk.object_id = pkc.object_id AND pk.index_id = pkc.index_id
             INNER JOIN sys.columns c ON c.object_id = pkc.object_id AND c.column_id = pkc.column_id
             INNER JOIN sys.types ty ON ty.user_type_id = c.user_type_id
             WHERE pk.object_id = t.object_id AND pk.is_primary_key = 1) AS PrimaryKeyColumnTypeName
        FROM sys.tables t
        INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
        ORDER BY s.name, t.name;
        """;

    // A single primary key column's SQL type name -> PrimaryKeyShape, matching TableModel.PrimaryKeyShape's
    // own rule exactly (only reachable with pkColumnCount == 1, so a null/other type name means "some other,
    // natural-key type" rather than "no primary key").
    private static PrimaryKeyShape ClassifyPrimaryKeyShape(int pkColumnCount, string? pkColumnTypeName) => pkColumnCount switch
    {
        0 => PrimaryKeyShape.None,
        > 1 => PrimaryKeyShape.Composite,
        _ => pkColumnTypeName switch
        {
            "uniqueidentifier" => PrimaryKeyShape.SingleUniqueIdentifier,
            "int" or "bigint" or "smallint" or "tinyint" => PrimaryKeyShape.SingleInt,
            _ => PrimaryKeyShape.SingleOther
        }
    };

    /// <summary> Lists user tables (system/framework tables filtered out via SystemTableFilter) for the
    /// TreeView. Read-only; a full TableModel is only built for the one table actually selected. </summary>
    public override async Task<List<TableSummary>> ListTablesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionRequest.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var (junctionTables, auditTables) = await DetermineJunctionAndAuditTablesAsync(connection, cancellationToken);
        var tablesWithChildren = await DetermineTablesWithChildForeignKeysAsync(connection, cancellationToken);
        var nameActiveTables = await DetermineNameActiveTablesAsync(connection, cancellationToken);

        var results = new List<TableSummary>();
        await using var command = new SqlCommand(ListTablesQuery, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string tableName = reader.GetString("TableName");
            if (tableName.IsSystemTable())
                continue;

            string schemaName = reader.GetString("SchemaName");
            int pkColumnCount = reader.GetInt32("PrimaryKeyColumnCount");
            string? pkColumnTypeName = reader.GetNullableString("PrimaryKeyColumnTypeName");

            results.Add(new TableSummary
            {
                SchemaName = schemaName,
                TableName = tableName,
                HasPrimaryKey = reader.GetInt32("HasPrimaryKey") == 1,
                HasUniqueIndex = reader.GetInt32("HasUniqueIndex") == 1,
                IsJunctionTable = junctionTables.Contains((schemaName, tableName)),
                HasChildForeignKeys = tablesWithChildren.Contains((schemaName, tableName)),
                PrimaryKeyShape = ClassifyPrimaryKeyShape(pkColumnCount, pkColumnTypeName),
                IsNameActiveTable = nameActiveTables.Contains((schemaName, tableName)),
                IsAuditTable = auditTables.Contains((schemaName, tableName)),
                IsReservedWordName = tableName.IsSqlReservedWord(),
                IsCSharpReservedWordName = tableName.IsCSharpReservedWord()
            });
        }

        return results;
    }

    // Bulk equivalent of TableModel.IsJunctionTable's JunctionCandidateColumns/IsJunctionTable rule, computed
    // for every table in two catalog-only queries (no per-table round trips) instead of building a full
    // TableModel per table. Reuses AuditColumnClassifier directly rather than re-expressing its name
    // patterns in T-SQL, so the two can never drift apart.
    private const string AllColumnsForJunctionCheckQuery = """
        SELECT OBJECT_SCHEMA_NAME(c.object_id) AS SchemaName, OBJECT_NAME(c.object_id) AS TableName, c.name AS ColumnName,
               c.is_identity AS IsIdentity,
               CASE WHEN pk.column_id IS NOT NULL THEN 1 ELSE 0 END AS IsPrimaryKey,
               CASE WHEN cc.object_id IS NOT NULL THEN 1 ELSE 0 END AS IsComputed
        FROM sys.columns c
        INNER JOIN sys.tables t ON t.object_id = c.object_id
        LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
        LEFT JOIN (
            SELECT ic.object_id, ic.column_id
            FROM sys.indexes i
            INNER JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            WHERE i.is_primary_key = 1
        ) pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id;
        """;

    private const string AllSingleColumnForeignKeysQuery = """
        SELECT OBJECT_SCHEMA_NAME(fkc.parent_object_id) AS SchemaName, OBJECT_NAME(fkc.parent_object_id) AS TableName, cpar.name AS ColumnName
        FROM sys.foreign_key_columns fkc
        INNER JOIN sys.columns cpar ON cpar.object_id = fkc.parent_object_id AND cpar.column_id = fkc.parent_column_id
        WHERE (SELECT COUNT(*) FROM sys.foreign_key_columns fkc2 WHERE fkc2.constraint_object_id = fkc.constraint_object_id) = 1;
        """;

    private static async Task<(HashSet<(string Schema, string Table)> Junction, HashSet<(string Schema, string Table)> Audit)> DetermineJunctionAndAuditTablesAsync(
        SqlConnection connection, CancellationToken cancellationToken)
    {
        var columnsByTable = new Dictionary<(string Schema, string Table), List<(string Name, bool IsIdentity, bool IsPrimaryKey, bool IsComputed)>>();
        await using (var command = new SqlCommand(AllColumnsForJunctionCheckQuery, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var key = (reader.GetString("SchemaName"), reader.GetString("TableName"));
                if (!columnsByTable.TryGetValue(key, out var list))
                    columnsByTable[key] = list = [];
                list.Add((reader.GetString("ColumnName"),
                          reader.GetBoolean("IsIdentity"),
                          reader.GetInt32("IsPrimaryKey") == 1,
                          reader.GetInt32("IsComputed") == 1));
            }
        }

        var singleColumnFkColumnsByTable = new Dictionary<(string Schema, string Table), HashSet<string>>();
        await using (var command = new SqlCommand(AllSingleColumnForeignKeysQuery, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var key = (reader.GetString("SchemaName"), reader.GetString("TableName"));
                if (!singleColumnFkColumnsByTable.TryGetValue(key, out var set))
                    singleColumnFkColumnsByTable[key] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add(reader.GetString("ColumnName"));
            }
        }

        var junctionTables = new HashSet<(string Schema, string Table)>();
        var auditTables = new HashSet<(string Schema, string Table)>();
        foreach (var (key, columns) in columnsByTable)
        {
            if (AuditTableShape.IsAuditTable(columns.Select(c => c.Name)))
                auditTables.Add(key);

            var candidates = columns
                .Where(c => !c.IsComputed && !c.Name.IsAuditColumn() && !(c.IsIdentity && c.IsPrimaryKey))
                .ToList();
            if (candidates.Count != 2)
                continue;

            if (singleColumnFkColumnsByTable.TryGetValue(key, out var fkColumns) &&
                candidates.All(c => fkColumns.Contains(c.Name)))
                junctionTables.Add(key);
        }

        return (junctionTables, auditTables);
    }

    // Every table referenced by at least one foreign key, i.e. every table that is a PARENT of some other
    // table (TableModel.HasAtLeastOneChildForeignKey's bulk equivalent) -- one catalog-only query, no
    // per-table round trips, reused across every row of ListTablesAsync the same way DetermineJunctionTablesAsync is.
    private const string AllReferencedTablesQuery = """
        SELECT DISTINCT OBJECT_SCHEMA_NAME(fk.referenced_object_id) AS SchemaName, OBJECT_NAME(fk.referenced_object_id) AS TableName
        FROM sys.foreign_keys fk;
        """;

    private static async Task<HashSet<(string Schema, string Table)>> DetermineTablesWithChildForeignKeysAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var tables = new HashSet<(string Schema, string Table)>();
        await using var command = new SqlCommand(AllReferencedTablesQuery, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            tables.Add((reader.GetString("SchemaName"), reader.GetString("TableName")));
        return tables;
    }

    // Bulk equivalent of TableModel.IsNameActiveTable: a NOT NULL text column named exactly "Name" and a NOT
    // NULL bit column named exactly "IsActive" -- an exact-name structural test (like the junction-table
    // detection above), not a SpecialLogicColumns.config pattern rule, matching how every existing inline
    // check of this shape was always written (API_Crud.tt, CS_Entity.tt, CS_Repo.tt, the WinUI3 CRUD-screen
    // family). One catalog query, grouped in-memory, rather than a per-table round trip.
    private const string AllNameActiveCandidateColumnsQuery = """
        SELECT OBJECT_SCHEMA_NAME(c.object_id) AS SchemaName, OBJECT_NAME(c.object_id) AS TableName, c.name AS ColumnName,
               ty.name AS SqlTypeName, c.is_nullable AS IsNullable
        FROM sys.columns c
        INNER JOIN sys.tables t ON t.object_id = c.object_id
        INNER JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE c.name IN ('Name', 'IsActive');
        """;

    private static async Task<HashSet<(string Schema, string Table)>> DetermineNameActiveTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var hasNotNullName = new HashSet<(string Schema, string Table)>();
        var hasNotNullIsActiveBit = new HashSet<(string Schema, string Table)>();

        await using var command = new SqlCommand(AllNameActiveCandidateColumnsQuery, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = (reader.GetString("SchemaName"), reader.GetString("TableName"));
            string columnName = reader.GetString("ColumnName");
            string sqlTypeName = reader.GetString("SqlTypeName");
            bool isNullable = reader.GetBoolean("IsNullable");
            if (isNullable)
                continue;

            bool isStringType = sqlTypeName is "char" or "nchar" or "varchar" or "nvarchar" or "text" or "ntext";
            if (columnName == "Name" && isStringType)
                hasNotNullName.Add(key);
            else if (columnName == "IsActive" && sqlTypeName == "bit")
                hasNotNullIsActiveBit.Add(key);
        }

        hasNotNullName.IntersectWith(hasNotNullIsActiveBit);
        return hasNotNullName;
    }

    private const string ColumnSummariesQuery = """
        SELECT
            c.name AS ColumnName,
            t.name AS SqlTypeName,
            c.is_nullable AS IsNullable,
            CASE WHEN pk.column_id IS NOT NULL THEN 1 ELSE 0 END AS IsPrimaryKey
        FROM sys.columns c
        INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
        LEFT JOIN (
            SELECT ic2.object_id, ic2.column_id
            FROM sys.indexes i
            INNER JOIN sys.index_columns ic2 ON ic2.object_id = i.object_id AND ic2.index_id = i.index_id
            WHERE i.is_primary_key = 1
        ) pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
        WHERE c.object_id = OBJECT_ID(@fullTableName)
        ORDER BY c.column_id;
        """;

    /// <summary> Column names/types for display under a table node in the TreeView
    /// (Docs/ARCHITECTURE.md section 7) -- loaded lazily, only when a table is expanded, and much cheaper than
    /// BuildTableModelAsync's full column metadata since nothing here drives code generation. </summary>
    public override async Task<List<ColumnSummary>> ListColumnSummariesAsync(string schemaName, string tableName, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionRequest.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var results = new List<ColumnSummary>();
        await using var command = new SqlCommand(ColumnSummariesQuery, connection);
        command.Parameters.AddWithValue("@fullTableName", $"[{schemaName}].[{tableName}]");

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string columnName = reader.GetString("ColumnName");
            if (columnName.IsSystemColumn())
                continue;

            results.Add(new ColumnSummary
            {
                Name = columnName,
                SqlTypeName = reader.GetString("SqlTypeName"),
                IsNullable = reader.GetBoolean("IsNullable"),
                IsPrimaryKey = reader.GetInt32("IsPrimaryKey") == 1
            });
        }

        return results;
    }

    protected override async Task<long> ReadRowCountAsync(DbConnection connection, string schemaName, string tableName, CancellationToken cancellationToken)
    {
        // Approximate row count from the catalog (no table scan): the sum over the heap/clustered-index partitions.
        await using var command = new SqlCommand(
            "SELECT COALESCE(SUM(p.rows), 0) FROM sys.partitions p WHERE p.object_id = OBJECT_ID(@fullTableName) AND p.index_id IN (0, 1);", (SqlConnection)connection);
        command.Parameters.AddWithValue("@fullTableName", QuotedTable(schemaName, tableName));
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    /// <summary> Reads every row (one value per model column, model order), ordered by primary key so the generated
    /// output is stable and parents with lower keys come first. Read-only. </summary>
    protected override async Task<List<object?[]>> ReadRowsAsync(
        DbConnection connection, string schemaName, string tableName, List<ColumnModel> columns, List<ColumnModel> primaryKeyColumns,
        CancellationToken cancellationToken)
    {
        string fullTableName = QuotedTable(schemaName, tableName);
        // Computed columns can't be loaded anywhere, and evaluating one can itself fail (e.g. overflow), so they
        // aren't selected; their slot in each row stays null to keep the row arrays aligned with Columns.
        var readIndexes = Enumerable.Range(0, columns.Count).Where(i => !columns[i].IsComputed).ToList();
        string columnList = string.Join(", ", readIndexes.Select(i => Quote(columns[i].DbName)));
        string orderBy = primaryKeyColumns.Count > 0 ? " ORDER BY " + string.Join(", ", primaryKeyColumns.Select(c => Quote(c.DbName))) : "";
        string sql = $"SELECT TOP (@maxRows) {columnList} FROM {fullTableName}{orderBy};";

        var rows = new List<object?[]>();
        await using var command = new SqlCommand(sql, (SqlConnection)connection);
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

    private const string ColumnsQuery = """
        SELECT
            c.name AS ColumnName,
            c.column_id AS OrdinalPosition,
            t.name AS SqlTypeName,
            c.max_length AS MaxLength,
            c.precision AS Precision,
            c.scale AS Scale,
            c.is_nullable AS IsNullable,
            c.is_identity AS IsIdentity,
            ic.seed_value AS IdentitySeed,
            ic.increment_value AS IdentityIncrement,
            cc.definition AS ComputedDefinition,
            dc.definition AS DefaultDefinition,
            CASE WHEN pk.column_id IS NOT NULL THEN 1 ELSE 0 END AS IsPrimaryKey,
            CASE WHEN EXISTS (
                SELECT 1 FROM sys.index_columns uic
                INNER JOIN sys.indexes ui ON ui.object_id = uic.object_id AND ui.index_id = uic.index_id
                WHERE uic.object_id = c.object_id AND uic.column_id = c.column_id
                  AND ui.is_unique = 1 AND ui.is_primary_key = 0 AND uic.is_included_column = 0
            ) THEN 1 ELSE 0 END AS IsInUniqueIndex
        FROM sys.columns c
        INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
        LEFT JOIN sys.identity_columns ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id
        LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
        LEFT JOIN sys.default_constraints dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
        LEFT JOIN (
            SELECT ic2.object_id, ic2.column_id
            FROM sys.indexes i
            INNER JOIN sys.index_columns ic2 ON ic2.object_id = i.object_id AND ic2.index_id = i.index_id
            WHERE i.is_primary_key = 1
        ) pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
        WHERE c.object_id = OBJECT_ID(@fullTableName)
        ORDER BY c.column_id;
        """;

    private const string CheckConstraintsQuery = """
        SELECT c.name AS ColumnName, cc.definition AS Definition
        FROM sys.check_constraints cc
        LEFT JOIN sys.columns c ON c.object_id = cc.parent_object_id AND c.column_id = cc.parent_column_id
        WHERE cc.parent_object_id = OBJECT_ID(@fullTableName) AND cc.is_disabled = 0
        """;

    // the MS_Description extended property: of a column (minor_id = its column id) or of the table itself (minor_id = 0)
    private const string DescriptionsQuery = """
        SELECT c.name AS ColumnName, CAST(ep.value AS nvarchar(4000)) AS Description
        FROM sys.extended_properties ep
        JOIN sys.columns c ON c.object_id = ep.major_id AND c.column_id = ep.minor_id
        WHERE ep.class = 1 AND ep.name = N'MS_Description' AND ep.major_id = OBJECT_ID(@fullTableName)
        """;

    private const string TableDescriptionQuery = """
        SELECT CAST(ep.value AS nvarchar(4000))
        FROM sys.extended_properties ep
        WHERE ep.class = 1 AND ep.name = N'MS_Description' AND ep.minor_id = 0 AND ep.major_id = OBJECT_ID(@fullTableName)
        """;

    protected override async Task<string?> ReadTableDescriptionAsync(DbConnection connection, string schemaName, string tableName, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(TableDescriptionQuery, (SqlConnection)connection);
        command.Parameters.AddWithValue("@fullTableName", QuotedTable(schemaName, tableName));
        return await command.ExecuteScalarAsync(cancellationToken) is string text && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
    }

    protected override async Task<List<RawColumn>> ReadColumnsAsync(DbConnection connection, string schemaName, string tableName, CancellationToken cancellationToken)
    {
        var columns = await ReadRawColumnsAsync(connection, schemaName, tableName, cancellationToken);
        var descriptions = new List<(string, string)>();
        await using (var describe = new SqlCommand(DescriptionsQuery, (SqlConnection)connection))
        {
            describe.Parameters.AddWithValue("@fullTableName", QuotedTable(schemaName, tableName));
            await using var described = await describe.ExecuteReaderAsync(cancellationToken);
            while (await described.ReadAsync(cancellationToken))
                descriptions.Add((described.GetString(0), described.IsDBNull(1) ? "" : described.GetString(1)));
        }
        columns = WithDescriptions(columns, descriptions);
        var checks = new List<(string?, string)>();
        await using var command = new SqlCommand(CheckConstraintsQuery, (SqlConnection)connection);
        command.Parameters.AddWithValue("@fullTableName", QuotedTable(schemaName, tableName));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            checks.Add((reader.GetNullableString("ColumnName"), reader.GetString(1)));
        return WithChecks(columns, checks);
    }

    private async Task<List<RawColumn>> ReadRawColumnsAsync(DbConnection connection, string schemaName, string tableName, CancellationToken cancellationToken)
    {
        var results = new List<RawColumn>();
        await using var command = new SqlCommand(ColumnsQuery, (SqlConnection)connection);
        command.Parameters.AddWithValue("@fullTableName", QuotedTable(schemaName, tableName));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string columnName = reader.GetString("ColumnName");
            if (columnName.IsSystemColumn())
                continue;

            results.Add(new RawColumn(
                Name: columnName,
                OrdinalPosition: reader.GetInt32("OrdinalPosition"),
                SqlTypeName: reader.GetString("SqlTypeName"),
                MaxLength: reader.GetInt16("MaxLength"),
                Precision: reader.GetByte("Precision"),
                Scale: reader.GetByte("Scale"),
                IsNullable: reader.GetBoolean("IsNullable"),
                IsIdentity: reader.GetBoolean("IsIdentity"),
                // seed_value/increment_value are sql_variant -- the underlying numeric type varies by
                // the identity column's own type (int, bigint, decimal, ...), so read via GetValue + Convert
                // rather than assuming a fixed CLR type like GetDecimal.
                IdentitySeed: reader.GetNullableInt32("IdentitySeed"),
                IdentityIncrement: reader.GetNullableInt32("IdentityIncrement"),
                ComputedDefinition: reader.GetNullableString("ComputedDefinition"),
                DefaultDefinition: reader.GetNullableString("DefaultDefinition"),
                IsPrimaryKey: reader.GetInt32("IsPrimaryKey") == 1,
                IsInUniqueIndex: reader.GetInt32("IsInUniqueIndex") == 1
            ));
        }

        return results;
    }

    // Both queries return the same shape (ConstraintName, the OTHER table's schema/name, the two sides'
    // column lists) -- aliased identically on purpose so one grouping loop can read either one.
    private const string ForeignKeysQuery = """
        SELECT
            fk.name AS ConstraintName,
            cpar.name AS ReferencingColumn,
            OBJECT_SCHEMA_NAME(fk.referenced_object_id) AS OtherSchema,
            OBJECT_NAME(fk.referenced_object_id) AS OtherTable,
            cref.name AS ReferencedColumn
        FROM sys.foreign_keys fk
        INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
        INNER JOIN sys.columns cpar ON cpar.object_id = fkc.parent_object_id AND cpar.column_id = fkc.parent_column_id
        INNER JOIN sys.columns cref ON cref.object_id = fkc.referenced_object_id AND cref.column_id = fkc.referenced_column_id
        WHERE fkc.parent_object_id = OBJECT_ID(@fullTableName)
        ORDER BY fk.name, fkc.constraint_column_id;
        """;

    // The mirror image of ForeignKeysQuery: same join, but filtered by referenced_object_id (this table is
    // the PARENT) instead of parent_object_id, so "the other table" is the CHILD referencing this one.
    private const string ChildForeignKeysQuery = """
        SELECT
            fk.name AS ConstraintName,
            OBJECT_SCHEMA_NAME(fk.parent_object_id) AS OtherSchema,
            OBJECT_NAME(fk.parent_object_id) AS OtherTable,
            cpar.name AS ReferencingColumn,
            cref.name AS ReferencedColumn
        FROM sys.foreign_keys fk
        INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
        INNER JOIN sys.columns cpar ON cpar.object_id = fkc.parent_object_id AND cpar.column_id = fkc.parent_column_id
        INNER JOIN sys.columns cref ON cref.object_id = fkc.referenced_object_id AND cref.column_id = fkc.referenced_column_id
        WHERE fkc.referenced_object_id = OBJECT_ID(@fullTableName)
        ORDER BY fk.name, fkc.constraint_column_id;
        """;

    protected override async Task<List<ForeignKeyRow>> ReadForeignKeyRowsAsync(
        DbConnection connection, string schemaName, string tableName, bool children, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(children ? ChildForeignKeysQuery : ForeignKeysQuery, (SqlConnection)connection);
        command.Parameters.AddWithValue("@fullTableName", QuotedTable(schemaName, tableName));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await GroupForeignKeyRowsAsync(reader, cancellationToken);
    }

    private const string IndexesQuery = """
        SELECT i.name, CAST(i.is_unique AS int), c.name
        FROM sys.indexes i
        JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0 AND ic.key_ordinal > 0
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.object_id = OBJECT_ID(@fullTableName) AND i.type > 0 AND i.is_hypothetical = 0 AND i.has_filter = 0
        ORDER BY i.index_id, ic.key_ordinal;
        """;

    protected override async Task<List<IndexRow>> ReadIndexRowsAsync(DbConnection connection, string schemaName, string tableName, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(IndexesQuery, (SqlConnection)connection);
        command.Parameters.AddWithValue("@fullTableName", QuotedTable(schemaName, tableName));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await GroupIndexRowsAsync(reader, cancellationToken);
    }
}
