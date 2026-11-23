using CodeGenNew.Core.Api;
using CodeGenNew.Core.Naming;

namespace CodeGenNew.Core.Payment;

/// <summary> What the generated ledger tests of the three APIs send (<see cref="CodeGenNew.Core.Api.ApiLogTests"/>, PY_TestLedger, RS_TestLedger): a document row and a ledger row with an example value for every column that must
/// have one. The tests fill the keys, the total, the kind, the status, the amount, the gateway id and the idempotency key themselves. A ledger keeps its rows (it is never changed or deleted), so the tests leave what they add in
/// a scratch database. </summary>
public sealed record LedgerTestPlan(LedgerShape Shape, string DocumentBody, string RowBody, string DocumentRoute, string RowRoute)
{
    /// <summary> The plan of the first ledger the tests can fill, or null: no column that must have a value, other than the ones the tests fill, is a foreign key or in a unique index or has no example value. </summary>
    public static LedgerTestPlan? For(DatabaseModel database, ProjectSettings project)
    {
        foreach (var table in database.Tables.OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase))
        {
            if (LedgerPlan.For(table, project, database) is not { } shape || !shape.Key.IsIdentity || !shape.DocumentKey.IsIdentity)
                continue;
            if (DocumentBodyOf(shape, project) is { } document && RowBodyOf(shape, project) is { } row)
                return new LedgerTestPlan(shape, document, row, ApiRequests.Route(shape.DocumentTable), ApiRequests.Route(shape.Table));
        }
        return null;
    }

    /// <summary> The plan for one table, from the database the generator remembered, or null when the table is not a ledger the tests can fill. </summary>
    public static LedgerTestPlan? ForTable(string tableName, ProjectSettings project) =>
        Rate.RatePlan.Remembered(project) is { } database && For(database, project) is { } plan && plan.Shape.Table.TableName.EqualsIgnoreCase(tableName) ? plan : null;

    private static string? DocumentBodyOf(LedgerShape shape, ProjectSettings project)
    {
        var parts = new List<string>();
        foreach (var c in ApiSampleValues.BodyColumns(shape.DocumentTable))
        {
            string value;
            if (c == shape.DocumentKey)
                value = "0";
            else if (c == shape.Total)
                value = "0";
            else if (c.IsInUniqueIndex || shape.DocumentTable.ForeignKeys.Any(f => f.ReferencingColumns.Any(r => r.EqualsIgnoreCase(c.Name))))
                return null;
            else
                value = c.ValidExample(project, shape.DocumentTable.TableName);
            if (value == "null" && !c.IsNullable)
                return null;
            parts.Add(ApiSampleValues.Json(JsonNames.Camel(c.Name)) + ": " + value);
        }
        return "{ " + string.Join(", ", parts) + " }";
    }

    private static string? RowBodyOf(LedgerShape shape, ProjectSettings project)
    {
        var parts = new List<string>();
        foreach (var c in ApiSampleValues.BodyColumns(shape.Table))
        {
            string value;
            if (c == shape.Key || c == shape.Document || c == shape.Amount)
                value = "0";
            else if (c == shape.Kind || c == shape.Status || c == shape.GatewayId || c == shape.IdempotencyKey)
                value = "\"\"";
            else if (c.IsInUniqueIndex || shape.Table.ForeignKeys.Any(f => f.ReferencingColumns.Any(r => r.EqualsIgnoreCase(c.Name))))
                return null;
            else
                value = c.ValidExample(project, shape.Table.TableName);
            if (value == "null" && !c.IsNullable)
                return null;
            parts.Add(ApiSampleValues.Json(JsonNames.Camel(c.Name)) + ": " + value);
        }
        return "{ " + string.Join(", ", parts) + " }";
    }

    public string DocumentKeyJson => JsonNames.Camel(Shape.DocumentKey.Name);
    public string TotalJson => JsonNames.Camel(Shape.Total.Name);
    public string KeyJson => JsonNames.Camel(Shape.Key.Name);
    public string DocumentJson => JsonNames.Camel(Shape.Document.Name);
    public string KindJson => JsonNames.Camel(Shape.Kind.Name);
    public string StatusJson => JsonNames.Camel(Shape.Status.Name);
    public string AmountJson => JsonNames.Camel(Shape.Amount.Name);
    public string GatewayJson => JsonNames.Camel(Shape.GatewayId.Name);
    public string IdempotencyJson => JsonNames.Camel(Shape.IdempotencyKey.Name);
}
