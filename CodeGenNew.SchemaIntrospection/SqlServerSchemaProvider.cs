using System.Data;
using CodeGenNew.Connections;
using CodeGenNew.Core;
using Microsoft.Data.SqlClient;

namespace CodeGenNew.SchemaIntrospection;

/// <summary> Builds a fully-populated TableModel for one table by reading live schema metadata from SQL Server. </summary>
public class SqlServerSchemaProvider
{
    private readonly ConnectionRequest _connectionRequest;
    private readonly string _specialLogicColumnsConfigPath;

    public SqlServerSchemaProvider(ConnectionRequest connectionRequest, string specialLogicColumnsConfigPath)
    {
        _connectionRequest = connectionRequest;
        _specialLogicColumnsConfigPath = specialLogicColumnsConfigPath;
    }

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
    public async Task<List<TableSummary>> ListTablesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionRequest.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var junctionTables = await DetermineJunctionTablesAsync(connection, cancellationToken);
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

    private static async Task<HashSet<(string Schema, string Table)>> DetermineJunctionTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
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
        foreach (var (key, columns) in columnsByTable)
        {
            var candidates = columns
                .Where(c => !c.IsComputed && !c.Name.IsAuditColumn() && !(c.IsIdentity && c.IsPrimaryKey))
                .ToList();
            if (candidates.Count != 2)
                continue;

            if (singleColumnFkColumnsByTable.TryGetValue(key, out var fkColumns) &&
                candidates.All(c => fkColumns.Contains(c.Name)))
                junctionTables.Add(key);
        }

        return junctionTables;
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

    /// <summary> Column names/types for display under a table node in the TreeView (Docs/specs.md
    /// section 9.4) -- loaded lazily, only when a table is expanded, and much cheaper than
    /// BuildTableModelAsync's full column metadata since nothing here drives code generation. </summary>
    public async Task<List<ColumnSummary>> ListColumnSummariesAsync(string schemaName, string tableName, CancellationToken cancellationToken = default)
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

    /// <summary> Row-data loading is meant for small reference/seed tables (SP_Load.tt). Refusing beyond this many rows
    /// keeps a mis-click on a big table from reading the whole thing into memory and into one generated file. </summary>
    public const int MaxRowDataRows = 5000;

    /// <param name="includeRowData"> Also read the table's rows into TableModel.Rows (read-only SELECT). Only
    /// templates that set NeedsRowData=true in their .tt.config need this. </param>
    /// <param name="includeReferencedDisplayColumns"> Also read each foreign-keyed table's columns to fill
    /// ForeignKeyModel.ReferencedDisplayColumns (read-only catalog queries). Only templates that set
    /// NeedsReferencedDisplayColumns=true (SP_Lookup) need this. </param>
    public async Task<TableModel> BuildTableModelAsync(
        string schemaName, string tableName, bool includeRowData = false, bool includeReferencedDisplayColumns = false,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionRequest.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        string fullName = $"[{schemaName}].[{tableName}]";
        var rawColumns = await ReadColumnsAsync(connection, fullName, cancellationToken);
        var foreignKeys = await ReadForeignKeysAsync(connection, fullName, cancellationToken);
        var childForeignKeys = await ReadChildForeignKeysAsync(connection, fullName, cancellationToken);

        var rules = SpecialLogicColumnsConfig.Load(_specialLogicColumnsConfigPath);
        if (includeReferencedDisplayColumns)
            foreignKeys = await AttachReferencedDisplayColumnsAsync(connection, foreignKeys, rules, cancellationToken);
        var columnNames = rawColumns.Select(c => c.Name).ToList();

        var columns = rawColumns.Select(c => BuildColumnModel(c, rules)).ToList();
        var primaryKeyColumns = columns.Where(c => c.IsPrimaryKey).OrderBy(c => c.OrdinalPosition).ToList();

        var (hasActivePair, activeCol, inactiveCol) = EvaluatePair(rules, "IsActiveFlag", columnNames, columns);
        var (hasStartEnd, startCol, endCol) = EvaluatePair(rules, "StartEndDate", columnNames, columns);
        var (hasSoftDelete, deletedCol, deletedDateCol) = EvaluatePair(rules, "SoftDelete", columnNames, columns);

        // Exact-name only (not a SpecialLogicColumns.config pattern rule -- that engine only matches one
        // column per side, and this needs two per side).
        ColumnModel? FindExact(string name) => columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(name));
        var inDateCol = FindExact("DateIn");
        var inTimeCol = FindExact("TimeIn");
        var outDateCol = FindExact("DateOut");
        var outTimeCol = FindExact("TimeOut");
        bool hasInOutDateTime = inDateCol is not null && inTimeCol is not null && outDateCol is not null && outTimeCol is not null;

        var rows = includeRowData
            ? await ReadRowsAsync(connection, fullName, columns, primaryKeyColumns, cancellationToken)
            : [];

        return new TableModel
        {
            SchemaName = schemaName,
            TableName = tableName,
            QuotedName = fullName,
            IsReservedWordName = tableName.IsSqlReservedWord(),
            IsCSharpReservedWordName = tableName.IsCSharpReservedWord(),
            Columns = columns,
            PrimaryKeyColumns = primaryKeyColumns,
            DisplayColumns = columns.SelectDisplayColumns(foreignKeys.SelectMany(fk => fk.ReferencingColumns).ToList()),
            HasReferencedDisplayColumns = includeReferencedDisplayColumns,
            HasRowData = includeRowData,
            Rows = rows,
            ForeignKeys = foreignKeys,
            ChildForeignKeys = childForeignKeys,
            HasActiveInactivePair = hasActivePair,
            ActiveColumn = activeCol,
            InactiveDateColumn = inactiveCol,
            HasStartEndDatePair = hasStartEnd,
            StartDateColumn = startCol,
            EndDateColumn = endCol,
            HasSoftDelete = hasSoftDelete,
            IsDeletedColumn = deletedCol,
            DeletedDateColumn = deletedDateCol,
            HasInOutDateTimePair = hasInOutDateTime,
            InDateColumn = hasInOutDateTime ? inDateCol : null,
            InTimeColumn = hasInOutDateTime ? inTimeCol : null,
            OutDateColumn = hasInOutDateTime ? outDateCol : null,
            OutTimeColumn = hasInOutDateTime ? outTimeCol : null
        };
    }

    private static string Bracket(string name) => "[" + name.Replace("]", "]]") + "]";

    /// <summary> Reads every row (one value per model column, model order), ordered by primary key so the generated
    /// output is stable and parents with lower keys come first. Read-only. </summary>
    private static async Task<List<object?[]>> ReadRowsAsync(
        SqlConnection connection, string fullTableName, List<ColumnModel> columns, List<ColumnModel> primaryKeyColumns,
        CancellationToken cancellationToken)
    {
        // Computed columns can't be loaded anywhere, and evaluating one can itself fail (e.g. overflow), so they
        // aren't selected; their slot in each row stays null to keep the row arrays aligned with Columns.
        var readIndexes = Enumerable.Range(0, columns.Count).Where(i => !columns[i].IsComputed).ToList();
        string columnList = string.Join(", ", readIndexes.Select(i => Bracket(columns[i].Name)));
        string orderBy = primaryKeyColumns.Count > 0 ? " ORDER BY " + string.Join(", ", primaryKeyColumns.Select(c => Bracket(c.Name))) : "";
        string sql = $"SELECT TOP (@maxRows) {columnList} FROM {fullTableName}{orderBy};";

        var rows = new List<object?[]>();
        await using var command = new SqlCommand(sql, connection);
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

    private static int? RankInCategory(List<SpecialLogicRule> rules, string category, string columnName)
    {
        var rule = rules.FirstOrDefault(r => r.Category.EqualsIgnoreCase(category) && !r.IsPairRule);
        return rule?.MatchRank(columnName);
    }

    // A Lookup shows people readable values: not blobs, XML, or unbounded text.
    private static bool IsDisplayEligibleType(SqlDbType t) =>
        t is not (SqlDbType.Text or SqlDbType.NText or SqlDbType.Xml or SqlDbType.Image or SqlDbType.Binary
                  or SqlDbType.VarBinary or SqlDbType.Variant);

    /// <summary> For each foreign key, reads the referenced table's columns (one catalog query per distinct table) and
    /// records which of them are its display columns. </summary>
    private static async Task<List<ForeignKeyModel>> AttachReferencedDisplayColumnsAsync(
        SqlConnection connection, List<ForeignKeyModel> foreignKeys, List<SpecialLogicRule> rules, CancellationToken cancellationToken)
    {
        var displayColumnsByTable = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ForeignKeyModel>();

        foreach (var fk in foreignKeys)
        {
            string referencedName = $"[{fk.ReferencedSchema}].[{fk.ReferencedTable}]";
            if (!displayColumnsByTable.TryGetValue(referencedName, out var displayColumns))
            {
                var referencedColumns = (await ReadColumnsAsync(connection, referencedName, cancellationToken))
                    .Select(raw => BuildColumnModel(raw, rules))
                    .ToList();
                var referencedForeignKeys = await ReadForeignKeysAsync(connection, referencedName, cancellationToken);
                displayColumns = referencedColumns
                    .SelectDisplayColumns(referencedForeignKeys.SelectMany(fk => fk.ReferencingColumns).ToList())
                    .Select(c => c.Name).ToList();
                displayColumnsByTable[referencedName] = displayColumns;
            }

            result.Add(new ForeignKeyModel
            {
                ConstraintName = fk.ConstraintName,
                ReferencingColumns = fk.ReferencingColumns,
                ReferencedSchema = fk.ReferencedSchema,
                ReferencedTable = fk.ReferencedTable,
                ReferencedColumns = fk.ReferencedColumns,
                ReferencedDisplayColumns = displayColumns
            });
        }

        return result;
    }

    private static bool MatchesCategory(List<SpecialLogicRule> rules, string category, string columnName)
    {
        var rule = rules.FirstOrDefault(r => r.Category.EqualsIgnoreCase(category) && !r.IsPairRule);
        return rule is not null && rule.MatchesColumnRule(columnName);
    }

    private static (bool, ColumnModel?, ColumnModel?) EvaluatePair(
        List<SpecialLogicRule> rules, string category, List<string> columnNames, List<ColumnModel> columns)
    {
        var rule = rules.FirstOrDefault(r => r.Category.EqualsIgnoreCase(category) && r.IsPairRule);
        if (rule is null)
            return (false, null, null);

        var match = rule.EvaluatePairRule(columnNames);
        if (match is null)
            return (false, null, null);

        var flagColumn = columns.First(c => c.Name.EqualsIgnoreCase(match.Value.FlagColumn));
        var companionColumn = columns.First(c => c.Name.EqualsIgnoreCase(match.Value.CompanionColumn));
        return (true, flagColumn, companionColumn);
    }

    private static ColumnModel BuildColumnModel(RawColumn raw, List<SpecialLogicRule> rules)
    {
        var sqlType = SqlTypeClassifier.MapSqlTypeName(raw.SqlTypeName);
        bool isInteger = SqlTypeClassifier.IsIntegerColumn(sqlType);
        bool isNumeric = SqlTypeClassifier.IsNumericColumn(sqlType);
        bool isMoney = SqlTypeClassifier.IsMoneyColumn(sqlType);
        bool isString = SqlTypeClassifier.IsStringColumn(sqlType);
        bool isDate = SqlTypeClassifier.IsDateColumn(sqlType);
        bool isBoolean = SqlTypeClassifier.IsBooleanColumn(sqlType);

        int? maxLength = isString || sqlType is SqlDbType.Binary or SqlDbType.VarBinary ? raw.MaxLength : null;

        return new ColumnModel
        {
            Name = raw.Name,
            QuotedName = $"[{raw.Name}]",
            IsReservedWordName = raw.Name.IsSqlReservedWord(),
            IsCSharpReservedWordName = raw.Name.IsCSharpReservedWord(),
            SqlType = sqlType,
            SqlTypeDeclaration = SqlTypeClassifier.BuildDeclaration(raw.SqlTypeName, sqlType, raw.MaxLength, raw.Precision, raw.Scale),
            MaxLength = maxLength,
            Precision = sqlType == SqlDbType.Decimal ? raw.Precision : null,
            Scale = sqlType == SqlDbType.Decimal ? raw.Scale : null,
            IsNullable = raw.IsNullable,
            OrdinalPosition = raw.OrdinalPosition,
            IsIdentity = raw.IsIdentity,
            IdentitySeed = raw.IdentitySeed,
            IdentityIncrement = raw.IdentityIncrement,
            IsPrimaryKey = raw.IsPrimaryKey,
            IsInUniqueIndex = raw.IsInUniqueIndex,
            IsComputed = raw.ComputedDefinition is not null,
            ComputedDefinition = raw.ComputedDefinition,
            DatabaseDefaultSql = raw.DefaultDefinition,
            SuggestedCSharpDefaultValueLiteral = ColumnDefaultResolver.Resolve(
                raw.Name, sqlType, raw.DefaultDefinition, isBoolean, isDate, isMoney, isNumeric, isInteger, isString),
            IsIntegerColumn = isInteger,
            IsNumericColumn = isNumeric,
            IsMoneyColumn = isMoney,
            IsStringColumn = isString,
            IsDateColumn = isDate,
            IsBooleanColumn = isBoolean,
            IsAuditColumn = raw.Name.IsAuditColumn(),
            IsCreateDateColumn = MatchesCategory(rules, "CreateDateColumn", raw.Name),
            DisplayRank = IsDisplayEligibleType(sqlType) ? RankInCategory(rules, "DisplayColumn", raw.Name) : null,
            IsCreateUserColumn = MatchesCategory(rules, "CreateUserColumn", raw.Name),
            IsModifiedDateColumn = MatchesCategory(rules, "ModifiedDateColumn", raw.Name),
            IsModifiedUserColumn = MatchesCategory(rules, "ModifiedUserColumn", raw.Name),
            IsLastChangedDateColumn = MatchesCategory(rules, "LastChangedDateColumn", raw.Name),
            IsInactiveReasonColumn = MatchesCategory(rules, "InactiveReasonColumn", raw.Name),
            IsAdminFlagColumn = MatchesCategory(rules, "AdminFlagColumn", raw.Name),
            IsFilePathColumn = MatchesCategory(rules, "FilePathColumn", raw.Name),
            ParameterName = raw.Name.ToSqlParameterName(sqlType)
        };
    }

    private sealed record RawColumn(
        string Name, int OrdinalPosition, string SqlTypeName, int MaxLength, int Precision, int Scale,
        bool IsNullable, bool IsIdentity, int? IdentitySeed, int? IdentityIncrement,
        string? ComputedDefinition, string? DefaultDefinition, bool IsPrimaryKey, bool IsInUniqueIndex);

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

    private static async Task<List<RawColumn>> ReadColumnsAsync(SqlConnection connection, string fullTableName, CancellationToken cancellationToken)
    {
        var results = new List<RawColumn>();
        await using var command = new SqlCommand(ColumnsQuery, connection);
        command.Parameters.AddWithValue("@fullTableName", fullTableName);

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
    // column lists) -- aliased identically on purpose so ReadForeignKeyRowsAsync below can read either one,
    // instead of two near-identical grouping loops that only differed in which alias named "the other table".
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

    private sealed record ForeignKeyRow(string ConstraintName, string OtherSchema, string OtherTable, List<string> ReferencingColumns, List<string> ReferencedColumns);

    /// <summary> Runs either ForeignKeysQuery or ChildForeignKeysQuery and groups its rows by constraint (a
    /// composite foreign key spans several rows, one per column pair) -- the one grouping loop both
    /// ReadForeignKeysAsync and ReadChildForeignKeysAsync build their own model type from. </summary>
    private static async Task<List<ForeignKeyRow>> ReadForeignKeyRowsAsync(SqlConnection connection, string query, string fullTableName, CancellationToken cancellationToken)
    {
        var grouped = new Dictionary<string, ForeignKeyRow>();

        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@fullTableName", fullTableName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string constraintName = reader.GetString("ConstraintName");
            if (!grouped.TryGetValue(constraintName, out var row))
            {
                row = new ForeignKeyRow(constraintName,
                    reader.GetString("OtherSchema"),
                    reader.GetString("OtherTable"),
                    [], []);
                grouped[constraintName] = row;
            }

            row.ReferencingColumns.Add(reader.GetString("ReferencingColumn"));
            row.ReferencedColumns.Add(reader.GetString("ReferencedColumn"));
        }

        return grouped.Values.ToList();
    }

    private static async Task<List<ForeignKeyModel>> ReadForeignKeysAsync(SqlConnection connection, string fullTableName, CancellationToken cancellationToken)
    {
        var rows = await ReadForeignKeyRowsAsync(connection, ForeignKeysQuery, fullTableName, cancellationToken);
        return rows.Select(r => new ForeignKeyModel
        {
            ConstraintName = r.ConstraintName,
            ReferencingColumns = r.ReferencingColumns,
            ReferencedSchema = r.OtherSchema,
            ReferencedTable = r.OtherTable,
            ReferencedColumns = r.ReferencedColumns
        }).ToList();
    }

    /// <summary> Other tables that have a foreign key pointing back at this one (TableModel.ChildForeignKeys) --
    /// e.g. finding that E_TimeSheetDetail has an FK to E_TimeSheet's primary key, so a template could offer
    /// to generate a child grid of E_TimeSheetDetail rows on E_TimeSheet's detail screen. </summary>
    private static async Task<List<ChildForeignKeyModel>> ReadChildForeignKeysAsync(SqlConnection connection, string fullTableName, CancellationToken cancellationToken)
    {
        var rows = await ReadForeignKeyRowsAsync(connection, ChildForeignKeysQuery, fullTableName, cancellationToken);
        return rows.Select(r => new ChildForeignKeyModel
        {
            ConstraintName = r.ConstraintName,
            ReferencingSchema = r.OtherSchema,
            ReferencingTable = r.OtherTable,
            ReferencingColumns = r.ReferencingColumns,
            ReferencedColumns = r.ReferencedColumns
        }).ToList();
    }
}
