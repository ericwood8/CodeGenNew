namespace CodeGenNew.Core;

/// <summary> A usage ledger table and the columns the routes use: <c>Tenant</c> (the tenant the event is billed to), <c>Kind</c> (what was used: <c>api_calls</c>, <c>storage_gb</c>), <c>Quantity</c> (how many units: a
/// whole number or a decimal such as 3.5 gigabytes; none: each event is one unit) and <c>Occurred</c> (when). </summary>
public sealed record UsageShape(TableModel Table, ColumnModel Key, ColumnModel Tenant, string TenantTable, ColumnModel Kind, ColumnModel? Quantity, ColumnModel Occurred)
{
    /// <summary> Whether the units are fractions (a decimal column), not whole numbers. </summary>
    public bool QuantityIsDecimal => Quantity is { IsIntegerColumn: false };

    /// <summary> The C# of the units of one event over a row <c>x</c>, as a <c>long</c> or a <c>decimal</c>. </summary>
    public string UnitsOf(string row = "x") => Quantity is null ? "1L" : QuantityIsDecimal ? $"{row}.{Quantity.CSharpName()}" : $"(long){row}.{Quantity.CSharpName()}";
}

/// <summary> The monthly total table of a usage ledger (<c>UsageRollup=UsageMonthly:UsageMonth:Quantity</c>): one row per tenant, kind and month holding the units of the events that were rolled up and removed. </summary>
public sealed record UsageRollupShape(TableModel Table, ColumnModel Key, ColumnModel Tenant, ColumnModel Kind, ColumnModel Month, ColumnModel Quantity)
{
    /// <summary> Whether the month column is a date (<c>DateOnly</c>) and not a date and time. </summary>
    public bool MonthIsDateOnly => Month.CSharpBase() == "DateOnly";

    public bool QuantityIsDecimal => Quantity is { IsIntegerColumn: false };
}

/// <summary> Which tables are an append-only ledger of billable events (<c>UsageTables=UsageEvent:Kind:Quantity:OccurredOn</c>: a table and optionally its kind, quantity and occurred-on columns, found by name when
/// left out), summed per tenant, kind and month. The table gets no list and edit screen and no ordinary API (see <see cref="ProjectSettings.NoApi"/>): nothing edits or deletes an event, and the only writer is the
/// <c>UsageLedger.RecordAsync</c> call that <c>API_Usage</c> writes. <c>UsageLimits=api_calls,emails</c> names plan limits (the limit table of <c>PlanLimitTable</c>) that are counted from the month's events. </summary>
public static class UsagePlan
{
    private static readonly string[] KindStems = ["Kind", "Metric", "Meter", "EventType", "Event", "Feature", "Type"];
    private static readonly string[] QuantityStems = ["Quantity", "Qty", "Units", "Amount", "Count", "Value"];
    private static readonly string[] OccurredStems = ["Occurred", "Recorded", "Timestamp", "EventDate", "EventTime", "CreatedOn", "CreatedAt", "LoggedOn", "At"];

    public static IReadOnlyList<(string Table, string? Kind, string? Quantity, string? Occurred)> Entries(ProjectSettings project) =>
        SettingParts.Items(project.UsageTables).Select(p => (p[0], SettingParts.At(p, 1), SettingParts.At(p, 2), SettingParts.At(p, 3))).ToList();

    public static bool IsListed(string table, ProjectSettings project) => Entries(project).Any(e => e.Table.EqualsIgnoreCase(table));

    /// <summary> Whether the table is the monthly total table of the ledger: it gets no ordinary API or screen either. </summary>
    public static bool IsRollupTable(string table, ProjectSettings project) => SettingParts.Items(project.UsageRollup).Any(p => p[0].EqualsIgnoreCase(table));

    /// <summary> The usage tables that can be ledgers, by table name. </summary>
    public static IReadOnlyList<UsageShape> Ledgers(DatabaseModel database, ProjectSettings project) =>
        database.Tables.Select(t => TableShapes.For(t, project).Usage).OfType<UsageShape>().OrderBy(u => u.Table.TableName, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary> The monthly total table of a usage table, or null when the project names none for it or it cannot be used (<see cref="Problems"/> says why). </summary>
    public static UsageRollupShape? RollupFor(DatabaseModel database, ProjectSettings project, UsageShape usage) =>
        EntryFor(database, project, usage) is { } entry ? ResolveRollup(database, project, usage, entry, out _) : null;

    /// <summary> The kinds of <c>UsageLimits</c> with the usage table each belongs to when it is named (<c>api_calls</c> or <c>hits:ApiCall</c>). </summary>
    public static IReadOnlyList<(string Key, string? Table)> LimitEntries(ProjectSettings project) =>
        SettingParts.Items(project.UsageLimitEntries).Select(parts => (parts[0], SettingParts.At(parts, 1))).ToList();

    /// <summary> The usage table whose events a plan limit counts: the table the entry names, else the first one <c>UsageTables</c> lists. </summary>
    public static UsageShape? LedgerOfLimit(DatabaseModel database, ProjectSettings project, string key)
    {
        var ledgers = Ledgers(database, project);
        UsageShape? Named(string? name) => name is null ? null : ledgers.FirstOrDefault(l => l.Table.TableName.EqualsIgnoreCase(name));
        var entry = LimitEntries(project).FirstOrDefault(e => e.Key.EqualsIgnoreCase(key));
        if (entry.Table is not null)
            return Named(entry.Table);
        return Entries(project).Select(e => Named(e.Table)).OfType<UsageShape>().FirstOrDefault();
    }

    /// <summary> What the Rust functions of a usage table end with: nothing for the first table by name (<c>record_usage</c>, <c>used_this_month</c>), the module name after an underscore for each other. </summary>
    public static string RustSuffix(DatabaseModel database, ProjectSettings project, UsageShape ledger) =>
        Ledgers(database, project).FirstOrDefault()?.Table.TableName.EqualsIgnoreCase(ledger.Table.TableName) == true ? "" : "_" + RustTable.Of(ledger.Table, project).Module;

    /// <summary> The plan limits counted from the events of this usage table. </summary>
    public static IReadOnlyList<string> KeysOf(DatabaseModel database, ProjectSettings project, UsageShape ledger) =>
        project.UsageLimits.Where(k => LedgerOfLimit(database, project, k) is { } owner && owner.Table.TableName.EqualsIgnoreCase(ledger.Table.TableName)).ToList();

    /// <summary> Whether the API rolls the old events up on its own: <c>UsageRollupHours</c> is set and some usage table has a monthly total table. </summary>
    public static bool HasSchedule(DatabaseModel database, ProjectSettings project) =>
        project.UsageRollupHours > 0 && Ledgers(database, project).Any(l => RollupFor(database, project, l) is not null);

    /// <summary> The monthly total table of the first usage table. </summary>
    public static UsageRollupShape? RollupOf(DatabaseModel database, ProjectSettings project) =>
        Ledgers(database, project).FirstOrDefault() is { } usage ? RollupFor(database, project, usage) : null;

    /// <summary> The UsageRollup entry of a usage table: the one that names it as its fourth part (<c>UsageMonthly:UsageMonth:Quantity:UsageEvent</c>), else the entries that name no table, one for each of the
    /// usage tables that no entry names, in the order <c>UsageTables</c> lists them. </summary>
    private static string[]? EntryFor(DatabaseModel database, ProjectSettings project, UsageShape usage)
    {
        var entries = SettingParts.Items(project.UsageRollup);
        bool Names(string[] e, UsageShape u) => SettingParts.At(e, 3) is { } name && name.EqualsIgnoreCase(u.Table.TableName);
        if (entries.FirstOrDefault(e => Names(e, usage)) is { } named)
            return named;
        var free = entries.Where(e => SettingParts.At(e, 3) is null).ToList();
        var ledgers = Ledgers(database, project);
        var unbound = Entries(project).Select(e => ledgers.FirstOrDefault(l => l.Table.TableName.EqualsIgnoreCase(e.Table))).OfType<UsageShape>().Where(l => !entries.Any(e => Names(e, l))).ToList();
        int at = unbound.FindIndex(l => l.Table.TableName.EqualsIgnoreCase(usage.Table.TableName));
        return at >= 0 && at < free.Count ? free[at] : null;
    }

    private static readonly string[] MonthStems = ["Month", "Period"];

    private static UsageRollupShape? ResolveRollup(DatabaseModel database, ProjectSettings project, UsageShape usage, string[] entry, out string? problem)
    {
        problem = null;
        var table = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(entry[0]));
        string prefix = $"UsageRollup: {entry[0]}";
        if (table is null)
        {
            problem = $"UsageRollup: {entry[0]} is not a table of the database.";
            return null;
        }
        if (table.PrimaryKeyColumns is not [{ IsIntegerColumn: true } key])
        {
            problem = $"{prefix} needs a single whole-number key.";
            return null;
        }
        ColumnModel? Named(string? name) => name is null ? null : table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(name));
        var tenant = Named(usage.Tenant.Name);
        var kind = Named(usage.Kind.Name) ?? Only(table.Columns.Where(c => !c.IsPrimaryKey && c.IsStringColumn && KindStems.Any(s => c.Name.StartsWith(s, StringComparison.OrdinalIgnoreCase) || c.Name.EndsWith(s, StringComparison.OrdinalIgnoreCase))));
        var month = SettingParts.At(entry, 1) is { } m ? Named(m) : Only(table.Columns.Where(c => !c.IsPrimaryKey && (c.CSharpBase() is "DateOnly" or "DateTime") && MonthStems.Any(s => c.Name.ContainsIgnoreCase(s))));
        var quantity = SettingParts.At(entry, 2) is { } q ? Named(q) : Named(usage.Quantity?.Name) ?? Only(table.Columns.Where(c => !c.IsPrimaryKey && (c.IsIntegerColumn || c.IsNumericColumn) && QuantityStems.Any(s => c.Name.Equals(s, StringComparison.OrdinalIgnoreCase))));
        var missing = new List<string>();
        if (tenant is not { IsIntegerColumn: true, IsNullable: false })
            missing.Add($"a NOT NULL whole-number {usage.Tenant.Name} column");
        if (kind is not { IsStringColumn: true, IsNullable: false })
            missing.Add("a NOT NULL text kind column");
        if (month is null || month.CSharpBase() is not ("DateOnly" or "DateTime") || month.IsNullable)
            missing.Add($"{SettingParts.At(entry, 1) ?? "a month column"} (a NOT NULL date, the first day of the month; name it as UsageRollup={entry[0]}:MonthColumn:QuantityColumn)");
        if (quantity is not { IsNullable: false } || !(quantity.IsIntegerColumn || quantity.IsNumericColumn))
            missing.Add($"{SettingParts.At(entry, 2) ?? "a quantity column"} (a NOT NULL whole-number or decimal column)");
        else if (usage.Quantity is not null && usage.QuantityIsDecimal && quantity.IsIntegerColumn)
            missing.Add($"{quantity.Name} to be a decimal column like the ledger's");
        if (missing.Count == 0)
        {
            string[] managed = new[] { key, tenant, kind, month, quantity }.OfType<ColumnModel>().Select(c => c.Name).ToArray();
            var other = table.Columns.Where(c => !managed.Any(x => x.EqualsIgnoreCase(c.Name)) && !c.IsNullable && !c.IsIdentity && c.DatabaseDefaultSql is null && !c.IsComputed).Select(c => c.Name).ToList();
            if (other.Count > 0)
                missing.Add($"no other column that cannot be NULL and has no default ({string.Join(", ", other)}): a roll-up row holds only the tenant, kind, month and units");
        }
        if (missing.Count > 0)
        {
            problem = $"{prefix} needs {string.Join(", ", missing)}.";
            return null;
        }
        return new UsageRollupShape(table, key, tenant!, kind!, month!, quantity!);
    }

    /// <summary> Events that happened before the first day of the month that contains "now minus this many days" are rolled up (<c>UsageRollupDays</c>, default 60). </summary>
    public static int RollupDays(ProjectSettings project) => project.UsageRollupDays;

    public static UsageShape? For(TableModel table, ProjectSettings project) => IsListed(table.TableName, project) && Resolve(table, project, out _) is { } shape ? shape : null;

    /// <summary> Why a listed table cannot be a usage ledger, or null when it can (or is not listed). </summary>
    public static string? Problem(TableModel table, ProjectSettings project)
    {
        if (!IsListed(table.TableName, project))
            return null;
        Resolve(table, project, out string? problem);
        return problem;
    }

    /// <summary> Everything wrong with the UsageTables and UsageLimits settings for this database. </summary>
    public static IReadOnlyList<string> Problems(DatabaseModel database, ProjectSettings project)
    {
        var problems = database.Tables.Select(t => Problem(t, project)).OfType<string>().ToList();
        problems.AddRange(Entries(project).Where(e => !database.Tables.Any(t => t.TableName.EqualsIgnoreCase(e.Table))).Select(e => $"UsageTables: {e.Table} is not a table of the database."));
        if (SettingParts.Items(project.UsageRollup).Count > 0)
        {
            if (Entries(project).Count == 0)
                problems.Add("UsageRollup needs UsageTables: the monthly totals hold the events of the usage table.");
            else
            {
                var ledgers = Ledgers(database, project);
                var used = new List<string>();
                foreach (var ledger in ledgers)
                    if (EntryFor(database, project, ledger) is { } entry)
                    {
                        used.Add(string.Join(":", entry));
                        ResolveRollup(database, project, ledger, entry, out string? rollupProblem);
                        if (rollupProblem is not null)
                            problems.Add(rollupProblem);
                    }
                foreach (var entry in SettingParts.Items(project.UsageRollup).Where(e => !used.Contains(string.Join(":", e))))
                    problems.Add($"UsageRollup: {entry[0]} belongs to no usage table: name one as its fourth part (UsageRollup={entry[0]}:MonthColumn:QuantityColumn:UsageTable) or list one more table in UsageTables.");
                foreach (var twice in used.GroupBy(e => e.Split(':')[0], StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                    problems.Add($"UsageRollup: {twice.Key} is the monthly total table of two usage tables; each needs its own.");
            }
        }
        if (project.UsageRollupHours > 0 && SettingParts.Items(project.UsageRollup).Count == 0)
            problems.Add("UsageRollupHours needs UsageRollup: there are no monthly totals to roll the events up into.");
        if (project.UsageRefuse && project.UsageLimits.Length == 0)
            problems.Add("UsageRefuse needs UsageLimits: a plan limit is what refuses a request.");
        foreach (var (key, table) in LimitEntries(project).Where(e => e.Table is not null))
            if (!Entries(project).Any(e => e.Table.EqualsIgnoreCase(table!)))
                problems.Add($"UsageLimits: {key}:{table} names a table that is not in UsageTables.");
        if (project.UsageLimits.Length > 0)
        {
            if (Entries(project).Count == 0)
                problems.Add("UsageLimits needs UsageTables: a limit is counted from the events of a usage table.");
            else if (project.SubscriptionTable is null || project.PlanLimitTable is null)
                problems.Add("UsageLimits needs SubscriptionTable and PlanLimitTable: the limit of a kind is a row of the plan limit table.");
            else if (project.PlanLimits.Any(p => project.UsageLimits.Any(u => u.EqualsIgnoreCase(p.Split(':').Last()))))
                problems.Add("UsageLimits names a limit that PlanLimits already counts from a table's rows.");
        }
        return problems;
    }

    private static UsageShape? Resolve(TableModel table, ProjectSettings project, out string? problem)
    {
        problem = null;
        var entry = Entries(project).First(e => e.Table.EqualsIgnoreCase(table.TableName));
        string prefix = $"UsageTables: {table.TableName}";
        if (!project.ApiAuth)
        {
            problem = $"{prefix} needs ApiAuth, since only a signed-in caller reads the summary.";
            return null;
        }
        if (project.AccessModeFor(table.Dialect) != AccessMode.Ef)
        {
            problem = $"{prefix} is read through EF, so the project needs AccessMode=Ef.";
            return null;
        }
        if (TableShapes.For(table, project).Tenant is not { } owned)
        {
            problem = $"{prefix} needs the tenant it is billed to: TenantTable, and a NOT NULL whole-number TenantColumn that is a foreign key to it.";
            return null;
        }
        if (table.PrimaryKeyColumns is not [{ IsIntegerColumn: true } key])
        {
            problem = $"{prefix} needs a single whole-number key.";
            return null;
        }
        ColumnModel? Named(string? name) => name is null ? null : table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(name));
        bool Plain(ColumnModel c) => !c.IsPrimaryKey && !c.Name.EqualsIgnoreCase(owned.Column.Name);
        var missing = new List<string>();

        var kind = entry.Kind is null ? Only(table.Columns.Where(c => Plain(c) && c.IsStringColumn && KindStems.Any(s => c.Name.StartsWith(s, StringComparison.OrdinalIgnoreCase) || c.Name.EndsWith(s, StringComparison.OrdinalIgnoreCase)))) : Named(entry.Kind);
        if (kind is not { IsStringColumn: true, IsNullable: false })
            missing.Add($"{entry.Kind ?? "a kind column"} (NOT NULL text, what was used; name it as UsageTables={table.TableName}:KindColumn)");

        ColumnModel? quantity = null;
        if (entry.Quantity is not null)
        {
            quantity = Named(entry.Quantity);
            if (quantity is not { IsNullable: false } || !(quantity.IsIntegerColumn || quantity.IsNumericColumn))
                missing.Add($"{entry.Quantity} (a NOT NULL whole-number or decimal column, how many units)");
        }
        else
            quantity = Only(table.Columns.Where(c => Plain(c) && (c.IsIntegerColumn || c.IsNumericColumn) && !c.IsNullable && !table.ForeignKeyByColumn.ContainsKey(c.Name) && QuantityStems.Any(s => c.Name.Equals(s, StringComparison.OrdinalIgnoreCase))));

        var occurred = entry.Occurred is null ? PickOccurred(table.Columns.Where(c => Plain(c) && LogTablePlan.HasTimeOfDay(c) && OccurredStems.Any(s => c.Name.StartsWith(s, StringComparison.OrdinalIgnoreCase) || c.Name.EndsWith(s, StringComparison.OrdinalIgnoreCase)))) : Named(entry.Occurred);
        if (occurred is null || !LogTablePlan.HasTimeOfDay(occurred) || occurred.IsNullable || occurred.CSharpBase() != "DateTime")
            missing.Add($"{entry.Occurred ?? "an occurred-on column"} (a NOT NULL date and time column, when it was used; name it as UsageTables={table.TableName}:Kind:Quantity:OccurredColumn)");

        if (missing.Count > 0)
        {
            problem = $"{prefix} needs {string.Join(", ", missing)}.";
            return null;
        }
        return new UsageShape(table, key, owned.Column, owned.TenantTable, kind!, quantity, occurred!);
    }

    /// <summary> The statements that give a new event a value in every column the routes do not manage and that cannot be NULL, as the record route and the generated tests need to add a row; null when a column needs
    /// more than that (a foreign key, a number type the generator does not fill): such a table gets no record route and no generated tests. </summary>
    public static IReadOnlyList<string>? RequiredFill(UsageShape t)
    {
        string[] managed = new[] { t.Key, t.Tenant, t.Kind, t.Quantity, t.Occurred }.OfType<ColumnModel>().Select(c => c.Name).ToArray();
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

    /// <summary> The columns, besides the ones the routes manage, that a row inserted by plain SQL must give a value (they cannot be NULL and have no default), with the kind of value; null when a column needs more
    /// than that (a foreign key, a number type the generator does not fill): the Python and Rust record routes and tests are not written for such a table. </summary>
    public static IReadOnlyList<(ColumnModel Column, TokenPlan.FillKind Kind)>? FillColumns(UsageShape t)
    {
        string[] managed = new[] { t.Key, t.Tenant, t.Kind, t.Quantity, t.Occurred }.OfType<ColumnModel>().Select(c => c.Name).ToArray();
        var columns = new List<(ColumnModel, TokenPlan.FillKind)>();
        foreach (var c in t.Table.Columns.Where(c => !managed.Any(m => m.EqualsIgnoreCase(c.Name)) && !c.IsNullable && !c.IsIdentity && c.DatabaseDefaultSql is null && !c.IsComputed))
        {
            if (t.Table.ForeignKeyByColumn.ContainsKey(c.Name))
                return null;
            if (LogTablePlan.HasTimeOfDay(c))
                columns.Add((c, TokenPlan.FillKind.Time));
            else if (c.IsStringColumn)
                columns.Add((c, TokenPlan.FillKind.Text));
            else if (c.IsGuidColumn)
                columns.Add((c, TokenPlan.FillKind.Guid));
            else if (c.IsBooleanColumn)
                columns.Add((c, TokenPlan.FillKind.Flag));
            else if (c.IsIntegerColumn)
                columns.Add((c, TokenPlan.FillKind.Number));
            else
                return null;
        }
        return columns;
    }

    private static ColumnModel? Only(IEnumerable<ColumnModel> columns) => columns.ToList() is [var one] ? one : null;

    /// <summary> The one column that says when the event happened: the only candidate, or the only one named Occurred... when several match. </summary>
    private static ColumnModel? PickOccurred(IEnumerable<ColumnModel> columns)
    {
        var list = columns.ToList();
        return list.Count == 1 ? list[0] : Only(list.Where(c => c.Name.StartsWith("Occurred", StringComparison.OrdinalIgnoreCase)));
    }
}
