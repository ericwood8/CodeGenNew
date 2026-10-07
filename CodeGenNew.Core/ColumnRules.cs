using System.Data;

namespace CodeGenNew.Core;

/// <summary> A text column whose name says what it holds, so its value can be checked for that shape. </summary>
public enum TextShape
{
    None,
    Email,
    Phone,
    Url
}

/// <summary> What the schema says a column's value must be: the one fact list behind the validators of every language (FluentValidation, zod, Angular). </summary>
public sealed record ColumnRule
{
    /// <summary> Text that cannot be NULL must not be empty. </summary>
    public bool Required { get; init; }
    /// <summary> The longest text in characters; 0 for no limit. </summary>
    public int MaxLength { get; init; }
    /// <summary> The only values a text column accepts (a CHECK list or an enum); null for any text. </summary>
    public IReadOnlyList<string>? Choices { get; init; }
    public TextShape Shape { get; init; }
    public double? Min { get; init; }
    public bool MinStrict { get; init; }
    public double? Max { get; init; }
    public bool MaxStrict { get; init; }

    public bool HasRange => Min is not null || Max is not null;
}

public static class ColumnRules
{
    /// <summary> Columns the database or the repository fills in, so a person is never asked for them. </summary>
    public static bool IsSystemFilled(this ColumnModel column) => column.IsIdentity || column.IsComputed || column.IsAuditColumn || column.IsCreateDateColumn
        || column.IsCreateUserColumn || column.IsModifiedDateColumn || column.IsModifiedUserColumn || column.IsLastChangedDateColumn;

    /// <summary> The rule for a column, or null when the schema says nothing about it or the system fills it in. A decimal or float keeps to its CHECK range, and failing that a latitude or
    /// longitude by name keeps to its degrees; a whole number keeps to the range its name or CHECK suggests. </summary>
    public static ColumnRule? RuleOf(this ColumnModel column, ProjectSettings project)
    {
        if (column.IsSystemFilled())
            return null;

        ColumnRule? rule = null;
        if (column.IsStringColumn)
        {
            var shape = TextShape.None;
            if (!column.HasChoices)
            {
                if (Names(column, "Email"))
                    shape = TextShape.Email;
                else if (Names(column, "Phone", "Fax"))
                    shape = TextShape.Phone;
                else if (Names(column, "Url", "Website"))
                    shape = TextShape.Url;
            }

            rule = new ColumnRule
            {
                Required = !column.IsNullable,
                MaxLength = Math.Max(0, column.CharacterLength),
                Choices = column.HasChoices ? column.Choices : null,
                Shape = shape
            };
        }
        else if (column.IsIntegerColumn && (column.NumericKind != NumericKind.None || column.Check is not null) && project.RangeFor(column) is { } range)
        {
            rule = new ColumnRule { Min = range.Min, Max = range.Max };
        }
        else if (column.IsNumericColumn || column.IsMoneyColumn)
        {
            if (column.Check is { } check)
                rule = new ColumnRule { Min = check.Min, MinStrict = check.MinStrict, Max = check.Max, MaxStrict = check.MaxStrict };
            else if (Names(column, "Latitude"))
                rule = new ColumnRule { Min = -90, Max = 90 };
            else if (Names(column, "Longitude"))
                rule = new ColumnRule { Min = -180, Max = 180 };
        }

        bool says = rule is not null && (rule.Required || rule.MaxLength > 0 || rule.Choices is not null || rule.Shape != TextShape.None || rule.HasRange);
        return says ? rule : null;
    }

    private static bool Names(ColumnModel column, params string[] words) => words.Any(word => column.Name.ContainsIgnoreCase(word));
}
