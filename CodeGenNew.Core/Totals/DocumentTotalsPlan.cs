using System.Globalization;
using CodeGenNew.Core.Allowance;
using CodeGenNew.Core.Settings;
using CodeGenNew.Core.Tenant;
using CodeGenNew.Core.Tax;

namespace CodeGenNew.Core.Totals;

/// <summary> A document header whose totals the server writes from its lines (<c>DocumentTotals=Invoice:InvoiceLine:InvoiceId:LineTotal:Lines=LineTotal:Allowances=AllowanceTotal:Charges=ChargeTotal:TaxExclusive=...</c>).
/// <c>Lines</c> is the line table, <c>LineKey</c> the column that points at the header and <c>LineAmount</c> the amount of a line that is added up. Each part is a header column or null (not kept). </summary>
public sealed record DocumentTotalsShape(TableModel Header, ColumnModel Key, TableModel Lines, ColumnModel LineKey, ColumnModel LineAmount, DocumentTotalsColumns Columns, AllowanceChargeShape? Allowance, TaxLineShape? Tax,
    ColumnModel? Currency, IReadOnlyList<DocumentTotalsRound> Rounds, IReadOnlyList<DocumentTotalsPlaces> Places, ColumnModel? AllowanceTax = null);

/// <summary> The header columns of the totals, in the order they are worked out: the sum of the lines, the allowances and the charges (from the allowance table of the document), the amount before tax, the tax (the tax amounts
/// of the lines), the amount with tax, what was paid in advance (written by the client, never by the server), the rounding and what is payable. </summary>
public sealed record DocumentTotalsColumns(ColumnModel? Lines, ColumnModel? Allowances, ColumnModel? Charges, ColumnModel? TaxExclusive, ColumnModel? Tax, ColumnModel? TaxInclusive, ColumnModel? Prepaid, ColumnModel? Rounding,
    ColumnModel? Payable)
{
    /// <summary> The columns the server writes: every part but the prepaid amount. </summary>
    public IEnumerable<(string Part, ColumnModel Column)> Owned()
    {
        if (Lines is not null) yield return (DocumentTotalsWords.PartLines, Lines);
        if (Allowances is not null) yield return (DocumentTotalsWords.PartAllowances, Allowances);
        if (Charges is not null) yield return (DocumentTotalsWords.PartCharges, Charges);
        if (TaxExclusive is not null) yield return (DocumentTotalsWords.PartTaxExclusive, TaxExclusive);
        if (Tax is not null) yield return (DocumentTotalsWords.PartTax, Tax);
        if (TaxInclusive is not null) yield return (DocumentTotalsWords.PartTaxInclusive, TaxInclusive);
        if (Rounding is not null) yield return (DocumentTotalsWords.PartRounding, Rounding);
        if (Payable is not null) yield return (DocumentTotalsWords.PartPayable, Payable);
    }
}

/// <summary> One rounding rule: the amount payable in <c>Currency</c> (<c>*</c> for every currency) is rounded to a multiple of <c>Increment</c> (0.05 for a currency with no 1 and 2 cent coins). </summary>
public sealed record DocumentTotalsRound(string Currency, decimal Increment);

/// <summary> One decimal places rule: the amounts of a document in <c>Currency</c> (<c>*</c> for every currency) are rounded to <c>Places</c> decimal places (0 for the yen), at most the places of the column. </summary>
public sealed record DocumentTotalsPlaces(string Currency, int Places);

public static class DocumentTotalsWords
{
    public const string PartLines = "Lines";
    public const string PartAllowances = "Allowances";
    public const string PartCharges = "Charges";
    public const string PartTaxExclusive = "TaxExclusive";
    public const string PartTax = "Tax";
    public const string PartTaxInclusive = "TaxInclusive";
    public const string PartPrepaid = "Prepaid";
    public const string PartRounding = "Rounding";
    public const string PartPayable = "Payable";
    public const string TokenCurrency = "Currency";
    public const string TokenRound = "Round";
    public const string TokenDecimals = "Decimals";
    public const string TokenAllowance = "Allowance";
    public const string TokenAllowanceTax = "AllowanceTax";
    public const string AnyCurrency = "*";
    public const string CheckRoute = "document-totals-check";
    public const int DefaultScale = 2;
    public const int MaxPlaces = 8;
    public const int CheckLimit = 1000;
}

/// <summary> <c>DocumentTotals</c>: after any change to a line, to an allowance or charge row of the document, or to the header itself, the server works the header's totals out again from the rows in the same transaction:
/// lines total = the sum of the line amounts; allowances and charges = the sums of the allowance table's rows; tax-exclusive = lines - allowances + charges; tax = the sum of the lines' tax amounts (<c>TaxLines</c>); tax-inclusive =
/// tax-exclusive + tax; payable = tax-inclusive - prepaid + rounding, where the rounding brings the payable amount to a multiple of the currency's increment (<c>Round=CHF/0.05</c>, or <c>Round=*/0.05</c>). Every
/// amount is rounded half away from zero to the scale of its column. A value a client writes into a column the server owns is replaced by the worked-out one. <c>GET &lt;header route&gt;/document-totals-check</c> lists the
/// headers whose stored totals differ from their rows. </summary>
public static class DocumentTotalsPlan
{
    public sealed record Entry(string Table, string? Lines, string? LineKey, string? LineAmount, IReadOnlyDictionary<string, string> Parts);

    private static readonly string[] PartNames =
        [DocumentTotalsWords.PartLines, DocumentTotalsWords.PartAllowances, DocumentTotalsWords.PartCharges, DocumentTotalsWords.PartTaxExclusive, DocumentTotalsWords.PartTax, DocumentTotalsWords.PartTaxInclusive,
         DocumentTotalsWords.PartPrepaid, DocumentTotalsWords.PartRounding, DocumentTotalsWords.PartPayable];

    public static IReadOnlyList<Entry> Entries(ProjectSettings project) =>
        SettingParts.Items(project.DocumentTotals).Select(p =>
        {
            var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in p.Skip(4).Where(x => x.Contains('=')))
                named[part[..part.IndexOf('=')].Trim()] = part[(part.IndexOf('=') + 1)..].Trim();
            return new Entry(p[0], SettingParts.At(p, 1), SettingParts.At(p, 2), SettingParts.At(p, 3), named);
        }).ToList();

    public static bool Any(ProjectSettings project) => project.DocumentTotals.Length > 0;

    public static bool IsListed(string table, ProjectSettings project) => Entries(project).Any(e => e.Table.EqualsIgnoreCase(table));

    /// <summary> Whether the table is a line table or an allowance table of a listed header: a change to it recalculates the header. </summary>
    public static bool Feeds(string table, ProjectSettings project) =>
        Entries(project).Any(e => (e.Lines?.EqualsIgnoreCase(table) ?? false) || (e.Parts.TryGetValue(DocumentTotalsWords.TokenAllowance, out string? a) && a.EqualsIgnoreCase(table)))
        || (Rate.RatePlan.Remembered(project) is { } database && All(project, database).Any(s => s.Allowance?.Table.TableName.EqualsIgnoreCase(table) ?? false));

    public static DocumentTotalsShape? For(TableModel table, ProjectSettings project, DatabaseModel database) =>
        IsListed(table.TableName, project) && Resolve(table, project, database, out _) is { } shape ? shape : null;

    /// <summary> The shape of a listed table when the generator remembered the database of the run (templates that see one table at a time). </summary>
    public static DocumentTotalsShape? For(TableModel table, ProjectSettings project) =>
        Rate.RatePlan.Remembered(project) is { } database ? For(table, project, database) : null;

    public static IEnumerable<DocumentTotalsShape> All(ProjectSettings project, DatabaseModel database) =>
        Entries(project).Select(e => database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(e.Table))).OfType<TableModel>().Select(t => For(t, project, database)).OfType<DocumentTotalsShape>();

    /// <summary> Whether <paramref name="column"/> is a total the server writes (every part but the prepaid amount): the forms show it as text. Works from the setting alone. </summary>
    public static bool IsOwnedColumn(TableModel table, ProjectSettings project, ColumnModel column) =>
        Entries(project).FirstOrDefault(e => e.Table.EqualsIgnoreCase(table.TableName)) is { } entry
        && entry.Parts.Any(p => !p.Key.EqualsIgnoreCase(DocumentTotalsWords.PartPrepaid) && PartNames.Contains(p.Key, StringComparer.OrdinalIgnoreCase) && p.Value.EqualsIgnoreCase(column.Name));

    /// <summary> Every total of a header, the prepaid amount too, as <c>Table.Column</c>: they hold money, so the screens show them as currency whatever their name (the project can still name others in MoneyColumns). </summary>
    public static IEnumerable<string> MoneyColumns(ProjectSettings project) =>
        Entries(project).SelectMany(e => e.Parts.Where(p => PartNames.Contains(p.Key, StringComparer.OrdinalIgnoreCase)).Select(p => $"{e.Table}.{p.Value}"));

    public static List<string> Problems(DatabaseModel database, ProjectSettings project)
    {
        var problems = new List<string>();
        foreach (var entry in Entries(project))
        {
            if (database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(entry.Table)) is not { } table)
                problems.Add($"DocumentTotals: {entry.Table} is not a table of the database.");
            else
            {
                Resolve(table, project, database, out string? problem);
                if (problem is not null)
                    problems.Add(problem);
            }
        }
        return problems;
    }

    private static IEnumerable<string> Rules(ProjectSettings project, TableModel table, string token) =>
        project.DocumentTotals.Where(i => i.Split(':')[0].Trim().EqualsIgnoreCase(table.TableName)).SelectMany(i => i.Split(':').Where(p => p.Trim().StartsWith(token + "=", StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Trim()[(token.Length + 1)..]);

    private static bool IsNumber(ColumnModel? column) => column?.CSharpBase() is "decimal" or "double" or "float";

    private static DocumentTotalsShape? Resolve(TableModel table, ProjectSettings project, DatabaseModel database, out string? problem)
    {
        problem = null;
        var entry = Entries(project).First(e => e.Table.EqualsIgnoreCase(table.TableName));
        string prefix = $"DocumentTotals: {table.TableName}";
        string syntax = $"DocumentTotals={table.TableName}:LineTable:LineKeyColumn:LineAmountColumn:Lines=Column[:Allowances=Column][:Charges=Column][:TaxExclusive=Column][:Tax=Column][:TaxInclusive=Column][:Prepaid=Column][:Rounding=Column][:Payable=Column][:Currency=Column][:Round=CHF/0.05][:Decimals=JPY/0][:AllowanceTax=TaxAmountColumn]";
        if (entry.Lines is null || entry.LineKey is null || entry.LineAmount is null)
        {
            problem = $"{prefix} needs the line table, the column of a line that points at the header and the amount column of a line ({syntax}).";
            return null;
        }
        if (table.PrimaryKeyColumns is not [{ IsIntegerColumn: true } key])
        {
            problem = $"{prefix} needs a whole-number primary key of one column.";
            return null;
        }
        if (database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(entry.Lines)) is not { } lines || lines.TableName.EqualsIgnoreCase(table.TableName))
        {
            problem = $"{prefix}: {entry.Lines} must be another table of the database.";
            return null;
        }
        if (project.SoftDeleteTables.Any(i => i.Split(':')[0].Trim().EqualsIgnoreCase(lines.TableName)))
        {
            problem = $"{prefix}: the line table {lines.TableName} is in SoftDeleteTables, whose deletes do not pass through a save, so the totals would keep a deleted line.";
            return null;
        }
        if (project.Stacks.Any(s => s.EqualsIgnoreCase("Python") || s.EqualsIgnoreCase("Rust")) && new[] { table, lines }.FirstOrDefault(t => TenantPlan.IsTenantTable(t, project)) is { } tenants)
        {
            problem = $"{prefix}: {tenants.TableName} is the tenant table, whose rows the Python and Rust APIs do not write totals for. Leave Python and Rust out of Stacks.";
            return null;
        }
        var lineKey = lines.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(entry.LineKey));
        if (lineKey is null || lineKey.CSharpBase() != key.CSharpBase())
        {
            problem = $"{prefix}: {entry.LineKey} must be a column of {lines.TableName} of the same type as the key of {table.TableName}.";
            return null;
        }
        var amount = lines.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(entry.LineAmount));
        if (!IsNumber(amount))
        {
            problem = $"{prefix}: {entry.LineAmount} must be a decimal, double or float column of {lines.TableName}.";
            return null;
        }
        foreach (string name in entry.Parts.Keys)
            if (!PartNames.Contains(name, StringComparer.OrdinalIgnoreCase) && !new[] { DocumentTotalsWords.TokenCurrency, DocumentTotalsWords.TokenRound, DocumentTotalsWords.TokenDecimals, DocumentTotalsWords.TokenAllowance, DocumentTotalsWords.TokenAllowanceTax }.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                problem = $"{prefix}: {name} is not a part of the totals ({syntax}).";
                return null;
            }
        ColumnModel? Part(string name) => entry.Parts.TryGetValue(name, out string? column) ? table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(column)) : null;
        var columns = new DocumentTotalsColumns(Part(DocumentTotalsWords.PartLines), Part(DocumentTotalsWords.PartAllowances), Part(DocumentTotalsWords.PartCharges), Part(DocumentTotalsWords.PartTaxExclusive),
            Part(DocumentTotalsWords.PartTax), Part(DocumentTotalsWords.PartTaxInclusive), Part(DocumentTotalsWords.PartPrepaid), Part(DocumentTotalsWords.PartRounding), Part(DocumentTotalsWords.PartPayable));
        foreach (string name in PartNames)
        {
            if (!entry.Parts.ContainsKey(name))
                continue;
            if (Part(name) is not { } column || !IsNumber(column) || column.IsPrimaryKey)
            {
                problem = $"{prefix}: {name}={entry.Parts[name]} must be a decimal, double or float column of {table.TableName}.";
                return null;
            }
        }
        var used = PartNames.Select(Part).OfType<ColumnModel>().Select(c => c.Name.ToLowerInvariant()).ToList();
        if (used.Distinct().Count() != used.Count)
        {
            problem = $"{prefix}: every part of the totals needs a column of its own.";
            return null;
        }
        if (!columns.Owned().Any())
        {
            problem = $"{prefix} names no total to write ({syntax}).";
            return null;
        }
        AllowanceChargeShape? allowance = null;
        if (columns.Allowances is not null || columns.Charges is not null)
        {
            string? named = entry.Parts.TryGetValue(DocumentTotalsWords.TokenAllowance, out string? n) ? n : null;
            allowance = database.Tables.Where(t => named is null || t.TableName.EqualsIgnoreCase(named)).Select(t => AllowanceChargePlan.For(t, project)).OfType<AllowanceChargeShape>()
                .FirstOrDefault(a => a.Document is { } d && a.Table.ForeignKeys.Any(f => f.ReferencedTable.EqualsIgnoreCase(table.TableName) && f.ReferencingColumns.Any(r => r.EqualsIgnoreCase(d.Name))));
            if (allowance is null)
            {
                problem = $"{prefix}: Allowances and Charges need a table of AllowanceCharges whose document column is a foreign key to {table.TableName} (or name it with Allowance=Table).";
                return null;
            }
            if (allowance.Table.TableName.EqualsIgnoreCase(lines.TableName) || !allowance.Table.PrimaryKeyColumns.All(c => c.IsIdentity))
            {
                problem = $"{prefix}: the allowance table must be another table than the lines, with an identity key.";
                return null;
            }
        }
        TaxLineShape? tax = null;
        if (columns.Tax is not null || columns.TaxInclusive is not null || columns.Payable is not null)
        {
            tax = TaxLinePlan.For(lines, project, database);
            if (columns.Tax is not null && tax is null)
            {
                problem = $"{prefix}: Tax needs the line table {lines.TableName} to be listed in TaxLines.";
                return null;
            }
        }
        ColumnModel? allowanceTax = null;
        if (entry.Parts.TryGetValue(DocumentTotalsWords.TokenAllowanceTax, out string? allowanceTaxName))
        {
            allowanceTax = allowance?.Table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(allowanceTaxName));
            if (allowance is null)
            {
                problem = $"{prefix}: AllowanceTax needs Allowances or Charges with an allowance table.";
                return null;
            }
            if (allowanceTax is null || TaxLinePlan.For(allowance.Table, project, database) is not { } allowanceLine || !allowanceLine.Tax.Name.EqualsIgnoreCase(allowanceTax.Name))
            {
                problem = $"{prefix}: AllowanceTax={allowanceTaxName} must be the tax amount column of {allowance.Table.TableName}, which must be listed in TaxLines (the rate and the category are kept there).";
                return null;
            }
        }
        ColumnModel? currency = null;
        if (entry.Parts.TryGetValue(DocumentTotalsWords.TokenCurrency, out string? currencyName))
        {
            currency = table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(currencyName));
            if (currency is null || currency.CSharpBase() != "string")
            {
                problem = $"{prefix}: Currency={currencyName} must be a text column of {table.TableName}.";
                return null;
            }
        }
        var rounds = new List<DocumentTotalsRound>();
        foreach (string rule in Rules(project, table, DocumentTotalsWords.TokenRound))
        {
            string[] pair = rule.Split('/');
            if (pair.Length != 2 || pair[0].Trim().Length == 0 || !decimal.TryParse(pair[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal increment) || increment <= 0)
            {
                problem = $"{prefix}: Round={rule} must read Currency/Increment, for example Round=CHF/0.05.";
                return null;
            }
            if (pair[0].Trim() != DocumentTotalsWords.AnyCurrency && currency is null)
            {
                problem = $"{prefix}: a rounding rule for {pair[0].Trim()} needs Currency=Column.";
                return null;
            }
            rounds.Add(new DocumentTotalsRound(pair[0].Trim(), increment));
        }
        var places = new List<DocumentTotalsPlaces>();
        foreach (string rule in Rules(project, table, DocumentTotalsWords.TokenDecimals))
        {
            string[] pair = rule.Split('/');
            if (pair.Length != 2 || pair[0].Trim().Length == 0 || !int.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count > DocumentTotalsWords.MaxPlaces)
            {
                problem = $"{prefix}: Decimals={rule} must read Currency/Places with 0 to {DocumentTotalsWords.MaxPlaces} places, for example Decimals=JPY/0.";
                return null;
            }
            if (pair[0].Trim() != DocumentTotalsWords.AnyCurrency && currency is null)
            {
                problem = $"{prefix}: a decimal places rule for {pair[0].Trim()} needs Currency=Column.";
                return null;
            }
            places.Add(new DocumentTotalsPlaces(pair[0].Trim(), count));
        }
        if (rounds.Count > 0 && columns.Rounding is null && columns.Payable is null)
        {
            problem = $"{prefix}: a rounding rule needs the Rounding or the Payable column.";
            return null;
        }
        return new DocumentTotalsShape(table, key, lines, lineKey, amount!, columns, allowance, tax, currency, rounds, places, allowanceTax);
    }
}
