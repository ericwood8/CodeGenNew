using CodeGenNew.Core.Allowance;

namespace CodeGenNew.Core.Python;

/// <summary> The Python (FastAPI) counterpart of <see cref="AllowanceChargeWriter"/> (<c>AllowanceCharges</c>): the table's router gets <c>check_allowance</c>, called before a create or an update commits, and a
/// <c>GET allowance-charge-totals</c> route when the entry names the document column. The words are <see cref="AllowanceChargeWords"/>. </summary>
public static class PythonAllowanceWriter
{
    /// <summary> The table as Python attributes. Annotations are <c>Decimal</c> or <c>float</c>. </summary>
    public sealed record Names(string Class, string Flag, string Base, string Percent, string PercentAnnotation, string Amount, string AmountAnnotation, int AmountScale, int PercentScale, string? Document, string? Line = null);

    private static string Back(string annotation, string value) => annotation == "Decimal" ? value : $"float({value})";

    public static string Helper(Names n)
    {
        return TemplateText.FillAll(HelperText, ("§BASE", n.Base), ("§PCT", n.Percent), ("§AMT", n.Amount),
            ("§AMOUNTVALUE", Back(n.AmountAnnotation, $"(base * percent / 100).quantize(Decimal(1).scaleb(-{n.AmountScale}), rounding=ROUND_HALF_UP)")),
            ("§PERCENTVALUE", Back(n.PercentAnnotation, $"(amount / base * 100).quantize(Decimal(1).scaleb(-{n.PercentScale}), rounding=ROUND_HALF_UP)")),
            ("§TYPE", AllowanceChargeWords.InvalidType), ("§TITLE", AllowanceChargeWords.InvalidTitle), ("§W_NEGATIVE", AllowanceChargeWords.Negative),
            ("§W_PERCENTBASE", AllowanceChargeWords.PercentNeedsBase), ("§W_AMOUNT", AllowanceChargeWords.NeedsAmount));
    }

    private const string HelperText = """"
def check_allowance(row) -> None:
    """The amount is worked out here: from the base and the percentage, or the percentage from the amount and the base. It is stored positive, the flag says whether it is added or taken off."""
    base = Decimal(str(row.§BASE or 0))
    percent = Decimal(str(row.§PCT or 0))
    amount = Decimal(str(row.§AMT))
    if base < 0 or percent < 0 or amount < 0:
        raise ApiError(400, "§TITLE", problem_type="§TYPE", detail="§W_NEGATIVE")
    if percent > 0 and base <= 0:
        raise ApiError(400, "§TITLE", problem_type="§TYPE", detail="§W_PERCENTBASE")
    if percent > 0:
        row.§AMT = §AMOUNTVALUE
    elif base > 0 and amount > 0:
        row.§PCT = §PERCENTVALUE
    elif amount <= 0:
        raise ApiError(400, "§TITLE", problem_type="§TYPE", detail="§W_AMOUNT")
"""";

    public static string Route(Names n, string route, string readDepends, string userParameter, string scopeLines = "")
    {
        if (n.Document is null)
            return "";
        return TemplateText.FillAll(RouteText, ("§ROUTE", route), ("§CLASS", n.Class), ("§DOC", n.Document), ("§FLAG", n.Flag), ("§AMT", n.Amount), ("§HEADER", readDepends), ("§USER", userParameter), ("§SCOPE", scopeLines),
            ("§SEGMENT", AllowanceChargeWords.TotalsRoute), ("§LINEPARAM", n.Line is null ? "" : ", line: int | None = Query(None)"),
            ("§LINEFILTER", n.Line is null ? "" : $"    if line is not None:\n        statement = statement.where({n.Class}.{n.Line} == line)\n"), ("§W_DOC", AllowanceChargeWords.NeedsDocument));
    }

    private const string RouteText = """"
@router.get("§ROUTE/§SEGMENT")
def allowance_charge_totals(§HEADERdocument: int | None = Query(None)§LINEPARAM, session: Session = Depends(get_session)§USER):
    """The allowances, the charges and the net (charges minus allowances) of one document."""
    if document is None:
        raise ApiError(400, "§W_DOC")
    statement = select(§CLASS).where(§CLASS.§DOC == document)
§LINEFILTER§SCOPE    rows = session.scalars(statement).all()
    allowances = sum((Decimal(str(r.§AMT)) for r in rows if not r.§FLAG), Decimal(0))
    charges = sum((Decimal(str(r.§AMT)) for r in rows if r.§FLAG), Decimal(0))
    return {"allowances": allowances, "charges": charges, "net": charges - allowances, "rows": len(rows)}
"""";
}
