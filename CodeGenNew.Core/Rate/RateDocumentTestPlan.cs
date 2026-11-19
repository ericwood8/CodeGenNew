using CodeGenNew.Core.Api;
using CodeGenNew.Core.Naming;





namespace CodeGenNew.Core.Rate;

/// <summary> What the generated rate document tests of the Python and Rust APIs send: a rate row and a document row with an example value for every column that must have one. The tests fill the keys, the
/// currency, the rate, the amounts and the days themselves, and take the rows they add out again. </summary>
public sealed record RateDocumentTestPlan(RateDocumentShape Shape, string RateBody, string Body)
{
    /// <summary> A day the tests save in the document, as the day column writes it, or null when the table has no day column. </summary>
    public string? DocumentDay(string date) => Shape.Day is null ? null : Shape.Day.IsDateOnlyColumn ? date : date + "T00:00:00";

    /// <summary> A day the tests save in the rate table, as its day column writes it. </summary>
    public string RateDay(string date) => Shape.Rates.Day.IsDateOnlyColumn ? date : date + "T00:00:00";

    /// <summary> The plan of the first listed document table the tests can fill, or null: both keys are identity columns, the currencies are long enough for the test codes and no other column that must have
    /// a value is a foreign key or in a unique index. </summary>
    public static RateDocumentTestPlan? For(DatabaseModel database, ProjectSettings project)
    {
        foreach (var table in database.Tables.OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase))
        {
            if (RateDocumentPlan.For(table, project, database) is not { } shape || !shape.Key.IsIdentity || !shape.Rates.Key.IsIdentity
                || !RateTestPlan.Fits(shape.Currency) || !RateTestPlan.Fits(shape.Rates.From) || !RateTestPlan.Fits(shape.Rates.To))
                continue;
            if (RateTestPlan.BodyOf(shape.Rates, project) is { } rateBody && BodyOf(shape, project) is { } body)
                return new RateDocumentTestPlan(shape, rateBody, body);
        }
        return null;
    }

    private static string? BodyOf(RateDocumentShape shape, ProjectSettings project)
    {
        var parts = new List<string>();
        foreach (var c in ApiSampleValues.BodyColumns(shape.Table))
        {
            string value;
            if (c == shape.Key)
                value = "0";
            else if (c == shape.Currency || c == shape.Day)
                value = "\"\"";
            else if (c == shape.Rate || c == shape.Amount || c == shape.BaseAmount)
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
