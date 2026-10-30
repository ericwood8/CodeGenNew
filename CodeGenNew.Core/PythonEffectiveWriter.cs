using System.Text;

namespace CodeGenNew.Core;

/// <summary> The Python (FastAPI) counterpart of the effective-dating part of <see cref="RowShapeWriter"/>: a table listed in <c>EffectiveDatedTables</c> refuses a period that runs backwards or overlaps another row of the same group,
/// and answers <c>GET current</c> with the rows in effect on a day. Written into the table's own router (PY_Routes). </summary>
public static class PythonEffectiveWriter
{
    /// <summary> A group column: the attribute, the JSON name of its query parameter and its Python type. </summary>
    public sealed record Group(string Attribute, string Json, string Annotation);

    /// <summary> The names an effective-dated table needs from its model. <paramref name="DateAnnotation"/> is <c>date</c> or <c>datetime</c>. </summary>
    public sealed record Names(string Class, string Key, string From, string To, string DateAnnotation, IReadOnlyList<Group> Groups, string TenantFilter, string Schema);

    /// <summary> The helper above the routes. </summary>
    public static string Helper(Names n) => Fill(Check, n);

    /// <summary> The groups of a row as the argument of the check: <c>{"item_id": body.item_id}</c>; <paramref name="value"/> turns an attribute into the expression that reads it. </summary>
    public static string GroupArgument(Names n, Func<string, string> value) => "{" + string.Join(", ", n.Groups.Select(g => $"\"{g.Attribute}\": {value(g.Attribute)}")) + "}";

    /// <summary> <c>GET current</c>, placed before the route of one row so the fixed path wins. </summary>
    public static string CurrentRoute(Names n, string route, string readDepends, string userParameter)
    {
        var parameters = new StringBuilder();
        var lines = new StringBuilder();
        foreach (var g in n.Groups)
        {
            parameters.Append($", {g.Attribute}: {g.Annotation} | None = Query(None, alias=\"{g.Json}\")");
            lines.Append($"    if {g.Attribute} is not None:\n        statement = statement.where({n.Class}.{g.Attribute} == {g.Attribute})\n");
        }
        string today = n.DateAnnotation == "datetime" ? "datetime.combine(date.today(), time.min)" : "date.today()";
        return Fill(CurrentText, n, ("§ROUTE", route), ("§READ", readDepends), ("§USER", userParameter), ("§PARAMETERS", parameters.ToString()), ("§GROUPLINES", lines.ToString()), ("§TODAY", today));
    }

    private static string Fill(string text, Names n, params (string Token, string Value)[] more)
    {
        var values = new List<(string, string)>
        {
            ("§CLASS", n.Class), ("§KEY", n.Key), ("§FROM", n.From), ("§TO", n.To), ("§DATE", n.DateAnnotation), ("§SCHEMA", n.Schema),
            ("§RANGETYPE", RowShapeWords.EffectiveRangeType), ("§OVERLAPTYPE", RowShapeWords.EffectiveOverlapType), ("§W_RANGE", RowShapeWords.BackwardsRange), ("§W_OVERLAP", RowShapeWords.Overlap),
            ("§TENANT", n.TenantFilter.Length == 0 ? "" : n.TenantFilter + "\n")
        };
        values.AddRange(more);
        foreach (var (token, value) in values.OrderByDescending(v => v.Item1.Length))
            text = text.Replace(token, value);
        return text;
    }

    private const string Check = """"
def check_effective(session: Session, row_id, valid_from, valid_to, groups: dict) -> None:
    """The period must not run backwards and must not overlap another row of the same group (both ends are inclusive; an empty end is open)."""
    if valid_to is not None and valid_to < valid_from:
        raise ApiError(400, "Not a valid period", problem_type="§RANGETYPE", detail="§W_RANGE")
    statement = select(§CLASS.§KEY)
    if row_id is not None:
        statement = statement.where(§CLASS.§KEY != row_id)
    for name, value in groups.items():
        statement = statement.where(getattr(§CLASS, name) == value)
    if valid_to is not None:
        statement = statement.where(§CLASS.§FROM <= valid_to)
    statement = statement.where(or_(§CLASS.§TO.is_(None), §CLASS.§TO >= valid_from))
    if session.scalars(statement.limit(1)).first() is not None:
        raise ApiError(409, "Overlapping period", problem_type="§OVERLAPTYPE", detail="§W_OVERLAP")
"""";

    private const string CurrentText = """"
@router.get("§ROUTE/current", response_model=list[§SCHEMA]§READ)
def current(on: §DATE | None = Query(None), session: Session = Depends(get_session)§USER§PARAMETERS):
    """The rows in effect on a day (today when none is given), narrowed by the group columns that are given."""
    day = on if on is not None else §TODAY
    statement = select(§CLASS).where(§CLASS.§FROM <= day, or_(§CLASS.§TO.is_(None), §CLASS.§TO >= day))
§GROUPLINES§TENANT    return session.scalars(statement.order_by(§CLASS.§FROM.desc(), §CLASS.§KEY)).all()


"""";
}
