using CodeGenNew.Core.Api;
using CodeGenNew.Core.Settings;
using CodeGenNew.Core.Tenant;





namespace CodeGenNew.Core.Party;

/// <summary> A party table and the table of its identifiers (<c>PartyTables=Party:PartyIdentifier:Scheme:Value</c>): <c>Link</c> is the identifier table's foreign key to the party, <c>Scheme</c> names the kind of
/// identifier (a tax number, a registration number) and <c>Value</c> holds it. </summary>
public sealed record PartyShape(TableModel Party, ColumnModel PartyKey, TableModel Identifiers, ColumnModel IdentifierKey, ColumnModel Link, ColumnModel Scheme, ColumnModel Value);

public static class PartyWords
{
    public const string IdentifierTakenType = ProblemTypes.Prefix + "party-identifier-taken";
    public const string IdentifierTakenTitle = "Identifier taken";
    public const string IdentifierTaken = "Another row already holds this identifier of this scheme.";
    public const string ValueNeeded = "Give the value to look for (value=).";
    public const int FindLimit = 50;
}

/// <summary> <c>PartyTables=Party:PartyIdentifier:Scheme:Value</c>: one party row (a customer, a supplier, a carrier or all three) has child rows of identifiers. A scheme and value pair is held by one row only (a save that
/// repeats it is refused, 409; both are trimmed and compared without regard to case), and <c>GET by-identifier?value=&amp;scheme=</c> on the party table answers the parties that hold an identifier. The scheme and value
/// columns default to <c>Scheme</c> and <c>Value</c>. </summary>
public static class PartyPlan
{
    public const string DefaultScheme = "Scheme";
    public const string DefaultValue = "Value";

    public sealed record Entry(string Party, string? Identifiers, string Scheme, string Value, IReadOnlyList<(string Role, string Table)> Children);

    /// <summary> The kinds of child table an entry can name after the identifiers (<c>names=PartyName</c>): the names and the contact methods of a party are default-row tables (one primary name per type, one
    /// preferred contact method per type), the tax registrations are effective-dated rows (a period per country), and the roles are the junction table of the party and its roles. </summary>
    public static readonly string[] ChildRoles = ["names", "contacts", "taxes", "roles"];

    public static IReadOnlyList<Entry> Entries(ProjectSettings project) =>
        SettingParts.Items(project.PartyTables).Select(p =>
        {
            var children = p.Skip(1).Where(part => part.Contains('=')).Select(part => (Role: part[..part.IndexOf('=')].Trim().ToLowerInvariant(), Table: part[(part.IndexOf('=') + 1)..].Trim())).ToList();
            var positional = p.Where(part => !part.Contains('=')).ToArray();
            return new Entry(positional[0], SettingParts.At(positional, 1), SettingParts.At(positional, 2) ?? DefaultScheme, SettingParts.At(positional, 3) ?? DefaultValue, children);
        }).ToList();

    /// <summary> The tables the entries name as children of the given roles, as bare table names: <c>DefaultRowTables</c> and <c>EffectiveDatedTables</c> read their columns from the table. </summary>
    public static IEnumerable<string> ChildTables(ProjectSettings project, params string[] roles) =>
        Entries(project).SelectMany(e => e.Children).Where(c => roles.Contains(c.Role) && c.Table.Length > 0).Select(c => c.Table).Distinct(StringComparer.OrdinalIgnoreCase);

    public static bool IsPartyTable(string table, ProjectSettings project) => Entries(project).Any(e => e.Party.EqualsIgnoreCase(table));

    public static bool IsIdentifierTable(string table, ProjectSettings project) => Entries(project).Any(e => e.Identifiers is not null && e.Identifiers.EqualsIgnoreCase(table));

    /// <summary> Whether <paramref name="child"/> is the identifier table of the party table <paramref name="parent"/>: rows a form of the party saves with it (<c>EditableChildren</c>) go through the rule too. </summary>
    public static bool SavedWith(TableModel parent, string child, ProjectSettings project) =>
        Entries(project).Any(e => e.Party.EqualsIgnoreCase(parent.TableName) && e.Identifiers is not null && e.Identifiers.EqualsIgnoreCase(child));

    /// <summary> The shape when <paramref name="child"/> is the identifier table of the party table <paramref name="parent"/> and the generator remembered the database of the run. </summary>
    public static PartyShape? SavedWithShape(TableModel parent, string child, ProjectSettings project) =>
        SavedWith(parent, child, project) && Known.TryGetValue(project, out var database) ? PartyOf(parent, project, database) : null;

    public static bool IsListed(string table, ProjectSettings project) => IsPartyTable(table, project) || IsIdentifierTable(table, project);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ProjectSettings, DatabaseModel> Known = new();

    /// <summary> Keeps the database of a run for the templates that see one table at a time (the Python and Rust routes), which need the other table of the pair. </summary>
    public static void Remember(DatabaseModel database, ProjectSettings project)
    {
        if (project.PartyTables.Length == 0)
            return;
        Known.Remove(project);
        Known.Add(project, database);
    }

    /// <summary> The shape of a listed party table when the generator remembered the database of the run; null when it did not (a table run alone). </summary>
    public static PartyShape? PartyOf(TableModel table, ProjectSettings project) => Known.TryGetValue(project, out var database) ? PartyOf(table, project, database) : null;

    /// <summary> The shape of a listed identifier table when the generator remembered the database of the run. </summary>
    public static PartyShape? IdentifiersOf(TableModel table, ProjectSettings project) => Known.TryGetValue(project, out var database) ? IdentifiersOf(table, project, database) : null;

    /// <summary> The shape when <paramref name="table"/> is a listed party table. </summary>
    public static PartyShape? PartyOf(TableModel table, ProjectSettings project, DatabaseModel database) =>
        Entries(project).FirstOrDefault(e => e.Party.EqualsIgnoreCase(table.TableName)) is { } entry ? Resolve(entry, database, project, out _) : null;

    /// <summary> The shape when <paramref name="table"/> is a listed identifier table. </summary>
    public static PartyShape? IdentifiersOf(TableModel table, ProjectSettings project, DatabaseModel database) =>
        Entries(project).FirstOrDefault(e => e.Identifiers is not null && e.Identifiers.EqualsIgnoreCase(table.TableName)) is { } entry ? Resolve(entry, database, project, out _) : null;

    /// <summary> Every reason the setting cannot be served by <paramref name="database"/>. </summary>
    public static List<string> Problems(DatabaseModel database, ProjectSettings project)
    {
        var problems = new List<string>();
        foreach (var entry in Entries(project))
        {
            Resolve(entry, database, project, out string? problem);
            if (problem is not null)
                problems.Add(problem);
        }
        foreach (var entry in Entries(project))
            problems.AddRange(ChildProblems(entry, database, project));
        foreach (var repeated in Entries(project).Where(e => e.Identifiers is not null).GroupBy(e => e.Identifiers!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            problems.Add($"PartyTables: {repeated.Key} is the identifier table of more than one party table.");
        return problems;
    }

    private static IEnumerable<string> ChildProblems(Entry entry, DatabaseModel database, ProjectSettings project)
    {
        string prefix = $"PartyTables: {entry.Party}";
        var party = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(entry.Party));
        foreach (var (role, name) in entry.Children)
        {
            if (!ChildRoles.Contains(role))
            {
                yield return $"{prefix}: {role}={name} is not a kind of child table ({string.Join(", ", ChildRoles)}).";
                continue;
            }
            var table = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(name));
            if (table is null)
            {
                yield return $"{prefix}: {role}={name}: {name} is not a table of the database.";
                continue;
            }
            if (party is null)
                continue;
            int links = table.ForeignKeys.Count(f => f.ReferencedTable.EqualsIgnoreCase(party.TableName) && f.ReferencingColumns.Count == 1);
            if (links != 1)
                yield return $"{prefix}: {role}={name}: {name} needs exactly one single-column foreign key to {party.TableName} (it has {links}).";
            else if (role == "roles" && !table.IsJunctionTable)
                yield return $"{prefix}: roles={name}: {name} is not a junction table (its key is the two foreign keys).";
            else if (role is "names" or "contacts" && DefaultRowPlan.Problem(table, project) is { } defaultProblem)
                yield return defaultProblem;
            else if (role == "taxes" && EffectiveDatePlan.Problem(table, project) is { } periodProblem)
                yield return periodProblem;
        }
    }

    /// <summary> One sentence for each identifier table with no unique index on exactly the scheme and the value: the rule is a read and then a save, so only the database stops two saves at the same moment. </summary>
    public static List<string> MissingIndexes(DatabaseModel database, ProjectSettings project)
    {
        var missing = new List<string>();
        foreach (var entry in Entries(project))
        {
            if (Resolve(entry, database, project, out _) is not { } shape)
                continue;
            string[] pair = [shape.Scheme.DbName, shape.Value.DbName];
            if (!shape.Identifiers.Indexes.Any(i => i.IsUnique && i.Columns.Count == pair.Length && pair.All(p => i.Columns.Any(c => c.EqualsIgnoreCase(p)))))
                missing.Add($"{shape.Identifiers.DbTableName} holds the identifiers of {shape.Party.DbTableName} without a unique index on {pair[0]} and {pair[1]}: two requests at once can still store the same pair. Add a unique index on the two columns; the database then refuses the second one.");
        }
        return missing;
    }

    /// <summary> The unique index on the scheme and the value of an identifier table: the table and the index as the database names them, and the index's columns in its own order. </summary>
    public sealed record PairIndex(string Table, string Name, IReadOnlyList<string> Columns);

    /// <summary> The pair indexes of the identifier tables of the run the generator remembered. A save one of them refuses repeats a pair, so the API answers it as the rule does; it tells the index
    /// from another unique index of the table by <see cref="RefusalMark"/>. </summary>
    public static IReadOnlyList<PairIndex> PairIndexes(ProjectSettings project)
    {
        if (!Known.TryGetValue(project, out var database))
            return [];
        var found = new List<PairIndex>();
        foreach (var entry in Entries(project))
        {
            if (Resolve(entry, database, project, out _) is not { } shape)
                continue;
            string[] pair = [shape.Scheme.DbName, shape.Value.DbName];
            if (shape.Identifiers.Indexes.FirstOrDefault(i => i.IsUnique && i.Columns.Count == pair.Length && pair.All(p => i.Columns.Any(c => c.EqualsIgnoreCase(p)))) is { } index)
                found.Add(new PairIndex(shape.Identifiers.DbTableName, index.Name, index.Columns));
        }
        return found;
    }

    /// <summary> The text the database's message ends the name of the refusing index with: SQL Server and MySQL quote the index's name with <c>'</c>, PostgreSQL with <c>"</c>, and SQLite names the
    /// columns (<c>PartyIdentifier.Scheme, PartyIdentifier.Value'</c>). The closing quote keeps a longer name or column list apart. </summary>
    public static string RefusalMark(PairIndex index, SqlDialect dialect) => dialect switch
    {
        SqlDialect.Sqlite => string.Join(", ", index.Columns.Select(c => index.Table + "." + c)) + "'",
        SqlDialect.PostgreSql => index.Name + "\"",
        _ => index.Name + "'"
    };

    private static PartyShape? Resolve(Entry entry, DatabaseModel database, ProjectSettings project, out string? problem)
    {
        problem = null;
        string prefix = $"PartyTables: {entry.Party}";
        string syntax = $"PartyTables={entry.Party}:IdentifierTable:SchemeColumn:ValueColumn";
        var party = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(entry.Party));
        if (party is null)
        {
            problem = $"{prefix} is not a table of the database.";
            return null;
        }
        if (entry.Identifiers is null)
        {
            problem = $"{prefix} needs the table of its identifiers ({syntax}).";
            return null;
        }
        var identifiers = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(entry.Identifiers));
        if (identifiers is null)
        {
            problem = $"{prefix}: {entry.Identifiers} is not a table of the database.";
            return null;
        }
        if (party.PrimaryKeyColumns is not [{ IsIntegerColumn: true } partyKey] || identifiers.PrimaryKeyColumns is not [{ IsIntegerColumn: true } identifierKey])
        {
            problem = $"{prefix} and {identifiers.TableName} each need a single whole-number key.";
            return null;
        }
        foreach (var table in new[] { party, identifiers })
        {
            if (RowShapeWords.NeverChanged(table, project) is { } never)
            {
                problem = $"{prefix}: {table.TableName} {never}, so it has no identifiers to keep apart.";
                return null;
            }
            if (TenantPlan.OwnedBy(table, project) is not null || OwnedTable.OwnerColumn(table, project) is not null)
            {
                problem = $"{prefix}: {table.TableName} is limited to a tenant or an owner, which a party table does not serve yet.";
                return null;
            }
        }
        var links = identifiers.ForeignKeys.Where(f => f.ReferencedTable.EqualsIgnoreCase(party.TableName) && f.ReferencingColumns.Count == 1).ToList();
        if (links.Count != 1 || identifiers.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(links[0].ReferencingColumns[0])) is not { } link)
        {
            problem = $"{prefix}: {identifiers.TableName} needs exactly one single-column foreign key to {party.TableName} (it has {links.Count}).";
            return null;
        }
        var scheme = identifiers.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(entry.Scheme));
        var value = identifiers.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(entry.Value));
        if (scheme is not { IsStringColumn: true, IsNullable: false } || value is not { IsStringColumn: true, IsNullable: false })
        {
            problem = $"{prefix}: {identifiers.TableName} needs {entry.Scheme} and {entry.Value} as text columns that cannot be NULL ({syntax}).";
            return null;
        }
        if (scheme.Name.EqualsIgnoreCase(value.Name))
        {
            problem = $"{prefix}: the scheme and the value cannot be the same column.";
            return null;
        }
        return new PartyShape(party, partyKey, identifiers, identifierKey, link, scheme, value);
    }
}
