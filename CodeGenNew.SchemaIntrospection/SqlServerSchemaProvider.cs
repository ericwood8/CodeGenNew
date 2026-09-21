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
            CASE WHEN EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id = t.object_id AND i.is_unique = 1) THEN 1 ELSE 0 END AS HasUniqueIndex
        FROM sys.tables t
        INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
        ORDER BY s.name, t.name;
        """;

    /// <summary> Lists user tables (system/framework tables filtered out via SystemTableFilter) for the
    /// TreeView. Read-only; a full TableModel is only built for the one table actually selected. </summary>
    public async Task<List<TableSummary>> ListTablesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = SqlServerConnectionFactory.CreateConnection(_connectionRequest);
        await connection.OpenAsync(cancellationToken);

        var results = new List<TableSummary>();
        await using var command = new SqlCommand(ListTablesQuery, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string tableName = reader.GetString(reader.GetOrdinal("TableName"));
            if (SystemTableFilter.IsSystemTable(tableName))
                continue;

            results.Add(new TableSummary
            {
                SchemaName = reader.GetString(reader.GetOrdinal("SchemaName")),
                TableName = tableName,
                HasPrimaryKey = reader.GetInt32(reader.GetOrdinal("HasPrimaryKey")) == 1,
                HasUniqueIndex = reader.GetInt32(reader.GetOrdinal("HasUniqueIndex")) == 1,
                IsReservedWordName = ReservedWordChecker.IsSqlReservedWord(tableName),
                IsCSharpReservedWordName = ReservedWordChecker.IsCSharpReservedWord(tableName)
            });
        }

        return results;
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
        await using var connection = SqlServerConnectionFactory.CreateConnection(_connectionRequest);
        await connection.OpenAsync(cancellationToken);

        var results = new List<ColumnSummary>();
        await using var command = new SqlCommand(ColumnSummariesQuery, connection);
        command.Parameters.AddWithValue("@fullTableName", $"[{schemaName}].[{tableName}]");

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string columnName = reader.GetString(reader.GetOrdinal("ColumnName"));
            if (SystemColumnFilter.IsSystemColumn(columnName))
                continue;

            results.Add(new ColumnSummary
            {
                Name = columnName,
                SqlTypeName = reader.GetString(reader.GetOrdinal("SqlTypeName")),
                IsNullable = reader.GetBoolean(reader.GetOrdinal("IsNullable")),
                IsPrimaryKey = reader.GetInt32(reader.GetOrdinal("IsPrimaryKey")) == 1
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
        await using var connection = SqlServerConnectionFactory.CreateConnection(_connectionRequest);
        await connection.OpenAsync(cancellationToken);

        string fullName = $"[{schemaName}].[{tableName}]";
        var rawColumns = await ReadColumnsAsync(connection, fullName, cancellationToken);
        var foreignKeys = await ReadForeignKeysAsync(connection, fullName, cancellationToken);

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
        // column per side, and this needs two per side). Bugs2.txt item 11.
        ColumnModel? FindExact(string name) => columns.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
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
            IsReservedWordName = ReservedWordChecker.IsSqlReservedWord(tableName),
            IsCSharpReservedWordName = ReservedWordChecker.IsCSharpReservedWord(tableName),
            Columns = columns,
            PrimaryKeyColumns = primaryKeyColumns,
            DisplayColumns = DisplayColumnSelector.Select(columns, foreignKeys.SelectMany(fk => fk.ReferencingColumns).ToList()),
            HasReferencedDisplayColumns = includeReferencedDisplayColumns,
            HasRowData = includeRowData,
            Rows = rows,
            ForeignKeys = foreignKeys,
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
        var rule = rules.FirstOrDefault(r => r.Category.Equals(category, StringComparison.OrdinalIgnoreCase) && !r.IsPairRule);
        return rule is null ? null : SpecialLogicColumnsConfig.MatchRank(rule, columnName);
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
                displayColumns = DisplayColumnSelector
                    .Select(referencedColumns, referencedForeignKeys.SelectMany(fk => fk.ReferencingColumns).ToList())
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
        var rule = rules.FirstOrDefault(r => r.Category.Equals(category, StringComparison.OrdinalIgnoreCase) && !r.IsPairRule);
        return rule is not null && SpecialLogicColumnsConfig.MatchesColumnRule(rule, columnName);
    }

    private static (bool, ColumnModel?, ColumnModel?) EvaluatePair(
        List<SpecialLogicRule> rules, string category, List<string> columnNames, List<ColumnModel> columns)
    {
        var rule = rules.FirstOrDefault(r => r.Category.Equals(category, StringComparison.OrdinalIgnoreCase) && r.IsPairRule);
        if (rule is null)
            return (false, null, null);

        var match = SpecialLogicColumnsConfig.EvaluatePairRule(rule, columnNames);
        if (match is null)
            return (false, null, null);

        var flagColumn = columns.First(c => c.Name.Equals(match.Value.FlagColumn, StringComparison.OrdinalIgnoreCase));
        var companionColumn = columns.First(c => c.Name.Equals(match.Value.CompanionColumn, StringComparison.OrdinalIgnoreCase));
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
            IsReservedWordName = ReservedWordChecker.IsSqlReservedWord(raw.Name),
            IsCSharpReservedWordName = ReservedWordChecker.IsCSharpReservedWord(raw.Name),
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
            IsAuditColumn = AuditColumnClassifier.IsAuditColumn(raw.Name),
            IsCreateDateColumn = MatchesCategory(rules, "CreateDateColumn", raw.Name),
            DisplayRank = IsDisplayEligibleType(sqlType) ? RankInCategory(rules, "DisplayColumn", raw.Name) : null,
            IsCreateUserColumn = MatchesCategory(rules, "CreateUserColumn", raw.Name),
            IsModifiedDateColumn = MatchesCategory(rules, "ModifiedDateColumn", raw.Name),
            IsLastChangedDateColumn = MatchesCategory(rules, "LastChangedDateColumn", raw.Name),
            IsInactiveReasonColumn = MatchesCategory(rules, "InactiveReasonColumn", raw.Name),
            IsAdminFlagColumn = MatchesCategory(rules, "AdminFlagColumn", raw.Name),
            ParameterName = ParameterNameBuilder.Build(raw.Name, sqlType)
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
            string columnName = reader.GetString(reader.GetOrdinal("ColumnName"));
            if (SystemColumnFilter.IsSystemColumn(columnName))
                continue;

            results.Add(new RawColumn(
                Name: columnName,
                OrdinalPosition: reader.GetInt32(reader.GetOrdinal("OrdinalPosition")),
                SqlTypeName: reader.GetString(reader.GetOrdinal("SqlTypeName")),
                MaxLength: reader.GetInt16(reader.GetOrdinal("MaxLength")),
                Precision: reader.GetByte(reader.GetOrdinal("Precision")),
                Scale: reader.GetByte(reader.GetOrdinal("Scale")),
                IsNullable: reader.GetBoolean(reader.GetOrdinal("IsNullable")),
                IsIdentity: reader.GetBoolean(reader.GetOrdinal("IsIdentity")),
                // seed_value/increment_value are sql_variant -- the underlying numeric type varies by
                // the identity column's own type (int, bigint, decimal, ...), so read via GetValue + Convert
                // rather than assuming a fixed CLR type like GetDecimal.
                IdentitySeed: reader.IsDBNull(reader.GetOrdinal("IdentitySeed")) ? null : Convert.ToInt32(reader.GetValue(reader.GetOrdinal("IdentitySeed"))),
                IdentityIncrement: reader.IsDBNull(reader.GetOrdinal("IdentityIncrement")) ? null : Convert.ToInt32(reader.GetValue(reader.GetOrdinal("IdentityIncrement"))),
                ComputedDefinition: reader.IsDBNull(reader.GetOrdinal("ComputedDefinition")) ? null : reader.GetString(reader.GetOrdinal("ComputedDefinition")),
                DefaultDefinition: reader.IsDBNull(reader.GetOrdinal("DefaultDefinition")) ? null : reader.GetString(reader.GetOrdinal("DefaultDefinition")),
                IsPrimaryKey: reader.GetInt32(reader.GetOrdinal("IsPrimaryKey")) == 1,
                IsInUniqueIndex: reader.GetInt32(reader.GetOrdinal("IsInUniqueIndex")) == 1
            ));
        }

        return results;
    }

    private const string ForeignKeysQuery = """
        SELECT
            fk.name AS ConstraintName,
            cpar.name AS ReferencingColumn,
            OBJECT_SCHEMA_NAME(fk.referenced_object_id) AS ReferencedSchema,
            OBJECT_NAME(fk.referenced_object_id) AS ReferencedTable,
            cref.name AS ReferencedColumn
        FROM sys.foreign_keys fk
        INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
        INNER JOIN sys.columns cpar ON cpar.object_id = fkc.parent_object_id AND cpar.column_id = fkc.parent_column_id
        INNER JOIN sys.columns cref ON cref.object_id = fkc.referenced_object_id AND cref.column_id = fkc.referenced_column_id
        WHERE fkc.parent_object_id = OBJECT_ID(@fullTableName)
        ORDER BY fk.name, fkc.constraint_column_id;
        """;

    private static async Task<List<ForeignKeyModel>> ReadForeignKeysAsync(SqlConnection connection, string fullTableName, CancellationToken cancellationToken)
    {
        var grouped = new Dictionary<string, (string Schema, string Table, List<string> Referencing, List<string> Referenced)>();

        await using var command = new SqlCommand(ForeignKeysQuery, connection);
        command.Parameters.AddWithValue("@fullTableName", fullTableName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string constraintName = reader.GetString(reader.GetOrdinal("ConstraintName"));
            if (!grouped.TryGetValue(constraintName, out var entry))
            {
                entry = (reader.GetString(reader.GetOrdinal("ReferencedSchema")),
                          reader.GetString(reader.GetOrdinal("ReferencedTable")),
                          [], []);
                grouped[constraintName] = entry;
            }

            entry.Referencing.Add(reader.GetString(reader.GetOrdinal("ReferencingColumn")));
            entry.Referenced.Add(reader.GetString(reader.GetOrdinal("ReferencedColumn")));
        }

        return grouped.Select(kvp => new ForeignKeyModel
        {
            ConstraintName = kvp.Key,
            ReferencingColumns = kvp.Value.Referencing,
            ReferencedSchema = kvp.Value.Schema,
            ReferencedTable = kvp.Value.Table,
            ReferencedColumns = kvp.Value.Referenced
        }).ToList();
    }
}
