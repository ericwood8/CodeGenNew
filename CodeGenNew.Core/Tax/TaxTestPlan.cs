using CodeGenNew.Core.Api;
using CodeGenNew.Core.Naming;





namespace CodeGenNew.Core.Tax;

/// <summary> What the generated tax tests of the Python and Rust APIs (PY_TestTax, RS_TestTax) send: a category row and a line row with an example value for every column that must have one. The tests fill the
/// key, the rate, the category, the amounts, the validity, the day, the document and the unique text columns themselves, and take the rows they add out again. </summary>
public sealed record TaxTestPlan(TaxLineShape Shape, string CategoryBody, string LineBody, string[] UniqueTexts)
{
    /// <summary> A day as the column writes it: a date, or a date and time of day. </summary>
    public static string Day(ColumnModel column, string date) => column.IsDateOnlyColumn ? date : date + "T00:00:00";

    public static TaxTestPlan? For(DatabaseModel database, ProjectSettings project)
    {
        foreach (var table in database.Tables.OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase))
        {
            if (TaxLinePlan.For(table, project, database) is not { } s || !s.Key.IsIdentity || !s.CategoryKey.IsIdentity)
                continue;
            if (MakeCategoryBody(s, project) is { } category && MakeLineBody(s, project) is { } line)
                return new TaxTestPlan(s, category, line, UniqueTextColumns(s).Select(c => JsonNames.Camel(c.Name)).ToArray());
        }
        return null;
    }

    public static IEnumerable<ColumnModel> UniqueTextColumns(TaxLineShape s) => ApiSampleValues.BodyColumns(s.CategoryTable).Where(c => c != s.CategoryKey && c.IsStringColumn && (c.IsInUniqueIndex || s.Groups.Contains(c)));   // a group column too: two categories of one group may not have overlapping periods

    /// <summary> The category row, or null when a column needs a value this plan cannot make up. </summary>
    public static string? MakeCategoryBody(TaxLineShape s, ProjectSettings project) => MakeCategoryBody(s, project, "0", "1", "\"\"", "null", null);

    /// <summary> The category row with the key, rate, validity and unique text given as JSON (<paramref name="unique"/> null: an empty text the test fills). </summary>
    public static string? MakeCategoryBody(TaxLineShape s, ProjectSettings project, string key, string rate, string from, string to, string? unique)
    {
        var parts = new List<string>();
        foreach (var c in ApiSampleValues.BodyColumns(s.CategoryTable))
        {
            string value;
            if (c == s.CategoryKey)
                value = key;
            else if (c == s.CategoryRate)
                value = rate;
            else if (c == s.ValidFrom)
                value = from;
            else if (c == s.ValidTo)
                value = to;
            else if (c.IsStringColumn && c.IsInUniqueIndex)
                value = unique ?? "\"\"";
            else if (c.IsInUniqueIndex || s.CategoryTable.ForeignKeys.Any(f => f.ReferencingColumns.Any(r => r.EqualsIgnoreCase(c.Name))))
                return null;
            else
            {
                value = c.ValidExample(project, s.CategoryTable.TableName);
                if (value == "null" && !c.IsNullable)
                    return null;
            }
            parts.Add(ApiSampleValues.Json(JsonNames.Camel(c.Name)) + ": " + value);
        }
        return "{ " + string.Join(", ", parts) + " }";
    }

    /// <summary> The line row, or null when a column needs a value this plan cannot make up. </summary>
    public static string? MakeLineBody(TaxLineShape s, ProjectSettings project)
    {
        var parts = new List<string>();
        foreach (var c in ApiSampleValues.BodyColumns(s.Table))
        {
            string value;
            if (c == s.Key)
                value = "0";
            else if (c == s.Category)
                value = "null";
            else if (c == s.Rate || c == s.Taxable || c == s.Tax)
                value = "0";
            else if (c == s.Day)
                value = "\"\"";
            else if (c == s.Document && !s.Table.ForeignKeys.Any(f => f.ReferencingColumns.Any(r => r.EqualsIgnoreCase(c.Name))))
                value = "1";
            else if (c.IsInUniqueIndex || s.Table.ForeignKeys.Any(f => f.ReferencingColumns.Any(r => r.EqualsIgnoreCase(c.Name))))
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
