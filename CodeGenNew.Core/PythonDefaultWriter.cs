namespace CodeGenNew.Core;

/// <summary> The Python (FastAPI) counterpart of the default-row part of <see cref="RowShapeWriter"/>: a table listed in <c>DefaultRowTables</c> keeps exactly one default row per group. The helpers and the
/// <c>POST {id}/make-default</c> route are written into the table's own router (PY_Routes); the save and the delete call them before the commit, so the marks move in the same transaction. </summary>
public static class PythonDefaultWriter
{
    /// <summary> The names a default-row table needs from its model: the class, the key, the flag and the group columns (all Python attributes). </summary>
    public sealed record Names(string Class, string Key, string Flag, IReadOnlyList<string> Groups);

    /// <summary> The groups of a row as the argument of the helpers: <c>{"customer_id": body.customer_id}</c>; <paramref name="value"/> turns an attribute into the expression that reads it. </summary>
    public static string GroupArgument(Names n, Func<string, string> value) => "{" + string.Join(", ", n.Groups.Select(g => $"\"{g}\": {value(g)}")) + "}";

    /// <summary> The helpers above the routes. </summary>
    public static string Helpers(Names n) => Fill(HelperText, n);

    /// <summary> <c>POST {id}/make-default</c>. </summary>
    public static string Route(Names n, string route, string schema, string keyType, string writeDepends, string userParameter) =>
        Fill(RouteText, n, ("§ROUTE", route), ("§SCHEMA", schema), ("§KEYTYPE", keyType), ("§WRITE", writeDepends), ("§USER", userParameter), ("§GROUPS", GroupArgument(n, g => "row." + g)));

    private static string Fill(string text, Names n, params (string Token, string Value)[] more)
    {
        var values = new List<(string, string)> { ("§CLASS", n.Class), ("§KEY", n.Key), ("§FLAG", n.Flag) };
        values.AddRange(more);
        return TemplateText.FillAll(text, [.. values]);
    }

    private const string HelperText = """"
def _others_of_group(row_id, groups: dict) -> list:
    conditions = [getattr(§CLASS, name) == value for name, value in groups.items()]
    if row_id is not None:
        conditions.append(§CLASS.§KEY != row_id)
    return conditions


def settle_default(session: Session, row_id, groups: dict, wants_default: bool) -> bool:
    """Exactly one row of a group is the default: a row saved without the mark is marked when its group has no default, and a row saved with it takes the mark from the others of its group (in the transaction the caller commits).
    Returns the mark the row is saved with."""
    with session.no_autoflush:
        others = _others_of_group(row_id, groups) + [§CLASS.§FLAG.is_(True)]
        has_default = session.scalars(select(§CLASS.§KEY).where(*others).limit(1)).first() is not None
        if not wants_default:
            return not has_default
        if has_default:
            session.execute(sql_update(§CLASS).where(*others).values({§CLASS.§FLAG: False}))
    return True


def promote_default(session: Session, row, groups: dict) -> None:
    """Before the default row of a group is deleted, the mark moves to the next row of the group (the lowest key); the deleted row never keeps it."""
    if not row.§FLAG:
        return
    with session.no_autoflush:
        following = session.scalars(select(§CLASS.§KEY).where(*_others_of_group(row.§KEY, groups)).order_by(§CLASS.§KEY).limit(1)).first()
        session.execute(sql_update(§CLASS).where(§CLASS.§KEY == row.§KEY).values({§CLASS.§FLAG: False}))
        if following is not None:
            session.execute(sql_update(§CLASS).where(§CLASS.§KEY == following).values({§CLASS.§FLAG: True}))
"""";

    private const string RouteText = """"
@router.post("§ROUTE/{id}/make-default", response_model=§SCHEMA§WRITE)
def make_default(id: §KEYTYPE, session: Session = Depends(get_session)§USER):
    """The row becomes the default of its group and the others lose the mark, in one transaction."""
    row = session.get(§CLASS, id)
    if row is None:
        raise ApiError(404, "Not found.")
    if not row.§FLAG:
        settle_default(session, id, §GROUPS, True)
        row.§FLAG = True
        session.commit()
        session.refresh(row)
    return row
"""";
}
