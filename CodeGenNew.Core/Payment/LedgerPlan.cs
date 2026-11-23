using CodeGenNew.Core.Rate;
using CodeGenNew.Core.Settings;
using CodeGenNew.Core.Tenant;

namespace CodeGenNew.Core.Payment;

/// <summary> <c>LedgerTables=Table:DocumentColumn:DocumentTable:TotalColumn[:KindColumn[:StatusColumn[:AmountColumn[:GatewayIdColumn[:IdempotencyKeyColumn]]]]]</c>: an append-only payment transaction ledger.
/// A row is recorded with <c>POST record</c> and never changed or deleted (the table is also a log table, so it has the read-only, filter-first screens); a refund is a new negative row; the gateway id and the
/// idempotency key are each stored once; <c>GET paid</c> answers what a document has been paid, worked out from the rows. </summary>
public sealed record LedgerShape(TableModel Table, ColumnModel Key, ColumnModel Document, ColumnModel Kind, ColumnModel Status, ColumnModel Amount, ColumnModel GatewayId, ColumnModel IdempotencyKey, TableModel DocumentTable, ColumnModel DocumentKey, ColumnModel Total);

public static class LedgerPlan
{
    public const string DefaultKind = "Kind";
    public const string DefaultStatus = "Status";
    public const string DefaultAmount = "Amount";
    public const string DefaultGatewayId = "GatewayId";
    public const string DefaultIdempotencyKey = "IdempotencyKey";

    public const string Authorize = "authorize";
    public const string Capture = "capture";
    public const string Refund = "refund";
    public const string Void = "void";
    public const string Succeeded = "succeeded";
    public const string Pending = "pending";
    public const string Failed = "failed";

    public const string RecordRoute = "record";
    public const string PaidRoute = "paid";

    public sealed record Entry(string Table, string Document, string DocumentTable, string Total, string Kind, string Status, string Amount, string GatewayId, string IdempotencyKey);

    public static IReadOnlyList<Entry> Entries(ProjectSettings project) =>
        SettingParts.Items(project.LedgerTables).Select(p => new Entry(p[0], SettingParts.At(p, 1) ?? "", SettingParts.At(p, 2) ?? "", SettingParts.At(p, 3) ?? "", SettingParts.At(p, 4) ?? DefaultKind, SettingParts.At(p, 5) ?? DefaultStatus,
            SettingParts.At(p, 6) ?? DefaultAmount, SettingParts.At(p, 7) ?? DefaultGatewayId, SettingParts.At(p, 8) ?? DefaultIdempotencyKey)).ToList();

    public static bool IsListed(string table, ProjectSettings project) => Entries(project).Any(e => e.Table.EqualsIgnoreCase(table));

    public static LedgerShape? For(TableModel table, ProjectSettings project, DatabaseModel database) =>
        IsListed(table.TableName, project) && Resolve(table, project, database, out _) is { } shape ? shape : null;

    public static LedgerShape? For(TableModel table, ProjectSettings project) => RatePlan.Remembered(project) is { } d ? For(table, project, d) : null;

    public static List<string> Problems(DatabaseModel database, ProjectSettings project)
    {
        var problems = new List<string>();
        foreach (var entry in Entries(project))
        {
            if (database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(entry.Table)) is not { } table)
                problems.Add($"LedgerTables: {entry.Table} is not a table of the database.");
            else
            {
                Resolve(table, project, database, out string? problem);
                if (problem is not null)
                    problems.Add(problem);
            }
        }
        return problems;
    }

    /// <summary> One sentence for each ledger that has no unique index on its gateway id or on its idempotency key: two requests at once could record the same call twice. </summary>
    public static List<string> MissingIndexes(DatabaseModel database, ProjectSettings project)
    {
        var missing = new List<string>();
        foreach (var table in database.Tables)
        {
            if (For(table, project, database) is not { } shape)
                continue;
            foreach (var column in new[] { shape.GatewayId, shape.IdempotencyKey })
                if (!table.Indexes.Any(i => i.IsUnique && i.Columns.Count == 1 && i.Columns[0].EqualsIgnoreCase(column.DbName)))
                    missing.Add($"{table.DbTableName} is a payment ledger without a unique index on {column.DbName}: two requests at once can still record the same gateway call twice. Add a unique index on the column.");
        }
        return missing;
    }

    private static LedgerShape? Resolve(TableModel table, ProjectSettings project, DatabaseModel database, out string? problem)
    {
        problem = PaymentPlan.CardProblem(table, "LedgerTables");
        if (problem is not null)
            return null;
        var entry = Entries(project).First(e => e.Table.EqualsIgnoreCase(table.TableName));
        string syntax = $"LedgerTables={table.TableName}:DocumentColumn:DocumentTable:TotalColumn[:KindColumn[:StatusColumn[:AmountColumn[:GatewayIdColumn[:IdempotencyKeyColumn]]]]]";
        if (entry.Document.Length == 0 || entry.DocumentTable.Length == 0 || entry.Total.Length == 0)
        {
            problem = $"LedgerTables: {table.TableName} needs the document column, the document table and its total column ({syntax}).";
            return null;
        }
        if (database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(entry.DocumentTable)) is not { } documents)
        {
            problem = $"LedgerTables: {entry.DocumentTable} is not a table of the database ({syntax}).";
            return null;
        }
        if (table.PrimaryKeyColumns is not [{ IsIntegerColumn: true } key] || documents.PrimaryKeyColumns is not [{ IsIntegerColumn: true } documentKey])
        {
            problem = $"LedgerTables: {table.TableName} and {documents.TableName} each need a single whole-number key.";
            return null;
        }
        var document = table.FindColumn(entry.Document);
        var kind = table.FindColumn(entry.Kind);
        var status = table.FindColumn(entry.Status);
        var amount = table.FindColumn(entry.Amount);
        var gateway = table.FindColumn(entry.GatewayId);
        var idempotency = table.FindColumn(entry.IdempotencyKey);
        var total = documents.FindColumn(entry.Total);
        if (document is not { IsIntegerColumn: true, IsNullable: false } || document.CSharpBase() != documentKey.CSharpBase())
            problem = $"LedgerTables: {entry.Document} must be a whole-number column that cannot be NULL, of the same type as the key of {documents.TableName} ({syntax}).";
        else if (kind is not { IsStringColumn: true, IsNullable: false })
            problem = $"LedgerTables: {entry.Kind} must be a text column that cannot be NULL (authorize, capture, refund or void).";
        else if (status is not { IsStringColumn: true, IsNullable: false })
            problem = $"LedgerTables: {entry.Status} must be a text column that cannot be NULL (pending, succeeded or failed).";
        else if (amount is not { IsNullable: false } || !amount.IsFractionalColumn)
            problem = $"LedgerTables: {entry.Amount} must be a decimal, double or float column that cannot be NULL.";
        else if (gateway is not { IsStringColumn: true, IsNullable: false } || idempotency is not { IsStringColumn: true, IsNullable: false })
            problem = $"LedgerTables: {entry.GatewayId} and {entry.IdempotencyKey} must be text columns that cannot be NULL.";
        else if (total is not { IsNullable: false } || !total.IsFractionalColumn)
            problem = $"LedgerTables: {documents.TableName}.{entry.Total} must be a decimal, double or float column that cannot be NULL.";
        else if (amount.CSharpBase() != total.CSharpBase())
            problem = $"LedgerTables: {entry.Amount} and {documents.TableName}.{entry.Total} must have the same type.";
        else if (OwnedTable.OwnerColumn(table, project) is not null || OwnedTable.Through(table, project) is not null || TenantPlan.OwnedBy(table, project) is not null)
            problem = $"LedgerTables: {table.TableName} is limited to an owner or a tenant, which the record and paid routes do not read yet.";
        else if (new[] { document.Name, kind.Name, status.Name, amount.Name, gateway.Name, idempotency.Name }.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 6)
            problem = $"LedgerTables: the document, kind, status, amount, gateway id and idempotency key must be six different columns of {table.TableName}.";
        if (problem is not null)
            return null;
        return new LedgerShape(table, key, document!, kind!, status!, amount!, gateway!, idempotency!, documents, documentKey, total!);
    }
}
