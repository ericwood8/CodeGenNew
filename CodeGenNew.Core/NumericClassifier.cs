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

    private static readonly string[] CurrencyNameParts = ["Amount", "Amt", "Price", "Cost", "Fee", "Charge", "Salary", "Wage", "Payment", "Balance", "MSRP"];
    private static readonly string[] NotMoneyAfterTotal = ["Qty", "Quantity", "Hours", "Count", "Units", "Weight", "Days", "Pages"];

    /// <summary> Whether a decimal column's NAME says it holds money (an Amount, a Price, a Cost, a Total ...). A column of the
    /// money / smallmoney SQL types is currency regardless of its name. A Quantity is deliberately not money, nor is a
    /// TotalHours or TotalQty: the Total prefix counts only when nothing after it names another unit. </summary>
    public static bool IsCurrencyName(string columnName)
    {
        string name = columnName.Replace("_", "");
        if (CurrencyNameParts.Any(p => name.ContainsIgnoreCase(p)))
            return true;
        return name.StartsWithIgnoreCase("Total")
            && !NotMoneyAfterTotal.Any(p => name.ContainsIgnoreCase(p));
    }

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
