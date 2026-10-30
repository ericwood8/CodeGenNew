using System.Text;

namespace CodeGenNew.Core;

/// <summary> The Python (FastAPI) counterpart of the tree part of <see cref="RowShapeWriter"/>: the checks and routes of a table listed in <c>TreeTables</c>, written into the table's own router (PY_Routes). </summary>
public static class PythonTreeWriter
{
    /// <summary> The names a tree needs from the table's model: its class, key, parent and optional text attribute, and the line that limits a statement to the caller's tenant (empty for none). </summary>
    public sealed record Names(string Class, string Key, string Parent, string? Text, string TenantFilter);

    /// <summary> The helpers placed above the routes: the depth limit, <c>check_tree</c> and <c>refuse_children</c>. </summary>
    public static string Helpers(TreeTable tree, Names n)
    {
        var o = new StringBuilder();
        o.Append($"TREE_MAX_DEPTH = {tree.MaxDepth}\n\n\n");
        o.Append(Fill(Check, n, tree));
        return o.ToString();
    }

    /// <summary> <c>GET tree</c>, placed before the route of one row so the fixed path wins. </summary>
    public static string TreeRoute(Names n, string route, string readDepends, string userParameter) =>
        Fill(TreeText, n, null, ("§ROUTE", route), ("§READ", readDepends), ("§USER", userParameter));

    /// <summary> <c>POST {id}/promote-children</c>. </summary>
    public static string PromoteRoute(Names n, string route, string keyType, string writeDepends, string userParameter) =>
        Fill(PromoteText, n, null, ("§ROUTE", route), ("§KT", keyType), ("§WRITE", writeDepends), ("§USER", userParameter));

    private static string Fill(string text, Names n, TreeTable? tree, params (string Token, string Value)[] more)
    {
        var values = new List<(string, string)>
        {
            ("§CLASS", n.Class), ("§KEY", n.Key), ("§PARENT", n.Parent), ("§TEXTVALUE", n.Text is null ? "str(r[0])" : "(r[2] or \"\")"),
            ("§TEXTCOLUMN", n.Text is null ? "" : $", {n.Class}.{n.Text}"), ("§CYCLE", RowShapeWords.TreeCycleType), ("§DEEP", RowShapeWords.TreeTooDeepType), ("§CHILDREN", RowShapeWords.TreeHasChildrenType),
            ("§W_OWN", RowShapeWords.OwnParent), ("§W_UNDER", RowShapeWords.UnderItself), ("§W_CHILDREN", RowShapeWords.HasChildren),
            ("§TENANT", n.TenantFilter.Length == 0 ? "" : n.TenantFilter + "\n")
        };
        if (tree is not null)
            values.Add(("§W_DEEP", RowShapeWords.TooDeep(tree.MaxDepth)));
        values.AddRange(more);
        foreach (var (token, value) in values.OrderByDescending(v => v.Item1.Length))
            text = text.Replace(token, value);
        return text;
    }

    private const string Check = """"
def check_tree(session: Session, row_id, parent_id) -> None:
    """A parent that is the row itself or one of its descendants would make a cycle, and a level past TREE_MAX_DEPTH (0: no limit) is too deep."""
    if parent_id is None:
        return
    if row_id is not None and parent_id == row_id:
        raise ApiError(400, "Not a valid parent", problem_type="§CYCLE", detail="§W_OWN")
    depth = 1
    seen = {row_id}
    up = parent_id
    while up is not None:
        if up in seen:
            raise ApiError(400, "Not a valid parent", problem_type="§CYCLE", detail="§W_UNDER")
        seen.add(up)
        depth += 1
        up = session.scalar(select(§CLASS.§PARENT).where(§CLASS.§KEY == up))
    if TREE_MAX_DEPTH > 0:
        below = 0
        level = [] if row_id is None else [row_id]
        while level and depth + below <= TREE_MAX_DEPTH:
            level = list(session.scalars(select(§CLASS.§KEY).where(§CLASS.§PARENT.in_(level))))
            if not level:
                break
            below += 1
        if depth + below > TREE_MAX_DEPTH:
            raise ApiError(400, "Too deep", problem_type="§DEEP", detail="§W_DEEP")


def refuse_children(session: Session, row_id) -> None:
    """A row with children is not deleted: they would be orphaned or refused by the foreign key."""
    if session.scalar(select(§CLASS.§KEY).where(§CLASS.§PARENT == row_id).limit(1)) is not None:
        raise ApiError(409, "Has child rows", problem_type="§CHILDREN", detail="§W_CHILDREN")
"""";

    private const string TreeText = """"
@router.get("§ROUTE/tree"§READ)
def tree(session: Session = Depends(get_session)§USER):
    """Every row depth first (children after their parent, by name) with its level; only a row with no parent is a root."""
    statement = select(§CLASS.§KEY, §CLASS.§PARENT§TEXTCOLUMN)
§TENANT    rows = [{"id": r[0], "parentId": r[1], "text": §TEXTVALUE} for r in session.execute(statement).all()]
    by_parent: dict = {}
    for r in rows:
        by_parent.setdefault(r["parentId"], []).append(r)
    order = lambda r: (r["text"].lower(), r["id"])
    result: list = []
    seen: set = set()
    stack = [(r, 1) for r in sorted(by_parent.get(None, []), key=order, reverse=True)]
    while stack:
        r, depth = stack.pop()
        if r["id"] in seen:
            continue
        seen.add(r["id"])
        children = sorted(by_parent.get(r["id"], []), key=order)
        result.append({"id": r["id"], "parentId": r["parentId"], "depth": depth, "hasChildren": len(children) > 0, "text": r["text"]})
        stack.extend((child, depth + 1) for child in reversed(children))
    return result


"""";

    private const string PromoteText = """"
@router.post("§ROUTE/{id}/promote-children"§WRITE)
def promote_children(id: §KT, session: Session = Depends(get_session)§USER):
    """The children of the row become children of the row's own parent (roots when it is a root); the row stays."""
    statement = select(§CLASS)
    statement = statement.where(§CLASS.§KEY == id)
§TENANT    row = session.scalars(statement).first()
    if row is None:
        raise ApiError(404, "Not found.")
    moved = session.query(§CLASS).where(§CLASS.§PARENT == id).update({§CLASS.§PARENT: row.§PARENT}, synchronize_session=False)
    session.commit()
    return {"moved": moved}
"""";
}
