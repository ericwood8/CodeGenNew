using CodeGenNew.Core.Api;
using CodeGenNew.Core.Naming;

namespace CodeGenNew.Core.Stock;

/// <summary> What the generated stock tests of the Python and Rust APIs send (PY_TestStock, RS_TestStock): an item row with an example value for every column that must have one, and the routes. The tests add an item of their own
/// for each case and leave it in the scratch database, since a ledger is never deleted. </summary>
public sealed record StockTestPlan(StockShape Shape, string ItemBody, string ItemRoute, string LedgerRoute, string ItemKeyJson)
{
    /// <summary> The plan of the first stock table the tests can fill, or null: an item column that must have a value, other than the key, is a foreign key, in a unique index or without an example. </summary>
    public static StockTestPlan? For(DatabaseModel database, ProjectSettings project)
    {
        foreach (var table in database.Tables.OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase))
        {
            if (StockPlan.ForLevel(table, project, database) is not { } shape || !shape.ItemKey.IsIdentity || !shape.Ledger.Key.IsIdentity)
                continue;
            if (ItemBodyOf(shape, project) is { } body)
                return new StockTestPlan(shape, body, ApiRequests.Route(shape.ItemTable), ApiRequests.Route(shape.Ledger.Table), JsonNames.Camel(shape.ItemKey.Name));
        }
        return null;
    }

    private static string? ItemBodyOf(StockShape shape, ProjectSettings project)
    {
        var parts = new List<string>();
        foreach (var c in ApiSampleValues.BodyColumns(shape.ItemTable))
        {
            string value;
            if (c == shape.ItemKey)
                value = "0";
            else if (c.IsInUniqueIndex || shape.ItemTable.ForeignKeys.Any(f => f.ReferencingColumns.Any(r => r.EqualsIgnoreCase(c.Name))))
                return null;
            else
                value = c.ValidExample(project, shape.ItemTable.TableName);
            if (value == "null" && !c.IsNullable)
                return null;
            parts.Add(ApiSampleValues.Json(JsonNames.Camel(c.Name)) + ": " + value);
        }
        return "{ " + string.Join(", ", parts) + " }";
    }

    public string LedgerKeyJson => JsonNames.Camel(Shape.Ledger.Key.Name);
    public string LedgerItemJson => JsonNames.Camel(Shape.Ledger.Item.Name);
}
