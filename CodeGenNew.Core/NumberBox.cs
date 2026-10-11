namespace CodeGenNew.Core;

/// <summary> What a web number box for one column needs besides its binding: the step, the smallest and largest value it accepts and how wide it is. Decided here so every stack limits the same column the same
/// way; a template only writes the attributes in its own syntax. </summary>
public sealed class NumberBox
{
    private NumberBox() { }

    /// <summary> The step between values: a decimal column's last place, a cent for money, <c>any</c> for a float; null for a whole number. </summary>
    public string? Step { get; private init; }

    /// <summary> The smallest value, as text with no culture; null for no limit. </summary>
    public string? Min { get; private init; }

    /// <summary> The largest value, as text with no culture; null for no limit. </summary>
    public string? Max { get; private init; }

    /// <summary> How many characters wide the box is when its values are short (a month, a year, a percentage); null keeps the form's own width. </summary>
    public int? Characters { get; private init; }

    /// <summary> The limits as html attributes, each with a leading space: <c> step="0.01" min="0" max="100" style="width: 8ch"</c>. </summary>
    public string Html => (Step is { } step ? $" step=\"{step}\"" : "") + (Min is { } min ? $" min=\"{min}\"" : "") + (Max is { } max ? $" max=\"{max}\"" : "")
                          + (Characters is { } width ? $" style=\"width: {width}ch\"" : "");

    /// <summary> The limits as JSX attributes, each with a leading space: <c> step="0.01" min="0" max="100" style={{ width: '8ch' }}</c>. </summary>
    public string Jsx => (Step is { } step ? $" step=\"{step}\"" : "") + (Min is { } min ? $" min=\"{min}\"" : "") + (Max is { } max ? $" max=\"{max}\"" : "")
                         + (Characters is { } width ? $" style={{{{ width: '{width}ch' }}}}" : "");

    /// <summary> The number box of <paramref name="column"/> of <paramref name="table"/>. The limits are, in this order: the name and type of a whole number (a month is 1-12), what a decimal column can hold,
    /// and what the CHECK constraint (or the project's <c>NonNegativeColumns</c>) says about an amount of money. </summary>
    public static NumberBox For(string table, ColumnModel column, ProjectSettings project)
    {
        string? step = column.SqlType is System.Data.SqlDbType.Decimal && (column.Scale ?? 0) > 0 ? "0." + new string('0', column.Scale!.Value - 1) + "1"
            : column.IsMoneyColumn ? "0.01"
            : column.SqlType is System.Data.SqlDbType.Float or System.Data.SqlDbType.Real ? "any" : null;
        string? min = null, max = null;
        if (project.RangeFor(column) is { } range)
            (min, max) = (range.Min.ToString(), range.Max.ToString());
        else if (TemplateHelpers.IsPlainDecimal(column))
        {
            var (decimalMin, decimalMax) = NumericClassifier.DecimalRange(column);
            (min, max) = (TemplateHelpers.Inv(decimalMin), TemplateHelpers.Inv(decimalMax));
        }
        else if (column.IsMoneyAmount)
        {
            var (moneyMin, moneyMax) = project.MoneyLimits(table, column);
            (min, max) = (moneyMin is { } lo ? TemplateHelpers.Inv(lo) : null, moneyMax is { } hi ? TemplateHelpers.Inv(hi) : null);
        }
        return new NumberBox { Step = step, Min = min, Max = max, Characters = project.NumberBoxCharacters(column) };
    }
}

/// <summary> What a money box needs: the currency, the digits shown and the limits (the column's CHECK constraint, or 0 for a column the project lists as never negative). </summary>
/// <param name="Min"> The smallest amount, as text with no culture; null for no limit. </param>
/// <param name="Max"> The largest amount; null for no limit. </param>
/// <param name="Digits"> The decimal places the column holds. </param>
/// <param name="Currency"> The ISO 4217 code the amount is formatted with. </param>
public sealed record MoneyBox(string? Min, string? Max, int Digits, string Currency)
{
    public static MoneyBox For(string table, ColumnModel column, ProjectSettings project)
    {
        var (min, max) = project.MoneyLimits(table, column);
        return new MoneyBox(min is { } lo ? TemplateHelpers.Inv(lo) : null, max is { } hi ? TemplateHelpers.Inv(hi) : null, NumericClassifier.CurrencyDigits(column), project.CurrencyCode);
    }
}
