using CodeGenNew.Core.Party;
using CodeGenNew.Core.Api;
using CodeGenNew.Core.Settings;
using CodeGenNew.Core.Tenant;





namespace CodeGenNew.Core.Rate;

/// <summary> A rate table (<c>RateTables=ExchangeRate:FromCurrency:ToCurrency:Rate:RateDate</c>): <c>From</c> and <c>To</c> are the text columns of the two currencies, <c>Rate</c> what one unit of <c>From</c> is worth in
/// <c>To</c> and <c>Day</c> the day the rate holds from. </summary>
public sealed record RateShape(TableModel Table, ColumnModel Key, ColumnModel From, ColumnModel To, ColumnModel Rate, ColumnModel Day);

public static class RateWords
{
    public const string InvalidType = ProblemTypes.Prefix + "rate-invalid";
    public const string InvalidTitle = "Not a valid rate";
    public const string TakenType = ProblemTypes.Prefix + "rate-taken";
    public const string TakenTitle = "Rate taken";
    public const string MissingType = ProblemTypes.Prefix + "rate-missing";
    public const string MissingTitle = "No rate";

    public const string NeededType = ProblemTypes.Prefix + "rate-needed";
    public const string NeededTitle = "No exchange rate";
    public const string DocumentNeedsCurrency = "Give the currency of the document.";
    public const string InvertedHeader = "X-Rate-Inverted";

    public const string NeedsCurrencies = "Give the two currencies of the rate.";
    public const string SameCurrency = "A rate needs two different currencies.";
    public const string NotPositive = "A rate must be greater than zero.";
    public const string Taken = "Another row already holds a rate for this pair of currencies on this day.";
    public const string NeedsPair = "Give the two currencies to convert (from= and to=).";

    public static string Missing(string from, string to) => $"There is no rate from {from} to {to} on or before that day.";
}

/// <summary> <c>RateTables=ExchangeRate:FromCurrency:ToCurrency:Rate:RateDate</c> (the four columns default to <c>FromCurrency</c>, <c>ToCurrency</c>, <c>Rate</c> and <c>RateDate</c>): a save is refused when a currency is
/// missing, the two currencies are the same or the rate is not above zero (400), and when another row holds a rate for the pair on the same day (409; the currencies are trimmed and compared in upper case).
/// <c>GET rate?from=&amp;to=&amp;on=</c> answers the row in force on a day: the latest one on or before it. </summary>
public static class RatePlan
{
    public const string DefaultFrom = "FromCurrency";
    public const string DefaultTo = "ToCurrency";
    public const string DefaultRate = "Rate";
    public const string DefaultDay = "RateDate";

    public sealed record Entry(string Table, string From, string To, string Rate, string Day);

    public static IReadOnlyList<Entry> Entries(ProjectSettings project) =>
        SettingParts.Items(project.RateTables).Select(p => new Entry(p[0], SettingParts.At(p, 1) ?? DefaultFrom, SettingParts.At(p, 2) ?? DefaultTo, SettingParts.At(p, 3) ?? DefaultRate, SettingParts.At(p, 4) ?? DefaultDay)).ToList();

    public static bool IsListed(string table, ProjectSettings project) => Entries(project).Any(e => e.Table.EqualsIgnoreCase(table));

    public static RateShape? For(TableModel table, ProjectSettings project) => IsListed(table.TableName, project) && Resolve(table, project, out _) is { } shape ? shape : null;

    /// <summary> Whether <paramref name="column"/> holds a currency of a rate table: a save writes it trimmed and in upper case, so a test that changes it to a value of its own reads it back changed. </summary>
    public static bool IsCurrency(TableModel table, ColumnModel column, ProjectSettings project) =>
        For(table, project) is { } shape && (column.Name.EqualsIgnoreCase(shape.From.Name) || column.Name.EqualsIgnoreCase(shape.To.Name));

    public static string? Problem(TableModel table, ProjectSettings project)
    {
        if (!IsListed(table.TableName, project))
            return null;
        Resolve(table, project, out string? problem);
        return problem;
    }

    /// <summary> Every reason the setting cannot be served by <paramref name="database"/>. </summary>
    public static List<string> Problems(DatabaseModel database, ProjectSettings project)
    {
        var problems = new List<string>();
        foreach (var entry in Entries(project))
        {
            if (database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(entry.Table)) is not { } table)
                problems.Add($"RateTables: {entry.Table} is not a table of the database.");
            else if (Problem(table, project) is { } problem)
                problems.Add(problem);
            else if (project.RateInverse && For(table, project) is { } shape && !InvertibleRate(shape.Rate))
                problems.Add($"RateInverse: {table.TableName}.{shape.Rate.Name} must be a decimal, double or float column to be divided into one.");
        }
        return problems;
    }

    /// <summary> One sentence for each rate table with no unique index on the pair and the day: the rule is a read and then a save, so only the database stops two saves at the same moment. </summary>
    public static List<string> MissingIndexes(DatabaseModel database, ProjectSettings project)
    {
        var missing = new List<string>();
        foreach (var table in database.Tables)
        {
            if (For(table, project) is not { } shape)
                continue;
            string[] columns = [shape.From.DbName, shape.To.DbName, shape.Day.DbName];
            if (!table.Indexes.Any(i => i.IsUnique && i.Columns.Count == columns.Length && columns.All(c => i.Columns.Any(x => x.EqualsIgnoreCase(c)))))
                missing.Add($"{table.DbTableName} holds exchange rates without a unique index on {columns[0]}, {columns[1]} and {columns[2]}: two requests at once can still store the same rate twice. Add a unique index on the three columns.");
        }
        return missing;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ProjectSettings, DatabaseModel> Known = new();

    /// <summary> The database of the run the generator remembered, or null. </summary>
    public static DatabaseModel? Remembered(ProjectSettings project) => Known.TryGetValue(project, out var database) ? database : null;

    /// <summary> Whether one can be divided by the rate column: a decimal, double or float. </summary>
    public static bool InvertibleRate(ColumnModel rate) => rate.IsFractionalColumn;

    /// <summary> The C# one of the rate column's type. </summary>
    public static string One(ColumnModel rate) => rate.CSharpBase() switch { "decimal" => "1m", "float" => "1f", _ => "1.0" };

    /// <summary> Keeps the database of a run for the templates that do not see the tables (the API's error answers). </summary>
    public static void Remember(DatabaseModel database, ProjectSettings project)
    {
        if (project.RateTables.Length == 0 && project.PriceListTables.Length == 0 && project.TaxLines.Length == 0 && project.AllowanceCharges.Length == 0 && project.DocumentTotals.Length == 0)
            return;
        Known.Remove(project);
        Known.Add(project, database);
    }

    /// <summary> The unique indexes on the pair and the day of the rate tables of the run the generator remembered. A save one of them refuses repeats a rate, so the API answers it as the rule does. </summary>
    public static IReadOnlyList<PartyPlan.PairIndex> PairIndexes(ProjectSettings project)
    {
        if (!Known.TryGetValue(project, out var database))
            return [];
        var found = new List<PartyPlan.PairIndex>();
        foreach (var table in database.Tables)
        {
            if (For(table, project) is not { } shape)
                continue;
            string[] columns = [shape.From.DbName, shape.To.DbName, shape.Day.DbName];
            if (table.Indexes.FirstOrDefault(i => i.IsUnique && i.Columns.Count == columns.Length && columns.All(c => i.Columns.Any(x => x.EqualsIgnoreCase(c)))) is { } index)
                found.Add(new PartyPlan.PairIndex(table.DbTableName, index.Name, index.Columns));
        }
        return found;
    }

    private static RateShape? Resolve(TableModel table, ProjectSettings project, out string? problem)
    {
        problem = null;
        var entry = Entries(project).First(e => e.Table.EqualsIgnoreCase(table.TableName));
        string prefix = $"RateTables: {table.TableName}";
        string syntax = $"RateTables={table.TableName}:FromCurrency:ToCurrency:Rate:RateDate";
        if (RowShapeWords.NeverChanged(table, project) is { } never)
        {
            problem = $"{prefix} {never}, so it has no rates to keep apart.";
            return null;
        }
        if (table.PrimaryKeyColumns is not [{ IsIntegerColumn: true } key])
        {
            problem = $"{prefix} needs a single whole-number key.";
            return null;
        }
        if (TenantPlan.OwnedBy(table, project) is not null || OwnedTable.OwnerColumn(table, project) is not null)
        {
            problem = $"{prefix} is limited to a tenant or an owner, which a rate table does not serve yet.";
            return null;
        }
        var from = table.FindColumn(entry.From);
        var to = table.FindColumn(entry.To);
        var rate = table.FindColumn(entry.Rate);
        var day = table.FindColumn(entry.Day);
        if (from is not { IsStringColumn: true, IsNullable: false } || to is not { IsStringColumn: true, IsNullable: false })
        {
            problem = $"{prefix} needs {entry.From} and {entry.To} as text columns that cannot be NULL ({syntax}).";
            return null;
        }
        if (from.Name.EqualsIgnoreCase(to.Name))
        {
            problem = $"{prefix}: the two currencies cannot be the same column.";
            return null;
        }
        if (rate is not { IsNumericColumn: true, IsNullable: false })
        {
            problem = $"{prefix} needs {entry.Rate} as a number column that cannot be NULL ({syntax}).";
            return null;
        }
        if (day is not { IsDateColumn: true, IsNullable: false })
        {
            problem = $"{prefix} needs {entry.Day} as a date column that cannot be NULL ({syntax}).";
            return null;
        }
        return new RateShape(table, key, from, to, rate, day);
    }
}
