namespace CodeGenNew.Core.Python;

/// <summary> The Python (FastAPI) counterpart of the rate part of <see cref="RowShapeWriter"/> (<c>RateTables</c>): the table's router gets <c>check_rate</c>, called before a create or an update commits, and
/// <c>GET rate</c>. With <c>RateInverse</c> <c>find_rate</c> also answers a pair from the opposite pair's newer row. The words are <see cref="RateWords"/>. </summary>
public static class PythonRateWriter
{
    /// <summary> The rate table as Python attributes. <paramref name="DayAnnotation"/> is <c>date</c> or <c>datetime</c>. </summary>
    public sealed record Names(string Class, string Key, string From, string To, string Rate, string Day, string DayAnnotation, string Schema, bool Inverse = false);

    /// <summary> The Python words the helper and the route need from <c>datetime</c>. </summary>
    public static string[] DateWords(Names n) => ["date", "datetime", "time", "timedelta"];

    public static string Helper(Names n) => Fill(HelperText, n, ("§FIND", Find(n)));

    /// <summary> <c>find_rate</c>: the row in force for a pair on a day, and whether it was worked out from the opposite pair. The document router imports it. </summary>
    private static string Find(Names n)
    {
        string onOrBefore = n.DayAnnotation == "datetime" ? $"{n.Class}.{n.Day} < datetime.combine(day.date(), time.min) + timedelta(days=1)" : $"{n.Class}.{n.Day} <= day";
        string inverse = n.Inverse ? Fill(InverseText, n, ("§ONORBEFORE", onOrBefore)) + "\n" : "";
        return Fill(FindText, n, ("§ONORBEFORE", onOrBefore), ("§INVERSE", inverse));
    }

    /// <summary> <c>GET rate</c>, placed before the route of one row so the fixed path wins. </summary>
    public static string Route(Names n, string route, string readDepends, string userParameter) => Fill(RouteText, n, ("§ROUTE", route), ("§READ", readDepends), ("§USER", userParameter),
        ("§TODAY", n.DayAnnotation == "datetime" ? "datetime.combine(date.today(), time.min)" : "date.today()"),
        ("§HEADER", n.Inverse ? "response: Response, " : ""),
        ("§INVERTED", n.Inverse ? $"    if inverted:\n        response.headers[\"{RateWords.InvertedHeader}\"] = \"true\"\n" : ""));

    /// <summary> The line after <c>check_rate</c> that keeps the pair and day for <see cref="RefusedLines"/>. </summary>
    public static string PairLine(Names n) => $"    pair = (row.{n.From}, row.{n.To}, row.{n.Day})";

    /// <summary> The lines after the rollback of a commit the database refused: a rate another request saved a moment ago (stopped by the unique index) answers as the rule does. </summary>
    public static string RefusedLines(string key) =>
        $"        if rate_taken(session, *pair, {key}):   # the unique index stopped a rate another request saved a moment ago\n            raise rate_refusal()";

    private static string Fill(string text, Names n, params (string Token, string Value)[] more)
    {
        var values = new List<(string, string)>
        {
            ("§CLASS", n.Class), ("§KEY", n.Key), ("§FROM", n.From), ("§TO", n.To), ("§RATE", n.Rate), ("§DAY", n.Day), ("§DATE", n.DayAnnotation), ("§SCHEMA", n.Schema),
            ("§INVALIDTYPE", RateWords.InvalidType), ("§INVALIDTITLE", RateWords.InvalidTitle), ("§TAKENTYPE", RateWords.TakenType), ("§TAKENTITLE", RateWords.TakenTitle),
            ("§MISSINGTYPE", RateWords.MissingType), ("§MISSINGTITLE", RateWords.MissingTitle),
            ("§W_NEEDS", RateWords.NeedsCurrencies), ("§W_SAME", RateWords.SameCurrency), ("§W_POSITIVE", RateWords.NotPositive), ("§W_TAKEN", RateWords.Taken), ("§W_PAIR", RateWords.NeedsPair)
        };
        values.AddRange(more);
        return TemplateText.Fill(text, [.. values]);
    }

    private const string FindText = """"
def find_rate(session: Session, source: str, target: str, day):
    """The row in force for the pair on a day: the latest one on or before it (the second value is true when it is worked out from the opposite pair)."""
    statement = select(§CLASS).where(func.upper(§CLASS.§FROM) == source, func.upper(§CLASS.§TO) == target, §ONORBEFORE)
    row = session.scalars(statement.order_by(§CLASS.§DAY.desc(), §CLASS.§KEY.desc()).limit(1)).first()
§INVERSE    return row, False
"""";

    private const string InverseText = """"
    opposite = select(§CLASS).where(func.upper(§CLASS.§FROM) == target, func.upper(§CLASS.§TO) == source, §ONORBEFORE)
    other = session.scalars(opposite.order_by(§CLASS.§DAY.desc(), §CLASS.§KEY.desc()).limit(1)).first()
    if other is not None and (row is None or other.§DAY > row.§DAY):
        session.expunge(other)
        other.§FROM, other.§TO = other.§TO, other.§FROM
        other.§RATE = 1 / other.§RATE
        other.§KEY = 0
        return other, True
"""";

    private const string HelperText = """"
def rate_taken(session: Session, source: str, target: str, day, key) -> bool:
    """Whether a row other than the one with this key holds a rate for the pair on the day."""
    start = datetime.combine(day.date(), time.min) if isinstance(day, datetime) else day
    conditions = [func.upper(§CLASS.§FROM) == source, func.upper(§CLASS.§TO) == target]
    if isinstance(start, datetime):
        conditions += [§CLASS.§DAY >= start, §CLASS.§DAY < start + timedelta(days=1)]
    else:
        conditions.append(§CLASS.§DAY == start)
    if key is not None:
        conditions.append(§CLASS.§KEY != key)
    return session.scalars(select(§CLASS.§KEY).where(*conditions).limit(1)).first() is not None


§FIND

def rate_refusal() -> ApiError:
    return ApiError(409, "§TAKENTITLE", problem_type="§TAKENTYPE", detail="§W_TAKEN")


def check_rate(session: Session, row) -> None:
    """The currencies are trimmed and upper case and differ, the rate is above zero, and no other row holds a rate for the pair on the same day."""
    row.§FROM = (row.§FROM or "").strip().upper()
    row.§TO = (row.§TO or "").strip().upper()
    if not row.§FROM or not row.§TO:
        raise ApiError(400, "§INVALIDTITLE", problem_type="§INVALIDTYPE", detail="§W_NEEDS")
    if row.§FROM == row.§TO:
        raise ApiError(400, "§INVALIDTITLE", problem_type="§INVALIDTYPE", detail="§W_SAME")
    if row.§RATE is None or row.§RATE <= 0:
        raise ApiError(400, "§INVALIDTITLE", problem_type="§INVALIDTYPE", detail="§W_POSITIVE")
    with session.no_autoflush:
        taken = rate_taken(session, row.§FROM, row.§TO, row.§DAY, row.§KEY)
    if taken:
        raise rate_refusal()
"""";

    private const string RouteText = """"
@router.get("§ROUTE/rate", response_model=§SCHEMA§READ)
def rate_on(§HEADERsource: str | None = Query(None, alias="from"), target: str | None = Query(None, alias="to"), on: §DATE | None = Query(None), session: Session = Depends(get_session)§USER):
    """The rate in force on a day (today when none is given): the latest row of the pair on or before it."""
    if source is None or not source.strip() or target is None or not target.strip():
        raise ApiError(400, "§W_PAIR")
    source, target = source.strip().upper(), target.strip().upper()
    if source == target:
        raise ApiError(400, "§INVALIDTITLE", problem_type="§INVALIDTYPE", detail="§W_SAME")
    day = on if on is not None else §TODAY
    row, inverted = find_rate(session, source, target, day)
    if row is None:
        raise ApiError(404, "§MISSINGTITLE", problem_type="§MISSINGTYPE", detail=f"There is no rate from {source} to {target} on or before that day.")
§INVERTED    return row
"""";
}
