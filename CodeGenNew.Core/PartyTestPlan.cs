using CodeGenNew.Core.Api;




namespace CodeGenNew.Core;

/// <summary> What the generated party tests of the Python and Rust APIs (PY_TestParties, RS_TestParties) send: a party and an identifier row, each with an example value for every column that must have one.
/// The tests fill the link, the scheme and the value themselves. </summary>
public sealed record PartyTestPlan(PartyShape Shape, string PartyBody, string IdentifierBody)
{
    public const string FirstScheme = "VAT";
    public const string OtherScheme = "DUNS";
    /// <summary> The longest value the tests store: two letters and a tag of eight. </summary>
    public const int ValueLength = 10;

    /// <summary> The plan of the first party table the tests can fill, or null: both keys are identity columns, no other column that must have a value is a foreign key or in a unique index, and the scheme and
    /// the value are long enough for the test values. </summary>
    public static PartyTestPlan? For(DatabaseModel database, ProjectSettings project)
    {
        foreach (var entry in PartyPlan.Entries(project).OrderBy(e => e.Party, StringComparer.OrdinalIgnoreCase))
        {
            if (database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(entry.Party)) is not { } table || PartyPlan.PartyOf(table, project, database) is not { } shape)
                continue;
            if (!shape.PartyKey.IsIdentity || !shape.IdentifierKey.IsIdentity || !Fits(shape.Scheme, OtherScheme.Length) || !Fits(shape.Value, ValueLength))
                continue;
            if (Body(shape.Party, project, []) is { } party && Body(shape.Identifiers, project, [shape.Link, shape.Scheme, shape.Value]) is { } identifier)
                return new PartyTestPlan(shape, party, identifier);
        }
        return null;
    }

    private static bool Fits(ColumnModel column, int length) => column.CharacterLength <= 0 || column.CharacterLength >= length;

    private static string? Body(TableModel table, ProjectSettings project, IReadOnlyList<ColumnModel> filled)
    {
        var parts = new List<string>();
        foreach (var c in ApiSampleValues.BodyColumns(table))
        {
            string value;
            if (c.IsPrimaryKey || c == filled.ElementAtOrDefault(0))
                value = "0";
            else if (filled.Contains(c))
                value = "\"\"";
            else if (c.IsInUniqueIndex || table.ForeignKeys.Any(f => f.ReferencingColumns.Any(r => r.EqualsIgnoreCase(c.Name))))
                return null;
            else
                value = c.ValidExample(project, table.TableName);
            if (value == "null")
                return null;
            parts.Add(ApiSampleValues.Json(JsonNames.Camel(c.Name)) + ": " + value);
        }
        return "{ " + string.Join(", ", parts) + " }";
    }
}
