using CodeGenNew.Core.Rate;
using CodeGenNew.Core.Settings;

namespace CodeGenNew.Core.Tax;

/// <summary> A document line that carries the tax rate of its category (<c>TaxLines=InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory:Rate</c>): <c>Category</c> is the key of the category
/// row, <c>Rate</c> the rate copied onto the line (a fraction: 0.08 is 8 percent), <c>Taxable</c> the amount the tax is worked out on and <c>Tax</c> the tax amount. <c>CategoryTable</c> holds the categories, <c>CategoryRate</c>
/// is its rate column (a fraction, so the tax is the taxable amount times it). </summary>
public sealed record TaxLineShape(TableModel Table, ColumnModel Key, ColumnModel Category, ColumnModel Rate, ColumnModel Taxable, ColumnModel Tax, TableModel CategoryTable, ColumnModel CategoryKey, ColumnModel CategoryRate,
    ColumnModel? ValidFrom, ColumnModel? ValidTo, ColumnModel? Day, ColumnModel? Document, ColumnModel? Label, IReadOnlyList<ColumnModel> Groups);

public static class TaxWords
{
    public const string UnknownType = ProblemTypes.Prefix + "tax-category-unknown";
    public const string UnknownTitle = "Unknown tax category";
    public const string NotInForceType = ProblemTypes.Prefix + "tax-category-not-in-force";
    public const string NotInForceTitle = "Tax category not in force";
    public const string SubtotalsNeedsDocument = "Give the document (document=).";
    public const int DefaultScale = 2;
    public const string DefaultCategoryRate = "Rate";

    public static string NotInForce(object category, object day) => $"Tax category {category} is not in force on {day}.";

    public static string Unknown(object category) => $"There is no tax category {category}.";
}

/// <summary> <c>TaxLines</c>: a save copies the rate of the line's category onto the line when the row is created or its category changes (400 <c>tax-category-unknown</c> when the category does not exist),
/// keeps the stored rate otherwise, so a later change of the category's rate alters no old line, and writes the taxable amount times the rate divided by 100, rounded to the scale of the tax column, into the
/// tax amount. A line with no category carries rate 0 and tax 0. </summary>
public static class TaxLinePlan
{
    public sealed record Entry(string Table, string? Category, string? Rate, string? Taxable, string? Tax, string? CategoryTable, string CategoryRate, string? ValidFrom, string? ValidTo, string? Day, string? Document, string? Label, string? Groups);

    public static IReadOnlyList<Entry> Entries(ProjectSettings project) =>
        SettingParts.Items(project.TaxLines).Select(p => new Entry(p[0], SettingParts.At(p, 1), SettingParts.At(p, 2), SettingParts.At(p, 3), SettingParts.At(p, 4), SettingParts.At(p, 5), SettingParts.At(p, 6) ?? TaxWords.DefaultCategoryRate, SettingParts.At(p, 7), SettingParts.At(p, 8), SettingParts.At(p, 9), SettingParts.At(p, 10), SettingParts.At(p, 11), SettingParts.At(p, 12))).ToList();

    /// <summary> The <c>EffectiveDatedTables</c> items the entries imply: a category table with a validity period and the columns that say what a category is (<c>Groups</c>, joined with +) keeps the periods of one group apart,
    /// the rule every stack already has for that setting. A table the project lists itself is left alone. </summary>
    public static IEnumerable<string> EffectiveItems(ProjectSettings project) =>
        Entries(project).Where(e => e.CategoryTable is not null && e.ValidFrom is not null && e.ValidTo is not null && e.Groups is not null)
            .Select(e => $"{e.CategoryTable}:{e.ValidFrom}:{e.ValidTo}:{e.Groups}");

    /// <summary> The rate column of every line table and of its category table as <c>Table.Column</c>: a rate is a fraction (0.08 for 8 percent), never money, so no grid or form shows it with a currency symbol. </summary>
    public static IEnumerable<string> RateColumns(ProjectSettings project) =>
        Entries(project).SelectMany(e => new[] { e.Rate is null ? null : $"{e.Table}.{e.Rate}", e.CategoryTable is null ? null : $"{e.CategoryTable}.{e.CategoryRate}" }).OfType<string>();

    public static bool IsListed(string table, ProjectSettings project) => Entries(project).Any(e => e.Table.EqualsIgnoreCase(table));

    public static TaxLineShape? For(TableModel table, ProjectSettings project, DatabaseModel database) =>
        IsListed(table.TableName, project) && Resolve(table, project, database, out _) is { } shape ? shape : null;

    public static TaxLineShape? For(TableModel table, ProjectSettings project) =>
        RatePlan.Remembered(project) is { } database ? For(table, project, database) : null;

    /// <summary> Whether <paramref name="column"/> is the rate or the tax amount of a tax line: the server writes both on every save. Works from the setting alone. </summary>
    public static bool IsOwnedColumn(TableModel table, ProjectSettings project, ColumnModel column) =>
        Entries(project).FirstOrDefault(e => e.Table.EqualsIgnoreCase(table.TableName)) is { } entry
        && (column.Name.EqualsIgnoreCase(entry.Rate ?? "") || column.Name.EqualsIgnoreCase(entry.Tax ?? ""));

    public static List<string> Problems(DatabaseModel database, ProjectSettings project)
    {
        var problems = new List<string>();
        foreach (var entry in Entries(project))
        {
            if (database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(entry.Table)) is not { } table)
                problems.Add($"TaxLines: {entry.Table} is not a table of the database.");
            else
            {
                Resolve(table, project, database, out string? problem);
                if (problem is not null)
                    problems.Add(problem);
            }
        }
        return problems;
    }

    private static bool IsNumber(ColumnModel? column) => column?.CSharpBase() is "decimal" or "double" or "float";

    private static TaxLineShape? Resolve(TableModel table, ProjectSettings project, DatabaseModel database, out string? problem)
    {
        problem = null;
        var entry = Entries(project).First(e => e.Table.EqualsIgnoreCase(table.TableName));
        string prefix = $"TaxLines: {table.TableName}";
        string syntax = $"TaxLines={table.TableName}:CategoryColumn:RateColumn:TaxableColumn:TaxColumn:CategoryTable[:CategoryRateColumn[:ValidFromColumn[:ValidToColumn[:DayColumn[:DocumentColumn[:LabelColumn[:GroupColumns]]]]]]]";
        if (entry.Category is null || entry.Rate is null || entry.Taxable is null || entry.Tax is null || entry.CategoryTable is null)
        {
            problem = $"{prefix} needs its category, rate, taxable amount and tax amount columns and the category table ({syntax}).";
            return null;
        }
        if (database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(entry.CategoryTable)) is not { } categories)
        {
            problem = $"{prefix}: {entry.CategoryTable} is not a table of the database.";
            return null;
        }
        if (categories.TableName.EqualsIgnoreCase(table.TableName))
        {
            problem = $"{prefix}: the category table must be another table.";
            return null;
        }
        if (table.PrimaryKeyColumns is not [{ IsIntegerColumn: true } key])
        {
            problem = $"{prefix} needs a single whole-number key.";
            return null;
        }
        if (categories.PrimaryKeyColumns is not [{ IsIntegerColumn: true } categoryKey])
        {
            problem = $"{prefix}: {categories.TableName} needs a single whole-number key.";
            return null;
        }
        ColumnModel? Find(TableModel t, string name) => t.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(name));
        var category = Find(table, entry.Category);
        var rate = Find(table, entry.Rate);
        var taxable = Find(table, entry.Taxable);
        var tax = Find(table, entry.Tax);
        var categoryRate = Find(categories, entry.CategoryRate);
        if (category is not { IsIntegerColumn: true } || category.CSharpBase() != categoryKey.CSharpBase())
        {
            problem = $"{prefix}: {entry.Category} must be a whole-number column of the same type as the key of {categories.TableName}.";
            return null;
        }
        foreach (var (column, name) in new[] { (rate, entry.Rate), (taxable, entry.Taxable), (tax, entry.Tax) })
        {
            if (column is not { IsNullable: false } || !IsNumber(column))
            {
                problem = $"{prefix}: {name} must be a decimal, double or float column that cannot be NULL.";
                return null;
            }
        }
        if (categoryRate is not { IsNullable: false } || !IsNumber(categoryRate))
        {
            problem = $"{prefix}: {categories.TableName}.{entry.CategoryRate} must be a decimal, double or float column that cannot be NULL (the rate as a fraction: 0.08 for 8 percent).";
            return null;
        }
        if (new[] { category.Name, rate!.Name, taxable!.Name, tax!.Name }.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4)
        {
            problem = $"{prefix}: the category, rate, taxable amount and tax amount must be four different columns.";
            return null;
        }
        ColumnModel? validFrom = entry.ValidFrom is null ? null : Find(categories, entry.ValidFrom);
        ColumnModel? validTo = entry.ValidTo is null ? null : Find(categories, entry.ValidTo);
        ColumnModel? day = entry.Day is null ? null : Find(table, entry.Day);
        ColumnModel? document = entry.Document is null ? null : Find(table, entry.Document);
        ColumnModel? label = entry.Label is not null ? Find(categories, entry.Label) : Find(categories, "Code") ?? Find(categories, "Name");
        if (entry.ValidFrom is not null && validFrom is not { IsDateColumn: true, IsNullable: false })
        {
            problem = $"{prefix}: {categories.TableName}.{entry.ValidFrom} must be a date column that cannot be NULL.";
            return null;
        }
        if (entry.ValidTo is not null && (entry.ValidFrom is null || validTo is not { IsDateColumn: true }))
        {
            problem = $"{prefix}: {categories.TableName}.{entry.ValidTo} must be a date column, and needs a valid-from column before it.";
            return null;
        }
        if (entry.Day is not null && day is not { IsDateColumn: true, IsNullable: false })
        {
            problem = $"{prefix}: {entry.Day} must be a date column that cannot be NULL.";
            return null;
        }
        if (entry.Document is not null && document is not { IsIntegerColumn: true })
        {
            problem = $"{prefix}: {entry.Document} must be a whole-number column (the document the line belongs to).";
            return null;
        }
        if (entry.Groups is not null)
        {
            if (entry.ValidFrom is null || entry.ValidTo is null)
            {
                problem = $"{prefix}: the group columns keep the validity periods apart, so {categories.TableName} needs a valid-from and a valid-to column.";
                return null;
            }
            foreach (string name in entry.Groups.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (Find(categories, name) is null)
                {
                    problem = $"{prefix}: {categories.TableName}.{name} is not a column.";
                    return null;
                }
            }
        }
        if (entry.Label is not null && label is not { IsStringColumn: true })
        {
            problem = $"{prefix}: {categories.TableName}.{entry.Label} must be a text column.";
            return null;
        }
        if (label is not null && !label.IsStringColumn)
            label = null;
        return new TaxLineShape(table, key, category, rate, taxable, tax, categories, categoryKey, categoryRate, validFrom, validTo, day, document, label,
            entry.Groups is null ? [] : entry.Groups.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(n => Find(categories, n)).OfType<ColumnModel>().ToList());
    }
}
