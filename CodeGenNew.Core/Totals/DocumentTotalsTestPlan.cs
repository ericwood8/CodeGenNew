using CodeGenNew.Core.Api;
using CodeGenNew.Core.Naming;
using CodeGenNew.Core.Tenant;

namespace CodeGenNew.Core.Totals;

/// <summary> What the generated totals tests of the Python and Rust APIs (PY_TestTotals, RS_TestTotals) send: a header row with every total at zero and a line row with an example value for every column that must
/// have one. The tests fill the header's key, the line's amount and the key that points at the header themselves, and take the rows they add out again. </summary>
public sealed record DocumentTotalsTestPlan(DocumentTotalsShape Shape, string HeaderBody, string LineBody)
{
    public static DocumentTotalsTestPlan? For(DatabaseModel database, ProjectSettings project)
    {
        foreach (var table in database.Tables.OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase))
        {
            if (DocumentTotalsPlan.For(table, project, database) is not { Columns.Lines: not null } s || !table.PrimaryKeyColumns.All(c => c.IsIdentity) || !s.Lines.PrimaryKeyColumns.All(c => c.IsIdentity)
                || OwnedTable.IsScoped(table, project) || TenantPlan.OwnedBy(table, project) is not null || OwnedTable.IsScoped(s.Lines, project) || TenantPlan.OwnedBy(s.Lines, project) is not null)
                continue;
            if (MakeHeaderBody(s, project) is { } header && MakeLineBody(s, project) is { } line)
                return new DocumentTotalsTestPlan(s, header, line);
        }
        return null;
    }

    private static bool IsKey(TableModel table, ColumnModel c) => table.ForeignKeys.Any(f => f.ReferencingColumns.Any(r => r.EqualsIgnoreCase(c.Name)));

    public static string? MakeHeaderBody(DocumentTotalsShape s, ProjectSettings project)
    {
        var totals = s.Columns.Owned().Select(p => p.Column).Append(s.Columns.Prepaid).OfType<ColumnModel>().ToList();
        var parts = new List<string>();
        foreach (var c in ApiSampleValues.BodyColumns(s.Header))
        {
            string value;
            if (c.IsPrimaryKey)
                value = "0";
            else if (totals.Contains(c))
                value = "0";
            else if (c.IsInUniqueIndex || IsKey(s.Header, c))
                return null;
            else
            {
                value = c.ValidExample(project, s.Header.TableName);
                if (value == "null" && !c.IsNullable)
                    return null;
            }
            parts.Add(ApiSampleValues.Json(JsonNames.Camel(c.Name)) + ": " + value);
        }
        return "{ " + string.Join(", ", parts) + " }";
    }

    public static string? MakeLineBody(DocumentTotalsShape s, ProjectSettings project)
    {
        var parts = new List<string>();
        foreach (var c in ApiSampleValues.BodyColumns(s.Lines))
        {
            string value;
            if (c.IsPrimaryKey || c == s.LineAmount)
                value = "0";
            else if (c == s.LineKey)
                value = "1";
            else if (IsKey(s.Lines, c) && c.IsNullable)
                value = "null";
            else if (c.IsInUniqueIndex || IsKey(s.Lines, c))
                return null;
            else
            {
                value = c.ValidExample(project, s.Lines.TableName);
                if (value == "null" && !c.IsNullable)
                    return null;
            }
            parts.Add(ApiSampleValues.Json(JsonNames.Camel(c.Name)) + ": " + value);
        }
        return "{ " + string.Join(", ", parts) + " }";
    }

    /// <summary> Whether the payable amount can be tested against the prepaid amount: it needs both columns and no rounding rule, which would move the payable amount to a multiple. </summary>
    public bool TestsPrepaid => Shape.Columns.Prepaid is not null && Shape.Columns.Payable is not null && Shape.Rounds.Count == 0;
}
