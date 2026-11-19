using CodeGenNew.Core.Tax;




namespace CodeGenNew.Core.Python;

/// <summary> The Python (FastAPI) counterpart of <see cref="TaxLineWriter"/> (<c>TaxLines</c>): the line's router gets <c>apply_tax</c>, called before a create or an update commits, and a
/// <c>GET tax-subtotals</c> route when the entry names the document column. The words are <see cref="TaxWords"/>. </summary>
public static class PythonTaxWriter
{
    /// <summary> The line and its category table as Python attributes. Annotations are <c>Decimal</c>, <c>float</c> or <c>int</c> (numbers) and <c>date</c> or <c>datetime</c> (days). </summary>
    public sealed record Names(string Class, string Key, string Category, bool CategoryNullable, string Rate, string RateAnnotation, string Taxable, string TaxableAnnotation, string Tax, string TaxAnnotation, int Scale,
        string CategoryClass, string CategoryKey, string CategoryRate, string? ValidFrom, string? ValidTo, string? ValidAnnotation, string? Day, string? DayAnnotation, string? Document, string? Label);

    /// <summary> The attributes the server owns, which an update must not take from the body. </summary>
    public static IEnumerable<string> Owned(Names n) => [n.Rate, n.Tax];

    public static string[] DateWords(Names n) => n.ValidFrom is null && n.Day is null ? [] : ["date", "datetime", "time"];

    private static string Number(string annotation, string value) => annotation == "Decimal" ? value : $"Decimal(str({value}))";

    private static string Back(string annotation, string value) => annotation switch { "Decimal" => value, "float" => $"float({value})", _ => $"int({value})" };

    public static string Helper(Names n)
    {
        string today = n.ValidAnnotation == "datetime" || n.ValidFrom is null && n.DayAnnotation == "datetime" ? "datetime.now()" : "date.today()";
        string dayValue = n.Day is null ? today : $"row.{n.Day}";
        string key = n.Day is null ? $"(row.{n.Category},)" : $"(row.{n.Category}, day_of(row.{n.Day}))";
        string zero = n.RateAnnotation switch { "Decimal" => "Decimal(0)", "float" => "0.0", _ => "0" };
        string zeroTax = n.TaxAnnotation switch { "Decimal" => "Decimal(0)", "float" => "0.0", _ => "0" };
        string none = n.CategoryNullable
            ? $"    if row.{n.Category} is None:\n        row.{n.Rate} = {zero}\n        row.{n.Tax} = {zeroTax}\n        return\n"
            : "";
        string dating = "";
        if (n.ValidFrom is not null)
        {
            string asOf = n.ValidAnnotation == "datetime" ? $"datetime.combine(day_of({dayValue}), time.min)" : $"day_of({dayValue})";
            string ended = n.ValidTo is null ? "" : $" or (found.{n.ValidTo} is not None and found.{n.ValidTo} < as_of)";
            dating = $"        as_of = {asOf}\n        if found.{n.ValidFrom} > as_of{ended}:\n            raise ApiError(400, \"{TaxWords.NotInForceTitle}\", problem_type=\"{TaxWords.NotInForceType}\", detail=TAX_NOT_IN_FORCE.format(row.{n.Category}, day_of({dayValue}).isoformat()))\n";
        }
        string rate = Back(n.RateAnnotation, $"found.{n.CategoryRate}");
        string money = Number(n.TaxableAnnotation, $"row.{n.Taxable}");
        string percent = Number(n.RateAnnotation, $"row.{n.Rate}");
        string rounded = $"({money} * {percent}).quantize(Decimal(1).scaleb(-{n.Scale}), rounding=ROUND_HALF_UP)";
        var text = TemplateText.FillAll(HelperText, ("§CLASS", n.Class), ("§CAT", n.Category), ("§CATCLASS", n.CategoryClass), ("§CATKEY", n.CategoryKey), ("§RATE", n.Rate), ("§TAX", n.Tax), ("§KEY", key), ("§NONE", none),
            ("§DATING", dating), ("§FOUNDRATE", rate), ("§ROUNDED", Back(n.TaxAnnotation, rounded)), ("§DAYOF", n.ValidFrom is null && n.Day is null ? "" : DayOfText),
            ("§UNKNOWNTYPE", TaxWords.UnknownType), ("§UNKNOWNTITLE", TaxWords.UnknownTitle), ("§W_UNKNOWN", TaxWords.Unknown("{row." + n.Category + "}")),
            ("§W_NOTINFORCE", TaxWords.NotInForce("{}", "{}")));
        return text;
    }

    private const string DayOfText = """"
def day_of(value):
    return value.date() if isinstance(value, datetime) else value


"""";

    private const string HelperText = """"
§DAYOFTAX_NOT_IN_FORCE = "§W_NOTINFORCE"


def tax_key(row) -> tuple:
    """The category and day a line was written with: the rate is kept while both stay."""
    return §KEY


def apply_tax(session: Session, row, stored: tuple | None) -> None:
    """The line carries the rate of its category: copied when the row is created or its category (or day) changes, kept otherwise. The tax is the taxable amount times the rate over 100."""
§NONE    if stored is None or stored != tax_key(row):
        found = session.get(§CATCLASS, row.§CAT)
        if found is None:
            raise ApiError(400, "§UNKNOWNTITLE", problem_type="§UNKNOWNTYPE", detail=f"§W_UNKNOWN")
§DATING        row.§RATE = §FOUNDRATE
    row.§TAX = §ROUNDED
"""";

    public static string Route(Names n, string route, string readDepends, string userParameter)
    {
        if (n.Document is null)
            return "";
        string label = n.Label is null ? "None" : $"labels.get(category)";
        string labels = n.Label is null ? "" : $"    labels = dict(session.execute(select({n.CategoryClass}.{n.CategoryKey}, {n.CategoryClass}.{n.Label}).where({n.CategoryClass}.{n.CategoryKey}.in_([r.{n.Category} for r in rows if r.{n.Category} is not None]))).all())\n";
        string tax = Number(n.TaxAnnotation, "r." + n.Tax), taxable = Number(n.TaxableAnnotation, "r." + n.Taxable);
        return TemplateText.FillAll(RouteText, ("§ROUTE", route), ("§CLASS", n.Class), ("§CAT", n.Category), ("§RATE", n.Rate), ("§DOC", n.Document), ("§LABELS", labels), ("§LABEL", label),
            ("§HEADER", readDepends), ("§USER", userParameter), ("§TAXVALUE", tax), ("§TAXABLEVALUE", taxable), ("§W_DOC", TaxWords.SubtotalsNeedsDocument));
    }

    private const string RouteText = """"
@router.get("§ROUTE/tax-subtotals")
def tax_subtotals(§HEADERdocument: int | None = Query(None), session: Session = Depends(get_session)§USER):
    """The tax of one document by category and rate: taxable amount, tax and number of lines, worked out from its lines."""
    if document is None:
        raise ApiError(400, "§W_DOC")
    rows = session.scalars(select(§CLASS).where(§CLASS.§DOC == document)).all()
§LABELS    groups: dict = {}
    for r in rows:
        group = groups.setdefault((r.§CAT, r.§RATE), {"taxable": Decimal(0), "tax": Decimal(0), "lines": 0})
        group["taxable"] += §TAXABLEVALUE
        group["tax"] += §TAXVALUE
        group["lines"] += 1
    return [
        {"category": category, "label": §LABEL, "rate": rate, "taxable": group["taxable"], "tax": group["tax"], "lines": group["lines"]}
        for (category, rate), group in sorted(groups.items(), key=lambda item: (item[0][0] is not None, item[0][0] or 0, item[0][1]))
    ]
"""";
}
