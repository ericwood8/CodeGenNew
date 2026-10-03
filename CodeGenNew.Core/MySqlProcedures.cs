using System.Data;
using System.Text;

namespace CodeGenNew.Core;

/// <summary> The MySQL counterparts of the SP_* templates. MySQL has real stored procedures (CREATE PROCEDURE, IN/OUT parameters, CALL, a result set from a plain SELECT),
/// so they keep the shape of the SQL Server ones; the dialect differs:
///   - backtick-quoted names, and the table / column names in the SQL are the database's own (TableModel.DbTableName, ColumnModel.DbName) while a parameter is named after the generated name;
///   - a MySQL procedure has no default parameter values, so a caller passes every parameter (NULL, '' or 0 where SQL Server would have defaulted);
///   - no RETURNING: the new key comes back as a one-row result set (SELECT LAST_INSERT_ID()), the way the SQL Server procedures SELECT SCOPE_IDENTITY();
///   - errors are raised with SIGNAL SQLSTATE '45000' (MYSQL_ERRNO 55508 / 55509 as in the SQL Server procedures); a foreign-key delete failure is error 1451;
///   - there is no CREATE OR REPLACE PROCEDURE: each file is DROP PROCEDURE IF EXISTS + DELIMITER + CREATE PROCEDURE, which the mysql client runs as it is.
/// The template files (SP_Insert.tt and so on) call these when TableModel.Dialect is MySql. </summary>
public static class MySqlProcedures
{
    private static string Q(string identifier) => "`" + identifier.Replace("`", "``") + "`";
    private static string P(ColumnModel c) => Q(c.ParameterName.TrimStart('@'));
    private static string Col(ColumnModel c) => Q(c.DbName);
    private static string Table(TableModel m) => Q(m.DbTableName);
    private static string Name(TableModel m, string suffix) => Q(m.TableName + "_" + suffix);
    private static string Message(string text) => text.Replace("'", "''");
    private static string Eq(IEnumerable<ColumnModel> keys, Func<ColumnModel, string> value, string? alias = null) =>
        string.Join(" AND ", keys.Select(k => $"{(alias is null ? "" : Q(alias) + ".")}{Col(k)} = {value(k)}"));

    private static string Flag(ColumnModel c, bool value) =>
        c.IsStringColumn ? (c.Name.Contains("Admin", StringComparison.OrdinalIgnoreCase) ? (value ? "'True'" : "'False'") : (value ? "'1'" : "'0'")) : (value ? "1" : "0");

    private static string Trimmed(ColumnModel c, string expression) => c.IsStringColumn ? $"TRIM({expression})" : expression;

    private static string Create(TableModel m, string suffix, IEnumerable<(string Name, string Type, string Mode)> parameters, string body)
    {
        string list = string.Join(",\n", parameters.Select(p => $"{p.Mode} {p.Name} {p.Type}"));
        return $"DROP PROCEDURE IF EXISTS {Name(m, suffix)};\nDELIMITER $$\nCREATE PROCEDURE {Name(m, suffix)}({(list.Length > 0 ? "\n" + list + "\n" : "")})\n{body}$$\nDELIMITER ;\n";
    }

    private static (string, string, string) In(ColumnModel c) => (P(c), c.SqlTypeDeclaration, "IN");

    private static void Validations(StringBuilder o, TableModel m, Func<ColumnModel, bool> isParameter)
    {
        static string Raise(string text) => $"\t\tSIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '{Message(text)}';\n\tEND IF;\n\n";
        if (m.HasStartEndDatePair && m.StartDateColumn is { } s && m.EndDateColumn is { } e && isParameter(s) && isParameter(e))
        {
            o.Append($"\tIF ({P(s)} IS NOT NULL AND {P(e)} IS NOT NULL AND {P(s)} > {P(e)}) THEN\n");
            o.Append(Raise($"{Q(s.DbName)} must not be later than {Q(e.DbName)}."));
        }
        if (m.HasInOutDateTimePair && m.InDateColumn is { } id && m.InTimeColumn is { } it && m.OutDateColumn is { } od && m.OutTimeColumn is { } ot
            && new[] { id, it, od, ot }.All(isParameter))
        {
            o.Append($"\tIF ({P(id)} IS NOT NULL AND {P(it)} IS NOT NULL AND {P(od)} IS NOT NULL AND {P(ot)} IS NOT NULL\n");
            o.Append($"\t    AND ({P(id)} > {P(od)} OR ({P(id)} = {P(od)} AND {P(it)} > {P(ot)}))) THEN\n");
            o.Append(Raise($"{Q(id.DbName)}/{Q(it.DbName)} must not be later than {Q(od.DbName)}/{Q(ot.DbName)}."));
        }
    }

    // ------------------------------------------------------------------------------------------------ Search

    public static string Search(TableModel m)
    {
        var searchable = m.SearchableColumns;
        string where = searchable.Count == 0 ? "" : "\tWHERE " + string.Join("\n\t  AND ", searchable
            .Select(c => $"({P(c)} IS NULL OR {Col(c)} LIKE CONCAT('%', TRIM({P(c)}), '%'))")) + "\n";

        var sortColumn = m.DisplayColumns.OrderBy(c => c.DisplayRank ?? int.MaxValue).ThenBy(c => c.OrdinalPosition).FirstOrDefault();
        var orderBy = new List<string>();
        if (sortColumn is not null) orderBy.Add(Col(sortColumn) + " ASC");
        foreach (var k in m.PrimaryKeyColumns.Where(k => k != sortColumn)) orderBy.Add(Col(k) + " ASC");

        // SortColumn / SortDescending: the grid's chosen sort (SearchSort); a MySQL procedure has no default parameter, so a caller passes NULL / 0 for "the default order".
        var parameters = searchable.Select(In).Append((Q("PageNumber"), "int", "IN")).Append((Q("PageSize"), "int", "IN"))
            .Append((Q("SortColumn"), $"varchar({SearchSort.MaxNameLength})", "IN")).Append((Q("SortDescending"), "tinyint(1)", "IN")).ToList();
        string body = "BEGIN\n\tDECLARE v_offset int;\n\tSET v_offset = (" + Q("PageNumber") + " - 1) * " + Q("PageSize") + ";\n\n" +
                      $"\tSELECT {string.Join(", ", m.Columns.Select(Col))}\n\tFROM {Table(m)} AS t\n{where}" +
                      $"\tORDER BY\n{SearchSort.MySql(m)}\t\t{string.Join(", ", orderBy)}\n\tLIMIT {Q("PageSize")} OFFSET v_offset;\nEND\n";
        string search = Create(m, "Search", parameters, body);

        var countParameters = searchable.Select(In);
        string count = Create(m, "SearchCount", countParameters,
            $"BEGIN\n\tSELECT COUNT(*) AS `Value`\n\tFROM {Table(m)}{(where.Length > 0 ? "\n" + where.TrimEnd('\n') : "")};\nEND\n");
        return search + "\n" + count;
    }

    // ------------------------------------------------------------------------------------------------ Insert

    public static string Insert(TableModel m)
    {
        var insertable = m.Columns
            .Where(c => !c.IsComputed && !c.IsModifiedDateColumn && !c.IsModifiedUserColumn && !c.IsInactiveReasonColumn && !c.IsAdminFlagColumn)
            .Where(c => c != m.InactiveDateColumn).ToList();
        bool singleIdentityKey = m.PrimaryKeyColumns.Count == 1 && m.PrimaryKeyColumns[0].IsIdentity;
        var identity = singleIdentityKey ? m.PrimaryKeyColumns[0] : null;
        var parameterColumns = insertable.Where(c => c != identity && !c.IsCreateDateColumn && !c.IsLastChangedDateColumn && c != m.ActiveColumn).ToList();
        var autoTouched = m.Columns.Where(c => c.IsCreateDateColumn || c.IsLastChangedDateColumn).ToList();

        var pairs = parameterColumns.Select(c => (Name: c.DbName, Value: Trimmed(c, P(c))))
            .Concat(autoTouched.Select(c => (Name: c.DbName, Value: "NOW()"))).ToList();
        if (m.HasActiveInactivePair && m.ActiveColumn is { } active)
            pairs.Add((active.DbName, Flag(active, !active.Name.Contains("Inactive", StringComparison.OrdinalIgnoreCase))));
        foreach (var admin in m.Columns.Where(c => c.IsAdminFlagColumn))
            pairs.Add((admin.DbName, Flag(admin, false)));

        var o = new StringBuilder("BEGIN\n");
        Validations(o, m, parameterColumns.Contains);
        o.Append($"\tINSERT INTO {Table(m)}");
        if (pairs.Count == 0)
            o.Append("\n\tVALUES ()");
        else
            o.Append($"\n\t({string.Join(", ", pairs.Select(p => Q(p.Name)))})\n\tVALUES\n\t({string.Join(", ", pairs.Select(p => p.Value))})");
        o.Append(";\n\n");
        o.Append(identity is not null ? $"\tSELECT LAST_INSERT_ID() AS {Q(identity.DbName)};\n" : "\tSELECT ROW_COUNT() AS `RowsInserted`;\n");
        o.Append("END\n");
        return "-- MySQL procedures have no default parameter values: pass every parameter (NULL, '' or 0 where SQL Server would have defaulted it).\n" +
               Create(m, "Insert", parameterColumns.Select(In), o.ToString());
    }

    // ------------------------------------------------------------------------------------------------ Update

    public static string Update(TableModel m)
    {
        var updatable = m.Columns
            .Where(c => !c.IsComputed && !c.IsCreateDateColumn && !c.IsCreateUserColumn && !c.IsModifiedDateColumn && !c.IsLastChangedDateColumn).ToList();
        var setColumns = updatable.Where(c => !c.IsPrimaryKey && c != m.ActiveColumn).ToList();
        var autoTouched = m.Columns.Where(c => c.IsModifiedDateColumn || c.IsLastChangedDateColumn).ToList();
        var triggers = new List<ColumnModel>();
        if (m.InactiveDateColumn is not null) triggers.Add(m.InactiveDateColumn);
        triggers.AddRange(m.Columns.Where(c => c.IsInactiveReasonColumn));

        // NULL means "do not change this column" (as in SP_Update): SET col = COALESCE(param, col).
        var sets = new List<string>();
        foreach (var c in autoTouched) sets.Add($"{Col(c)} = NOW()");
        foreach (var c in setColumns) sets.Add($"{Col(c)} = COALESCE({Trimmed(c, P(c))}, {Col(c)})");
        if (m.HasActiveInactivePair && m.ActiveColumn is { } active)
        {
            bool negative = active.Name.Contains("Inactive", StringComparison.OrdinalIgnoreCase);
            string own = $"COALESCE({Trimmed(active, P(active))}, {Col(active)})";
            sets.Add(triggers.Count > 0
                ? $"{Col(active)} = CASE WHEN {string.Join(" OR ", triggers.Select(c => $"{P(c)} IS NOT NULL"))} THEN {Flag(active, negative)} ELSE {own} END"
                : $"{Col(active)} = {own}");
        }

        var pk = m.PrimaryKeyColumns;
        var o = new StringBuilder("BEGIN\n");
        Validations(o, m, updatable.Contains);
        if (sets.Count == 0)
        {
            o.Append("\tSELECT 0 AS `Result`; -- nothing to change\n");
        }
        else
        {
            o.Append($"\tIF {P(pk[0])} IS NOT NULL AND EXISTS (SELECT 1 FROM {Table(m)} WHERE {Eq(pk, P)}) THEN\n");
            o.Append($"\t\tUPDATE {Table(m)} SET\n\t\t\t{string.Join(",\n\t\t\t", sets)}\n\t\tWHERE {Eq(pk, P)};\n");
            o.Append("\t\tSELECT 1 AS `Result`; -- the row was found and updated\n\tELSE\n\t\tSELECT 0 AS `Result`;\n\tEND IF;\n");
        }
        o.Append("END\n");
        return "-- A parameter that is NULL leaves its column as it is; pass every parameter. Result: 1 = updated, 0 = no such row.\n" +
               Create(m, "Update", updatable.Select(In), o.ToString());
    }

    // ------------------------------------------------------------------------------------------------ Save

    public static string Save(TableModel m)
    {
        var identityKey = m.PrimaryKeyColumns.FirstOrDefault(c => c.IsIdentity);
        var parameterColumns = m.Columns
            .Where(c => !c.IsComputed && !c.IsCreateDateColumn && !c.IsModifiedDateColumn && !c.IsLastChangedDateColumn && (!c.IsIdentity || c.IsPrimaryKey)).ToList();
        var pk = m.PrimaryKeyColumns;
        bool hasActive = m.HasActiveInactivePair && m.ActiveColumn is not null;
        var active = hasActive ? m.ActiveColumn : null;
        bool negative = active is not null && active.Name.Contains("Inactive", StringComparison.OrdinalIgnoreCase);

        string Supplied(ColumnModel c) => c.IsStringColumn ? $"CHAR_LENGTH({P(c)}) > 0" : $"{P(c)} IS NOT NULL";
        var triggers = new List<ColumnModel>();
        if (hasActive && m.InactiveDateColumn is not null) triggers.Add(m.InactiveDateColumn);
        if (hasActive) triggers.AddRange(m.Columns.Where(c => c.IsInactiveReasonColumn && parameterColumns.Contains(c)));

        var updatePairs = new List<(string Name, string Value)>();
        foreach (var c in parameterColumns.Where(c => !c.IsPrimaryKey && !c.IsCreateUserColumn))
        {
            updatePairs.Add((c.DbName, c == active && triggers.Count > 0
                ? $"CASE WHEN {string.Join(" OR ", triggers.Select(Supplied))} THEN {Flag(c, negative)} ELSE {P(c)} END"
                : P(c)));
        }
        foreach (var c in m.Columns.Where(c => !c.IsComputed && (c.IsModifiedDateColumn || c.IsLastChangedDateColumn)))
            updatePairs.Add((c.DbName, "NOW()"));

        var insertPairs = parameterColumns
            .Where(c => c != identityKey && !c.IsInactiveReasonColumn && !c.IsAdminFlagColumn && !c.IsModifiedUserColumn && c != m.InactiveDateColumn && c != active)
            .Select(c => (Name: c.DbName, Value: P(c))).ToList();
        foreach (var c in m.Columns.Where(c => !c.IsComputed && (c.IsCreateDateColumn || c.IsLastChangedDateColumn)))
            insertPairs.Add((c.DbName, "NOW()"));
        if (active is not null) insertPairs.Add((active.DbName, Flag(active, !negative)));
        foreach (var c in m.Columns.Where(c => c.IsAdminFlagColumn && !c.IsComputed))
            insertPairs.Add((c.DbName, Flag(c, false)));

        var o = new StringBuilder("BEGIN\n");
        foreach (var c in parameterColumns.Where(c => c.IsStringColumn))
            o.Append($"\tSET {P(c)} = {(c.IsNullable ? $"TRIM({P(c)})" : $"COALESCE(TRIM({P(c)}), '')")};\n");
        if (parameterColumns.Any(c => c.IsStringColumn)) o.Append('\n');
        Validations(o, m, parameterColumns.Contains);

        string selectKey = identityKey is not null ? $"SELECT {P(identityKey)} AS {Q(identityKey.DbName)};" : "SELECT 0 AS `Result`;";
        string whereClause = Eq(pk, P);
        string exists = $"EXISTS (SELECT 1 FROM {Table(m)} WHERE {whereClause})";
        string insert = $"INSERT INTO {Table(m)}\n\t\t({string.Join(", ", insertPairs.Select(p => Q(p.Name)))})\n\t\tVALUES\n\t\t({string.Join(", ", insertPairs.Select(p => p.Value))});\n" +
                        (identityKey is not null ? $"\t\tSELECT LAST_INSERT_ID() AS {Q(identityKey.DbName)}; -- the key of the new row\n" : "\t\tSELECT 0 AS `Result`;\n");
        string keyNotNull = string.Join(" AND ", pk.Select(k => $"{P(k)} IS NOT NULL"));
        if (updatePairs.Count > 0)
        {
            o.Append($"\tIF {keyNotNull} AND {exists} THEN\n");
            o.Append($"\t\tUPDATE {Table(m)} SET\n\t\t\t{string.Join(",\n\t\t\t", updatePairs.Select(p => $"{Q(p.Name)} = {p.Value}"))}\n\t\tWHERE {whereClause};\n");
            o.Append($"\t\t{selectKey}\n\tELSE\n\t\t{insert}\tEND IF;\n");
        }
        else
        {
            o.Append($"\tIF NOT ({keyNotNull} AND {exists}) THEN\n\t\t{insert}\tELSE\n\t\t{selectKey}\n\tEND IF;\n");
        }
        o.Append("END\n");

        string header = "-- Inserts the row if the primary key is not found, otherwise updates it. Always pass the entire record (an identity key as NULL to insert):\n" +
                        "-- on update every column is overwritten with what you pass. The key of the saved row comes back as a one-row result set.\n";
        return header + Create(m, "Save", parameterColumns.Select(In), o.ToString());
    }

    // ------------------------------------------------------------------------------------------------ Delete

    public static string Delete(TableModel m)
    {
        var pk = m.PrimaryKeyColumns;
        string body = "BEGIN\n" +
                      "\tDECLARE EXIT HANDLER FOR 1451 SELECT -1 AS `Result`; -- blocked by a foreign key elsewhere\n" +
                      "\tDECLARE EXIT HANDLER FOR SQLEXCEPTION SELECT -2 AS `Result`; -- the DELETE failed for another reason\n\n" +
                      $"\tDELETE FROM {Table(m)} WHERE ({Eq(pk, P)});\n" +
                      "\tSELECT 0 AS `Result`; -- deleted\n" +
                      "END\n";
        return "-- Result: 0 = deleted, -1 = blocked by a foreign key elsewhere, -2 = failed for some other reason.\n" +
               Create(m, "Delete", pk.Select(In), body);
    }

    // ------------------------------------------------------------------------------------------------ Clone

    public static string Clone(TableModel m)
    {
        var pk = m.PrimaryKeyColumns;
        bool singleGuidKey = pk.Count == 1 && pk[0].SqlType == SqlDbType.UniqueIdentifier && !pk[0].IsIdentity;
        bool IsGenerated(ColumnModel c) => c.IsIdentity || (singleGuidKey && c == pk[0]);
        string CopyFrom(ColumnModel c) => Q("CopyFrom" + c.Name);
        string NewKey(ColumnModel c) => Q("New" + c.Name);

        bool hasActive = m.HasActiveInactivePair && m.ActiveColumn is not null;
        var active = hasActive ? m.ActiveColumn : null;
        var inactiveDate = hasActive ? m.InactiveDateColumn : null;
        bool negative = active is not null && active.Name.Contains("Inactive", StringComparison.OrdinalIgnoreCase);
        bool IsRule(ColumnModel c) =>
            c.IsCreateDateColumn || c.IsLastChangedDateColumn || c.IsModifiedDateColumn || c.IsModifiedUserColumn || c.IsCreateUserColumn
            || c.IsInactiveReasonColumn || c.IsAdminFlagColumn || c == active || c == inactiveDate
            || (m.HasSoftDelete && (c == m.IsDeletedColumn || c == m.DeletedDateColumn));

        var copyable = m.Columns.Where(c => !c.IsComputed && !c.IsIdentity).ToList();
        var createUsers = copyable.Where(c => c.IsCreateUserColumn).ToList();
        var overrides = copyable.Where(c => c.IsInUniqueIndex && !c.IsPrimaryKey && !IsRule(c)).ToList();

        var pairs = new List<(string Name, string Value)>();
        foreach (var c in copyable)
        {
            string source = $"{Q("src")}.{Col(c)}";
            if (c.IsPrimaryKey)
                pairs.Add((c.DbName, IsGenerated(c) ? "v_new_key" : Trimmed(c, NewKey(c))));
            else if (c.IsCreateDateColumn || c.IsLastChangedDateColumn)
                pairs.Add((c.DbName, "NOW()"));
            else if (c.IsModifiedDateColumn || c.IsModifiedUserColumn || c.IsInactiveReasonColumn || c == inactiveDate || (m.HasSoftDelete && c == m.DeletedDateColumn))
                continue;
            else if (c == active)
                pairs.Add((c.DbName, Flag(c, !negative)));
            else if (c.IsAdminFlagColumn || (m.HasSoftDelete && c == m.IsDeletedColumn))
                pairs.Add((c.DbName, Flag(c, false)));
            else if (c.IsCreateUserColumn)
                pairs.Add((c.DbName, Trimmed(c, P(c))));
            else if (overrides.Contains(c))
                pairs.Add((c.DbName, $"COALESCE({Trimmed(c, P(c))}, {source})"));
            else
                pairs.Add((c.DbName, source));
        }

        var parameters = new List<(string, string, string)>();
        foreach (var k in pk) parameters.Add((CopyFrom(k), k.SqlTypeDeclaration, "IN"));
        foreach (var k in pk.Where(k => !IsGenerated(k))) parameters.Add((NewKey(k), k.SqlTypeDeclaration, "IN"));
        foreach (var c in createUsers) parameters.Add(In(c));
        foreach (var c in overrides) parameters.Add(In(c));

        var generated = pk.Where(IsGenerated).ToList();
        var generatedKey = generated.Count == 1 ? generated[0] : null;
        string keyMatch = Eq(pk, CopyFrom, "src");
        string keyDescription = string.Join(", ', ', ", pk.Select(k => $"'{Message(k.Name)} = ', COALESCE(CAST({CopyFrom(k)} AS CHAR), 'NULL')"));

        var o = new StringBuilder("BEGIN\n");
        if (generatedKey is not null)
            o.Append($"\tDECLARE v_new_key {generatedKey.SqlTypeDeclaration};\n");
        o.Append("\tDECLARE v_message varchar(400);\n\n");
        if (generatedKey is not null && !generatedKey.IsIdentity)
            o.Append("\tSET v_new_key = UUID(); -- a new key for the new row\n\n");
        o.Append($"\tIF NOT EXISTS (SELECT 1 FROM {Table(m)} AS {Q("src")} WHERE {keyMatch}) THEN\n");
        o.Append($"\t\tSET v_message = CONCAT('No {Message(m.TableName)} found to clone (', {keyDescription}, ').');\n");
        o.Append("\t\tSIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = v_message, MYSQL_ERRNO = 55509;\n\tEND IF;\n\n");
        if (pairs.Count == 0)
        {
            o.Append($"\tINSERT INTO {Table(m)} () VALUES ();\n");
        }
        else
        {
            o.Append($"\tINSERT INTO {Table(m)}\n\t({string.Join(", ", pairs.Select(p => Q(p.Name)))})\n");
            o.Append($"\tSELECT\n\t\t{string.Join(",\n\t\t", pairs.Select(p => p.Value))}\n\tFROM {Table(m)} AS {Q("src")}\n\tWHERE {keyMatch};\n");
        }
        if (generatedKey is { IsIdentity: true })
            o.Append($"\n\tSET v_new_key = LAST_INSERT_ID();\n");
        // The new key comes back as a one-row result set whose column is called Value, which is where EF Core's SqlQueryRaw<int> reads a scalar from.
        o.Append(generatedKey is not null ? "\n\tSELECT v_new_key AS `Value`; -- the key of the new row\n" : "\n\tSELECT 0 AS `Result`;\n");
        o.Append("END\n");
        return $"-- Copies the {m.TableName} row named by `CopyFrom...` into a new row and returns the new key as a one-row result set.\n" +
               Create(m, "Clone", parameters, o.ToString());
    }

    // ------------------------------------------------------------------------------------------------ Load

    public static string Load(TableModel m)
    {
        if (!m.HasRowData)
            throw new InvalidOperationException("SP_Load needs the table's rows; its .tt.config must say NeedsRowData=true.");

        var loadColumns = m.Columns.Where(c => !c.IsComputed && !c.IsModifiedDateColumn && !c.IsModifiedUserColumn).ToList();
        int Index(ColumnModel c) => m.Columns.IndexOf(c);
        string Value(object?[] row, ColumnModel c) => c.IsCreateDateColumn || c.IsLastChangedDateColumn ? "NOW()" : MySqlLiteral.Format(c, row[Index(c)]);
        string columnList = string.Join(", ", loadColumns.Select(Col));
        // "already there" is decided by the primary key: ON DUPLICATE KEY UPDATE with the key set to itself changes nothing, and unlike INSERT IGNORE it does not hide other errors.
        string duplicate = m.PrimaryKeyColumns.Count > 0 ? $"\n\t  ON DUPLICATE KEY UPDATE {Col(m.PrimaryKeyColumns[0])} = {Col(m.PrimaryKeyColumns[0])}" : "";

        var o = new StringBuilder("BEGIN\n");
        if (m.Rows.Count == 0)
            o.Append($"\t-- {Table(m)} had no rows when this procedure was generated, so there is nothing to load.\n\tSELECT 0 AS `Result`;\n");
        else
            foreach (var row in m.Rows)
                o.Append($"\tINSERT INTO {Table(m)} ({columnList})\n\t  VALUES ({string.Join(", ", loadColumns.Select(c => Value(row, c)))}){duplicate};\n");
        o.Append("END\n");

        string header = $"-- Loads the {m.Rows.Count} row(s) of {Table(m)} that existed when this was generated. Safe to run repeatedly: a row whose primary key is already present is left alone.\n" +
                        "-- Load tables that this one refers to first. Keys are loaded as they are; the AUTO_INCREMENT counter moves past the highest one by itself.\n";
        return header + Create(m, "Load", [], o.ToString());
    }

    // ------------------------------------------------------------------------------------------------ Lookup

    public static string Lookup(TableModel m)
    {
        if (m.ForeignKeys.Count > 0 && !m.HasReferencedDisplayColumns)
            throw new InvalidOperationException("SP_Lookup needs the foreign-keyed tables' display columns; its .tt.config must say NeedsReferencedDisplayColumns=true.");

        string baseTable = Table(m);
        string BaseCol(ColumnModel c) => $"{Q(m.DbTableName)}.{Col(c)}";

        string Unique(HashSet<string> used, string wanted)
        {
            string candidate = wanted;
            for (int n = 2; !used.Add(candidate); n++) candidate = wanted + n;
            return candidate;
        }
        string Role(ForeignKeyModel fk)
        {
            if (fk.ReferencingColumns.Count != 1) return fk.ReferencedTable;
            string name = fk.ReferencingColumns[0];
            foreach (string suffix in new[] { "EnumID", "ID", "Id" })
                if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { name = name[..^suffix.Length]; break; }
            string tail = "_" + fk.ReferencedTable;
            if (name.Length > tail.Length && name.EndsWith(tail, StringComparison.OrdinalIgnoreCase)) name = name[..^tail.Length];
            name = name.TrimEnd('_');
            return name.Length > 0 ? name : fk.ReferencedTable;
        }
        string TrimTablePrefix(string column, string table) =>
            column.Length > table.Length && column.StartsWith(table, StringComparison.OrdinalIgnoreCase) ? column[table.Length..] : column;

        var select = new List<string>();
        var outputNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var baseSelected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddBase(ColumnModel c)
        {
            if (!baseSelected.Add(c.Name)) return;
            outputNames.Add(c.Name);
            select.Add(c.DbName == c.Name ? BaseCol(c) : $"{BaseCol(c)} AS {Q(c.Name)}");
        }

        var active = m.HasActiveInactivePair ? m.ActiveColumn : null;
        foreach (var c in m.PrimaryKeyColumns) AddBase(c);
        foreach (var c in m.DisplayColumns) AddBase(c);
        if (active is not null) AddBase(active);

        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { m.TableName };
        var foreignKeys = new List<(string Role, ForeignKeyModel Fk, List<ColumnModel> Columns)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fk in m.ForeignKeys)
        {
            var columns = fk.ReferencingColumns.Select(n => m.Columns.FirstOrDefault(c => c.Name.Equals(n, StringComparison.OrdinalIgnoreCase))).ToList();
            if (columns.Any(c => c is null)) continue;
            if (!seen.Add(string.Join(",", fk.ReferencingColumns) + ">" + fk.ReferencedSchema + "." + fk.ReferencedTable)) continue;
            foreignKeys.Add((Unique(aliases, Role(fk)), fk, columns!));
        }

        var joins = new List<string>();
        foreach (var (role, fk, fkColumns) in foreignKeys.OrderBy(f => f.Role, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var c in fkColumns) AddBase(c);
            var display = fk.ReferencedDisplayColumns.Where(n => !fk.ReferencedColumns.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
            if (display.Count == 0) continue;
            foreach (string d in display)
            {
                string wanted = display.Count == 1 ? role : role + TrimTablePrefix(d, fk.ReferencedTable);
                string dbColumn = DatabaseNameOf(fk, d);
                select.Add($"{Q(role)}.{Q(dbColumn)} AS {Q(Unique(outputNames, wanted))}");
            }
            string on = string.Join(" AND ", fk.ReferencedDbColumns.Select((r, i) => $"{Q(role)}.{Q(r)} = {BaseCol(fkColumns[i])}"));
            joins.Add($"{(fkColumns.Any(c => c.IsNullable) ? "LEFT JOIN" : "INNER JOIN")} {Q(fk.ReferencedDbTable)} AS {Q(role)} ON {on}");
        }

        var sortColumn = m.DisplayColumns.OrderBy(c => c.DisplayRank ?? int.MaxValue).ThenBy(c => c.OrdinalPosition).FirstOrDefault();
        var orderBy = new List<string>();
        if (sortColumn is not null) orderBy.Add(BaseCol(sortColumn) + " ASC");
        foreach (var k in m.PrimaryKeyColumns.Where(k => k != sortColumn)) orderBy.Add(BaseCol(k) + " ASC");

        string activeFilter = "";
        if (active is not null)
        {
            bool negative = active.Name.Contains("Inactive", StringComparison.OrdinalIgnoreCase);
            string value = Flag(active, !negative);
            string test = active.IsNullable ? $"COALESCE({BaseCol(active)}, {value}) = {value}" : $"{BaseCol(active)} = {value}";
            activeFilter = $"({Q("pblnIncludeInactive")} = 1 OR {test})";
        }

        var o = new StringBuilder("BEGIN\n");
        o.Append($"\tSELECT\n\t\t{string.Join(",\n\t\t", select)}\n\tFROM {baseTable}\n");
        foreach (string join in joins) o.Append($"\t{join}\n");
        if (activeFilter.Length > 0) o.Append($"\tWHERE {activeFilter}\n");
        o.Append($"\tORDER BY {string.Join(", ", orderBy)};\nEND\n");

        var parameters = active is not null ? new List<(string, string, string)> { (Q("pblnIncludeInactive"), "tinyint(1)", "IN") } : [];
        string header = (active is not null ? "-- Returns only active rows unless `pblnIncludeInactive` is 1.\n" : "") +
                        "-- Unlike the SQL Server version this returns an empty result when there is nothing to return (it does not raise error 55508).\n";
        return header + Create(m, "Lookup", parameters, o.ToString());
    }

    // The database name of one of a referenced table's display columns (the model lists them under the generated names and, in parallel, the real ones).
    private static string DatabaseNameOf(ForeignKeyModel fk, string generatedName)
    {
        int i = fk.ReferencedDisplayColumns.FindIndex(c => c.Equals(generatedName, StringComparison.OrdinalIgnoreCase));
        return i >= 0 ? fk.ReferencedDisplayDbColumns[i] : generatedName;
    }

    // ------------------------------------------------------------------------------------------------ Junction

    public static string Junction(TableModel m)
    {
        if (!m.IsJunctionTable)
            throw new InvalidOperationException($"SP_Junction cannot generate from {Table(m)}: it is not a many-to-many junction table -- TableModel.IsJunctionTable is false.");
        if (!m.HasReferencedDisplayColumns)
            throw new InvalidOperationException("SP_Junction needs the target table's display columns; its .tt.config must say NeedsReferencedDisplayColumns=true.");

        var anchorFk = m.JunctionForeignKeys[0];
        var targetFk = m.JunctionForeignKeys[1];
        var anchor = m.Columns.First(c => c.Name.Equals(anchorFk.ReferencingColumns[0], StringComparison.OrdinalIgnoreCase));
        var target = m.Columns.First(c => c.Name.Equals(targetFk.ReferencingColumns[0], StringComparison.OrdinalIgnoreCase));
        string anchorParam = Q("Anchor" + anchor.Name);
        string targetParam = Q("Target" + target.Name);
        string targetKey = targetFk.ReferencedDbColumns[0];
        string junction = Table(m);
        string targetTable = Q(targetFk.ReferencedDbTable);
        var createDate = m.Columns.FirstOrDefault(c => c.IsCreateDateColumn);
        var createUser = m.Columns.FirstOrDefault(c => c.IsCreateUserColumn);

        var items = new List<string> { $"{Q("t")}.{Q(targetKey)} AS {Q("TargetId")}" };
        foreach (string d in targetFk.ReferencedDisplayColumns)
            items.Add($"CAST({Q("t")}.{Q(DatabaseNameOf(targetFk, d))} AS CHAR(500)) AS {Q(d)}");
        items.Add($"CASE WHEN EXISTS (SELECT 1 FROM {junction} AS {Q("x")} WHERE {Q("x")}.{Col(anchor)} = {anchorParam} AND {Q("x")}.{Col(target)} = {Q("t")}.{Q(targetKey)}) THEN 1 ELSE 0 END AS {Q("IsSelected")}");
        string orderBy = targetFk.ReferencedDisplayColumns.Count > 0 ? Q(DatabaseNameOf(targetFk, targetFk.ReferencedDisplayColumns[0])) : Q(targetKey);
        string list = Create(m, "List", [(anchorParam, anchor.SqlTypeDeclaration, "IN")],
            $"BEGIN\n\tSELECT\n\t\t{string.Join(",\n\t\t", items)}\n\tFROM {targetTable} AS {Q("t")}\n\tORDER BY {Q("t")}.{orderBy};\nEND\n");

        var linkColumns = new List<(string Name, string Value)> { (anchor.DbName, anchorParam), (target.DbName, targetParam) };
        if (createDate is not null) linkColumns.Add((createDate.DbName, "NOW()"));
        if (createUser is not null) linkColumns.Add((createUser.DbName, Q("CreateUser")));
        var linkParameters = new List<(string, string, string)> { (anchorParam, anchor.SqlTypeDeclaration, "IN"), (targetParam, target.SqlTypeDeclaration, "IN") };
        if (createUser is not null) linkParameters.Add((Q("CreateUser"), createUser.SqlTypeDeclaration, "IN"));
        string link = Create(m, "Link", linkParameters,
            "BEGIN\n" +
            $"\tIF NOT EXISTS (SELECT 1 FROM {junction} WHERE {Col(anchor)} = {anchorParam} AND {Col(target)} = {targetParam}) THEN\n" +
            $"\t\tINSERT INTO {junction}\n\t\t({string.Join(", ", linkColumns.Select(c => Q(c.Name)))})\n\t\tVALUES\n\t\t({string.Join(", ", linkColumns.Select(c => c.Value))});\n\tEND IF;\nEND\n");

        string unlink = Create(m, "Unlink", [(anchorParam, anchor.SqlTypeDeclaration, "IN"), (targetParam, target.SqlTypeDeclaration, "IN")],
            $"BEGIN\n\tDELETE FROM {junction} WHERE {Col(anchor)} = {anchorParam} AND {Col(target)} = {targetParam};\nEND\n");

        return list + "\n" + link + "\n" + unlink;
    }
}
