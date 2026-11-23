using CodeGenNew.Core.Price;

namespace CodeGenNew.Core.Python;

/// <summary> The Python (FastAPI) counterpart of the price part of <see cref="RowShapeWriter"/> (<c>PriceListTables</c>): the price table's router gets <c>check_price</c>, called before a create or an update commits, and
/// <c>GET price</c>. The words are <see cref="PriceWords"/>. </summary>
public static class PythonPriceWriter
{
    /// <summary> The price table and the list table as Python attributes: <paramref name="Group"/> is the list's customer group column, or null. </summary>
    public sealed record Names(string Class, string Key, string List, string Item, string ItemAnnotation, string Min, string Price, string Schema, string ListClass, string ListKey, string Currency, string From, string To, string? Group);

    /// <summary> The names of <paramref name="plan"/> for the price table <paramref name="t"/> and its list table, or null when there is no plan or a column has no field. </summary>
    public static Names? NamesFor(PriceListShape? plan, PythonTable t, PythonTable? listTable)
    {
        if (plan is null || listTable is null)
            return null;
        var list = t.SchemaFieldOf(plan.List);
        var item = t.SchemaFieldOf(plan.Item);
        var min = t.SchemaFieldOf(plan.MinQuantity);
        var price = t.SchemaFieldOf(plan.Price);
        var key = t.SchemaFieldOf(plan.Key);
        var listKey = listTable.SchemaFieldOf(plan.ListKey);
        var currency = listTable.SchemaFieldOf(plan.Currency);
        var from = listTable.SchemaFieldOf(plan.ValidFrom);
        var to = listTable.SchemaFieldOf(plan.ValidTo);
        var group = plan.Group is null ? null : listTable.SchemaFieldOf(plan.Group);
        if (list is null || item is null || min is null || price is null || key is null || listKey is null || currency is null || from is null || to is null || plan.Group is not null && group is null)
            return null;
        return new Names(t.Class, key.Name, list.Name, item.Name, item.Annotation, min.Name, price.Name, t.Schema, listTable.Class, listKey.Name, currency.Name, from.Name, to.Name, group?.Name);
    }

    /// <summary> The Python words the helper and the route need from <c>datetime</c>. </summary>
    public static string[] DateWords(Names n) => ["date"];

    /// <summary> The imports of SQLAlchemy the helper needs. </summary>
    public static string[] SqlAlchemyWords => ["func", "or_"];

    public static string Helper(Names n) => Fill(HelperText, n, ("§GROUPTEST", n.Group is null ? "" : $", (func.upper({n.ListClass}.{n.Group}) == held) if held is not None else {n.ListClass}.{n.Group}.is_(None)"));

    /// <summary> <paramref name="scopeLines"/> are the lines (indented four spaces, on <c>statement</c>) that limit the price rows to the caller's owners or tenant; empty for a table that is not limited. </summary>
    public static string Route(Names n, string route, string readDepends, string userParameter, string scopeLines = "") => Fill(RouteText, n, ("§ROUTE", route), ("§READ", readDepends), ("§USER", userParameter),
        ("§SCOPEDEF", scopeLines.Length == 0 ? "" : "    def scope(statement):\n" + string.Concat(scopeLines.TrimEnd('\n').Split('\n').Select(l => "    " + l + "\n")) + "        return statement\n"),
        ("§SCOPEARG", scopeLines.Length == 0 ? "" : ", scope"),
        ("§GROUPPARAM", n.Group is null ? "" : ", group: str | None = Query(None)"), ("§GROUPARG", n.Group is null ? "None" : "held"),
        ("§GROUPVALUE", n.Group is null ? "" : "    held = group.strip().upper() if group is not None and group.strip() else None\n"),
        ("§MISSING", "f\"" + PriceWords.MissingFormat.Replace("{0}", "{item}").Replace("{1}", "{asked}") + "\""));

    /// <summary> The lines before <c>check_price</c> for a row with an owner column of its own whose list has one too: the list must belong to the row's owner (400 otherwise). </summary>
    public static string OwnerLines(Names n, string rowOwner, string listOwner) =>
        $"    listed = select({n.ListClass}.{n.ListKey}).where({n.ListClass}.{n.ListKey} == row.{n.List}, {n.ListClass}.{listOwner} == row.{rowOwner})\n"
        + $"    if session.scalars(listed.limit(1)).first() is None:\n        raise ApiError(400, \"{PriceWords.InvalidTitle}\", problem_type=\"{PriceWords.InvalidType}\", detail=\"{PriceWords.ListMissing}\")";

    /// <summary> The lines before <c>check_price</c> for a table limited to a tenant: the list the row names must be one of the caller's tenant (400 otherwise), so a row cannot point at another tenant's list. </summary>
    public static string ListLines(Names n, string tenantFunction, string listTenantAttribute) =>
        $"    listed = {tenantFunction}(select({n.ListClass}.{n.ListKey}).where({n.ListClass}.{n.ListKey} == row.{n.List}), {n.ListClass}.{listTenantAttribute}, user)\n"
        + $"    if session.scalars(listed.limit(1)).first() is None:\n        raise ApiError(400, \"{PriceWords.InvalidTitle}\", problem_type=\"{PriceWords.InvalidType}\", detail=\"{PriceWords.ListMissing}\")";

    /// <summary> The line after <c>check_price</c> that keeps the list, item and minimum quantity for <see cref="RefusedLines"/>. </summary>
    public static string PairLine(Names n) => $"    pair = (row.{n.List}, row.{n.Item}, row.{n.Min})";

    /// <summary> The lines after the rollback of a commit the database refused: a price another request saved a moment ago (stopped by the unique index) answers as the rule does. </summary>
    public static string RefusedLines(string key) =>
        $"        if price_taken(session, *pair, {key}):   # the unique index stopped a price another request saved a moment ago\n            raise price_refusal()";

    private static string Fill(string text, Names n, params (string Token, string Value)[] more)
    {
        var values = new List<(string, string)>
        {
            ("§CLASS", n.Class), ("§KEY", n.Key), ("§LISTCLASS", n.ListClass), ("§LISTKEY", n.ListKey), ("§LIST", n.List), ("§ITEMTYPE", n.ItemAnnotation), ("§ITEM", n.Item), ("§MIN", n.Min), ("§PRICE", n.Price), ("§SCHEMA", n.Schema),
            ("§CURRENCY", n.Currency), ("§FROM", n.From), ("§TO", n.To),
            ("§INVALIDTYPE", PriceWords.InvalidType), ("§INVALIDTITLE", PriceWords.InvalidTitle), ("§TAKENTYPE", PriceWords.TakenType), ("§TAKENTITLE", PriceWords.TakenTitle),
            ("§MISSINGTYPE", PriceWords.MissingType), ("§MISSINGTITLE", PriceWords.MissingTitle),
            ("§W_MIN", PriceWords.QuantityNotPositive), ("§W_PRICE", PriceWords.PriceNegative), ("§W_TAKEN", PriceWords.Taken), ("§W_LOOKUP", PriceWords.NeedsLookup), ("§W_QTY", PriceWords.AskedQuantity)
        };
        values.AddRange(more);
        return TemplateText.Fill(text, [.. values]);
    }

    private const string HelperText = """"
def price_taken(session: Session, list_id, item, minimum, key) -> bool:
    """Whether a row other than the one with this key holds a price for the item in the list from this minimum quantity."""
    conditions = [§CLASS.§LIST == list_id, §CLASS.§ITEM == item, §CLASS.§MIN == minimum]
    if key is not None:
        conditions.append(§CLASS.§KEY != key)
    return session.scalars(select(§CLASS.§KEY).where(*conditions).limit(1)).first() is not None


def find_price(session: Session, item, quantity, day, currency: str, group: str | None, scope=None):
    """The row in force: of the lists in the currency that are valid on the day the row of the item with the largest minimum quantity not above the quantity. A group's lists come before the lists with no group."""
    for held in ([group, None] if group is not None else [None]):
        lists = select(§LISTCLASS.§LISTKEY).where(func.upper(§LISTCLASS.§CURRENCY) == currency, §LISTCLASS.§FROM <= day, or_(§LISTCLASS.§TO.is_(None), §LISTCLASS.§TO >= day)§GROUPTEST)
        statement = select(§CLASS).where(§CLASS.§ITEM == item, §CLASS.§MIN <= quantity, §CLASS.§LIST.in_(lists))
        if scope is not None:
            statement = scope(statement)
        row = session.scalars(statement.order_by(§CLASS.§MIN.desc(), §CLASS.§KEY.desc()).limit(1)).first()
        if row is not None:
            return row
    return None


def price_refusal() -> ApiError:
    return ApiError(409, "§TAKENTITLE", problem_type="§TAKENTYPE", detail="§W_TAKEN")


def check_price(session: Session, row) -> None:
    """The minimum quantity is above zero, the price is not negative, and no other row of the list holds a price for the item from the same quantity."""
    if row.§MIN is None or row.§MIN <= 0:
        raise ApiError(400, "§INVALIDTITLE", problem_type="§INVALIDTYPE", detail="§W_MIN")
    if row.§PRICE is None or row.§PRICE < 0:
        raise ApiError(400, "§INVALIDTITLE", problem_type="§INVALIDTYPE", detail="§W_PRICE")
    with session.no_autoflush:
        taken = price_taken(session, row.§LIST, row.§ITEM, row.§MIN, row.§KEY)
    if taken:
        raise price_refusal()
"""";

    private const string RouteText = """"
@router.get("§ROUTE/price", response_model=§SCHEMA§READ)
def price_on(item: §ITEMTYPE | None = Query(None), quantity: float | None = Query(None), on: date | None = Query(None), currency: str | None = Query(None)§GROUPPARAM, session: Session = Depends(get_session)§USER):
    """The price in force for an item and a quantity (1 when none is given) on a day (today when none is given) in a currency."""
    if item is None or currency is None or not currency.strip():
        raise ApiError(400, "§W_LOOKUP")
    wanted = quantity if quantity is not None else 1
    if wanted <= 0:
        raise ApiError(400, "§INVALIDTITLE", problem_type="§INVALIDTYPE", detail="§W_QTY")
    asked = currency.strip().upper()
§GROUPVALUE§SCOPEDEF    row = find_price(session, item, wanted, on if on is not None else date.today(), asked, §GROUPARG§SCOPEARG)
    if row is None:
        raise ApiError(404, "§MISSINGTITLE", problem_type="§MISSINGTYPE", detail=§MISSING)
    return row
"""";
}
