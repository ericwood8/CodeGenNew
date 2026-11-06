namespace CodeGenNew.Core;

/// <summary> A session or token table and the columns the routes use. <c>Ends</c> are the flags and stamps that say a token was used or revoked (a row is active when none is set); without any, ending a token sets its
/// expiry to now. <c>Person</c> is the foreign key to the project's user table, which the sign-out-everywhere route needs. <c>Family</c> groups the tokens of one sign-in, so a token that is used a second time ends
/// the whole family. <c>Anonymous</c> lets the spend route be called without a sign-in. </summary>
public sealed record TokenShape(TableModel Table, ColumnModel Key, ColumnModel Hash, ColumnModel Expiry, IReadOnlyList<ColumnModel> Ends, ColumnModel? Family, ColumnModel? Person, string? PersonTable, string? PersonKey, bool Anonymous)
{
    /// <summary> The column a revoke sets: the one named Revoked..., else the first. </summary>
    public ColumnModel? Revoked => Ends.FirstOrDefault(c => c.Name.StartsWith("Revoked", StringComparison.OrdinalIgnoreCase)) ?? Ends.FirstOrDefault();

    /// <summary> The column a spend sets: the one named Used..., Spent... or Consumed..., else the revoke column. </summary>
    public ColumnModel? Spent => Ends.FirstOrDefault(c => !c.Name.StartsWith("Revoked", StringComparison.OrdinalIgnoreCase)) ?? Revoked;

    public bool RevokedIsFlag => Revoked is { IsBooleanColumn: true };

    public bool SpentIsFlag => Spent is { IsBooleanColumn: true };

    public bool FamilyIsText => Family is { IsStringColumn: true };

    /// <summary> Every column but the hash: what a list may show. </summary>
    public IReadOnlyList<ColumnModel> Shown => Table.Columns.Where(c => c.Name != Hash.Name).ToList();
}

/// <summary> Which tables hold sessions, refresh tokens, password resets or verification codes (<c>TokenTables=Session:TokenHash:ExpiresOn:RevokedOn</c>: a table and optionally its hash, expiry and revoked columns,
/// which are found by name when left out). Such a row is keyed by a hash of the token, has an expiry and may carry a revoked or used flag. The table gets no list and edit screen and no ordinary API (see
/// <see cref="ProjectSettings.NoApi"/>); <c>API_Token</c> writes the few routes that make sense: an administrator's list without the hash, revoking one row or every row of a person, and spending a token once. </summary>
public static class TokenPlan
{
    public const int HashLength = 64;
    public const int FamilyLength = 32;

    private static readonly string[] ExpiryStems = ["Expir", "ValidUntil", "ValidTo", "Until", "EndsOn"];
    private static readonly string[] RevokedStems = ["Revoked", "Used", "Spent", "Consumed"];

    private const char EndSeparator = '+';

    public static IReadOnlyList<(string Table, string? Hash, string? Expiry, string? Ends, string? Family)> Entries(ProjectSettings project) =>
        SettingParts.Items(project.TokenTables).Select(p => (p[0], SettingParts.At(p, 1), SettingParts.At(p, 2), SettingParts.At(p, 3), SettingParts.At(p, 4))).ToList();

    public static bool IsListed(string table, ProjectSettings project) => Entries(project).Any(e => e.Table.EqualsIgnoreCase(table));

    public static TokenShape? For(TableModel table, ProjectSettings project) => IsListed(table.TableName, project) && Resolve(table, project, out _) is { } shape ? shape : null;

    /// <summary> Everything wrong with the TokenTables and TokenAnonymousSpend settings for this database. </summary>
    public static IReadOnlyList<string> Problems(DatabaseModel database, ProjectSettings project)
    {
        var problems = database.Tables.Select(t => Problem(t, project)).OfType<string>().ToList();
        problems.AddRange(Entries(project).Where(e => !database.Tables.Any(t => t.TableName.EqualsIgnoreCase(e.Table))).Select(e => $"TokenTables: {e.Table} is not a table of the database."));
        problems.AddRange(project.TokenAnonymousSpend.Where(a => !IsListed(a, project)).Select(a => $"TokenAnonymousSpend: {a} is not in TokenTables."));
        return problems;
    }

    /// <summary> Why a listed table cannot be a token table, or null when it can (or is not listed). </summary>
    public static string? Problem(TableModel table, ProjectSettings project)
    {
        if (!IsListed(table.TableName, project))
            return null;
        Resolve(table, project, out string? problem);
        return problem;
    }

    private static TokenShape? Resolve(TableModel table, ProjectSettings project, out string? problem)
    {
        problem = null;
        var entry = Entries(project).First(e => e.Table.EqualsIgnoreCase(table.TableName));
        string prefix = $"TokenTables: {table.TableName}";
        if (!project.ApiAuth)
        {
            problem = $"{prefix} needs ApiAuth, since only a signed-in caller may reach the routes.";
            return null;
        }
        if (project.AccessModeFor(table.Dialect) != AccessMode.Ef)
        {
            problem = $"{prefix} is read through EF, so the project needs AccessMode=Ef.";
            return null;
        }
        if (table.PrimaryKeyColumns is not [{ IsIntegerColumn: true } key])
        {
            problem = $"{prefix} needs a single whole-number key.";
            return null;
        }
        ColumnModel? Named(string? name) => name is null ? null : table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(name));
        var missing = new List<string>();

        var hash = entry.Hash is null ? Only(table.Columns.Where(c => c.IsStringColumn && c.Name.ContainsIgnoreCase("Hash"))) : Named(entry.Hash);
        if (hash is not { IsStringColumn: true })
            missing.Add($"{entry.Hash ?? "a hash column"} (text, holds the hash of the token; name it as TokenTables={table.TableName}:HashColumn)");
        else if (hash.CharacterLength is > 0 and < HashLength)
            missing.Add($"{hash.Name} to hold at least {HashLength} characters (the SHA-256 of the token in hex)");

        var expiry = entry.Expiry is null ? Only(table.Columns.Where(c => LogTablePlan.HasTimeOfDay(c) && ExpiryStems.Any(s => c.Name.ContainsIgnoreCase(s)))) : Named(entry.Expiry);
        if (expiry is null || !LogTablePlan.HasTimeOfDay(expiry))
            missing.Add($"{entry.Expiry ?? "an expiry column"} (a date and time column; name it as TokenTables={table.TableName}:HashColumn:ExpiryColumn)");
        else if (expiry.IsNullable)
            missing.Add($"{expiry.Name} to be NOT NULL (a token without an expiry would never end)");

        var ends = new List<ColumnModel>();
        foreach (string name in entry.Ends is null ? [] : entry.Ends.Split(EndSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Named(name) is { } named)
                ends.Add(named);
            else
                missing.Add($"{name} (a yes/no column or a date and time column that allows NULL)");
        }
        if (entry.Ends is null)
            ends.AddRange(table.Columns.Where(c => RevokedStems.Any(s => c.Name.StartsWith(s, StringComparison.OrdinalIgnoreCase)) && (c.IsBooleanColumn || LogTablePlan.HasTimeOfDay(c))));
        foreach (var end in ends)
        {
            if (end is { IsBooleanColumn: true, IsNullable: true })
                missing.Add($"{end.Name} to be NOT NULL (a flag that is neither yes nor no)");
            else if (end is { IsBooleanColumn: false } && (!LogTablePlan.HasTimeOfDay(end) || !end.IsNullable))
                missing.Add($"{end.Name} to be a yes/no column, or a date and time column that allows NULL");
        }

        var family = entry.Family is null ? Only(table.Columns.Where(c => c.Name.ContainsIgnoreCase("Family") && !c.IsPrimaryKey)) : Named(entry.Family);
        if (entry.Family is not null && family is null)
            missing.Add($"{entry.Family} (the column that groups the tokens of one sign-in)");
        else if (family is not null && (table.ForeignKeyByColumn.ContainsKey(family.Name) || !(family.CSharpBase() is "int" or "long" || family.IsGuidColumn || family.IsStringColumn && family.CharacterLength is <= 0 or >= FamilyLength)))
            missing.Add($"{family.Name} to be an int, a bigint, a guid or text of at least {FamilyLength} characters, and not a foreign key");

        if (missing.Count > 0)
        {
            problem = $"{prefix} needs {string.Join(", ", missing)}.";
            return null;
        }
        var (person, fk) = PersonOf(table, project);
        return new TokenShape(table, key, hash!, expiry!, ends, family, person, fk?.ReferencedTable, fk?.ReferencedColumns[0], project.TokenAnonymousSpend.Any(a => a.EqualsIgnoreCase(table.TableName)));
    }

    /// <summary> The statements that give a new row a value in every column the routes do not manage and that cannot be NULL, as the generated tests need to add a row; null when a column needs more than that (a foreign key,
    /// a number type the generator does not fill): such a table gets no generated tests. </summary>
    public static IReadOnlyList<string>? RequiredFill(TokenShape t)
    {
        string[] managed = new[] { t.Key, t.Hash, t.Expiry, t.Family, t.Person }.Concat(t.Ends).OfType<ColumnModel>().Select(c => c.Name).ToArray();
        var statements = new List<string>();
        foreach (var c in t.Table.Columns.Where(c => !managed.Any(m => m.EqualsIgnoreCase(c.Name)) && !c.IsNullable && !c.IsIdentity && c.DatabaseDefaultSql is null && !c.IsComputed))
        {
            if (t.Table.ForeignKeyByColumn.ContainsKey(c.Name))
                return null;
            if (LogTablePlan.HasTimeOfDay(c))
                statements.Add($"row.{c.CSharpName()} = DateTime.Now;");
            else if (c.IsStringColumn)
                statements.Add($"row.{c.CSharpName()} = {(c.CharacterLength is > 0 and < 4 ? "\"\"" : "\"test\"")};");
            else if (c.IsGuidColumn)
                statements.Add($"row.{c.CSharpName()} = Guid.NewGuid();");
            else if (!c.IsBooleanColumn && !c.IsIntegerColumn)
                return null;
        }
        return statements;
    }

    public enum FillKind { Text, Time, Flag, Number, Guid }

    /// <summary> The columns, besides the ones the routes manage, that a row inserted by plain SQL must give a value (they cannot be NULL and have no default), with the kind of value; null when a column needs more than
    /// that (a foreign key, a number type the generator does not fill): the Python and Rust tests are not written for such a table. </summary>
    public static IReadOnlyList<(ColumnModel Column, FillKind Kind)>? TestColumns(TokenShape t)
    {
        string[] managed = new[] { t.Key, t.Hash, t.Expiry, t.Family, t.Person }.Concat(t.Ends).OfType<ColumnModel>().Select(c => c.Name).ToArray();
        var columns = new List<(ColumnModel, FillKind)>();
        foreach (var c in t.Table.Columns.Where(c => !managed.Any(m => m.EqualsIgnoreCase(c.Name)) && !c.IsNullable && !c.IsIdentity && c.DatabaseDefaultSql is null && !c.IsComputed))
        {
            if (t.Table.ForeignKeyByColumn.ContainsKey(c.Name))
                return null;
            if (LogTablePlan.HasTimeOfDay(c))
                columns.Add((c, FillKind.Time));
            else if (c.IsStringColumn)
                columns.Add((c, FillKind.Text));
            else if (c.IsGuidColumn)
                columns.Add((c, FillKind.Guid));
            else if (c.IsBooleanColumn)
                columns.Add((c, FillKind.Flag));
            else if (c.IsIntegerColumn)
                columns.Add((c, FillKind.Number));
            else
                return null;
        }
        return columns;
    }

    private static ColumnModel? Only(IEnumerable<ColumnModel> columns) => columns.ToList() is [var one] ? one : null;

    private static (ColumnModel? Column, ForeignKeyModel? Key) PersonOf(TableModel table, ProjectSettings project)
    {
        if (project.UserTable is not { } user)
            return (null, null);
        var fk = table.ForeignKeys.FirstOrDefault(f => f.ReferencingColumns.Count == 1 && f.ReferencedColumns.Count == 1 && f.ReferencedTable.EqualsIgnoreCase(user));
        var column = fk is null ? null : table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(fk.ReferencingColumns[0]) && c.IsIntegerColumn);
        return column is null ? (null, null) : (column, fk);
    }
}
