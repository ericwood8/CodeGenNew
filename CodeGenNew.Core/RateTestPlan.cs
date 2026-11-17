namespace CodeGenNew.Core;

/// <summary> What the generated rate tests of the Python and Rust APIs (PY_TestRates, RS_TestRates) send: a rate row with an example value for every column that must have one. The tests fill the key, the
/// currencies, the rate and the day themselves, and take the rows they add out again. </summary>
public sealed record RateTestPlan(RateShape Shape, string Body)
{
    /// <summary> The days the tests save and ask for, as the day column writes them: a date, or a date and time of day. </summary>
    public string Day(string date) => Shape.Day.IsDateOnlyColumn ? date : date + "T00:00:00";

    /// <summary> The plan of the first rate table the tests can fill, or null: the key is an identity column, the currencies are long enough for the test codes and no other column that must have a value is a
    /// foreign key or in a unique index. </summary>
    public static RateTestPlan? For(DatabaseModel database, ProjectSettings project)
    {
        foreach (var table in database.Tables.OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase))
        {
            if (RatePlan.For(table, project) is not { } shape || !shape.Key.IsIdentity || !Fits(shape.From) || !Fits(shape.To))
                continue;
            if (BodyOf(shape, project) is { } body)
                return new RateTestPlan(shape, body);
        }
        return null;
    }

    public static bool Fits(ColumnModel column) => column.CharacterLength <= 0 || column.CharacterLength >= 3;

    public static string? BodyOf(RateShape shape, ProjectSettings project)
    {
        var parts = new List<string>();
        foreach (var c in ApiSampleValues.BodyColumns(shape.Table))
        {
            string value;
            if (c == shape.Key)
                value = "0";
            else if (c == shape.From || c == shape.To || c == shape.Day)
                value = "\"\"";
            else if (c == shape.Rate)
                value = "1";
            else if (c.IsInUniqueIndex || shape.Table.ForeignKeys.Any(f => f.ReferencingColumns.Any(r => r.EqualsIgnoreCase(c.Name))))
                return null;
            else
                value = c.ValidExample(project, shape.Table.TableName);
            if (value == "null")
                return null;
            parts.Add(ApiSampleValues.Json(JsonNames.Camel(c.Name)) + ": " + value);
        }
        return "{ " + string.Join(", ", parts) + " }";
    }
}
