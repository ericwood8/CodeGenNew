using System.Data;
using System.Text.RegularExpressions;

namespace CodeGenNew.SchemaIntrospection;

/// <summary>
/// Resolves ColumnModel.SuggestedCSharpDefaultValueLiteral: prefer translating the real database DEFAULT
/// constraint, fall back to the ported heuristic (Avatar.CodeGen.SqlServer.DataLayer.ColumnTools.SetColumnDefault)
/// only when there is no database default at all. Per Docs/specs.md round-5 Q1: this is a rarely-used,
/// low-priority convenience value -- an unparseable database default simply yields no suggestion, never an error.
/// </summary>
public static class ColumnDefaultResolver
{
    public static string? Resolve(string columnName, SqlDbType sqlType, string? databaseDefaultSql, bool isBooleanColumn,
        bool isDateColumn, bool isMoneyColumn, bool isNumericColumn, bool isIntegerColumn, bool isStringColumn)
    {
        if (!string.IsNullOrWhiteSpace(databaseDefaultSql))
        {
            string? translated = TryTranslateSqlDefault(databaseDefaultSql, isStringColumn);
            if (translated is not null)
                return translated;

            return null; // unparseable -- don't guess, per round-5 Q1
        }

        return HeuristicDefault(columnName, isBooleanColumn, isDateColumn, isMoneyColumn, isNumericColumn, isIntegerColumn, isStringColumn);
    }

    private static string? TryTranslateSqlDefault(string sqlDefault, bool isStringColumn)
    {
        string expr = sqlDefault.Trim();
        // Strip any number of wrapping parentheses, e.g. "((0))" -> "0", "(getdate())" -> "getdate()"
        while (expr.StartsWith('(') && expr.EndsWith(')'))
            expr = expr[1..^1].Trim();

        if (expr.Equals("getdate()", StringComparison.OrdinalIgnoreCase))
            return "DateTime.Now";
        if (expr.Equals("getutcdate()", StringComparison.OrdinalIgnoreCase))
            return "DateTime.UtcNow";
        if (expr.Equals("newid()", StringComparison.OrdinalIgnoreCase))
            return "Guid.NewGuid()";

        if (decimal.TryParse(expr, out decimal numericValue))
            return numericValue.ToString();

        var stringLiteralMatch = Regex.Match(expr, "^N?'(.*)'$");
        if (stringLiteralMatch.Success)
        {
            string literalText = stringLiteralMatch.Groups[1].Value.Replace("''", "'");
            return isStringColumn ? $"\"{literalText}\"" : literalText;
        }

        return null; // anything else (function calls, expressions) -- not recognized, don't guess
    }

    private static string? HeuristicDefault(string columnName, bool isBooleanColumn, bool isDateColumn,
        bool isMoneyColumn, bool isNumericColumn, bool isIntegerColumn, bool isStringColumn)
    {
        if (isBooleanColumn)
        {
            bool defaultsToTrue = columnName.Equals("IsActive", StringComparison.OrdinalIgnoreCase)
                || columnName.Equals("IsPrintable", StringComparison.OrdinalIgnoreCase);
            return defaultsToTrue ? "true" : "false";
        }

        if (isDateColumn)
            return "DateTime.Now";

        if (isMoneyColumn || isNumericColumn || isIntegerColumn)
            return "0";

        if (isStringColumn)
            return "\"\"";

        return null;
    }
}
