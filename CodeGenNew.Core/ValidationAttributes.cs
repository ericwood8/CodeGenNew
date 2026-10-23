using System.Data;
using System.Globalization;

namespace CodeGenNew.Core;

/// <summary> The DataAnnotations attributes of one column (<c>[Required]</c>, <c>[Display]</c>, <c>[StringLength]</c>, <c>[DataType]</c>, the email / phone / URL / pattern attribute, <c>[Range]</c>), written the same way on
/// the metadata class of an entity (CS_Validation) and on the request type of a table (CS_DtoInput). The classification is <see cref="ColumnRules.RuleOf"/>, the one every validator reads. </summary>
public static class ValidationAttributes
{
    public static IReadOnlyList<string> Of(ColumnModel column, ProjectSettings project, string table)
    {
        var attributes = new List<string>();
        string words = CSharpText.Escape(Labels.Words(column.Name, column.IsBooleanColumn));
        int chars = column.CharacterLength;
        bool isText = column.SqlType is SqlDbType.Char or SqlDbType.VarChar or SqlDbType.NChar or SqlDbType.NVarChar;
        var rule = column.RuleOf(project, table);

        if (!column.IsNullable)
            attributes.Add("[Required]");
        attributes.Add($"[Display(Name = \"{words}\", Description = \"{words}\")]");
        if (isText && chars > 0)
            attributes.Add($"[StringLength({chars})]");

        if (column.Name.ContainsIgnoreCase("Password"))
            attributes.Add("[DataType(DataType.Password)]");
        else if (isText && chars >= 100)
            attributes.Add("[DataType(DataType.MultilineText)]");

        if (rule?.Shape switch
            {
                TextShape.Email => "[EmailAddress]",
                TextShape.Phone => "[Phone]",
                TextShape.Url => "[Url]",
                TextShape.Postal or TextShape.Ssn or TextShape.Custom => "[RegularExpression(@\"^(?:" + ColumnRules.PatternOf(rule)!.Replace("\"", "\"\"") + ")$\", ErrorMessage = \"" + words + " is not " + ColumnRules.NounOf(rule).Replace("\"", "\\\"") + ".\")]",
                _ => null
            } is { } shape)
            attributes.Add(shape);

        // A whole-number column whose name says what it holds (a year, a month, a percentage, ...) gets that range; every other integer keeps the plain limits of its SQL type, which a [Range] would
        // only repeat. A decimal gets the bounds of its CHECK constraint, the project's NonNegativeColumns or the degrees of a latitude / longitude, and the side with no bound is the type's own.
        if ((column.NumericKind != NumericKind.None || column.Check is not null) && project.RangeFor(column) is { } range)
            attributes.Add($"[Range({range.Min}, {range.Max})]");
        else if ((column.IsNumericColumn || column.IsMoneyColumn) && rule is { HasRange: true })
            attributes.Add($"[Range({(rule.Min is { } lo ? Number(lo) : "double.MinValue")}, {(rule.Max is { } hi ? Number(hi) : "double.MaxValue")})]");

        return attributes;
    }

    private static string Number(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
}
