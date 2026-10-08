using System.Data;
using System.Text;

namespace CodeGenNew.Core;

/// <summary> The PostgreSQL counterparts of the SP_* templates. PostgreSQL has functions, not stored procedures with @parameters,
/// so each T-SQL procedure becomes a function in the same schema with the same name, called with SELECT (or SELECT * FROM for one
/// that returns rows). The rules each template documents (audit columns, active/inactive, admin flag, start/end dates, unique-index
/// overrides) are the same; only the SQL dialect and the calling convention change:
///   - a parameter is the quoted T-SQL parameter name without the @ ("pstrName"), so it never equals a column name;
///   - PostgreSQL puts the parameters without a default first, so a required key parameter may move ahead of optional ones (call by name or position);
///   - an OUTPUT parameter becomes the function's return value (the new row's key); errors are raised, not returned;
///   - the caller's transaction is always the function's transaction (there is no BEGIN TRANSACTION inside a function);
///   - GETDATE() is now(), NEWID() is gen_random_uuid(), ISNULL is COALESCE, LTRIM(RTRIM()) is btrim().
/// The template files (SP_Insert.tt and so on) call these when TableModel.Dialect is PostgreSql. </summary>
public static class PostgresProcedures
{
    private static string Q(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
    private static string P(string parameterName) => Q(parameterName.TrimStart('@'));
    private static string P(ColumnModel c) => P(c.ParameterName);
    private static string Table(TableModel m) => Q(m.SchemaName) + "." + Q(m.DbTableName);
    private static string Fn(TableModel m, string suffix) => Q(m.SchemaName) + "." + Q(m.TableName + "_" + suffix);
    private static string Message(string text) => text.Replace("'", "''").Replace("%", "%%");
    private static string Eq(IEnumerable<ColumnModel> keys, Func<ColumnModel, string> value, string? tableAlias = null) =>
        string.Join(" AND ", keys.Select(k => $"{(tableAlias is null ? "" : Q(tableAlias) + ".")}{Q(k.DbName)} = {value(k)}"));

    /// <summary> The literal for a flag column in its own type (boolean, number or text). </summary>
    private static string Flag(ColumnModel c, bool value) =>
        c.IsStringColumn ? (c.Name.ContainsIgnoreCase("Admin") ? (value ? "'True'" : "'False'") : (value ? "'1'" : "'0'"))
        : c.IsBooleanColumn ? (value ? "true" : "false")
        : (value ? "1" : "0");

    private static string Trimmed(ColumnModel c, string expression) => c.IsStringColumn ? $"btrim({expression})" : expression;

    /// <summary> Parameters as "name type [DEFAULT x]" lines, those without a default first (PostgreSQL requires it). </summary>
    private static string ParameterList(IEnumerable<(string Name, string Type, string? Default)> parameters)
    {
        var ordered = parameters.Where(p => p.Default is null).Concat(parameters.Where(p => p.Default is not null)).ToList();
        return string.Join(",\n", ordered.Select(p => $"{p.Name} {p.Type}{(p.Default is null ? "" : " DEFAULT " + p.Default)}"));
    }

    private static string Create(TableModel m, string suffix, string parameters, string returns, string body) =>
        $"CREATE OR REPLACE FUNCTION {Fn(m, suffix)}({(parameters.Length > 0 ? "\n" + parameters + "\n" : "")})\n" +
        $"RETURNS {returns}\nLANGUAGE plpgsql\nAS $$\n{body}$$;\n";

    private static void Validations(StringBuilder o, TableModel m, Func<ColumnModel, bool> isParameter)
    {
        if (m.HasStartEndDatePair && m.StartDateColumn is { } s && m.EndDateColumn is { } e && isParameter(s) && isParameter(e))
        {
            o.Append($"\tIF ({P(s)} IS NOT NULL AND {P(e)} IS NOT NULL AND {P(s)} > {P(e)}) THEN\n");
            o.Append($"\t\tRAISE EXCEPTION '{Message($"{Q(s.DbName)} must not be later than {Q(e.DbName)}.")}';\n\tEND IF;\n\n");
        }
        if (m.HasInOutDateTimePair && m.InDateColumn is { } id && m.InTimeColumn is { } it && m.OutDateColumn is { } od && m.OutTimeColumn is { } ot
            && new[] { id, it, od, ot }.All(isParameter))
        {
            o.Append($"\tIF ({P(id)} IS NOT NULL AND {P(it)} IS NOT NULL AND {P(od)} IS NOT NULL AND {P(ot)} IS NOT NULL\n");
            o.Append($"\t    AND ({P(id)} > {P(od)} OR ({P(id)} = {P(od)} AND {P(it)} > {P(ot)}))) THEN\n");
            o.Append($"\t\tRAISE EXCEPTION '{Message($"{Q(id.DbName)}/{Q(it.DbName)} must not be later than {Q(od.DbName)}/{Q(ot.DbName)}.")}';\n\tEND IF;\n\n");
        }
    }

    // =============== Insert ===============

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
            .Concat(autoTouched.Select(c => (Name: c.DbName, Value: "now()"))).ToList();
        if (m.HasActiveInactivePair && m.ActiveColumn is { } active)
            pairs.Add((active.DbName, Flag(active, !active.IsInactive)));
        foreach (var admin in m.Columns.Where(c => c.IsAdminFlagColumn))
            pairs.Add((admin.DbName, Flag(admin, false)));

        // Non-key string columns default to '' and money to 0 (as in SP_Insert), everything else to NULL.
        var parameters = parameterColumns.Select(c => (P(c), c.SqlTypeDeclaration,
            c.IsPrimaryKey ? null : c.IsStringColumn ? "''" : c.IsMoneyColumn ? "0" : "NULL"));

        var o = new StringBuilder("BEGIN\n");
        Validations(o, m, parameterColumns.Contains);
        o.Append($"\tINSERT INTO {Table(m)}");
        if (pairs.Count == 0)
            o.Append(" DEFAULT VALUES");
        else
            o.Append($"\n\t({string.Join(", ", pairs.Select(p => Q(p.Name)))})\n\tVALUES\n\t({string.Join(", ", pairs.Select(p => p.Value))})");
        if (identity is not null)
            o.Append($"\n\tRETURNING {Q(identity.DbName)} INTO v_new_id");
        o.Append(";\n");
        if (identity is not null)
            o.Append("\n\tRETURN v_new_id;\n");
        else
            o.Append("\n\tGET DIAGNOSTICS v_rows = ROW_COUNT;\n\tRETURN v_rows;\n");
        o.Append("END\n");
        string declare = identity is not null ? $"DECLARE\n\tv_new_id {identity.SqlTypeDeclaration};\n" : "DECLARE\n\tv_rows integer;\n";
        return Create(m, "Insert", ParameterList(parameters), identity?.SqlTypeDeclaration ?? "integer", declare + o);
    }

    // =============== Update ===============

    public static string Update(TableModel m)
    {
        var updatable = m.Columns
            .Where(c => !c.IsComputed && !c.IsCreateDateColumn && !c.IsCreateUserColumn && !c.IsModifiedDateColumn && !c.IsLastChangedDateColumn).ToList();
        var setColumns = updatable.Where(c => !c.IsPrimaryKey && c != m.ActiveColumn).ToList();
        var autoTouched = m.Columns.Where(c => c.IsModifiedDateColumn || c.IsLastChangedDateColumn).ToList();

        var triggers = new List<ColumnModel>();
        if (m.InactiveDateColumn is not null) triggers.Add(m.InactiveDateColumn);
        triggers.AddRange(m.Columns.Where(c => c.IsInactiveReasonColumn));

        // NULL means "do not change this column" (as in SP_Update), which COALESCE gives: SET col = COALESCE(param, col).
        var sets = new List<string>();
        foreach (var c in autoTouched) sets.Add($"{Q(c.DbName)} = now()");
        foreach (var c in setColumns) sets.Add($"{Q(c.DbName)} = COALESCE({Trimmed(c, P(c))}, {Q(c.DbName)})");
        if (m.HasActiveInactivePair && m.ActiveColumn is { } active)
        {
            bool negative = active.IsInactive;
            string own = $"COALESCE({Trimmed(active, P(active))}, {Q(active.DbName)})";
            sets.Add(triggers.Count > 0
                ? $"{Q(active.DbName)} = CASE WHEN {string.Join(" OR ", triggers.Select(c => $"{P(c)} IS NOT NULL"))} THEN {Flag(active, negative)} ELSE {own} END"
                : $"{Q(active.DbName)} = {own}");
        }

        var parameters = updatable.Select(c => (P(c), c.SqlTypeDeclaration, c.IsPrimaryKey ? null : "NULL"));
        var o = new StringBuilder("BEGIN\n");
        Validations(o, m, updatable.Contains);
        o.Append($"\tIF {P(m.PrimaryKeyColumns[0])} IS NULL THEN\n\t\tRETURN 0;\n\tEND IF;\n\n");
        if (sets.Count == 0)
        {
            o.Append("\tRETURN 0; -- nothing to change\n");
        }
        else
        {
            o.Append($"\tUPDATE {Table(m)} SET\n\t\t{string.Join(",\n\t\t", sets)}\n\tWHERE {Eq(m.PrimaryKeyColumns, k => P(k))};\n\n");
            o.Append("\tGET DIAGNOSTICS v_rows = ROW_COUNT;\n\tRETURN CASE WHEN v_rows > 0 THEN 1 ELSE 0 END; -- 1 = the row was found and updated\n");
        }
        o.Append("END\n");
        return Create(m, "Update", ParameterList(parameters), "integer", "DECLARE\n\tv_rows integer;\n" + o);
    }

    // =============== Save =============== 

    public static string Save(TableModel m)
    {
        var identityKey = m.PrimaryKeyColumns.FirstOrDefault(c => c.IsIdentity);
        var parameterColumns = m.Columns
            .Where(c => !c.IsComputed && !c.IsCreateDateColumn && !c.IsModifiedDateColumn && !c.IsLastChangedDateColumn && (!c.IsIdentity || c.IsPrimaryKey)).ToList();
        var pk = m.PrimaryKeyColumns;
        bool hasActive = m.HasActiveInactivePair && m.ActiveColumn is not null;
        var active = hasActive ? m.ActiveColumn : null;
        bool negative = active is not null && active.IsInactive;

        string Supplied(ColumnModel c) => c.IsStringColumn ? $"length({P(c)}) > 0" : $"{P(c)} IS NOT NULL";
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
            updatePairs.Add((c.DbName, "now()"));

        var insertPairs = parameterColumns
            .Where(c => c != identityKey && !c.IsInactiveReasonColumn && !c.IsAdminFlagColumn && !c.IsModifiedUserColumn && c != m.InactiveDateColumn && c != active)
            .Select(c => (Name: c.DbName, Value: P(c))).ToList();
        foreach (var c in m.Columns.Where(c => !c.IsComputed && (c.IsCreateDateColumn || c.IsLastChangedDateColumn)))
            insertPairs.Add((c.DbName, "now()"));
        if (active is not null)
            insertPairs.Add((active.DbName, Flag(active, !negative)));
        foreach (var c in m.Columns.Where(c => c.IsAdminFlagColumn && !c.IsComputed))
            insertPairs.Add((c.DbName, Flag(c, false)));

        // Every parameter is required (no defaults, so a forgotten one is an error rather than a NULL written over the column); only an identity key may be omitted.
        var parameters = parameterColumns.Select(c => (P(c), c.SqlTypeDeclaration, c == identityKey ? "NULL" : (string?)null));

        var o = new StringBuilder("BEGIN\n");
        foreach (var c in parameterColumns.Where(c => c.IsStringColumn))
            o.Append($"\t{P(c)} := {(c.IsNullable ? $"btrim({P(c)})" : $"COALESCE(btrim({P(c)}), '')")};\n");
        if (parameterColumns.Any(c => c.IsStringColumn)) o.Append('\n');
        Validations(o, m, parameterColumns.Contains);

        string returnValue = identityKey is not null ? $"RETURN {P(identityKey)};" : "RETURN;";
        string whereClause = Eq(pk, k => P(k));
        if (updatePairs.Count > 0)
        {
            o.Append($"\tIF {string.Join(" AND ", pk.Select(k => $"{P(k)} IS NOT NULL"))} THEN\n");
            o.Append($"\t\tUPDATE {Table(m)} SET\n\t\t\t{string.Join(",\n\t\t\t", updatePairs.Select(p => $"{Q(p.Name)} = {p.Value}"))}\n\t\tWHERE {whereClause};\n");
            o.Append($"\t\tIF FOUND THEN\n\t\t\t{returnValue}\n\t\tEND IF;\n\tEND IF;\n\n");
        }
        else
        {
            // Every column is part of the key: nothing to update, so only insert when the key is missing.
            o.Append($"\tIF EXISTS (SELECT 1 FROM {Table(m)} WHERE {whereClause}) THEN\n\t\t{returnValue}\n\tEND IF;\n\n");
        }
        o.Append($"\tINSERT INTO {Table(m)}\n\t({string.Join(", ", insertPairs.Select(p => Q(p.Name)))})\n\tVALUES\n\t({string.Join(", ", insertPairs.Select(p => p.Value))})");
        if (identityKey is not null)
            o.Append($"\n\tRETURNING {Q(identityKey.DbName)} INTO v_new_id");
        o.Append(";\n\n");
        o.Append(identityKey is not null ? "\tRETURN v_new_id; -- the key of the new row\n" : "\tRETURN;\n");
        o.Append("END\n");

        string declare = identityKey is not null ? $"DECLARE\n\tv_new_id {identityKey.SqlTypeDeclaration};\n" : "";
        string header = "-- Inserts the row if the primary key is not found, otherwise updates it. Always pass the entire record: on update every column is overwritten with what you pass.\n";
        return header + Create(m, "Save", ParameterList(parameters), identityKey?.SqlTypeDeclaration ?? "void", declare + o);
    }

    // =============== Delete =============== 

    public static string Delete(TableModel m)
    {
        var pk = m.PrimaryKeyColumns;
        var parameters = pk.Select(c => (P(c), c.SqlTypeDeclaration, (string?)null));
        string body = "BEGIN\n" +
                      $"\tDELETE FROM {Table(m)} WHERE ({Eq(pk, k => P(k))});\n" +
                      $"\tRETURN {DeleteResult.Deleted}; -- deleted\n" +
                      "EXCEPTION\n" +
                      $"\tWHEN foreign_key_violation THEN\n\t\tRETURN {DeleteResult.BlockedByForeignKey}; -- blocked by a foreign key elsewhere\n" +
                      $"\tWHEN OTHERS THEN\n\t\tRETURN {DeleteResult.Failed}; -- the DELETE failed for another reason\n" +
                      "END\n";
        return "-- Returns 0 = deleted, -1 = blocked by a foreign key elsewhere, -2 = failed for some other reason.\n" +
               Create(m, "Delete", ParameterList(parameters), "integer", body);
    }

    // =============== Clone =============== 

    public static string Clone(TableModel m)
    {
        var pk = m.PrimaryKeyColumns;
        bool singleGuidKey = pk.Count == 1 && pk[0].IsGuidColumn && !pk[0].IsIdentity;
        bool IsGenerated(ColumnModel c) => c.IsIdentity || (singleGuidKey && c == pk[0]);
        string CopyFrom(ColumnModel c) => Q("CopyFrom" + c.Name);
        string NewKey(ColumnModel c) => Q("New" + c.Name);

        bool hasActive = m.HasActiveInactivePair && m.ActiveColumn is not null;
        var active = hasActive ? m.ActiveColumn : null;
        var inactiveDate = hasActive ? m.InactiveDateColumn : null;
        bool negative = active is not null && active.IsInactive;
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
            string source = $"{Q("src")}.{Q(c.DbName)}";
            if (c.IsPrimaryKey)
                pairs.Add((c.DbName, IsGenerated(c) ? "v_new_key" : Trimmed(c, NewKey(c))));
            else if (c.IsCreateDateColumn || c.IsLastChangedDateColumn)
                pairs.Add((c.DbName, "now()"));
            else if (c.IsModifiedDateColumn || c.IsModifiedUserColumn || c.IsInactiveReasonColumn || c == inactiveDate || (m.HasSoftDelete && c == m.DeletedDateColumn))
                continue; // a new row has nothing to put here: left at the column's default (normally NULL)
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

        // Which row, the new key (only for a key the database cannot generate), then the optional extras.
        var parameters = new List<(string, string, string?)>();
        foreach (var k in pk) parameters.Add((CopyFrom(k), k.SqlTypeDeclaration, null));
        foreach (var k in pk.Where(k => !IsGenerated(k))) parameters.Add((NewKey(k), k.SqlTypeDeclaration, null));
        foreach (var c in createUsers) parameters.Add((P(c), c.SqlTypeDeclaration, c.IsStringColumn ? "''" : "NULL"));
        foreach (var c in overrides) parameters.Add((P(c), c.SqlTypeDeclaration, "NULL"));

        var generated = pk.Where(IsGenerated).ToList();
        var generatedKey = generated.Count == 1 ? generated[0] : null;
        string keyMatch = Eq(pk, k => CopyFrom(k), "src");
        string keyDescription = string.Join(" || ', ' || ", pk.Select(k => $"'{Message(k.Name)} = ' || COALESCE({CopyFrom(k)}::text, 'NULL')"));

        var o = new StringBuilder("BEGIN\n");
        if (generatedKey is not null && !generatedKey.IsIdentity)
            o.Append("\tv_new_key := gen_random_uuid(); -- a new key for the new row\n\n");
        o.Append($"\tIF NOT EXISTS (SELECT 1 FROM {Table(m)} AS {Q("src")} WHERE {keyMatch}) THEN\n");
        o.Append($"\t\tRAISE EXCEPTION 'No {Message(m.TableName)} found to clone (%).', {keyDescription} USING ERRCODE = '{SqlErrorCodes.NoRowToClone}';\n\tEND IF;\n\n");
        if (pairs.Count == 0)
        {
            o.Append($"\tINSERT INTO {Table(m)} DEFAULT VALUES");
        }
        else
        {
            o.Append($"\tINSERT INTO {Table(m)}\n\t({string.Join(", ", pairs.Select(p => Q(p.Name)))})\n");
            o.Append($"\tSELECT\n\t\t{string.Join(",\n\t\t", pairs.Select(p => p.Value))}\n\tFROM {Table(m)} AS {Q("src")}\n\tWHERE {keyMatch}");
        }
        if (generatedKey is { IsIdentity: true })
            o.Append($"\n\tRETURNING {Q(generatedKey.DbName)} INTO v_new_key");
        o.Append(";\n\n");
        o.Append(generatedKey is not null ? "\tRETURN v_new_key; -- the key of the new row\n" : "\tRETURN;\n");
        o.Append("END\n");

        string declare = generatedKey is not null ? $"DECLARE\n\tv_new_key {generatedKey.SqlTypeDeclaration};\n" : "";
        string header = $"-- Copies the {m.TableName} row named by \"CopyFrom...\" into a new row and returns the new key.\n";
        return header + Create(m, "Clone", ParameterList(parameters), generatedKey?.SqlTypeDeclaration ?? "void", declare + o);
    }

    // =============== Load =============== 

    public static string Load(TableModel m)
    {
        if (!m.HasRowData)
            throw new InvalidOperationException("SP_Load needs the table's rows; its .tt.config must say NeedsRowData=true.");

        var loadColumns = m.Columns.Where(c => !c.IsComputed && !c.IsModifiedDateColumn && !c.IsModifiedUserColumn).ToList();
        var identity = loadColumns.FirstOrDefault(c => c.IsIdentity);
        string table = Table(m);
        int Index(ColumnModel c) => m.Columns.IndexOf(c);
        string Value(object?[] row, ColumnModel c) => c.IsCreateDateColumn || c.IsLastChangedDateColumn ? "now()" : PostgresLiteral.Format(c, row[Index(c)]);
        string columnList = string.Join(", ", loadColumns.Select(c => Q(c.DbName)));
        string conflict = m.PrimaryKeyColumns.Count > 0 ? $" ON CONFLICT ({string.Join(", ", m.PrimaryKeyColumns.Select(k => Q(k.DbName)))}) DO NOTHING" : "";

        var o = new StringBuilder("BEGIN\n");
        if (m.Rows.Count == 0)
        {
            o.Append($"\t-- {table} had no rows when this function was generated, so there is nothing to load.\n\tNULL;\n");
        }
        else
        {
            foreach (var row in m.Rows)
                o.Append($"\tINSERT INTO {table} ({columnList}){(identity is not null ? " OVERRIDING SYSTEM VALUE" : "")}\n\t  VALUES ({string.Join(", ", loadColumns.Select(c => Value(row, c)))}){conflict};\n");
            if (identity is not null)
            {
                // Keys were loaded as they are, so move the identity counter past the highest one (SQL Server does this by itself).
                o.Append($"\n\tPERFORM setval(pg_get_serial_sequence('{table.Replace("'", "''")}', '{identity.Name.Replace("'", "''")}'), " +
                         $"(SELECT COALESCE(MAX({Q(identity.DbName)}), 1) FROM {table}));\n");
            }
        }
        o.Append("END\n");

        string header = $"-- Loads the {m.Rows.Count} row(s) of {table} that existed when this was generated. Safe to run repeatedly: a row whose primary key is already present is left alone.\n" +
                        "-- Load tables that this one refers to first. All rows or none (one transaction: the caller's).\n";
        return header + Create(m, "Load", "", "void", o.ToString());
    }

    // =============== Lookup ===============

    public static string Lookup(TableModel m)
    {
        if (m.ForeignKeys.Count > 0 && !m.HasReferencedDisplayColumns)
            throw new InvalidOperationException("SP_Lookup needs the foreign-keyed tables' display columns; its .tt.config must say NeedsReferencedDisplayColumns=true.");

        string baseTable = Table(m);
        string BaseCol(ColumnModel c) => $"{Q(m.DbTableName)}.{Q(c.DbName)}";

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
            foreach (string suffix in new[] { "EnumID", "ID" })
                if (name.Length > suffix.Length && name.EndsWithIgnoreCase(suffix)) { name = name[..^suffix.Length]; break; }
            string tail = "_" + fk.ReferencedTable;
            if (name.Length > tail.Length && name.EndsWithIgnoreCase(tail)) name = name[..^tail.Length];
            name = name.TrimEnd('_');
            return name.Length > 0 ? name : fk.ReferencedTable;
        }
        string TrimTablePrefix(string column, string table) =>
            column.Length > table.Length && column.StartsWithIgnoreCase(table) ? column[table.Length..] : column;

        // (expression, output name, output type): RETURNS TABLE needs every output column's type.
        var select = new List<(string Sql, string Name, string Type)>();
        var outputNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var baseSelected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddBase(ColumnModel c)
        {
            if (!baseSelected.Add(c.Name)) return;
            outputNames.Add(c.Name);
            select.Add((BaseCol(c), c.Name, c.SqlTypeDeclaration));
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
            var columns = fk.ReferencingColumns.Select(n => m.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(n))).OfType<ColumnModel>().ToList();
            if (columns.Count != fk.ReferencingColumns.Count) continue;
            if (!seen.Add(string.Join(",", fk.ReferencingColumns) + ">" + fk.ReferencedSchema + "." + fk.ReferencedTable)) continue;
            foreignKeys.Add((Unique(aliases, Role(fk)), fk, columns));
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
                string outName = Unique(outputNames, wanted);
                select.Add(($"{Q(role)}.{Q(DatabaseNameOf(fk, d))}::text AS {Q(outName)}", outName, "text"));
            }
            string on = string.Join(" AND ", fk.ReferencedDbColumns.Select((r, i) => $"{Q(role)}.{Q(r)} = {BaseCol(fkColumns[i])}"));
            joins.Add($"{(fkColumns.Any(c => c.IsNullable) ? "LEFT JOIN" : "INNER JOIN")} {Q(fk.ReferencedSchema)}.{Q(fk.ReferencedDbTable)} AS {Q(role)} ON {on}");
        }

        var sortColumn = m.DisplayColumns.OrderBy(c => c.DisplayRank ?? int.MaxValue).ThenBy(c => c.OrdinalPosition).FirstOrDefault();
        var orderBy = new List<string>();
        if (sortColumn is not null) orderBy.Add(BaseCol(sortColumn) + " ASC");
        foreach (var k in m.PrimaryKeyColumns.Where(k => k != sortColumn)) orderBy.Add(BaseCol(k) + " ASC");

        string activeFilter = "";
        if (active is not null)
        {
            bool negative = active.IsInactive;
            string value = Flag(active, !negative);
            string test = active.IsNullable ? $"COALESCE({BaseCol(active)}, {value}) = {value}" : $"{BaseCol(active)} = {value}";
            activeFilter = $"(\"pblnIncludeInactive\" OR {test})";
        }

        var o = new StringBuilder("#variable_conflict use_column\nBEGIN\n");
        o.Append($"\tRETURN QUERY\n\tSELECT\n\t\t{string.Join(",\n\t\t", select.Select(s => s.Sql))}\n");
        o.Append($"\tFROM {baseTable}\n");
        foreach (string join in joins) o.Append($"\t{join}\n");
        if (activeFilter.Length > 0) o.Append($"\tWHERE {activeFilter}\n");
        o.Append($"\tORDER BY {string.Join(", ", orderBy)};\n\n");
        o.Append($"\tIF NOT FOUND THEN\n\t\tRAISE EXCEPTION 'No {Message(m.TableName)} found for {Message(m.TableName + "_Lookup")}.' USING ERRCODE = '{SqlErrorCodes.NoRowToLookUp}';\n\tEND IF;\nEND\n");

        string parameters = active is not null ? "\"pblnIncludeInactive\" boolean DEFAULT false" : "";
        string header = active is not null ? "-- Returns only active rows unless \"pblnIncludeInactive\" is true.\n" : "";
        string returns = "TABLE (" + string.Join(", ", select.Select(s => $"{Q(s.Name)} {s.Type}")) + ")";
        return header + Create(m, "Lookup", parameters, returns, o.ToString());
    }

    // The database name of one of a referenced table's display columns (the model lists them under the generated names and, in parallel, the real ones).
    private static string DatabaseNameOf(ForeignKeyModel fk, string generatedName)
    {
        int i = fk.ReferencedDisplayColumns.FindIndex(c => c.EqualsIgnoreCase(generatedName));
        return i >= 0 ? fk.ReferencedDisplayDbColumns[i] : generatedName;
    }

    // ============================================== Junction ==============================================

    public static string Junction(TableModel m)
    {
        if (!m.IsJunctionTable)
            throw new InvalidOperationException($"SP_Junction cannot generate from {Table(m)}: it is not a many-to-many junction table -- TableModel.IsJunctionTable is false.");
        if (!m.HasReferencedDisplayColumns)
            throw new InvalidOperationException("SP_Junction needs the target table's display columns; its .tt.config must say NeedsReferencedDisplayColumns=true.");

        var anchorFk = m.JunctionForeignKeys[0];
        var targetFk = m.JunctionForeignKeys[1];
        var anchor = m.Columns.First(c => c.Name.EqualsIgnoreCase(anchorFk.ReferencingColumns[0]));
        var target = m.Columns.First(c => c.Name.EqualsIgnoreCase(targetFk.ReferencingColumns[0]));
        string anchorParam = Q("Anchor" + anchor.Name);
        string targetParam = Q("Target" + target.Name);
        string targetKey = targetFk.ReferencedDbColumns[0];
        string junction = Table(m);
        string targetTable = Q(targetFk.ReferencedSchema) + "." + Q(targetFk.ReferencedDbTable);
        var createDate = m.Columns.FirstOrDefault(c => c.IsCreateDateColumn);
        var createUser = m.Columns.FirstOrDefault(c => c.IsCreateUserColumn);

        // ---- List: every target row, plus whether it is linked to the anchor. Display columns are text, as in the T-SQL version.
        var outputs = new List<string> { $"{Q("TargetId")} {target.SqlTypeDeclaration}" };
        var items = new List<string> { $"{Q("t")}.{Q(targetKey)}" };
        foreach (string d in targetFk.ReferencedDisplayColumns)
        {
            outputs.Add($"{Q(d)} text");
            items.Add($"{Q("t")}.{Q(DatabaseNameOf(targetFk, d))}::text");
        }
        outputs.Add($"{Q("IsSelected")} boolean");
        items.Add($"EXISTS (SELECT 1 FROM {junction} AS {Q("x")} WHERE {Q("x")}.{Q(anchor.DbName)} = {anchorParam} AND {Q("x")}.{Q(target.DbName)} = {Q("t")}.{Q(targetKey)})");
        string orderBy = targetFk.ReferencedDisplayColumns.Count > 0 ? Q(DatabaseNameOf(targetFk, targetFk.ReferencedDisplayColumns[0])) : Q(targetKey);
        string list = Create(m, "List", $"{anchorParam} {anchor.SqlTypeDeclaration}", "TABLE (" + string.Join(", ", outputs) + ")",
            "#variable_conflict use_column\nBEGIN\n" +
            $"\tRETURN QUERY\n\tSELECT\n\t\t{string.Join(",\n\t\t", items)}\n\tFROM {targetTable} AS {Q("t")}\n\tORDER BY {Q("t")}.{orderBy};\nEND\n");

        // ---- Link: add the association if it does not exist; a creation date is now(), a creating user an optional parameter.
        var linkColumns = new List<(string Name, string Value)> { (anchor.DbName, anchorParam), (target.DbName, targetParam) };
        if (createDate is not null) linkColumns.Add((createDate.DbName, "now()"));
        if (createUser is not null) linkColumns.Add((createUser.DbName, Q("CreateUser")));
        var linkParameters = new List<(string, string, string?)> { (anchorParam, anchor.SqlTypeDeclaration, null), (targetParam, target.SqlTypeDeclaration, null) };
        if (createUser is not null) linkParameters.Add((Q("CreateUser"), createUser.SqlTypeDeclaration, "NULL"));
        string link = Create(m, "Link", ParameterList(linkParameters), "void",
            "BEGIN\n" +
            $"\tIF NOT EXISTS (SELECT 1 FROM {junction} WHERE {Q(anchor.DbName)} = {anchorParam} AND {Q(target.DbName)} = {targetParam}) THEN\n" +
            $"\t\tINSERT INTO {junction}\n\t\t({string.Join(", ", linkColumns.Select(c => Q(c.Name)))})\n\t\tVALUES\n\t\t({string.Join(", ", linkColumns.Select(c => c.Value))});\n\tEND IF;\nEND\n");

        string unlink = Create(m, "Unlink", $"{anchorParam} {anchor.SqlTypeDeclaration},\n{targetParam} {target.SqlTypeDeclaration}", "void",
            $"BEGIN\n\tDELETE FROM {junction} WHERE {Q(anchor.DbName)} = {anchorParam} AND {Q(target.DbName)} = {targetParam};\nEND\n");

        return list + "\n" + link + "\n" + unlink;
    }
}
