using System.Text.RegularExpressions;
using CodeGenNew.Core.Api;
using CodeGenNew.Core.Rate;
using CodeGenNew.Core.Settings;
using CodeGenNew.Core.Tenant;

namespace CodeGenNew.Core.Payment;

public static class PaymentWords
{
    public const string InvalidType = ProblemTypes.Prefix + "payment-invalid";
    public const string InvalidTitle = "Not a valid payment";
    public const string OverTotalType = ProblemTypes.Prefix + "installment-over-total";
    public const string OverTotalTitle = "Installments over the total";

    public const string IbanBad = "The IBAN is not valid.";
    public const string RoutingBad = "The routing number is not valid.";
    public const string AccountNumberBad = "The account number must be 4 to 17 digits.";
    public const string AccountMissing = "Give an IBAN, or an account number with a routing number.";

    public const string DocumentMissing = "The document does not exist.";
    public const string SequenceNotPositive = "The sequence number must be greater than zero.";
    public const string AmountNotPositive = "The amount of an installment must be greater than zero.";
    public const string SequenceTaken = "Another installment of this document has that sequence number.";
    public const string OutOfOrder = "An installment cannot be due before an earlier one or after a later one.";
    public const string OverTotal = "The installments would add up to more than the total of the document.";
    public const string NeedsDocument = "Give the document (document=).";

    public const string NeedsPayment = "Give the payment (id=).";
    public const string PaymentMissing = "The payment does not exist.";
    public const string RuleEarly = "early-discount";
    public const string RuleOnTime = "on-time";
    public const string RuleLate = "late";

    public const string TermsMissing = "The payment terms do not exist.";
    public const string DueBeforeDocument = "The due date cannot be before the date of the document.";

    public const string LedgerDuplicateType = ProblemTypes.Prefix + "ledger-duplicate";
    public const string LedgerDuplicateTitle = "Already recorded";
    public const string RefundOverType = ProblemTypes.Prefix + "refund-over-paid";
    public const string RefundOverTitle = "Refund over the amount paid";
    public const string KindBad = "The kind must be authorize, capture, refund or void.";
    public const string StatusBad = "The status must be pending, succeeded or failed.";
    public const string RefundSign = "A refund is a negative amount.";
    public const string AmountPositive = "The amount of an authorize, capture or void must be greater than zero.";
    public const string GatewayMissing = "Give the gateway id and the idempotency key.";
    public const string GatewayTaken = "A transaction with that gateway id is already recorded.";
    public const string KeyTaken = "A transaction with that idempotency key is already recorded.";
    public const string RefundOver = "The refund is more than the amount paid so far.";
}

/// <summary> <c>BankAccountTables=Table[:IbanColumn[:AccountColumn[:RoutingColumn]]]</c>: a save is refused unless the row holds a valid IBAN or an account number with a valid routing number. </summary>
public sealed record BankAccountShape(TableModel Table, ColumnModel Key, ColumnModel? Iban, ColumnModel? Account, ColumnModel? Routing);

/// <summary> <c>InstallmentTables=Table:DocumentColumn:DocumentTable:TotalColumn[:SequenceColumn[:DueColumn[:AmountColumn]]]</c>: the installments of one document are numbered, fall due in the order of their numbers and may not add up to more than the document's total. </summary>
public sealed record InstallmentShape(TableModel Table, ColumnModel Key, ColumnModel Document, ColumnModel Sequence, ColumnModel Due, ColumnModel Amount, TableModel DocumentTable, ColumnModel DocumentKey, ColumnModel Total);

/// <summary> <c>PaymentTermsTables=Table:DocumentColumn:DocumentTable:DocumentDateColumn[:TermsColumn[:DueColumn[:TermsTable[:DaysColumn]]]]</c>: a row that names payment terms and has no due date gets the date of the document plus the days to pay;
/// a due date before the date of the document is refused. </summary>
public sealed record DueDateShape(TableModel Table, ColumnModel Key, ColumnModel Document, ColumnModel Terms, ColumnModel Due, ColumnModel DocumentDate, TableModel DocumentTable, ColumnModel DocumentKey, TableModel TermsTable, ColumnModel TermsKey, ColumnModel Days, ColumnModel? EarlyPercent = null, ColumnModel? EarlyDays = null, ColumnModel? LatePercent = null, ColumnModel? Total = null)
{
    /// <summary> Whether the payable amount can be worked out: the document has a total and the terms have a discount or a penalty. </summary>
    public bool CanPay => Total is not null && (EarlyPercent is not null && EarlyDays is not null || LatePercent is not null);
}

public static partial class PaymentPlan
{
    public const string DefaultIban = "Iban";
    public const string DefaultAccount = "AccountNumber";
    public const string DefaultRouting = "RoutingNumber";
    public const string DefaultSequence = "SequenceNo";
    public const string DefaultDue = "DueDate";
    public const string DefaultAmount = "Amount";
    public const string DefaultTerms = "PaymentTermsId";
    public const string DefaultTermsTable = "PaymentTerms";
    public const string DefaultDays = "DaysToPay";
    public const string DefaultEarlyPercent = "EarlyDiscountPercent";
    public const string DefaultEarlyDays = "EarlyDiscountDays";
    public const string DefaultLatePercent = "LatePenaltyPercent";
    public const string DefaultTotal = "Total";

    /// <summary> The IBAN lengths of the countries that have one (ISO 13616); a country that is not here has no valid IBAN for the rule. </summary>
    public const string IbanLengths = "AD24 AE23 AL28 AT20 AZ28 BA20 BE16 BG22 BH22 BR29 BY28 CH21 CR22 CY28 CZ24 DE22 DK18 DO28 EE20 EG29 ES24 FI18 FO18 FR27 GB22 GE22 GI23 GL18 GR27 GT28 HR21 HU28 IE22 IL23 IQ23 IS26 IT27 JO30 KW30 KZ20 LB28 LC32 LI21 LT20 LU20 LV21 MC27 MD24 ME22 MK19 MR27 MT31 MU30 NL18 NO15 PK24 PL28 PS29 PT25 QA29 RO24 RS22 SA24 SC31 SE24 SI19 SK24 SM27 ST25 SV28 TL23 TN24 TR26 UA29 VA22 VG24 XK20";

    /// <summary> Names a column that must never exist on a payment table (item 19: no card number, security code or expiry is stored). </summary>
    [GeneratedRegex("card.?(number|no|num)|^pan$|cvv|cvc|security.?code|expir", RegexOptions.IgnoreCase)]
    private static partial Regex CardColumn();

    public sealed record BankEntry(string Table, string Iban, string Account, string Routing);
    public sealed record InstallmentEntry(string Table, string Document, string DocumentTable, string Total, string Sequence, string Due, string Amount);
    public sealed record DueEntry(string Table, string Document, string DocumentTable, string DocumentDate, string Terms, string Due, string TermsTable, string Days, string EarlyPercent, string EarlyDays, string LatePercent, string Total);

    public static IReadOnlyList<BankEntry> BankEntries(ProjectSettings project) =>
        SettingParts.Items(project.BankAccountTables).Select(p => new BankEntry(p[0], SettingParts.At(p, 1) ?? DefaultIban, SettingParts.At(p, 2) ?? DefaultAccount, SettingParts.At(p, 3) ?? DefaultRouting)).ToList();

    public static IReadOnlyList<InstallmentEntry> InstallmentEntries(ProjectSettings project) =>
        SettingParts.Items(project.InstallmentTables).Select(p => new InstallmentEntry(p[0], SettingParts.At(p, 1) ?? "", SettingParts.At(p, 2) ?? "", SettingParts.At(p, 3) ?? "", SettingParts.At(p, 4) ?? DefaultSequence, SettingParts.At(p, 5) ?? DefaultDue, SettingParts.At(p, 6) ?? DefaultAmount)).ToList();

    public static IReadOnlyList<DueEntry> DueEntries(ProjectSettings project) =>
        SettingParts.Items(project.PaymentTermsTables).Select(p => new DueEntry(p[0], SettingParts.At(p, 1) ?? "", SettingParts.At(p, 2) ?? "", SettingParts.At(p, 3) ?? "", SettingParts.At(p, 4) ?? DefaultTerms, SettingParts.At(p, 5) ?? DefaultDue, SettingParts.At(p, 6) ?? DefaultTermsTable, SettingParts.At(p, 7) ?? DefaultDays, SettingParts.At(p, 8) ?? DefaultEarlyPercent, SettingParts.At(p, 9) ?? DefaultEarlyDays, SettingParts.At(p, 10) ?? DefaultLatePercent, SettingParts.At(p, 11) ?? DefaultTotal)).ToList();

    public static bool IsListed(string table, ProjectSettings project) =>
        BankEntries(project).Any(e => e.Table.EqualsIgnoreCase(table)) || InstallmentEntries(project).Any(e => e.Table.EqualsIgnoreCase(table)) || DueEntries(project).Any(e => e.Table.EqualsIgnoreCase(table));

    /// <summary> The value a generated test sends for a column of a bank account table: an IBAN, an account number or a routing number that passes the checks. Null for any other column. </summary>
    public static string? BankSample(TableModel table, ColumnModel column, ProjectSettings project)
    {
        if (BankFor(table, project) is not { } shape)
            return null;
        if (shape.Iban is not null && shape.Iban.Name.EqualsIgnoreCase(column.Name))
            return "DE89370400440532013000";
        if (shape.Account is not null && shape.Account.Name.EqualsIgnoreCase(column.Name))
            return "123456789";
        return shape.Routing is not null && shape.Routing.Name.EqualsIgnoreCase(column.Name) ? "011000015" : null;
    }

    public static bool IsInstallmentTable(string table, ProjectSettings project) => InstallmentEntries(project).Any(e => e.Table.EqualsIgnoreCase(table));

    public static BankAccountShape? BankFor(TableModel table, ProjectSettings project, DatabaseModel database) =>
        BankEntries(project).Any(e => e.Table.EqualsIgnoreCase(table.TableName)) && ResolveBank(table, project, out _) is { } shape ? shape : null;

    public static InstallmentShape? InstallmentFor(TableModel table, ProjectSettings project, DatabaseModel database) =>
        InstallmentEntries(project).Any(e => e.Table.EqualsIgnoreCase(table.TableName)) && ResolveInstallment(table, project, database, out _) is { } shape ? shape : null;

    public static DueDateShape? DueFor(TableModel table, ProjectSettings project, DatabaseModel database) =>
        DueEntries(project).Any(e => e.Table.EqualsIgnoreCase(table.TableName)) && ResolveDue(table, project, database, out _) is { } shape ? shape : null;

    public static BankAccountShape? BankFor(TableModel table, ProjectSettings project) => RatePlan.Remembered(project) is { } d ? BankFor(table, project, d) : null;

    public static InstallmentShape? InstallmentFor(TableModel table, ProjectSettings project) => RatePlan.Remembered(project) is { } d ? InstallmentFor(table, project, d) : null;

    public static DueDateShape? DueFor(TableModel table, ProjectSettings project) => RatePlan.Remembered(project) is { } d ? DueFor(table, project, d) : null;

    public static List<string> Problems(DatabaseModel database, ProjectSettings project)
    {
        var problems = new List<string>();
        foreach (var e in BankEntries(project))
            Collect(database, e.Table, "BankAccountTables", problems, t => { ResolveBank(t, project, out string? p); return p; });
        foreach (var e in InstallmentEntries(project))
            Collect(database, e.Table, "InstallmentTables", problems, t => { ResolveInstallment(t, project, database, out string? p); return p; });
        foreach (var e in DueEntries(project))
            Collect(database, e.Table, "PaymentTermsTables", problems, t => { ResolveDue(t, project, database, out string? p); return p; });
        return problems;
    }

    private static void Collect(DatabaseModel database, string name, string setting, List<string> problems, Func<TableModel, string?> resolve)
    {
        if (database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(name)) is not { } table)
            problems.Add($"{setting}: {name} is not a table of the database.");
        else if (resolve(table) is { } problem)
            problems.Add(problem);
    }

    /// <summary> One sentence for each installment table with no unique index on the document and the sequence number: two requests at once could store the same number twice. </summary>
    public static List<string> MissingIndexes(DatabaseModel database, ProjectSettings project)
    {
        var missing = new List<string>();
        foreach (var table in database.Tables)
        {
            if (InstallmentFor(table, project, database) is not { } shape)
                continue;
            string[] columns = [shape.Document.DbName, shape.Sequence.DbName];
            if (!table.Indexes.Any(i => i.IsUnique && i.Columns.Count == columns.Length && columns.All(c => i.Columns.Any(x => x.EqualsIgnoreCase(c)))))
                missing.Add($"{table.DbTableName} holds installments without a unique index on {columns[0]} and {columns[1]}: two requests at once can still store the same number twice. Add a unique index on the two columns.");
        }
        return missing;
    }

    internal static string? CardProblem(TableModel table, string setting) =>
        table.Columns.FirstOrDefault(c => CardColumn().IsMatch(c.Name)) is { } card
            ? $"{setting}: {table.TableName}.{card.Name} looks like a card number, security code or expiry. A card is never stored; keep a reference from the payment gateway instead."
            : null;

    private static BankAccountShape? ResolveBank(TableModel table, ProjectSettings project, out string? problem)
    {
        problem = CardProblem(table, "BankAccountTables");
        if (problem is not null)
            return null;
        var entry = BankEntries(project).First(e => e.Table.EqualsIgnoreCase(table.TableName));
        if (table.PrimaryKeyColumns is not [{ IsIntegerColumn: true } key])
        {
            problem = $"BankAccountTables: {table.TableName} needs a single whole-number key.";
            return null;
        }
        var iban = table.FindColumn(entry.Iban);
        var account = table.FindColumn(entry.Account);
        var routing = table.FindColumn(entry.Routing);
        if (iban is not { IsStringColumn: true } && !(account is { IsStringColumn: true } && routing is { IsStringColumn: true }))
        {
            problem = $"BankAccountTables: {table.TableName} needs a text column {entry.Iban} (the IBAN) or the text columns {entry.Account} and {entry.Routing}.";
            return null;
        }
        return new BankAccountShape(table, key, iban is { IsStringColumn: true } ? iban : null, account is { IsStringColumn: true } && routing is { IsStringColumn: true } ? account : null, account is { IsStringColumn: true } && routing is { IsStringColumn: true } ? routing : null);
    }

    private static InstallmentShape? ResolveInstallment(TableModel table, ProjectSettings project, DatabaseModel database, out string? problem)
    {
        problem = CardProblem(table, "InstallmentTables");
        if (problem is not null)
            return null;
        var entry = InstallmentEntries(project).First(e => e.Table.EqualsIgnoreCase(table.TableName));
        string syntax = $"InstallmentTables={table.TableName}:DocumentColumn:DocumentTable:TotalColumn[:SequenceColumn[:DueColumn[:AmountColumn]]]";
        if (entry.Document.Length == 0 || entry.DocumentTable.Length == 0 || entry.Total.Length == 0)
        {
            problem = $"InstallmentTables: {table.TableName} needs the document column, the document table and its total column ({syntax}).";
            return null;
        }
        if (database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(entry.DocumentTable)) is not { } documents)
        {
            problem = $"InstallmentTables: {entry.DocumentTable} is not a table of the database ({syntax}).";
            return null;
        }
        if (table.PrimaryKeyColumns is not [{ IsIntegerColumn: true } key] || documents.PrimaryKeyColumns is not [{ IsIntegerColumn: true } documentKey])
        {
            problem = $"InstallmentTables: {table.TableName} and {documents.TableName} each need a single whole-number key.";
            return null;
        }
        var document = table.FindColumn(entry.Document);
        var sequence = table.FindColumn(entry.Sequence);
        var due = table.FindColumn(entry.Due);
        var amount = table.FindColumn(entry.Amount);
        var total = documents.FindColumn(entry.Total);
        if (document is not { IsIntegerColumn: true, IsNullable: false } || document.CSharpBase() != documentKey.CSharpBase())
            problem = $"InstallmentTables: {entry.Document} must be a whole-number column that cannot be NULL, of the same type as the key of {documents.TableName} ({syntax}).";
        else if (sequence is not { IsIntegerColumn: true, IsNullable: false })
            problem = $"InstallmentTables: {entry.Sequence} must be a whole-number column that cannot be NULL (the number of the installment).";
        else if (due is not { IsNullable: false } || !due.IsLocalDateColumn)
            problem = $"InstallmentTables: {entry.Due} must be a date column that cannot be NULL (the day the installment falls due).";
        else if (amount is not { IsNullable: false } || !amount.IsFractionalColumn)
            problem = $"InstallmentTables: {entry.Amount} must be a decimal, double or float column that cannot be NULL.";
        else if (total is not { IsNullable: false } || !total.IsFractionalColumn)
            problem = $"InstallmentTables: {documents.TableName}.{entry.Total} must be a decimal, double or float column that cannot be NULL.";
        else if (amount.CSharpBase() != total.CSharpBase())
            problem = $"InstallmentTables: {entry.Amount} and {documents.TableName}.{entry.Total} must have the same type.";
        else if (OwnedTable.OwnerColumn(table, project) is not null || OwnedTable.Through(table, project) is not null || TenantPlan.OwnedBy(table, project) is not null)
            problem = $"InstallmentTables: {table.TableName} is limited to an owner or a tenant, which the schedule route does not read yet.";
        else if (new[] { document.Name, sequence.Name, due.Name, amount.Name }.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4)
            problem = $"InstallmentTables: the document, sequence, due date and amount must be four different columns of {table.TableName}.";
        if (problem is not null)
            return null;
        return new InstallmentShape(table, key, document!, sequence!, due!, amount!, documents, documentKey, total!);
    }

    private static DueDateShape? ResolveDue(TableModel table, ProjectSettings project, DatabaseModel database, out string? problem)
    {
        problem = CardProblem(table, "PaymentTermsTables");
        if (problem is not null)
            return null;
        var entry = DueEntries(project).First(e => e.Table.EqualsIgnoreCase(table.TableName));
        string syntax = $"PaymentTermsTables={table.TableName}:DocumentColumn:DocumentTable:DocumentDateColumn[:TermsColumn[:DueColumn[:TermsTable[:DaysColumn]]]]";
        if (entry.Document.Length == 0 || entry.DocumentTable.Length == 0 || entry.DocumentDate.Length == 0)
        {
            problem = $"PaymentTermsTables: {table.TableName} needs the document column, the document table and the date column of the document ({syntax}).";
            return null;
        }
        var documents = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(entry.DocumentTable));
        var terms = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(entry.TermsTable));
        if (documents is null || terms is null)
        {
            problem = $"PaymentTermsTables: {(documents is null ? entry.DocumentTable : entry.TermsTable)} is not a table of the database ({syntax}).";
            return null;
        }
        if (table.PrimaryKeyColumns is not [{ IsIntegerColumn: true } key] || documents.PrimaryKeyColumns is not [{ IsIntegerColumn: true } documentKey] || terms.PrimaryKeyColumns is not [{ IsIntegerColumn: true } termsKey])
        {
            problem = $"PaymentTermsTables: {table.TableName}, {documents.TableName} and {terms.TableName} each need a single whole-number key.";
            return null;
        }
        var document = table.FindColumn(entry.Document);
        var termsColumn = table.FindColumn(entry.Terms);
        var due = table.FindColumn(entry.Due);
        var date = documents.FindColumn(entry.DocumentDate);
        var days = terms.FindColumn(entry.Days);
        if (document is not { IsIntegerColumn: true, IsNullable: false } || document.CSharpBase() != documentKey.CSharpBase())
            problem = $"PaymentTermsTables: {entry.Document} must be a whole-number column that cannot be NULL, of the same type as the key of {documents.TableName} ({syntax}).";
        else if (termsColumn is not { IsIntegerColumn: true } || termsColumn.CSharpBase() != termsKey.CSharpBase())
            problem = $"PaymentTermsTables: {entry.Terms} must be a whole-number column of the same type as the key of {terms.TableName}.";
        else if (due is not { IsNullable: true } || !due.IsLocalDateColumn)
            problem = $"PaymentTermsTables: {entry.Due} must be a date column that allows NULL (empty means it is worked out from the terms).";
        else if (date is null || !date.IsLocalDateColumn)
            problem = $"PaymentTermsTables: {documents.TableName}.{entry.DocumentDate} must be a date column (a document with no date gets no due date worked out).";
        else if (days is not { IsIntegerColumn: true, IsNullable: false })
            problem = $"PaymentTermsTables: {terms.TableName}.{entry.Days} must be a whole-number column that cannot be NULL (the days to pay).";
        if (problem is not null)
            return null;
        // the discount, the penalty and the total are optional: they are used only when the tables have them (a decimal, double or float column; the days a whole number)
        var earlyPercent = terms.FindColumn(entry.EarlyPercent) is { IsFractionalColumn: true } ep ? ep : null;
        var earlyDays = terms.FindColumn(entry.EarlyDays) is { IsIntegerColumn: true } ed ? ed : null;
        var latePercent = terms.FindColumn(entry.LatePercent) is { IsFractionalColumn: true } lp ? lp : null;
        var total = documents.FindColumn(entry.Total) is { IsFractionalColumn: true, IsNullable: false } t ? t : null;
        return new DueDateShape(table, key, document!, termsColumn!, due!, date!, documents, documentKey, terms, termsKey, days!, earlyPercent, earlyPercent is null ? null : earlyDays, latePercent, total);
    }
}
