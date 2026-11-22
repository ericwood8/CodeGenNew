using CodeGenNew.Core.Api;
using CodeGenNew.Core.Settings;
using CodeGenNew.Core.Tenant;

namespace CodeGenNew.Core.Allowance;

/// <summary> A table of allowances (discounts) and charges (surcharges) (<c>AllowanceCharges=InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:InvoiceId</c>): <c>Flag</c> is the yes/no column that says
/// charge (true) or allowance (false), <c>Base</c> the amount a percentage is taken from, <c>Percent</c> the percentage (10 is 10 percent) and <c>Amount</c> the resulting amount, always stored positive. <c>Document</c>
/// is the column that points at the document or line the row sits on (null: the totals route is not offered). </summary>
public sealed record AllowanceChargeShape(TableModel Table, ColumnModel Flag, ColumnModel Base, ColumnModel Percent, ColumnModel Amount, ColumnModel? Document, ColumnModel? Line = null);

public static class AllowanceChargeWords
{
    public const string InvalidType = ProblemTypes.Prefix + "allowance-charge-invalid";
    public const string InvalidTitle = "Not a valid allowance or charge";
    public const string Negative = "An allowance or charge cannot be negative: the yes/no column says which way it goes.";
    public const string PercentNeedsBase = "A percentage needs the base amount it is taken from.";
    public const string NeedsAmount = "Give the amount, or a base amount and a percentage.";
    public const string NeedsDocument = "Give the document (document=).";
    public const string TotalsRoute = "allowance-charge-totals";
    public const int DefaultScale = 2;
    public const string DefaultFlag = "IsCharge";
    public const string DefaultBase = "BaseAmount";
    public const string DefaultPercent = "Percentage";
    public const string DefaultAmount = "Amount";
}

/// <summary> <c>AllowanceCharges</c>: a save works the amount out on the server from the base and the percentage (rounded to the amount column's scale), or the percentage from the amount and the base, and refuses
/// a negative value, a percentage with no base and a row with neither an amount nor a base and a percentage (400 <c>allowance-charge-invalid</c>). The amount is stored positive: the flag says whether it is added
/// (a charge) or taken off (an allowance). With a document column, <c>GET allowance-charge-totals?document=</c> answers the allowances, the charges and the net (charges minus allowances) of one document; with a line column too, <c>&amp;line=</c> narrows the answer to the rows of one line of the document. </summary>
public static class AllowanceChargePlan
{
    public sealed record Entry(string Table, string Flag, string Base, string Percent, string Amount, string? Document, string? Line = null);

    public static IReadOnlyList<Entry> Entries(ProjectSettings project) =>
        SettingParts.Items(project.AllowanceCharges).Select(p => new Entry(p[0], SettingParts.At(p, 1) ?? AllowanceChargeWords.DefaultFlag, SettingParts.At(p, 2) ?? AllowanceChargeWords.DefaultBase,
            SettingParts.At(p, 3) ?? AllowanceChargeWords.DefaultPercent, SettingParts.At(p, 4) ?? AllowanceChargeWords.DefaultAmount, SettingParts.At(p, 5), SettingParts.At(p, 6))).ToList();

    public static bool IsListed(string table, ProjectSettings project) => Entries(project).Any(e => e.Table.EqualsIgnoreCase(table));

    public static AllowanceChargeShape? For(TableModel table, ProjectSettings project) => IsListed(table.TableName, project) && Resolve(table, project, out _) is { } shape ? shape : null;

    /// <summary> Whether <paramref name="column"/> is the amount or the percentage of an allowance table: the server writes them from the other columns, so a test cannot expect its own value back. </summary>
    public static bool IsOwnedColumn(TableModel table, ProjectSettings project, ColumnModel column) =>
        For(table, project) is { } shape && (column.Name.EqualsIgnoreCase(shape.Amount.Name) || column.Name.EqualsIgnoreCase(shape.Percent.Name));

    public static string? Problem(TableModel table, ProjectSettings project)
    {
        if (!IsListed(table.TableName, project))
            return null;
        Resolve(table, project, out string? problem);
        return problem;
    }

    public static List<string> Problems(DatabaseModel database, ProjectSettings project)
    {
        var problems = new List<string>();
        foreach (var entry in Entries(project))
        {
            if (database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(entry.Table)) is not { } table)
                problems.Add($"AllowanceCharges: {entry.Table} is not a table of the database.");
            else if (Problem(table, project) is { } problem)
                problems.Add(problem);
        }
        return problems;
    }

    /// <summary> Whether the column holds a number a percentage can be worked out in: a decimal, double or float. </summary>
    public static bool IsFractional(ColumnModel column) => column.CSharpBase() is "decimal" or "double" or "float";

    private static AllowanceChargeShape? Resolve(TableModel table, ProjectSettings project, out string? problem)
    {
        problem = null;
        var entry = Entries(project).First(e => e.Table.EqualsIgnoreCase(table.TableName));
        string prefix = $"AllowanceCharges: {table.TableName}";
        string syntax = $"AllowanceCharges={table.TableName}:{AllowanceChargeWords.DefaultFlag}:{AllowanceChargeWords.DefaultBase}:{AllowanceChargeWords.DefaultPercent}:{AllowanceChargeWords.DefaultAmount}[:DocumentColumn[:LineColumn]]";
        if (RowShapeWords.NeverChanged(table, project) is { } never)
        {
            problem = $"{prefix} {never}, so it has no allowances to work out.";
            return null;
        }
        if (TenantPlan.IsTenantTable(table, project) || OwnedTable.Through(table, project) is not null)
        {
            problem = $"{prefix} is the tenant table or belongs to the owner of its parent row, which an allowance table does not serve (a table with its own owner column or tenant column is served).";
            return null;
        }
        ColumnModel? Find(string? name) => name is null ? null : table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(name));
        var flag = Find(entry.Flag);
        var baseColumn = Find(entry.Base);
        var percent = Find(entry.Percent);
        var amount = Find(entry.Amount);
        if (flag is not { IsBooleanColumn: true, IsNullable: false })
        {
            problem = $"{prefix} needs {entry.Flag} as a yes/no column that cannot be NULL ({syntax}).";
            return null;
        }
        if (baseColumn is not { } || !IsFractional(baseColumn) || percent is not { } || !IsFractional(percent) || amount is not { IsNullable: false } || !IsFractional(amount))
        {
            problem = $"{prefix} needs {entry.Base} and {entry.Percent} as decimal, double or float columns and {entry.Amount} as one that cannot be NULL ({syntax}).";
            return null;
        }
        if (new[] { flag, baseColumn, percent, amount }.Select(c => c.Name.ToLowerInvariant()).Distinct().Count() != 4)
        {
            problem = $"{prefix}: the flag, base, percent and amount must be four different columns.";
            return null;
        }
        ColumnModel? document = null;
        if (entry.Document is not null)
        {
            document = Find(entry.Document);
            if (document is null)
            {
                problem = $"{prefix}: {entry.Document} is not a column of the table ({syntax}).";
                return null;
            }
        }
        ColumnModel? line = null;
        if (entry.Line is not null)
        {
            line = Find(entry.Line);
            if (document is null || line is null || line.Name.EqualsIgnoreCase(document.Name) || !line.IsIntegerColumn)
            {
                problem = $"{prefix}: the line column {entry.Line} must be a whole-number column of the table, different from the document column, and the entry needs the document column too ({syntax}).";
                return null;
            }
        }
        return new AllowanceChargeShape(table, flag, baseColumn, percent, amount, document, line);
    }
}
