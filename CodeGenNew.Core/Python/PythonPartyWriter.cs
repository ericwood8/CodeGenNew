namespace CodeGenNew.Core.Python;

/// <summary> The Python (FastAPI) counterpart of the party part of <see cref="RowShapeWriter"/> (<c>PartyTables</c>): the identifier table's router gets <c>check_identifier</c>, called before a create or an update commits, and
/// the party table's router gets <c>GET by-identifier</c>. The words are <see cref="PartyWords"/>. </summary>
public static class PythonPartyWriter
{
    /// <summary> The identifier table as Python attributes: its class, key, scheme and value. </summary>
    public sealed record IdentifierNames(string Class, string Key, string Scheme, string Value);

    /// <summary> The party table and its identifier table as Python names: the link is the identifier's attribute that holds the party's key. </summary>
    public sealed record PartyNames(string Class, string Key, string IdentifierClass, string IdentifierModule, string Link, string Scheme, string Value);

    /// <summary> The helper above the routes of the identifier table. </summary>
    public static string Helper(IdentifierNames n) => TemplateText.FillAll(HelperText, ("§CLASS", n.Class), ("§KEY", n.Key), ("§SCHEME", n.Scheme), ("§VALUE", n.Value),
        ("§TITLE", PartyWords.IdentifierTakenTitle), ("§TYPE", PartyWords.IdentifierTakenType), ("§W_TAKEN", PartyWords.IdentifierTaken));

    /// <summary> <c>GET by-identifier</c> of the party table; it goes before the route that reads one row by its key. </summary>
    public static string Route(PartyNames n, string route, string schema, string readDepends, string userParameter) => TemplateText.FillAll(RouteText, ("§CLASS", n.Class), ("§KEY", n.Key),
        ("§ICLASS", n.IdentifierClass), ("§LINK", n.Link), ("§SCHEME", n.Scheme), ("§VALUE", n.Value), ("§ROUTE", route), ("§SCHEMA", schema), ("§READ", readDepends), ("§USER", userParameter),
        ("§LIMIT", PartyWords.FindLimit.ToString()), ("§W_VALUE", PartyWords.ValueNeeded));

    /// <summary> The line after <c>check_identifier</c> that keeps the pair for <see cref="RefusedLines"/>. </summary>
    public static string PairLine(IdentifierNames n) => $"    pair = (row.{n.Scheme}, row.{n.Value})";

    /// <summary> The lines after the rollback of a commit the database refused: a pair another row holds by now (two saves at the same moment, stopped by the unique index) answers as the rule does. </summary>
    public static string RefusedLines(string key) =>
        $"        if identifier_taken(session, *pair, {key}):   # the unique index stopped a pair another request saved a moment ago\n            raise identifier_refusal()";

    private const string HelperText = """"
def identifier_taken(session: Session, scheme: str, value: str, key) -> bool:
    """Whether a row other than the one with this key holds the scheme and value pair, compared without regard to case."""
    others = [func.lower(§CLASS.§SCHEME) == scheme.lower(), func.lower(§CLASS.§VALUE) == value.lower()]
    if key is not None:
        others.append(§CLASS.§KEY != key)
    return session.scalars(select(§CLASS.§KEY).where(*others).limit(1)).first() is not None


def identifier_refusal() -> ApiError:
    return ApiError(409, "§TITLE", problem_type="§TYPE", detail="§W_TAKEN")


def check_identifier(session: Session, row) -> None:
    """A scheme and value pair is held by one row only: both are trimmed, and a pair another row holds (compared without regard to case) is refused."""
    row.§SCHEME = (row.§SCHEME or "").strip()
    row.§VALUE = (row.§VALUE or "").strip()
    with session.no_autoflush:
        taken = identifier_taken(session, row.§SCHEME, row.§VALUE, row.§KEY)
    if taken:
        raise identifier_refusal()
"""";

    private const string RouteText = """"
@router.get("§ROUTE/by-identifier", response_model=list[§SCHEMA]§READ)
def by_identifier(value: str | None = None, scheme: str | None = None, session: Session = Depends(get_session)§USER):
    """The rows that hold an identifier with this value (of this scheme, when one is given), compared without regard to case; at most §LIMIT."""
    if value is None or not value.strip():
        raise ApiError(400, "§W_VALUE")
    held = select(§ICLASS.§LINK).where(func.lower(§ICLASS.§VALUE) == value.strip().lower())
    if scheme is not None and scheme.strip():
        held = held.where(func.lower(§ICLASS.§SCHEME) == scheme.strip().lower())
    return session.scalars(select(§CLASS).where(§CLASS.§KEY.in_(held)).order_by(§CLASS.§KEY).limit(§LIMIT)).all()
"""";
}
