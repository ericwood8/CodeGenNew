namespace CodeGenNew.Core;

/// <summary> A document that keeps the exchange rate it was written with (<c>RateDocuments=SalesInvoice:CurrencyCode:ExchangeRate:Total:BaseTotal:InvoiceDate</c>): <c>Currency</c> is the currency the amount
/// is in, <c>Rate</c> what one unit of it was worth in the base currency when the row was written, <c>Amount</c> the amount and <c>BaseAmount</c> the amount converted to the base currency. <c>Day</c> is the
/// date the rate is looked up for (null: today). <c>Rates</c> is the first rate table of <c>RateTables</c>. </summary>
public sealed record RateDocumentShape(TableModel Table, ColumnModel Key, ColumnModel Currency, ColumnModel Rate, ColumnModel Amount, ColumnModel BaseAmount, ColumnModel? Day, RateShape Rates, string BaseCurrency);

/// <summary> <c>RateDocuments</c>: a save writes the currency trimmed and in upper case, takes the rate in force on the day from the rate table when the row is created or its currency or day changes (1 for the
/// base currency; 400 <c>rate-needed</c> when there is none), keeps the stored rate otherwise, and writes the amount times the rate, rounded to the scale of the converted column, into the converted amount. </summary>
public static class RateDocumentPlan
{
    public const int DefaultScale = 2;

    public sealed record Entry(string Table, string? Currency, string? Rate, string? Amount, string? BaseAmount, string? Day, string? RateTable, string? Base);

    public static IReadOnlyList<Entry> Entries(ProjectSettings project) =>
        SettingParts.Items(project.RateDocuments).Select(p => new Entry(p[0], SettingParts.At(p, 1), SettingParts.At(p, 2), SettingParts.At(p, 3), SettingParts.At(p, 4), SettingParts.At(p, 5), SettingParts.At(p, 6), SettingParts.At(p, 7))).ToList();

    public static bool IsListed(string table, ProjectSettings project) => Entries(project).Any(e => e.Table.EqualsIgnoreCase(table));

    /// <summary> The shape of a listed table, given the database of the run. </summary>
    public static RateDocumentShape? For(TableModel table, ProjectSettings project, DatabaseModel database) =>
        IsListed(table.TableName, project) && Resolve(table, project, database, out _) is { } shape ? shape : null;

    /// <summary> The shape of a listed table when the generator remembered the database of the run. </summary>
    public static RateDocumentShape? For(TableModel table, ProjectSettings project) =>
        RatePlan.Remembered(project) is { } database ? For(table, project, database) : null;

    /// <summary> Whether <paramref name="column"/> is the rate or the converted amount of a rate document: the server writes both on every save, so the forms show them as text. Works from the setting alone. </summary>
    public static bool IsOwnedColumn(TableModel table, ProjectSettings project, ColumnModel column) =>
        Entries(project).FirstOrDefault(e => e.Table.EqualsIgnoreCase(table.TableName)) is { } entry
        && (column.Name.EqualsIgnoreCase(entry.Rate ?? "") || column.Name.EqualsIgnoreCase(entry.BaseAmount ?? ""));

    /// <summary> Whether <paramref name="column"/> is the currency of a rate document: the tests write the base currency into it, since a save looks the rate up and refuses a currency with none. </summary>
    public static bool IsCurrency(TableModel table, ColumnModel column, ProjectSettings project) =>
        For(table, project) is { } shape && column.Name.EqualsIgnoreCase(shape.Currency.Name);

    public static List<string> Problems(DatabaseModel database, ProjectSettings project)
    {
        var problems = new List<string>();
        foreach (var entry in Entries(project))
        {
            if (database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(entry.Table)) is not { } table)
                problems.Add($"RateDocuments: {entry.Table} is not a table of the database.");
            else
            {
                Resolve(table, project, database, out string? problem);
                if (problem is not null)
                    problems.Add(problem);
            }
        }
        return problems;
    }

    private static RateDocumentShape? Resolve(TableModel table, ProjectSettings project, DatabaseModel database, out string? problem)
    {
        problem = null;
        var entry = Entries(project).First(e => e.Table.EqualsIgnoreCase(table.TableName));
        string prefix = $"RateDocuments: {table.TableName}";
        string syntax = $"RateDocuments={table.TableName}:CurrencyColumn:RateColumn:AmountColumn:BaseAmountColumn[:DateColumn[:RateTable[:BaseCurrency]]]";
        if (entry.Currency is null || entry.Rate is null || entry.Amount is null || entry.BaseAmount is null)
        {
            problem = $"{prefix} needs its currency, rate, amount and converted amount columns ({syntax}).";
            return null;
        }
        string baseCurrency = (entry.Base ?? project.BaseCurrency).ToUpperInvariant();
        if (baseCurrency.Length < 3)
        {
            problem = $"{prefix} needs BaseCurrency (the currency the amounts are converted to, for example USD).";
            return null;
        }
        if (entry.RateTable is not null && !RatePlan.IsListed(entry.RateTable, project))
        {
            problem = $"{prefix}: {entry.RateTable} is not in RateTables.";
            return null;
        }
        if (RatePlan.Entries(project).Where(e => entry.RateTable is null || e.Table.EqualsIgnoreCase(entry.RateTable)).Select(e => database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(e.Table))).FirstOrDefault(t => t is not null) is not { } rateTable
            || RatePlan.For(rateTable, project) is not { } rates)
        {
            problem = $"{prefix} needs a rate table in RateTables.";
            return null;
        }
        if (RatePlan.IsListed(table.TableName, project))
        {
            problem = $"{prefix} is a rate table itself.";
            return null;
        }
        if (table.PrimaryKeyColumns is not [{ IsIntegerColumn: true } key])
        {
            problem = $"{prefix} needs a single whole-number key.";
            return null;
        }
        ColumnModel? Find(string name) => table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(name));
        var currency = Find(entry.Currency);
        var rate = Find(entry.Rate);
        var amount = Find(entry.Amount);
        var baseAmount = Find(entry.BaseAmount);
        var day = entry.Day is null ? null : Find(entry.Day);
        if (currency is not { IsStringColumn: true, IsNullable: false })
        {
            problem = $"{prefix}: {entry.Currency} must be a text column that cannot be NULL.";
            return null;
        }
        foreach (var (column, name) in new[] { (rate, entry.Rate), (amount, entry.Amount), (baseAmount, entry.BaseAmount) })
        {
            if (column is not { IsNullable: false } || column.CSharpBase() is not ("decimal" or "double" or "float" or "int" or "long" or "short"))
            {
                problem = $"{prefix}: {name} must be a number column that cannot be NULL.";
                return null;
            }
        }
        if (new[] { currency, rate!, amount!, baseAmount! }.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4)
        {
            problem = $"{prefix}: the currency, rate, amount and converted amount must be four different columns.";
            return null;
        }
        if (entry.Day is not null && day is not { IsNullable: false } or { SqlType: not (System.Data.SqlDbType.Date or System.Data.SqlDbType.DateTime or System.Data.SqlDbType.DateTime2 or System.Data.SqlDbType.SmallDateTime) })
        {
            problem = $"{prefix}: {entry.Day} must be a date column that cannot be NULL.";
            return null;
        }
        return new RateDocumentShape(table, key, currency, rate!, amount!, baseAmount!, day, rates, baseCurrency);
    }
}
