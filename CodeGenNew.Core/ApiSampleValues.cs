using System.Data;
using System.Globalization;

namespace CodeGenNew.Core;

/// <summary> One example JSON value for a column of a request body (the .http files, the generated API tests): a choice from its CHECK list, a number inside its range, an email address
/// for an email column, text cut to the column's length. A nullable column is left out of a body, so only the columns that cannot be NULL need a value. </summary>
public static class ApiSampleValues
{
    /// <summary> The JSON text of a quoted, escaped string. </summary>
    public static string Json(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary> The text a string column's example is made of, before it is cut to the column's length. </summary>
    public static string Text(ColumnModel column) =>
        column.HasChoices ? column.Choices![0]
        : column.Name.ContainsIgnoreCase("Email") ? "someone@example.com"
        : column.Name.ContainsIgnoreCase("Phone") || column.Name.ContainsIgnoreCase("Fax") ? "555-0100"
        : column.Name.ContainsIgnoreCase("Url") || column.Name.ContainsIgnoreCase("Website") ? "https://example.com"
        : "Sample " + Labels.Words(column.Name).ToLowerInvariant();

    /// <summary> The example value of a column as JSON text (which is also a valid C# literal for every type but a null). A key is 0 for an identity column and 1 otherwise. </summary>
    public static string Value(ColumnModel c, ProjectSettings project)
    {
        if (c.IsPrimaryKey && c.IsStringColumn)
            return Json(KeyText(c));
        if (c.IsPrimaryKey && !c.IsGuidColumn)
            return c.IsIdentity ? "0" : "1";
        if (c.IsStringColumn)
        {
            string text = Text(c);
            int chars = c.CharacterLength;
            if (chars > 0 && text.Length > chars)
                text = text[..chars];
            return Json(text);
        }
        if (c.IsIntegerColumn)
            return project.RangeFor(c) is { } range ? Math.Max(range.Min, Math.Min(1, range.Max)).ToString(CultureInfo.InvariantCulture) : "1";
        if (c.IsNumericColumn || c.IsMoneyColumn)
        {
            double value = 1;
            if (c.Check is { } check)
            {
                if (check.Min is { } min && value <= min)
                    value = check.MinStrict ? min + 1 : min;
                if (check.Max is { } max && value > max)
                    value = max;
            }
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
        if (c.IsBooleanColumn)
            return "false";
        switch (c.SqlType)
        {
            case SqlDbType.Date or SqlDbType.DateTime or SqlDbType.DateTime2 or SqlDbType.SmallDateTime: return "\"2026-01-15T00:00:00\"";
            case SqlDbType.DateTimeOffset: return "\"2026-01-15T00:00:00+00:00\"";
            case SqlDbType.Time: return "\"08:00:00\"";
            case SqlDbType.UniqueIdentifier: return "\"00000000-0000-0000-0000-000000000001\"";
            case SqlDbType.Binary or SqlDbType.VarBinary or SqlDbType.Image or SqlDbType.Timestamp: return "\"AA==\"";
            default: return "null";
        }
    }

    /// <summary> The example of a text key: the sample words with no spaces (it goes into a route), cut to the column's length. </summary>
    public static string KeyText(ColumnModel key)
    {
        string text = Text(key).Replace(' ', '_');
        return key.CharacterLength > 0 && text.Length > key.CharacterLength ? text[..key.CharacterLength] : text;
    }

    /// <summary> The key the request files put in their <c>id</c> variable: the row the "add" request creates (1, a GUID ending in 1, or the text key's example). </summary>
    public static string SampleId(TableModel table)
    {
        var key = table.PrimaryKeyColumns[0];
        return key.IsStringColumn ? KeyText(key) : key.IsGuidColumn ? "00000000-0000-0000-0000-000000000001" : "1";
    }

    /// <summary> The columns a create or update body carries: every column that cannot be NULL and is not computed, plus the key. </summary>
    public static List<ColumnModel> BodyColumns(TableModel table) =>
        table.Columns.OrderBy(c => c.OrdinalPosition).Where(c => !c.IsComputed && (!c.IsNullable || c.IsPrimaryKey)).ToList();
}
