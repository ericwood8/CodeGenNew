namespace CodeGenNew.Core;

/// <summary> The Python (FastAPI) counterpart of the soft-delete part of <see cref="RowShapeWriter"/>: a table listed in <c>SoftDeleteTables</c> hides its flagged rows from every read, its delete sets the flag, and <c>GET deleted</c> and
/// <c>POST {id}/restore</c> list and bring back flagged rows. Written into the table's own router (PY_Routes). </summary>
public static class PythonSoftDeleteWriter
{
    /// <summary> The names a soft-delete table needs from its model: class, key, flag attribute, optional stamp attributes (and the longest <c>DeletedBy</c>), the line that limits a statement to the caller's tenant (empty for none), and the schema class. </summary>
    public sealed record Names(string Class, string Key, string Flag, string? DeletedOn, string? DeletedBy, int ByLength, string TenantFilter, string Schema);

    /// <summary> The listener that adds the flag test to every select of the table: the counterpart of the query filter of the ASP.NET context. A statement run with <c>include_deleted</c> is left alone. </summary>
    public static string Filter(Names n) => Fill(FilterText, n);

    /// <summary> The lines of the delete route that flag the row instead of removing it (the row is loaded and the checks have run). </summary>
    public static string DeleteLines(Names n, bool userKnown) => Fill(DeleteText, n,
        ("§ONLINE", n.DeletedOn is null ? "" : $"    row.{n.DeletedOn} = datetime.now()\n"),
        ("§BYLINE", n.DeletedBy is null || !userKnown ? "" : $"    row.{n.DeletedBy} = user.name[:{(n.ByLength > 0 ? n.ByLength : 1000000)}]\n"));

    /// <summary> <c>GET deleted</c> and <c>POST {id}/restore</c>, placed before the route of one row so the fixed path wins. </summary>
    public static string Routes(Names n, string route, string keyType, string adminDepends, string userParameter) => Fill(RoutesText, n,
        ("§ROUTE", route), ("§KT", keyType), ("§ADMIN", adminDepends), ("§USER", userParameter),
        ("§ORDER", n.DeletedOn is null ? $"{n.Class}.{n.Key}.desc()" : $"{n.Class}.{n.DeletedOn}.desc(), {n.Class}.{n.Key}.desc()"),
        ("§CLEARON", n.DeletedOn is null ? "" : $"    row.{n.DeletedOn} = None\n"),
        ("§CLEARBY", n.DeletedBy is null ? "" : $"    row.{n.DeletedBy} = None\n"));

    private static string Fill(string text, Names n, params (string Token, string Value)[] more)
    {
        var values = new List<(string, string)>
        {
            ("§CLASS", n.Class), ("§KEY", n.Key), ("§FLAG", n.Flag), ("§SCHEMA", n.Schema), ("§NOTDELETEDTYPE", RowShapeWords.NotDeletedType), ("§W_NOTDELETED", RowShapeWords.NotDeleted),
            ("§TENANT", n.TenantFilter.Length == 0 ? "" : n.TenantFilter + "\n")
        };
        values.AddRange(more);
        foreach (var (token, value) in values.OrderByDescending(v => v.Item1.Length))
            text = text.Replace(token, value);
        return text;
    }

    private const string FilterText = """"
@event.listens_for(Session, "do_orm_execute")
def hide_deleted_§CLASS(state) -> None:
    """A row flagged as deleted is left out of every read of the table; a statement with the include_deleted option sees them."""
    if state.is_select and not state.execution_options.get("include_deleted", False):
        state.statement = state.statement.options(with_loader_criteria(§CLASS, §CLASS.§FLAG.is_(False), include_aliases=True))


"""";

    private const string DeleteText = """"
    row.§FLAG = True
§ONLINE§BYLINE    session.commit()
    return Response(status_code=200)
"""";

    private const string RoutesText = """"
@router.get("§ROUTE/deleted", response_model=list[§SCHEMA]§ADMIN)
def get_deleted(session: Session = Depends(get_session)§USER):
    """The rows that were soft deleted, the last deleted first."""
    statement = select(§CLASS).where(§CLASS.§FLAG.is_(True)).execution_options(include_deleted=True)
§TENANT    return session.scalars(statement.order_by(§ORDER).limit(500)).all()


@router.post("§ROUTE/{id}/restore", response_model=§SCHEMA§ADMIN)
def restore(id: §KT, session: Session = Depends(get_session)§USER):
    """A deleted row comes back; 404 when there is no such row, 409 when it is not deleted."""
    statement = select(§CLASS).where(§CLASS.§KEY == id).execution_options(include_deleted=True)
§TENANT    row = session.scalars(statement).first()
    if row is None:
        raise ApiError(404, "Not found.")
    if not row.§FLAG:
        raise ApiError(409, "Not deleted", problem_type="§NOTDELETEDTYPE", detail="§W_NOTDELETED")
    row.§FLAG = False
§CLEARON§CLEARBY    session.commit()
    session.refresh(row)
    return row


"""";
}
