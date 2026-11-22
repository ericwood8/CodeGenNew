using CodeGenNew.Core.Api;
using CodeGenNew.Core.Naming;
using CodeGenNew.Core.Tenant;

namespace CodeGenNew.Core.Allowance;

/// <summary> What the generated allowance tests of the Python and Rust APIs (PY_TestAllowance, RS_TestAllowance) send: a row of the table with an example value for every column that must have one. The tests fill
/// the flag, the base, the percentage, the amount and the document themselves and take the rows they add out again. </summary>
public sealed record AllowanceTestPlan(AllowanceChargeShape Shape, string Body)
{
    public static AllowanceTestPlan? For(DatabaseModel database, ProjectSettings project)
    {
        foreach (var table in database.Tables.OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase))
        {
            if (AllowanceChargePlan.For(table, project) is not { } s || !table.PrimaryKeyColumns.All(c => c.IsIdentity) || OwnedTable.IsScoped(table, project) || TenantPlan.OwnedBy(table, project) is not null)
                continue;
            if (MakeBody(s, project) is { } body)
                return new AllowanceTestPlan(s, body);
        }
        return null;
    }

    /// <summary> The row as JSON, or null when a column needs a value this plan cannot make up (a unique column, a foreign key). </summary>
    public static string? MakeBody(AllowanceChargeShape s, ProjectSettings project)
    {
        var parts = new List<string>();
        bool IsKey(ColumnModel c) => s.Table.ForeignKeys.Any(f => f.ReferencingColumns.Any(r => r.EqualsIgnoreCase(c.Name)));
        foreach (var c in ApiSampleValues.BodyColumns(s.Table))
        {
            string value;
            if (c.IsPrimaryKey)
                value = "0";
            else if (c == s.Flag)
                value = "false";
            else if (c == s.Base || c == s.Percent || c == s.Amount)
                value = "0";
            else if ((c == s.Document || c == s.Line) && !IsKey(c))
                value = "1";
            else if (c.IsInUniqueIndex || IsKey(c))
                return null;
            else
            {
                value = c.ValidExample(project, s.Table.TableName);
                if (value == "null" && !c.IsNullable)
                    return null;
            }
            parts.Add(ApiSampleValues.Json(JsonNames.Camel(c.Name)) + ": " + value);
        }
        return "{ " + string.Join(", ", parts) + " }";
    }
}
