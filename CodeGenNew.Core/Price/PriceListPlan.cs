using CodeGenNew.Core.Api;
using CodeGenNew.Core.Party;
using CodeGenNew.Core.Rate;
using CodeGenNew.Core.Settings;
using CodeGenNew.Core.Tenant;

namespace CodeGenNew.Core.Price;

/// <summary> A price row of a price list (<c>PriceListTables=PriceListItem:PriceListId:ItemId:MinQuantity:Price:PriceList</c>): <c>List</c> names the price list, <c>Item</c> what is priced, <c>MinQuantity</c> the quantity the
/// price starts at and <c>Price</c> the price. <c>ListTable</c> holds the lists: <c>Currency</c>, the first day <c>ValidFrom</c>, the last day <c>ValidTo</c> (NULL: open) and the optional <c>Group</c> of customers (used only when the entry names it). </summary>
public sealed record PriceListShape(TableModel Table, ColumnModel Key, ColumnModel List, ColumnModel Item, ColumnModel MinQuantity, ColumnModel Price, TableModel ListTable, ColumnModel ListKey, ColumnModel Currency,
    ColumnModel ValidFrom, ColumnModel ValidTo, ColumnModel? Group, ColumnModel? Owner = null, ColumnModel? ListOwner = null)
{
    /// <summary> Whether the price rows are limited to an owner (<c>OwnedTables</c>, on the table or through its list) or a tenant. </summary>
    public bool IsLimited { get; init; }
}

public static class PriceWords
{
    public const string InvalidType = ProblemTypes.Prefix + "price-invalid";
    public const string InvalidTitle = "Not a valid price";
    public const string TakenType = ProblemTypes.Prefix + "price-taken";
    public const string TakenTitle = "Price taken";
    public const string MissingType = ProblemTypes.Prefix + "price-missing";
    public const string MissingTitle = "No price";

    public const string ListMissing = "The price list does not exist.";
    public const string QuantityNotPositive = "The minimum quantity must be greater than zero.";
    public const string PriceNegative = "A price cannot be negative.";
    public const string Taken = "Another row of this price list already holds a price for this item from this quantity.";
    public const string NeedsLookup = "Give the item and the currency (item= and currency=).";
    public const string AskedQuantity = "The quantity must be greater than zero.";

    /// <summary> The answer for no price: <c>{0}</c> is the item and <c>{1}</c> the currency. </summary>
    public const string MissingFormat = "There is no price for item {0} in {1} on that day for that quantity.";

    public static string Missing(object item, string currency) => string.Format(MissingFormat, item, currency);
}

/// <summary> <c>PriceListTables=PriceListItem:PriceListId:ItemId:MinQuantity:Price:PriceList[:CurrencyCode[:ValidFrom[:ValidTo[:CustomerGroup]]]]</c> (the columns default to the names shown): a save is refused when the minimum
/// quantity is not above zero or the price is negative (400) and when another row of the same list holds a price for the item from the same quantity (409). <c>GET price?item=&amp;quantity=&amp;on=&amp;currency=[&amp;group=]</c>
/// answers the row in force: of the lists in the currency that are valid on the day (the day defaults to today, the quantity to 1), the row of the item with the largest minimum quantity not above the quantity. With a group
/// the lists of that group come first and the lists with no group are the fallback; with none only the lists with no group are read. The price lists of one currency and group may not overlap in time (the list table is added to
/// <c>EffectiveDatedTables</c> with the currency and the group as its group columns, so the stacks' existing period rule keeps them apart). </summary>
public static class PriceListPlan
{
    public const string DefaultList = "PriceListId";
    public const string DefaultItem = "ItemId";
    public const string DefaultMinQuantity = "MinQuantity";
    public const string DefaultPrice = "Price";
    public const string DefaultListTable = "PriceList";
    public const string DefaultCurrency = "CurrencyCode";
    public const string DefaultValidFrom = "ValidFrom";
    public const string DefaultValidTo = "ValidTo";
    public const string DefaultGroup = "CustomerGroup";

    public sealed record Entry(string Table, string List, string Item, string MinQuantity, string Price, string ListTable, string? Currency, string? ValidFrom, string? ValidTo, string? Group);

    public static IReadOnlyList<Entry> Entries(ProjectSettings project) =>
        SettingParts.Items(project.PriceListTables).Select(p => new Entry(p[0], SettingParts.At(p, 1) ?? DefaultList, SettingParts.At(p, 2) ?? DefaultItem, SettingParts.At(p, 3) ?? DefaultMinQuantity, SettingParts.At(p, 4) ?? DefaultPrice,
            SettingParts.At(p, 5) ?? DefaultListTable, SettingParts.At(p, 6), SettingParts.At(p, 7), SettingParts.At(p, 8), SettingParts.At(p, 9))).ToList();

    public static bool IsListed(string table, ProjectSettings project) => Entries(project).Any(e => e.Table.EqualsIgnoreCase(table));

    /// <summary> The plan of the price table that points at <paramref name="table"/> as its table of lists, or null when the table is not one. </summary>
    public static PriceListShape? HeadOf(TableModel table, ProjectSettings project, DatabaseModel database) =>
        Entries(project).Where(e => e.ListTable.EqualsIgnoreCase(table.TableName))
            .Select(e => database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(e.Table))).OfType<TableModel>()
            .Select(prices => For(prices, project, database)).OfType<PriceListShape>().FirstOrDefault();

    public static PriceListShape? For(TableModel table, ProjectSettings project, DatabaseModel database) =>
        IsListed(table.TableName, project) && Resolve(table, project, database, out _) is { } shape ? shape : null;

    public static PriceListShape? For(TableModel table, ProjectSettings project) =>
        RatePlan.Remembered(project) is { } database ? For(table, project, database) : null;

    /// <summary> The <c>EffectiveDatedTables</c> items the entries imply: a price list table with a period keeps the periods of one currency (and customer group) apart, and of one owner when the list table has an owner
    /// column of its own. A table the project lists itself is left alone. A list table owned only through a parent gets no implied rule (no column of its own says the owner). </summary>
    public static IEnumerable<string> EffectiveItems(ProjectSettings project) =>
        Entries(project).Where(e => e.ListTable.Length > 0 && !OwnedOnlyThroughParent(e, project)).Select(e => $"{e.ListTable}:{e.ValidFrom ?? DefaultValidFrom}:{e.ValidTo ?? DefaultValidTo}:{e.Currency ?? DefaultCurrency}"
            + (e.Group is null ? "" : "+" + e.Group) + (OwnerColumnOf(e, project) is { } owner ? "+" + owner.Name : ""));

    private static TableModel? ListTableOf(Entry entry, ProjectSettings project) =>
        RatePlan.Remembered(project)?.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(entry.ListTable));

    private static ColumnModel? OwnerColumnOf(Entry entry, ProjectSettings project) => ListTableOf(entry, project) is { } lists ? OwnedTable.OwnerColumn(lists, project) : null;

    private static bool OwnedOnlyThroughParent(Entry entry, ProjectSettings project) =>
        ListTableOf(entry, project) is { } lists && OwnerColumnOf(entry, project) is null && OwnedTable.Through(lists, project) is not null;

    public static List<string> Problems(DatabaseModel database, ProjectSettings project)
    {
        var problems = new List<string>();
        foreach (var entry in Entries(project))
        {
            if (database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(entry.Table)) is not { } table)
                problems.Add($"PriceListTables: {entry.Table} is not a table of the database.");
            else
            {
                Resolve(table, project, database, out string? problem);
                if (problem is not null)
                    problems.Add(problem);
            }
        }
        return problems;
    }

    /// <summary> One sentence for each price table with no unique index on the list, the item and the minimum quantity: the rule is a read and then a save, so only the database stops two saves at the same moment. </summary>
    public static List<string> MissingIndexes(DatabaseModel database, ProjectSettings project)
    {
        var missing = new List<string>();
        foreach (var table in database.Tables)
        {
            if (For(table, project, database) is not { } shape)
                continue;
            string[] columns = [shape.List.DbName, shape.Item.DbName, shape.MinQuantity.DbName];
            if (!table.Indexes.Any(i => i.IsUnique && i.Columns.Count == columns.Length && columns.All(c => i.Columns.Any(x => x.EqualsIgnoreCase(c)))))
                missing.Add($"{table.DbTableName} holds prices without a unique index on {columns[0]}, {columns[1]} and {columns[2]}: two requests at once can still store the same price twice. Add a unique index on the three columns.");
        }
        return missing;
    }

    /// <summary> The unique indexes on the list, the item and the minimum quantity of the price tables of the run the generator remembered. A save one of them refuses repeats a price, so the API answers it as the rule does. </summary>
    public static IReadOnlyList<PartyPlan.PairIndex> PairIndexes(ProjectSettings project)
    {
        if (RatePlan.Remembered(project) is not { } database)
            return [];
        var found = new List<PartyPlan.PairIndex>();
        foreach (var table in database.Tables)
        {
            if (For(table, project, database) is not { } shape)
                continue;
            string[] columns = [shape.List.DbName, shape.Item.DbName, shape.MinQuantity.DbName];
            if (table.Indexes.FirstOrDefault(i => i.IsUnique && i.Columns.Count == columns.Length && columns.All(c => i.Columns.Any(x => x.EqualsIgnoreCase(c)))) is { } index)
                found.Add(new PartyPlan.PairIndex(table.DbTableName, index.Name, index.Columns));
        }
        return found;
    }

    private static PriceListShape? Resolve(TableModel table, ProjectSettings project, DatabaseModel database, out string? problem)
    {
        problem = null;
        var entry = Entries(project).First(e => e.Table.EqualsIgnoreCase(table.TableName));
        string prefix = $"PriceListTables: {table.TableName}";
        string syntax = $"PriceListTables={table.TableName}:ListColumn:ItemColumn:MinQuantityColumn:PriceColumn:ListTable[:CurrencyColumn[:ValidFromColumn[:ValidToColumn[:GroupColumn]]]]";
        if (RowShapeWords.NeverChanged(table, project) is { } never)
        {
            problem = $"{prefix} {never}, so it has no prices to keep apart.";
            return null;
        }
        if (database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(entry.ListTable)) is not { } lists)
        {
            problem = $"{prefix}: {entry.ListTable} is not a table of the database ({syntax}).";
            return null;
        }
        if (lists.TableName.EqualsIgnoreCase(table.TableName))
        {
            problem = $"{prefix}: the price list table must be another table.";
            return null;
        }
        if (table.PrimaryKeyColumns is not [{ IsIntegerColumn: true } key])
        {
            problem = $"{prefix} needs a single whole-number key.";
            return null;
        }
        if (lists.PrimaryKeyColumns is not [{ IsIntegerColumn: true } listKey])
        {
            problem = $"{prefix}: {lists.TableName} needs a single whole-number key.";
            return null;
        }
        if (TenantPlan.IsTenantTable(table, project) || TenantPlan.IsTenantTable(lists, project))
        {
            problem = $"{prefix}: the tenant table cannot hold prices or lists.";
            return null;
        }
        var priceOwner = OwnedTable.OwnerColumn(table, project);
        var through = OwnedTable.Through(table, project);
        bool priceLimited = priceOwner is not null || through is not null || TenantPlan.OwnedBy(table, project) is not null;
        var listOwner = OwnedTable.OwnerColumn(lists, project) ?? (through is not null && through.ParentTable.EqualsIgnoreCase(lists.TableName) ? lists.FindColumn(through.ParentOwner) : null);
        bool listLimited = listOwner is not null || OwnedTable.Through(lists, project) is not null || TenantPlan.OwnedBy(lists, project) is not null;
        if (listLimited && !priceLimited)
        {
            problem = $"{prefix}: {lists.TableName} is limited to an owner or a tenant, so the price rows must be limited the same way (list the table in OwnedTables, or give it the tenant column).";
            return null;
        }
        if (through is not null && !through.ParentTable.EqualsIgnoreCase(lists.TableName))
        {
            problem = $"{prefix}: its owner comes through {through.ParentTable}, but the price rows must hang off {lists.TableName}.";
            return null;
        }
        var list = table.FindColumn(entry.List);
        var item = table.FindColumn(entry.Item);
        var min = table.FindColumn(entry.MinQuantity);
        var price = table.FindColumn(entry.Price);
        if (list is not { IsIntegerColumn: true, IsNullable: false } || list.CSharpBase() != listKey.CSharpBase())
        {
            problem = $"{prefix}: {entry.List} must be a whole-number column that cannot be NULL, of the same type as the key of {lists.TableName} ({syntax}).";
            return null;
        }
        if (item is not { IsIntegerColumn: true, IsNullable: false })
        {
            problem = $"{prefix}: {entry.Item} must be a whole-number column that cannot be NULL (the item that is priced).";
            return null;
        }
        if (min is not { IsNullable: false } || !(min.IsNumericColumn || min.IsIntegerColumn))
        {
            problem = $"{prefix}: {entry.MinQuantity} must be a whole-number or number column that cannot be NULL.";
            return null;
        }
        if (price is not { IsNullable: false } || !price.IsFractionalColumn)
        {
            problem = $"{prefix}: {entry.Price} must be a decimal, double or float column that cannot be NULL.";
            return null;
        }
        if (new[] { list.Name, item.Name, min.Name, price.Name }.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4)
        {
            problem = $"{prefix}: the list, item, minimum quantity and price must be four different columns.";
            return null;
        }
        var currency = lists.FindColumn(entry.Currency ?? DefaultCurrency);
        var validFrom = lists.FindColumn(entry.ValidFrom ?? DefaultValidFrom);
        var validTo = lists.FindColumn(entry.ValidTo ?? DefaultValidTo);
        var group = entry.Group is null ? null : lists.FindColumn(entry.Group);
        if (currency is not { IsStringColumn: true, IsNullable: false })
        {
            problem = $"{prefix}: {lists.TableName}.{entry.Currency ?? DefaultCurrency} must be a text column that cannot be NULL (the currency of the list).";
            return null;
        }
        if (validFrom is not { IsNullable: false, IsDateOnlyColumn: true })
        {
            problem = $"{prefix}: {lists.TableName}.{entry.ValidFrom ?? DefaultValidFrom} must be a date column (no time) that cannot be NULL (the first day of the list).";
            return null;
        }
        if (validTo is not { IsNullable: true, IsDateOnlyColumn: true })
        {
            problem = $"{prefix}: {lists.TableName}.{entry.ValidTo ?? DefaultValidTo} must be a date column (no time) that allows NULL (the last day of the list; empty means it has no end).";
            return null;
        }
        if (entry.Group is not null && group is not { IsStringColumn: true })
        {
            problem = $"{prefix}: {lists.TableName}.{entry.Group} must be a text column (the customer group of the list).";
            return null;
        }
        return new PriceListShape(table, key, list, item, min, price, lists, listKey, currency, validFrom, validTo, group, priceOwner, listOwner) { IsLimited = priceLimited };
    }
}
