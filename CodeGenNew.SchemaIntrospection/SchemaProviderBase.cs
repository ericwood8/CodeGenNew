using System.Data;
using System.Data.Common;
using CodeGenNew.Core;

namespace CodeGenNew.SchemaIntrospection;

/// <summary> What the app and the CLI need from a database: the table list, a table's columns for the tree, and a fully
/// populated TableModel. One implementation per database (SQL Server, PostgreSQL). </summary>
public interface ISchemaProvider
{
    Task<List<TableSummary>> ListTablesAsync(CancellationToken cancellationToken = default);
    Task<List<ColumnSummary>> ListColumnSummariesAsync(string schemaName, string tableName, CancellationToken cancellationToken = default);
    Task<TableModel> BuildTableModelAsync(string schemaName, string tableName, bool includeRowData = false,
        bool includeReferencedDisplayColumns = false, CancellationToken cancellationToken = default);
}

/// <summary> A column as one database's catalog describes it, already put in the vocabulary the rest of the generator
/// uses: <see cref="SqlTypeName"/> is a SQL Server type name (int, bit, varchar, decimal, datetime2 ...) whatever the
/// source database is, so classification, C# and TypeScript type mapping need no per-database branches. A database whose own
/// type spelling differs (PostgreSQL's boolean, timestamptz) supplies <see cref="DeclarationOverride"/> for the SQL text. </summary>
public sealed record RawColumn(
    string Name, int OrdinalPosition, string SqlTypeName, int MaxLength, int Precision, int Scale,
    bool IsNullable, bool IsIdentity, int? IdentitySeed, int? IdentityIncrement,
    string? ComputedDefinition, string? DefaultDefinition, bool IsPrimaryKey, bool IsInUniqueIndex,
    string? DeclarationOverride = null, string? DefaultForCSharp = null);

/// <summary> One foreign key, either direction: the OTHER table's name and the two column lists. </summary>
public sealed record ForeignKeyRow(string ConstraintName, string OtherSchema, string OtherTable, List<string> ReferencingColumns, List<string> ReferencedColumns);

/// <summary> The database-independent part of reading a schema: turning raw catalog rows into a TableModel (display columns,
/// lookup shape, special-logic column rules, child tables). A subclass implements the catalog queries. </summary>
public abstract class SchemaProviderBase : ISchemaProvider
{
    private readonly string _specialLogicColumnsConfigPath;
    private readonly NamingStyle _naming;

    /// <param name="naming"> How a table or column name from the database becomes the name generated code uses (the project's NamingStyle). The SQL text keeps the
    /// real names (ColumnModel.DbName, TableModel.DbTableName); a table name passed in (and listed in TableSummary) is always the real one. </param>
    protected SchemaProviderBase(string specialLogicColumnsConfigPath, NamingStyle naming = NamingStyle.AsIs)
    {
        _specialLogicColumnsConfigPath = specialLogicColumnsConfigPath;
        _naming = naming;
    }

    protected string Named(string databaseName) => NameConverter.Apply(_naming, databaseName);
    private static string? WhenDifferent(string databaseName, string named) => databaseName == named ? null : databaseName;

    protected abstract SqlDialect Dialect { get; }
    protected abstract Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken);
    protected abstract string Quote(string name);
    protected abstract Task<List<RawColumn>> ReadColumnsAsync(DbConnection connection, string schemaName, string tableName, CancellationToken cancellationToken);
    /// <summary> Foreign keys this table has (<paramref name="children"/> false) or that point at it (true), grouped per constraint. </summary>
    protected abstract Task<List<ForeignKeyRow>> ReadForeignKeyRowsAsync(DbConnection connection, string schemaName, string tableName, bool children, CancellationToken cancellationToken);
    protected abstract Task<long> ReadRowCountAsync(DbConnection connection, string schemaName, string tableName, CancellationToken cancellationToken);
    protected abstract Task<List<object?[]>> ReadRowsAsync(DbConnection connection, string schemaName, string tableName,
        List<ColumnModel> columns, List<ColumnModel> primaryKeyColumns, CancellationToken cancellationToken);

    public abstract Task<List<TableSummary>> ListTablesAsync(CancellationToken cancellationToken = default);
    public abstract Task<List<ColumnSummary>> ListColumnSummariesAsync(string schemaName, string tableName, CancellationToken cancellationToken = default);

    protected string QuotedTable(string schemaName, string tableName) => $"{Quote(schemaName)}.{Quote(tableName)}";

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
        await using var connection = await OpenConnectionAsync(cancellationToken);

        var rawColumns = await ReadColumnsAsync(connection, schemaName, tableName, cancellationToken);
        var foreignKeys = await ReadForeignKeysAsync(connection, schemaName, tableName, cancellationToken);
        var childForeignKeys = await ReadChildForeignKeysAsync(connection, schemaName, tableName, cancellationToken);

        var rules = SpecialLogicColumnsConfig.Load(_specialLogicColumnsConfigPath);
        if (includeReferencedDisplayColumns)
        {
            if (childForeignKeys.Count > 0)
                childForeignKeys = await AttachChildTableOwnKeysAsync(connection, childForeignKeys, rules, cancellationToken);
        }
        // Always read (the lookup shape of each referenced table); the display columns only when the template asks.
        foreignKeys = await AttachReferencedDisplayColumnsAsync(connection, foreignKeys, rules, includeReferencedDisplayColumns, cancellationToken);
        var columnNames = rawColumns.Select(c => Named(c.Name)).ToList();

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
            ? await ReadRowsAsync(connection, schemaName, tableName, columns, primaryKeyColumns, cancellationToken)
            : [];

        return new TableModel
        {
            SchemaName = schemaName,
            TableName = Named(tableName),
            DatabaseTableName = WhenDifferent(tableName, Named(tableName)),
            QuotedName = QuotedTable(schemaName, tableName),
            Dialect = Dialect,
            IsReservedWordName = tableName.IsSqlReservedWord(),
            IsCSharpReservedWordName = Named(tableName).IsCSharpReservedWord(),
            Columns = columns,
            PrimaryKeyColumns = primaryKeyColumns,
            DisplayColumns = columns.SelectDisplayColumns(foreignKeys.SelectMany(fk => fk.ReferencingColumns).ToList()),
            HasReferencedDisplayColumns = includeReferencedDisplayColumns,
            LookupShape = new LookupShape(LookupShape.Looks(columns, foreignKeys.Count), await ReadRowCountAsync(connection, schemaName, tableName, cancellationToken), LookupShape.HasKeyAndText(columns)),
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

    private static int? RankInCategory(List<SpecialLogicRule> rules, string category, string columnName)
    {
        var rule = rules.FirstOrDefault(r => r.Category.EqualsIgnoreCase(category) && !r.IsPairRule);
        return rule?.MatchRank(columnName);
    }

    // A Lookup/WinUI3 parent-name display shows a human-recognizable NAME, so it must be a plain, bounded
    // string column -- not just "anything that isn't a blob". Restricting eligibility to actual string types
    // prevents any numeric/date/etc. column from being chosen as a display column in the first place (a plain int
    // "MonthNumber" matching the "*Number" pattern meant for a string business key rendered as `row.MonthNumber?.ToString()`,
    // which does not compile for a non-nullable int), matching DisplayColumnSelector's own fallback path.
    private static bool IsDisplayEligibleType(SqlDbType t) =>
        t is SqlDbType.Char or SqlDbType.VarChar or SqlDbType.NChar or SqlDbType.NVarChar;

    /// <summary> For each foreign key, reads the referenced table's columns (one catalog query per distinct table) and
    /// records which of them are its display columns. </summary>
    private async Task<List<ForeignKeyModel>> AttachReferencedDisplayColumnsAsync(
        DbConnection connection, List<ForeignKeyModel> foreignKeys, List<SpecialLogicRule> rules, bool includeDisplayColumns, CancellationToken cancellationToken)
    {
        var displayColumnsByTable = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var displayDbColumnsByTable = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var lookupShapeByTable = new Dictionary<string, LookupShape>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ForeignKeyModel>();

        foreach (var fk in foreignKeys)
        {
            string referencedName = QuotedTable(fk.ReferencedSchema, fk.ReferencedDbTable);
            if (!displayColumnsByTable.TryGetValue(referencedName, out var displayColumns))
            {
                var referencedColumns = (await ReadColumnsAsync(connection, fk.ReferencedSchema, fk.ReferencedDbTable, cancellationToken))
                    .Select(raw => BuildColumnModel(raw, rules))
                    .ToList();
                var referencedForeignKeys = await ReadForeignKeysAsync(connection, fk.ReferencedSchema, fk.ReferencedDbTable, cancellationToken);
                var displayModels = includeDisplayColumns
                    ? referencedColumns.SelectDisplayColumns(referencedForeignKeys.SelectMany(f => f.ReferencingColumns).ToList()).ToList()
                    : [];
                displayColumns = displayModels.Select(c => c.Name).ToList();
                displayColumnsByTable[referencedName] = displayColumns;
                displayDbColumnsByTable[referencedName] = displayModels.Select(c => c.DbName).ToList();
                lookupShapeByTable[referencedName] = new LookupShape(
                    LookupShape.Looks(referencedColumns, referencedForeignKeys.Count),
                    await ReadRowCountAsync(connection, fk.ReferencedSchema, fk.ReferencedDbTable, cancellationToken),
                    LookupShape.HasKeyAndText(referencedColumns));
            }

            result.Add(new ForeignKeyModel
            {
                ConstraintName = fk.ConstraintName,
                ReferencingColumns = fk.ReferencingColumns,
                ReferencedSchema = fk.ReferencedSchema,
                ReferencedTable = fk.ReferencedTable,
                ReferencedColumns = fk.ReferencedColumns,
                ReferencingDatabaseColumns = fk.ReferencingDatabaseColumns,
                ReferencedDatabaseTable = fk.ReferencedDatabaseTable,
                ReferencedDatabaseColumns = fk.ReferencedDatabaseColumns,
                ReferencedDisplayColumns = displayColumns,
                ReferencedDisplayDatabaseColumns = _naming == NamingStyle.AsIs ? null : displayDbColumnsByTable[referencedName],
                ReferencedLookupShape = lookupShapeByTable[referencedName]
            });
        }

        return result;
    }

    /// <summary> For each child (referencing) table, reads its own primary key and its own full foreign-key list
    /// (with THEIR referenced display columns resolved too) so a generated child grid can hide the child's
    /// identity column and resolve its OTHER foreign keys to a display name -- the same building blocks
    /// AttachReferencedDisplayColumnsAsync already uses for the primary table's own drop-downs, reused per child
    /// table instead of per referenced table. One extra catalog round-trip per distinct child table. </summary>
    private async Task<List<ChildForeignKeyModel>> AttachChildTableOwnKeysAsync(
        DbConnection connection, List<ChildForeignKeyModel> childForeignKeys, List<SpecialLogicRule> rules, CancellationToken cancellationToken)
    {
        var result = new List<ChildForeignKeyModel>();
        foreach (var child in childForeignKeys)
        {
            var childColumns = (await ReadColumnsAsync(connection, child.ReferencingSchema, child.ReferencingDbTable, cancellationToken))
                .Select(raw => BuildColumnModel(raw, rules))
                .ToList();
            var childPrimaryKeyColumns = childColumns
                .Where(c => c.IsPrimaryKey).OrderBy(c => c.OrdinalPosition).Select(c => c.Name).ToList();
            var childForeignKeysOfItsOwn = await ReadForeignKeysAsync(connection, child.ReferencingSchema, child.ReferencingDbTable, cancellationToken);
            childForeignKeysOfItsOwn = await AttachReferencedDisplayColumnsAsync(connection, childForeignKeysOfItsOwn, rules, true, cancellationToken);

            result.Add(new ChildForeignKeyModel
            {
                ConstraintName = child.ConstraintName,
                ReferencingSchema = child.ReferencingSchema,
                ReferencingTable = child.ReferencingTable,
                ReferencingDatabaseTable = child.ReferencingDatabaseTable,
                ReferencingColumns = child.ReferencingColumns,
                ReferencedColumns = child.ReferencedColumns,
                ReferencingPrimaryKeyColumns = childPrimaryKeyColumns,
                ReferencingTableForeignKeys = childForeignKeysOfItsOwn,
                ReferencingTableColumns = childColumns
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

    private ColumnModel BuildColumnModel(RawColumn raw, List<SpecialLogicRule> rules)
    {
        var sqlType = SqlTypeClassifier.MapSqlTypeName(raw.SqlTypeName);
        bool isInteger = SqlTypeClassifier.IsIntegerColumn(sqlType);
        bool isNumeric = SqlTypeClassifier.IsNumericColumn(sqlType);
        bool isMoney = SqlTypeClassifier.IsMoneyColumn(sqlType);
        bool isString = SqlTypeClassifier.IsStringColumn(sqlType);
        bool isDate = SqlTypeClassifier.IsDateColumn(sqlType);
        bool isBoolean = SqlTypeClassifier.IsBooleanColumn(sqlType);

        int? maxLength = isString || sqlType is SqlDbType.Binary or SqlDbType.VarBinary ? raw.MaxLength : null;

        // Everything that classifies a column by its name (audit columns, the special-logic rules, a currency name) reads the generated name, so
        // customer_id and CustomerId are the same column to every rule; the SQL keeps the real name.
        string name = Named(raw.Name);

        return new ColumnModel
        {
            Name = name,
            DatabaseName = WhenDifferent(raw.Name, name),
            QuotedName = Quote(raw.Name),
            IsReservedWordName = raw.Name.IsSqlReservedWord(),
            IsCSharpReservedWordName = name.IsCSharpReservedWord(),
            SqlType = sqlType,
            SqlTypeDeclaration = raw.DeclarationOverride ?? SqlTypeClassifier.BuildDeclaration(raw.SqlTypeName, sqlType, raw.MaxLength, raw.Precision, raw.Scale),
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
                raw.Name, sqlType, raw.DefaultForCSharp ?? raw.DefaultDefinition, isBoolean, isDate, isMoney, isNumeric, isInteger, isString),
            IsIntegerColumn = isInteger,
            IsNumericColumn = isNumeric,
            IsMoneyColumn = isMoney,
            IsStringColumn = isString,
            IsDateColumn = isDate,
            IsBooleanColumn = isBoolean,
            NumericKind = isInteger ? NumericClassifier.Classify(name) : NumericKind.None,
            IsCurrencyColumn = isMoney || (sqlType == SqlDbType.Decimal && NumericClassifier.IsCurrencyName(name)),
            IsAuditColumn = name.IsAuditColumn(),
            IsCreateDateColumn = MatchesCategory(rules, "CreateDateColumn", name),
            DisplayRank = IsDisplayEligibleType(sqlType) ? RankInCategory(rules, "DisplayColumn", name) : null,
            IsCreateUserColumn = MatchesCategory(rules, "CreateUserColumn", name),
            IsModifiedDateColumn = MatchesCategory(rules, "ModifiedDateColumn", name),
            IsModifiedUserColumn = MatchesCategory(rules, "ModifiedUserColumn", name),
            IsLastChangedDateColumn = MatchesCategory(rules, "LastChangedDateColumn", name),
            IsInactiveReasonColumn = MatchesCategory(rules, "InactiveReasonColumn", name),
            IsAdminFlagColumn = MatchesCategory(rules, "AdminFlagColumn", name),
            IsFilePathColumn = MatchesCategory(rules, "FilePathColumn", name),
            ParameterName = name.ToSqlParameterName(sqlType)
        };
    }

    private async Task<List<ForeignKeyModel>> ReadForeignKeysAsync(DbConnection connection, string schemaName, string tableName, CancellationToken cancellationToken)
    {
        var rows = await ReadForeignKeyRowsAsync(connection, schemaName, tableName, children: false, cancellationToken);
        return rows.Select(r => new ForeignKeyModel
        {
            ConstraintName = r.ConstraintName,
            ReferencingColumns = r.ReferencingColumns.Select(Named).ToList(),
            ReferencedSchema = r.OtherSchema,
            ReferencedTable = Named(r.OtherTable),
            ReferencedColumns = r.ReferencedColumns.Select(Named).ToList(),
            ReferencingDatabaseColumns = _naming == NamingStyle.AsIs ? null : r.ReferencingColumns,
            ReferencedDatabaseTable = WhenDifferent(r.OtherTable, Named(r.OtherTable)),
            ReferencedDatabaseColumns = _naming == NamingStyle.AsIs ? null : r.ReferencedColumns
        }).ToList();
    }

    /// <summary> Other tables that have a foreign key pointing back at this one (TableModel.ChildForeignKeys) --
    /// e.g. finding that E_TimeSheetDetail has an FK to E_TimeSheet's primary key, so a template could offer
    /// to generate a child grid of E_TimeSheetDetail rows on E_TimeSheet's detail screen. </summary>
    private async Task<List<ChildForeignKeyModel>> ReadChildForeignKeysAsync(DbConnection connection, string schemaName, string tableName, CancellationToken cancellationToken)
    {
        var rows = await ReadForeignKeyRowsAsync(connection, schemaName, tableName, children: true, cancellationToken);
        return rows.Select(r => new ChildForeignKeyModel
        {
            ConstraintName = r.ConstraintName,
            ReferencingSchema = r.OtherSchema,
            ReferencingTable = Named(r.OtherTable),
            ReferencingDatabaseTable = WhenDifferent(r.OtherTable, Named(r.OtherTable)),
            ReferencingColumns = r.ReferencingColumns.Select(Named).ToList(),
            ReferencedColumns = r.ReferencedColumns.Select(Named).ToList()
        }).ToList();
    }

    /// <summary> Groups catalog rows (one per column pair) into one <see cref="ForeignKeyRow"/> per constraint: a composite foreign
    /// key spans several rows. The key includes the other table, because PostgreSQL only requires a constraint name to be unique per table. </summary>
    protected static async Task<List<ForeignKeyRow>> GroupForeignKeyRowsAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        var grouped = new Dictionary<(string, string, string), ForeignKeyRow>();
        var order = new List<ForeignKeyRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            string constraintName = reader.GetString(reader.GetOrdinal("ConstraintName"));
            string otherSchema = reader.GetString(reader.GetOrdinal("OtherSchema"));
            string otherTable = reader.GetString(reader.GetOrdinal("OtherTable"));
            var key = (constraintName, otherSchema, otherTable);
            if (!grouped.TryGetValue(key, out var row))
            {
                row = new ForeignKeyRow(constraintName, otherSchema, otherTable, [], []);
                grouped[key] = row;
                order.Add(row);
            }

            row.ReferencingColumns.Add(reader.GetString(reader.GetOrdinal("ReferencingColumn")));
            row.ReferencedColumns.Add(reader.GetString(reader.GetOrdinal("ReferencedColumn")));
        }

        return order;
    }
}
