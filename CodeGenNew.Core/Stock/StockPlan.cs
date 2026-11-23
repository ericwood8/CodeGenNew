using CodeGenNew.Core.Api;
using CodeGenNew.Core.Rate;
using CodeGenNew.Core.Settings;
using CodeGenNew.Core.Tenant;

namespace CodeGenNew.Core.Stock;

public static class StockWords
{
    public const string InvalidType = ProblemTypes.Prefix + "stock-invalid";
    public const string InvalidTitle = "Not a valid stock request";
    public const string ShortType = ProblemTypes.Prefix + "insufficient-stock";
    public const string ShortTitle = "Not enough stock";
    public const string ClosedType = ProblemTypes.Prefix + "reservation-closed";
    public const string ClosedTitle = "Reservation closed";
    public const string UnknownType = ProblemTypes.Prefix + "stock-unknown";
    public const string UnknownTitle = "No such stock row";
    public const string ManagedType = ProblemTypes.Prefix + "stock-managed";
    public const string ManagedTitle = "Stock is kept by the ledger";
    public const string BelowReservedType = ProblemTypes.Prefix + "stock-below-reserved";
    public const string BelowReservedTitle = "Count below what is reserved";
    public const string ConflictType = ProblemTypes.Prefix + "stock-conflict";
    public const string ConflictTitle = "Stock changed";

    public const string NeedsItem = "Give the item (item=).";
    public const string ItemMissing = "The item does not exist.";
    public const string ItemUnknown = "There is no stock row for that item.";
    public const string QuantityPositive = "The quantity must be a whole number above zero.";
    public const string CountNeeded = "Give the counted quantity (counted), zero or more.";
    public const string KindBad = "The kind must be receipt or return.";
    public const string MinutesBad = "The minutes a hold lasts must be above zero.";
    public const string ShortStock = "There is not enough stock available.";
    public const string ReservationMissing = "The reservation does not exist.";
    public const string ReservationClosed = "The reservation is no longer held: it was committed, released or has expired.";
    public const string ReservationInconsistent = "The stock level does not hold that reservation.";
    public const string BelowReserved = "The count is below what is reserved.";
    public const string Conflict = "The stock changed while it was counted: count again.";
    public const string Managed = "The quantity on hand and the quantity reserved change through the stock routes (receive, reserve, commit, release, adjust), not by editing the level.";
    public const string InUse = "A stock level that still has stock or reservations cannot be deleted.";
}

/// <summary> The ledger of a stock table: every change of the level is a row of it. </summary>
public sealed record StockLedgerShape(TableModel Table, ColumnModel Key, ColumnModel Item, ColumnModel Kind, ColumnModel Quantity, ColumnModel Stamp, ColumnModel? Reference, ColumnModel? Expires);

/// <summary> The holds of a stock table: a quantity held for a buyer until it is committed, released or expires. </summary>
public sealed record StockReservationShape(TableModel Table, ColumnModel Key, ColumnModel Item, ColumnModel Quantity, ColumnModel Status, ColumnModel Created, ColumnModel Expires, ColumnModel? Resolved, ColumnModel? Reference);

/// <summary> <c>StockTables=Table[:ItemColumn[:OnHandColumn[:ReservedColumn[:LedgerTable[:ReservationTable[:ReorderColumn]]]]]]</c>: a stock level table (a row for each item with what is on hand and what is reserved), the append-only ledger that explains it
/// and the table of holds. The quantities change only through the routes of the ledger table (receive, reserve, commit, release, expire, adjust); each of them moves the level with one guarded update, so two buyers never take the last item. </summary>
public sealed record StockShape(TableModel Level, ColumnModel Key, ColumnModel Item, ColumnModel OnHand, ColumnModel Reserved, ColumnModel? Reorder, TableModel ItemTable, ColumnModel ItemKey, StockLedgerShape Ledger, StockReservationShape Reservation)
{
    /// <summary> The C# type of the quantities (<c>int</c> or <c>long</c>). </summary>
    public string QuantityType => OnHand.CSharpBase();
}

/// <summary> The shape of a ledger table of <c>StockTables</c>: the whole stock it belongs to. </summary>
public sealed record StockLedgerOf(StockShape Stock);

public static class StockPlan
{
    public const string DefaultItem = "ItemId";
    public const string DefaultOnHand = "OnHand";
    public const string DefaultReserved = "Reserved";
    public const string DefaultReorder = "ReorderLevel";
    public const string KindName = "Kind";
    public const string QuantityName = "Quantity";
    public const string ReferenceName = "Reference";
    public const string ExpiresName = "ExpiresOn";
    public const string StatusName = "Status";
    public const string ResolvedName = "ResolvedOn";

    public const string Receipt = "receipt";
    public const string Sale = "sale";
    public const string Return = "return";
    public const string Adjustment = "adjustment";
    public const string Reservation = "reservation";
    public const string Release = "release";

    public const string Held = "held";
    public const string Committed = "committed";
    public const string Released = "released";
    public const string Expired = "expired";

    public const string AvailableRoute = "available";
    public const string LowRoute = "low";
    public const string CheckRoute = "check";
    public const string ReceiveRoute = "receive";
    public const string ReserveRoute = "reserve";
    public const string CommitRoute = "commit";
    public const string ReleaseRoute = "release";
    public const string ExpireRoute = "expire";
    public const string AdjustRoute = "adjust";

    public sealed record Entry(string Level, string Item, string OnHand, string Reserved, string Ledger, string ReservationTable, string Reorder);

    public static IReadOnlyList<Entry> Entries(ProjectSettings project) =>
        SettingParts.Items(project.StockTables).Select(p =>
        {
            string stem = p[0].EndsWith("Level", StringComparison.OrdinalIgnoreCase) && p[0].Length > 5 ? p[0][..^5] : p[0];
            return new Entry(p[0], SettingParts.At(p, 1) ?? DefaultItem, SettingParts.At(p, 2) ?? DefaultOnHand, SettingParts.At(p, 3) ?? DefaultReserved, SettingParts.At(p, 4) ?? stem + "Ledger", SettingParts.At(p, 5) ?? stem + "Reservation",
                SettingParts.At(p, 6) ?? DefaultReorder);
        }).ToList();

    public static bool IsLevel(string table, ProjectSettings project) => Entries(project).Any(e => e.Level.EqualsIgnoreCase(table));

    /// <summary> Whether the column is the on hand or the reserved quantity of a stock level table: a test sends 0 for it and never changes it, since a save leaves both to the stock routes. </summary>
    public static bool IsManagedQuantity(string table, string column, ProjectSettings project) =>
        Entries(project).Any(e => e.Level.EqualsIgnoreCase(table) && (e.OnHand.EqualsIgnoreCase(column) || e.Reserved.EqualsIgnoreCase(column)));

    public static bool IsLedger(string table, ProjectSettings project) => Entries(project).Any(e => e.Ledger.EqualsIgnoreCase(table));

    public static bool IsReservation(string table, ProjectSettings project) => Entries(project).Any(e => e.ReservationTable.EqualsIgnoreCase(table));

    /// <summary> The ledger and the hold tables: both are read-only lists, newest first, so they join the log tables. </summary>
    public static IEnumerable<string> LogTableNames(ProjectSettings project) => Entries(project).SelectMany(e => new[] { e.Ledger, e.ReservationTable });

    /// <summary> The stock the level table belongs to (null when the table is not a listed level table or the entry has a problem). </summary>
    public static StockShape? ForLevel(TableModel table, ProjectSettings project, DatabaseModel database) =>
        IsLevel(table.TableName, project) && Resolve(Entries(project).First(e => e.Level.EqualsIgnoreCase(table.TableName)), project, database, out _) is { } shape ? shape : null;

    public static StockShape? ForLevel(TableModel table, ProjectSettings project) => RatePlan.Remembered(project) is { } d ? ForLevel(table, project, d) : null;

    /// <summary> The stock the ledger table belongs to: the ledger table is the one that carries the routes. </summary>
    public static StockShape? ForLedger(TableModel table, ProjectSettings project, DatabaseModel database) =>
        Entries(project).FirstOrDefault(e => e.Ledger.EqualsIgnoreCase(table.TableName)) is { } entry && Resolve(entry, project, database, out _) is { } shape ? shape : null;

    public static StockShape? ForLedger(TableModel table, ProjectSettings project) => RatePlan.Remembered(project) is { } d ? ForLedger(table, project, d) : null;

    public static StockShape? ForReservation(TableModel table, ProjectSettings project, DatabaseModel database) =>
        Entries(project).FirstOrDefault(e => e.ReservationTable.EqualsIgnoreCase(table.TableName)) is { } entry && Resolve(entry, project, database, out _) is { } shape ? shape : null;

    /// <summary> Whether the API frees the holds that have run out on its own: <c>StockSweepMinutes</c> is set and some stock table resolves. </summary>
    public static bool HasSweep(DatabaseModel database, ProjectSettings project) =>
        project.StockSweepMinutes > 0 && database.Tables.Any(t => ForLedger(t, project, database) is not null);

    /// <summary> The ledger tables whose holds the sweep frees, in table order. </summary>
    public static IEnumerable<TableModel> SweptLedgers(DatabaseModel database, ProjectSettings project) =>
        database.Tables.Where(t => ForLedger(t, project, database) is not null).OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase);

    public static List<string> Problems(DatabaseModel database, ProjectSettings project)
    {
        var problems = new List<string>();
        if (project.StockSweepMinutes > 0 && Entries(project).Count == 0)
            problems.Add("StockSweepMinutes needs StockTables: there are no holds to free.");
        foreach (var entry in Entries(project))
        {
            Resolve(entry, project, database, out string? problem);
            if (problem is not null)
                problems.Add(problem);
        }
        return problems;
    }

    /// <summary> One sentence for each stock level table that has no unique index on its item column: two requests at once could each add the first row of an item. </summary>
    public static List<string> MissingIndexes(DatabaseModel database, ProjectSettings project)
    {
        var missing = new List<string>();
        foreach (var table in database.Tables)
            if (ForLevel(table, project, database) is { } shape && !table.Indexes.Any(i => i.IsUnique && i.Columns.Count == 1 && i.Columns[0].EqualsIgnoreCase(shape.Item.DbName)))
                missing.Add($"{table.DbTableName} is a stock level table without a unique index on {shape.Item.DbName}: two requests at once can still add two rows for one item. Add a unique index on the column.");
        return missing;
    }

    /// <summary> The level, the ledger and the hold table with the columns the routes read, or null and the reason. </summary>
    public static StockShape? Resolve(Entry entry, ProjectSettings project, DatabaseModel database, out string? problem)
    {
        problem = null;
        string syntax = $"StockTables={entry.Level}[:ItemColumn[:OnHandColumn[:ReservedColumn[:LedgerTable[:ReservationTable[:ReorderColumn]]]]]]";
        TableModel? Find(string name) => database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(name));
        if (Find(entry.Level) is not { } level)
        {
            problem = $"StockTables: {entry.Level} is not a table of the database.";
            return null;
        }
        if (Find(entry.Ledger) is not { } ledger || Find(entry.ReservationTable) is not { } holds)
        {
            problem = $"StockTables: {level.TableName} needs a ledger table ({entry.Ledger}) and a table of holds ({entry.ReservationTable}); name them in the setting ({syntax}).";
            return null;
        }
        if (new[] { level, ledger, holds }.Select(t => t.TableName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3)
        {
            problem = $"StockTables: the level, the ledger and the holds must be three different tables ({syntax}).";
            return null;
        }
        if (level.PrimaryKeyColumns is not [{ IsIntegerColumn: true } levelKey] || ledger.PrimaryKeyColumns is not [{ IsIntegerColumn: true } ledgerKey] || holds.PrimaryKeyColumns is not [{ IsIntegerColumn: true } holdKey])
        {
            problem = $"StockTables: {level.TableName}, {ledger.TableName} and {holds.TableName} each need a single whole-number key.";
            return null;
        }
        var item = level.FindColumn(entry.Item);
        var onHand = level.FindColumn(entry.OnHand);
        var reserved = level.FindColumn(entry.Reserved);
        var reorder = level.FindColumn(entry.Reorder);
        if (item is not { IsIntegerColumn: true, IsNullable: false })
            problem = $"StockTables: {level.TableName}.{entry.Item} must be a whole-number column that cannot be NULL ({syntax}).";
        else if (onHand is not { IsIntegerColumn: true, IsNullable: false } || reserved is not { IsIntegerColumn: true, IsNullable: false } || onHand.CSharpBase() is not ("int" or "long") || onHand.CSharpBase() != reserved.CSharpBase())
            problem = $"StockTables: {level.TableName}.{entry.OnHand} and {entry.Reserved} must be int or bigint columns of one type that cannot be NULL.";
        else if (reorder is not null && (!reorder.IsIntegerColumn || reorder.IsNullable || reorder.CSharpBase() != onHand.CSharpBase()))
            reorder = null;
        if (problem is not null)
            return null;
        if (level.ForeignKeys.FirstOrDefault(f => f.ReferencingColumns.Count == 1 && f.ReferencingColumns[0].EqualsIgnoreCase(item!.Name)) is not { } fk || Find(fk.ReferencedTable) is not { } itemTable
            || itemTable.PrimaryKeyColumns is not [{ IsIntegerColumn: true } itemKey] || itemKey.CSharpBase() != item!.CSharpBase())
        {
            problem = $"StockTables: {level.TableName}.{entry.Item} must be a foreign key to the table of the items, with a whole-number key of the same type ({syntax}).";
            return null;
        }
        var ledgerItem = ledger.FindColumn(entry.Item);
        var kind = ledger.FindColumn(KindName);
        var ledgerQuantity = ledger.FindColumn(QuantityName);
        var stamp = LogTablePlan.Guess(ledger);
        var holdItem = holds.FindColumn(entry.Item);
        var holdQuantity = holds.FindColumn(QuantityName);
        var status = holds.FindColumn(StatusName);
        var expires = holds.FindColumn(ExpiresName);
        var created = LogTablePlan.Guess(holds);
        if (ledgerItem is not { IsIntegerColumn: true, IsNullable: false } || ledgerItem.CSharpBase() != item!.CSharpBase())
            problem = $"StockTables: {ledger.TableName} needs the item column {entry.Item} of the same type as the level's, that cannot be NULL.";
        else if (kind is not { IsStringColumn: true, IsNullable: false })
            problem = $"StockTables: {ledger.TableName}.{KindName} must be a text column that cannot be NULL (receipt, sale, return, adjustment, reservation or release).";
        else if (ledgerQuantity is not { IsIntegerColumn: true, IsNullable: false } || ledgerQuantity.CSharpBase() != onHand!.CSharpBase())
            problem = $"StockTables: {ledger.TableName}.{QuantityName} must be a column that cannot be NULL, of the same type as {level.TableName}.{entry.OnHand}.";
        else if (stamp is not { IsNullable: false } || stamp.CSharpBase() != "DateTime")
            problem = $"StockTables: {ledger.TableName} needs a date and time column that cannot be NULL (OccurredOn) for the time of a row.";
        else if (holdItem is not { IsIntegerColumn: true, IsNullable: false } || holdItem.CSharpBase() != item.CSharpBase())
            problem = $"StockTables: {holds.TableName} needs the item column {entry.Item} of the same type as the level's, that cannot be NULL.";
        else if (holdQuantity is not { IsIntegerColumn: true, IsNullable: false } || holdQuantity.CSharpBase() != onHand.CSharpBase())
            problem = $"StockTables: {holds.TableName}.{QuantityName} must be a column that cannot be NULL, of the same type as {level.TableName}.{entry.OnHand}.";
        else if (status is not { IsStringColumn: true, IsNullable: false })
            problem = $"StockTables: {holds.TableName}.{StatusName} must be a text column that cannot be NULL (held, committed, released or expired).";
        else if (expires is not { IsNullable: false } || expires.CSharpBase() != "DateTime" || created is not { IsNullable: false } || created.CSharpBase() != "DateTime" || created.Name.EqualsIgnoreCase(expires.Name))
            problem = $"StockTables: {holds.TableName} needs two date and time columns that cannot be NULL: when the hold was made (CreatedOn) and when it ends ({ExpiresName}).";
        else if (OwnedTable.OwnerColumn(level, project) is not null || OwnedTable.Through(level, project) is not null || TenantPlan.OwnedBy(level, project) is not null
            || OwnedTable.OwnerColumn(ledger, project) is not null || OwnedTable.OwnerColumn(holds, project) is not null || TenantPlan.OwnedBy(ledger, project) is not null || TenantPlan.OwnedBy(holds, project) is not null)
            problem = $"StockTables: {level.TableName} and its ledger and holds are limited to an owner or a tenant, which the stock routes do not read yet.";
        if (problem is not null)
            return null;
        var ledgerReference = ledger.FindColumn(ReferenceName) is { IsStringColumn: true } r1 ? r1 : null;
        var ledgerExpires = ledger.FindColumn(ExpiresName) is { IsNullable: true } e1 && e1.CSharpBase() == "DateTime" ? e1 : null;
        var holdReference = holds.FindColumn(ReferenceName) is { IsStringColumn: true } r2 ? r2 : null;
        var resolved = holds.FindColumn(ResolvedName) is { IsNullable: true } r3 && r3.CSharpBase() == "DateTime" ? r3 : null;
        return new StockShape(level, levelKey, item!, onHand!, reserved!, reorder, itemTable, itemKey,
            new StockLedgerShape(ledger, ledgerKey, ledgerItem!, kind!, ledgerQuantity!, stamp!, ledgerReference, ledgerExpires),
            new StockReservationShape(holds, holdKey, holdItem!, holdQuantity!, status!, created!, expires!, resolved, holdReference));
    }
}
