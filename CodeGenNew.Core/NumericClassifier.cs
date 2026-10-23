using System.Data;

namespace CodeGenNew.Core;

/// <summary> What an integer column's NAME says about the values it holds, so a generated form can offer a number box with
/// sensible limits instead of a free text box. Only whole-number columns are classified, and only by a deliberately narrow
/// list of names: a wrong guess (a BirthYear column limited to 2000-2100) rejects real data, while a missed one only
/// leaves the column with the plain limits of its SQL type. </summary>
public enum NumericKind { None, Year, Month, DayOfMonth, Quarter, WeekNumber, Percentage, Count, Sequence }

/// <summary> An inclusive range of whole numbers. </summary>
public readonly record struct NumericRange(long Min, long Max);

public static class NumericClassifier
{
    private static readonly (NumericKind Kind, string[] Exact, string[] Suffixes)[] Rules =
    [
        (NumericKind.Year, ["Year", "Yr", "FiscalYear", "FiscalYr", "TaxYear", "CalendarYear", "BudgetYear", "PeriodYear", "ReportYear"], []),
        (NumericKind.Month, ["Month", "Mth", "MonthNumber", "MonthNum", "MonthNo", "FiscalMonth", "PeriodMonth"], []),
        (NumericKind.DayOfMonth, ["Day", "DayOfMonth"], []),
        (NumericKind.Quarter, ["Quarter", "Qtr", "QuarterNumber", "FiscalQuarter"], []),
        (NumericKind.WeekNumber, ["Week", "WeekNumber", "WeekNo", "WeekOfYear"], []),
        (NumericKind.Percentage, ["Percent", "Percentage", "Pct"], ["Percent", "Percentage", "Pct"]),
        (NumericKind.Count, ["Quantity", "Qty", "Count"], ["Count", "Qty", "Quantity"]),
        (NumericKind.Sequence, ["SortOrder", "Sequence", "SeqNo", "DisplayOrder"], ["SortOrder", "Sequence", "DisplayOrder"])
    ];

    /// <summary> The kind a column NAME suggests (None when it matches nothing); the caller only asks about integer columns. </summary>
    public static NumericKind Classify(string columnName)
    {
        string name = columnName.Replace("_", "");
        foreach (var (kind, exact, suffixes) in Rules)
        {
            if (exact.Contains(name, StringComparer.OrdinalIgnoreCase))
                return kind;
            if (suffixes.Any(s => name.Length > s.Length && name.EndsWithIgnoreCase(s)))
                return kind;
        }
        return NumericKind.None;
    }

    private static readonly string[] CurrencyWords = ["amount", "amt", "fee", "charge", "surcharge", "upcharge", "discount", "price", "total", "average", "avg", "tax", "cost", "rate",
        "balance", "salary", "wage", "payment", "msrp"];
    // a name that also says what the number counts or measures is not money (TotalHours, DiscountPercent, QuantityUmRatio)
    private static readonly string[] NotMoneyWords = ["percent", "percentage", "pct", "qty", "quantity", "hours", "hrs", "count", "units", "weight", "days", "pages", "ratio", "um"];

    /// <summary> Whether a decimal or floating-point column's NAME says it holds money: one of its words is a Fee, Balance, Amount, Surcharge, Upcharge, Discount, Price, Total, Average, Tax, Cost or Rate
    /// (or a Charge, Salary, Wage, Payment, MSRP), in the singular or plural, and no word says it is a percentage, a quantity, an hour count, a ratio or a unit. A column of the money / smallmoney SQL
    /// types is currency regardless of its name. </summary>
    public static bool IsCurrencyName(string columnName) => HasWord(columnName, CurrencyWords) && !IsNotMoneyName(columnName);

    /// <summary> Whether a column's name also says what the number counts or measures (a percentage, a quantity, an hour count, a ratio, a unit), so it is not money whatever else its name says. </summary>
    public static bool IsNotMoneyName(string columnName) => HasWord(columnName, NotMoneyWords);

    /// <summary> The size a money column is given in a database with no money type: 19 digits and 4 places (SQL Server's money), or 10 and 4 (its smallmoney). </summary>
    public static bool IsMoneySize(int precision, int scale) => scale == 4 && precision is 19 or 10;

    private static bool HasWord(string columnName, string[] list)
    {
        var words = ScreenNames.Words(columnName).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.ToLowerInvariant());
        return words.Any(word => list.Contains(word) || (word.EndsWith("es") && list.Contains(word[..^2])) || (word.EndsWith('s') && list.Contains(word[..^1])));
    }

    /// <summary> The decimal places a decimal column keeps in a number box: its scale, between 0 and 8 (2 when unknown). </summary>
    public static int DecimalPlaces(ColumnModel column) => Math.Clamp(column.Scale ?? 2, 0, 8);

    /// <summary> The C# keyword for a numeric column's value (<c>int</c>, <c>long</c>, <c>decimal</c> ...); <c>int</c> for anything else. </summary>
    public static string NumericCSharpType(ColumnModel column) => column.SqlType switch
    {
        SqlDbType.Int => "int", SqlDbType.BigInt => "long", SqlDbType.SmallInt => "short", SqlDbType.TinyInt => "byte",
        SqlDbType.Money or SqlDbType.SmallMoney or SqlDbType.Decimal => "decimal",
        SqlDbType.Float => "double", SqlDbType.Real => "float", _ => "int"
    };

    /// <summary> How many decimal places a currency column is shown with: the column's own scale for a decimal, two for money / smallmoney
    /// (whose four stored places are never what people read). </summary>
    public static int CurrencyDigits(ColumnModel column) =>
        column.SqlType is SqlDbType.Decimal && column.SqlTypeDeclaration != "numeric" ? Math.Clamp(column.Scale ?? 2, 0, 8) : 2;   // a bare PostgreSQL numeric has no scale to follow

    /// <summary> The limits a number box puts on a decimal / float / real column that is not money: what the column's precision and scale can hold
    /// (decimal(5,2) holds -999.99 to 999.99), or +/-10^15 for a floating-point type; a percentage (by name) is 0 to 100 within that. </summary>
    public static (double Min, double Max) DecimalRange(ColumnModel column)
    {
        double max = 1e15;
        if (column.SqlType == SqlDbType.Decimal && column.Precision is { } precision && column.Scale is { } scale)
            max = Math.Round(Math.Pow(10, precision - scale) - Math.Pow(10, -scale), scale);
        double min = -max;
        if (Classify(column.Name) == NumericKind.Percentage)
        {
            min = 0;
            max = Math.Min(max, 100);
        }
        // a CHECK constraint of the database narrows it (CHECK (price > 0): strict bounds are taken as inclusive, the database still refuses the edge)
        if (column.Check is { } check)
        {
            double lo = check.Min is { } cm ? Math.Max(min, cm) : min, hi = check.Max is { } cx ? Math.Min(max, cx) : max;
            if (lo <= hi)
                (min, max) = (lo, hi);
        }
        return (min, max);
    }

    /// <summary> The limits of a SQL integer type. bigint stops at 2^53 - 1, the largest whole number a number box (a double) holds exactly. </summary>
    public static NumericRange TypeRange(SqlDbType type) => type switch
    {
        SqlDbType.TinyInt => new(0, 255),
        SqlDbType.SmallInt => new(short.MinValue, short.MaxValue),
        SqlDbType.Int => new(int.MinValue, int.MaxValue),
        _ => new(-9007199254740991, 9007199254740991)
    };
}
