using CodeGenNew.Core.Rate;

namespace CodeGenNew.Core.Python;

/// <summary> The Python (FastAPI) counterpart of the rate document part of <see cref="RowShapeWriter"/> (<c>RateDocuments</c>): the document's router gets <c>convert_currency</c>, called before a create or an
/// update commits, which writes the currency trimmed and in upper case, the rate in force on the day (kept while the currency and day stay) and the converted amount. The words are <see cref="RateWords"/>. </summary>
public static class PythonRateDocumentWriter
{
    /// <summary> The document as Python attributes. <c>DayAnnotation</c> is <c>date</c> or <c>datetime</c> (null: the rate is looked up for today). The amount annotations are <c>Decimal</c>, <c>float</c> or <c>int</c>. </summary>
    public sealed record Names(string Class, string Currency, string Rate, string RateAnnotation, string Amount, string AmountAnnotation, string BaseAmount, string BaseAnnotation, string? Day, string? DayAnnotation,
        string RateDayAnnotation, string FoundRate, string Base, int Scale);

    /// <summary> The attributes the server owns, which an update must not take from the body. </summary>
    public static IEnumerable<string> Owned(Names n) => [n.Rate, n.BaseAmount];

    /// <summary> The Python words the helper needs from <c>datetime</c>. </summary>
    public static string[] DateWords(Names n) => ["date", "datetime", "time"];

    public static string Helper(Names n)
    {
        string one = n.RateAnnotation switch { "Decimal" => "Decimal(1)", "float" => "1.0", _ => "1" };
        string today = "date.today()";
        string dayValue = n.Day is null ? today : $"row.{n.Day}";
        string key = n.Day is null ? "(currency,)" : "(currency, day_of(row." + n.Day + "))";
        string toRateDay = n.RateDayAnnotation == "datetime" ? "datetime.combine(day_of(" + dayValue + "), time.min)" : "day_of(" + dayValue + ")";
        string money = n.AmountAnnotation == "Decimal" ? $"row.{n.Amount}" : $"Decimal(str(row.{n.Amount}))";
        string rate = n.RateAnnotation == "Decimal" ? $"row.{n.Rate}" : $"Decimal(str(row.{n.Rate}))";
        string rounded = $"({money} * {rate}).quantize(Decimal(1).scaleb(-{n.Scale}), rounding=ROUND_HALF_UP)";
        string baseValue = n.BaseAnnotation switch { "Decimal" => rounded, "float" => $"float({rounded})", _ => $"int({rounded})" };
        string found = n.RateAnnotation == "Decimal" ? $"found.{n.FoundRate}" : n.RateAnnotation == "float" ? $"float(found.{n.FoundRate})" : $"int(found.{n.FoundRate})";
        return TemplateText.FillAll(HelperText, ("§CLASS", n.Class), ("§CUR", n.Currency), ("§RATE", n.Rate), ("§BASEAMT", n.BaseAmount), ("§KEY", key), ("§DAYVALUE", dayValue), ("§RATEDAY", toRateDay),
            ("§ONE", one), ("§BASEVALUE", baseValue), ("§FOUND", found), ("§BASE", n.Base),
            ("§INVALIDTYPE", RateWords.InvalidType), ("§INVALIDTITLE", RateWords.InvalidTitle), ("§W_DOCNEEDS", RateWords.DocumentNeedsCurrency),
            ("§NEEDEDTYPE", RateWords.NeededType), ("§NEEDEDTITLE", RateWords.NeededTitle));
    }

    private const string HelperText = """"
def day_of(value):
    return value.date() if isinstance(value, datetime) else value


def document_key(row) -> tuple:
    """The currency and day a row was written with: the rate is kept while both stay."""
    currency = (row.§CUR or "").strip().upper()
    return §KEY


def convert_currency(session: Session, row, stored: tuple | None) -> None:
    """The currency is trimmed and upper case. The rate is the one in force on the day (1 for §BASE), fixed when the row is created and kept while the currency and day stay; the converted amount is the amount times the rate."""
    row.§CUR = (row.§CUR or "").strip().upper()
    if not row.§CUR:
        raise ApiError(400, "§INVALIDTITLE", problem_type="§INVALIDTYPE", detail="§W_DOCNEEDS")
    if stored is None or stored != document_key(row):
        if row.§CUR == "§BASE":
            row.§RATE = §ONE
        else:
            found, _ = find_rate(session, row.§CUR, "§BASE", §RATEDAY)
            if found is None:
                raise ApiError(400, "§NEEDEDTITLE", problem_type="§NEEDEDTYPE", detail=f"There is no exchange rate from {row.§CUR} to §BASE on or before {day_of(§DAYVALUE):%Y-%m-%d}.")
            row.§RATE = §FOUND
    row.§BASEAMT = §BASEVALUE
"""";
}
