using CodeGenNew.Core.Stock;

namespace CodeGenNew.Core.Python;

/// <summary> The Python (FastAPI) counterpart of <see cref="StockWriter"/> (<c>StockTables</c>): the ledger table's router gets the stock routes (receive, reserve, commit, release, expire, adjust, available, low, check) and the level table's router
/// refuses a change of the quantities by hand. Every change of the level is one guarded UPDATE and a ledger row, committed together. The words are <see cref="StockWords"/>. </summary>
public static class PythonStockWriter
{
    /// <summary> The Python names the stock routes of a ledger table need. </summary>
    public sealed record Names(PythonTable Ledger, PythonTable Level, PythonTable Hold, PythonTable Items, string LvItem, string LvOn, string LvRes, string? LvReorder, string ItemAnn, string ItemKey, string LgItem, string LgKind, string LgQty, string LgStamp,
        string? LgRef, string? LgExpires, string HKey, string HKeyAnn, string HItem, string HQty, string HStatus, string HCreated, string HExpires, string? HResolved, string? HRef, string Now, int RefMax, int HoldMinutes, int SweepMinutes = 0)
    {
        /// <summary> The tables whose model the routes read. </summary>
        public IEnumerable<PythonTable> Related => new[] { Level, Hold, Items }.DistinctBy(t => t.Class).Where(t => t.Class != Ledger.Class);

        /// <summary> The Python words the routes need from <c>datetime</c>. </summary>
        public static string[] DateWords => ["datetime", "timedelta"];
    }

    /// <summary> The names a level table's router needs for its save and delete checks. </summary>
    public sealed record LevelNames(string On, string Res);

    public static Names? LedgerNames(StockShape? stock, PythonTable ledger, ProjectSettings project)
    {
        if (stock is null)
            return null;
        var level = PythonTable.Of(stock.Level, project);
        var hold = PythonTable.Of(stock.Reservation.Table, project);
        var items = PythonTable.Of(stock.ItemTable, project);
        var l = stock.Ledger;
        var h = stock.Reservation;
        int refMax = new[] { l.Reference?.CharacterLength ?? 0, h.Reference?.CharacterLength ?? 0 }.Where(n => n > 0).DefaultIfEmpty(0).Min();
        return new Names(ledger, level, hold, items, level.FieldOf(stock.Item).Name, level.FieldOf(stock.OnHand).Name, level.FieldOf(stock.Reserved).Name, stock.Reorder is null ? null : level.FieldOf(stock.Reorder).Name, level.FieldOf(stock.Item).Annotation,
            items.FieldOf(stock.ItemKey).Name, ledger.FieldOf(l.Item).Name, ledger.FieldOf(l.Kind).Name, ledger.FieldOf(l.Quantity).Name, ledger.FieldOf(l.Stamp).Name, l.Reference is null ? null : ledger.FieldOf(l.Reference).Name,
            l.Expires is null ? null : ledger.FieldOf(l.Expires).Name, hold.FieldOf(h.Key).Name, hold.FieldOf(h.Key).Annotation, hold.FieldOf(h.Item).Name, hold.FieldOf(h.Quantity).Name, hold.FieldOf(h.Status).Name, hold.FieldOf(h.Created).Name, hold.FieldOf(h.Expires).Name,
            h.Resolved is null ? null : hold.FieldOf(h.Resolved).Name, h.Reference is null ? null : hold.FieldOf(h.Reference).Name, "datetime.now()", refMax, project.StockHoldMinutes, project.StockSweepMinutes);
    }

    public static LevelNames? LevelFor(StockShape? stock, PythonTable level) =>
        stock is null ? null : new LevelNames(level.FieldOf(stock.OnHand).Name, level.FieldOf(stock.Reserved).Name);

    public const string LevelCreateLine = "    check_stock_new(row)";
    public const string LevelUpdateLine = "    check_stock_changes(row, body.changes())";
    public const string LevelDeleteLine = "    check_stock_delete(row)";

    private static (string, string)[] Words(string suffix = "") =>
    [
        ("§INVALIDTITLE", StockWords.InvalidTitle), ("§INVALIDTYPE", StockWords.InvalidType), ("§UNKNOWNTITLE", StockWords.UnknownTitle), ("§UNKNOWNTYPE", StockWords.UnknownType), ("§SHORTTITLE", StockWords.ShortTitle), ("§SHORTTYPE", StockWords.ShortType),
        ("§CLOSEDTITLE", StockWords.ClosedTitle), ("§CLOSEDTYPE", StockWords.ClosedType), ("§CONFLICTTITLE", StockWords.ConflictTitle), ("§CONFLICTTYPE", StockWords.ConflictType), ("§MANAGEDTITLE", StockWords.ManagedTitle), ("§MANAGEDTYPE", StockWords.ManagedType),
        ("§BELOWTITLE", StockWords.BelowReservedTitle), ("§BELOWTYPE", StockWords.BelowReservedType), ("§W_NEEDSITEM", StockWords.NeedsItem), ("§W_ITEMMISSING", StockWords.ItemMissing), ("§W_QUANTITY", StockWords.QuantityPositive), ("§W_COUNT", StockWords.CountNeeded),
        ("§W_KIND", StockWords.KindBad), ("§W_MINUTES", StockWords.MinutesBad), ("§W_SHORT", StockWords.ShortStock), ("§W_RESMISSING", StockWords.ReservationMissing), ("§W_CLOSED", StockWords.ReservationClosed), ("§W_INCONSISTENT", StockWords.ReservationInconsistent),
        ("§W_BELOW", StockWords.BelowReserved), ("§W_CONFLICT", StockWords.Conflict), ("§W_MANAGED", StockWords.Managed), ("§W_INUSE", StockWords.InUse),
        ("§RECEIPT", StockPlan.Receipt), ("§SALE", StockPlan.Sale), ("§RETURN", StockPlan.Return), ("§ADJUSTMENT", StockPlan.Adjustment), ("§RESERVATION", StockPlan.Reservation), ("§RELEASE", StockPlan.Release),
        ("§HELD", StockPlan.Held), ("§COMMITTED", StockPlan.Committed), ("§RELEASED", StockPlan.Released), ("§EXPIRED", StockPlan.Expired),
    ];

    /// <summary> The checks of the level table, written before its routes. </summary>
    public static string LevelHelper(LevelNames n) => TemplateText.Fill(LevelText, [("§ON", n.On), ("§RES", n.Res), .. Words()]);

    /// <summary> The request body, the helpers and the guarded updates, written before the routes of the ledger table. </summary>
    public static string LedgerHelper(Names n) => Fill(n.SweepMinutes > 0 ? HelperText + "\n\n\n" + SweepText : HelperText, n);

    /// <summary> The name of the job main.py starts for the sweep (StockSweepMinutes). </summary>
    public const string SweepJob = "stock_sweep_every";

    private const string SweepText = """"
STOCK_SWEEP_MINUTES = §SWEEP


def _sweep_once() -> None:
    with SessionLocal() as session:
        expire_holds(session)


async def stock_sweep_every() -> None:
    """Frees the holds that have run out every STOCK_SWEEP_MINUTES minutes (StockSweepMinutes); the first run is one interval after the start. Run it in one instance of the API only."""
    while True:
        await asyncio.sleep(STOCK_SWEEP_MINUTES * 60)
        try:
            await asyncio.to_thread(_sweep_once)
        except Exception:
            logging.getLogger("stock").exception("The scheduled stock sweep failed.")
"""";

    public static string LedgerRoutes(Names n, string route, string readDepends, string userParameter, string writeDepends) =>
        Fill(RouteText, n, ("§ROUTE", route), ("§READ", readDepends), ("§USER", userParameter), ("§WRITE", writeDepends), ("§AVAILABLEROUTE", StockPlan.AvailableRoute), ("§LOWROUTE", StockPlan.LowRoute), ("§CHECKROUTE", StockPlan.CheckRoute),
            ("§RECEIVEROUTE", StockPlan.ReceiveRoute), ("§RESERVEROUTE", StockPlan.ReserveRoute), ("§COMMITROUTE", StockPlan.CommitRoute), ("§RELEASEROUTE", StockPlan.ReleaseRoute), ("§EXPIREROUTE", StockPlan.ExpireRoute), ("§ADJUSTROUTE", StockPlan.AdjustRoute));

    private static string Fill(string text, Names n, params (string Token, string Value)[] more)
    {
        string clip = n.RefMax > 0 ? $"text[:{n.RefMax}]" : "text";
        string ledgerRef(string value) => n.LgRef is null ? "" : $", {n.LgRef}={value}";
        string holdRef = n.HRef is null ? "" : $", {n.HRef}=reference";
        var values = new List<(string, string)>
        {
            ("§LEDGER", n.Ledger.Class), ("§LEVEL", n.Level.Class), ("§HOLD", n.Hold.Class), ("§ITEMS", n.Items.Class), ("§LVITEM", n.LvItem), ("§LVON", n.LvOn), ("§LVRES", n.LvRes), ("§ITEMANN", n.ItemAnn), ("§ITEMKEY", n.ItemKey),
            ("§LGITEM", n.LgItem), ("§LGKIND", n.LgKind), ("§LGQTY", n.LgQty), ("§LGSTAMP", n.LgStamp), ("§HKEYANN", n.HKeyAnn), ("§HKEY", n.HKey), ("§HITEM", n.HItem), ("§HQTY", n.HQty), ("§HSTATUS", n.HStatus), ("§HCREATED", n.HCreated), ("§HEXPIRES", n.HExpires),
            ("§NOW", n.Now), ("§MINUTES", n.HoldMinutes.ToString()), ("§SWEEP", n.SweepMinutes.ToString()), ("§CLIP", clip),
            ("§REORDERVALUE", n.LvReorder is null ? "0" : $"{n.Level.Class}.{n.LvReorder}"), ("§REORDERATTR", n.LvReorder is null ? "0" : "row." + n.LvReorder), ("§REORDERINIT", n.LvReorder is null ? "" : $", {n.LvReorder}=0"),
            ("§LEDGERREFNOTE", ledgerRef("note")), ("§LEDGERREFREF", ledgerRef("reference")), ("§LEDGERREFSTOCKTAKE", ledgerRef("reference or \"stock-take\"")),
            ("§LEDGEREXPIRES", n.LgExpires is null ? "" : $", {n.LgExpires}=expires"), ("§HOLDREF", holdRef),
            ("§HOLDREFNOTE", n.HRef is null ? "None" : "hold." + n.HRef), ("§HRESOLVED", n.HResolved is null ? "" : $", {n.Hold.Class}.{n.HResolved}: now"),
        };
        values.AddRange(Words());
        values.AddRange(more);
        return TemplateText.Fill(text, [.. values]);
    }

    private const string LevelText = """"
def check_stock_new(row) -> None:
    """A new level starts with nothing on hand and nothing reserved: the quantities change through the stock routes of the ledger."""
    if (row.§ON or 0) != 0 or (row.§RES or 0) != 0:
        raise ApiError(400, "§MANAGEDTITLE", problem_type="§MANAGEDTYPE", detail="§W_MANAGED")


def check_stock_changes(row, changes: dict) -> None:
    """An update does not change either quantity."""
    for name in ("§ON", "§RES"):
        if name in changes and changes[name] != getattr(row, name):
            raise ApiError(400, "§MANAGEDTITLE", problem_type="§MANAGEDTYPE", detail="§W_MANAGED")


def check_stock_delete(row) -> None:
    """A level that still has stock or reservations is not deleted."""
    if row.§ON != 0 or row.§RES != 0:
        raise ApiError(409, "§MANAGEDTITLE", problem_type="§MANAGEDTYPE", detail="§W_INUSE")
"""";

    private const string HelperText = """"
class StockRequest(BaseModel):
    """What the stock routes read: the item and the quantity (or the count), and optionally a reference, the minutes a hold lasts and the kind of a receipt."""
    item: int | None = None
    quantity: int | None = None
    counted: int | None = None
    reference: str | None = None
    minutes: int | None = None
    kind: str | None = None


def stock_invalid(detail: str) -> ApiError:
    return ApiError(400, "§INVALIDTITLE", problem_type="§INVALIDTYPE", detail=detail)


def stock_closed(detail: str = "§W_CLOSED") -> ApiError:
    return ApiError(409, "§CLOSEDTITLE", problem_type="§CLOSEDTYPE", detail=detail)


def stock_conflict(detail: str = "§W_CONFLICT") -> ApiError:
    return ApiError(409, "§CONFLICTTITLE", problem_type="§CONFLICTTYPE", detail=detail)


def read_stock(body: StockRequest, needs_quantity: bool):
    """The item, the quantity (above zero when the route needs one) and the reference of a request, or the problem."""
    if body.item is None:
        raise stock_invalid("§W_NEEDSITEM")
    if needs_quantity and (body.quantity is None or body.quantity <= 0):
        raise stock_invalid("§W_QUANTITY")
    text = (body.reference or "").strip()
    return body.item, body.quantity or 0, (§CLIP if text else None)


def stock_level(session: Session, item: int) -> dict:
    """What an item has: on hand, reserved, available (on hand less reserved), the reorder level and whether it is at or under it."""
    row = session.execute(select(§LEVEL.§LVON, §LEVEL.§LVRES, §REORDERVALUE).where(§LEVEL.§LVITEM == item)).first()
    on_hand, reserved, reorder = (row[0], row[1], row[2]) if row is not None else (0, 0, 0)
    return {"item": item, "onHand": on_hand, "reserved": reserved, "available": on_hand - reserved, "reorderLevel": reorder, "low": on_hand - reserved <= reorder}


def resolve_hold(session: Session, hold, status: str, now, sell: bool) -> bool:
    """Closes one hold that is still held: committed (the stock leaves: a sale), released or expired (the stock is available again). Only one caller moves a hold out of held; the level moves with a guarded update and the ledger gets the rows, in one commit."""
    key = hold.§HKEY
    item = hold.§HITEM
    quantity = hold.§HQTY
    note = §HOLDREFNOTE or f"reservation {key}"
    claimed = session.execute(update(§HOLD).where(§HOLD.§HKEY == key, §HOLD.§HSTATUS == "§HELD").values({§HOLD.§HSTATUS: status§HRESOLVED}), execution_options={"synchronize_session": False}).rowcount
    if claimed == 0:
        session.rollback()
        return False
    moved = update(§LEVEL).where(§LEVEL.§LVITEM == item, §LEVEL.§LVRES >= quantity)
    if sell:
        moved = moved.where(§LEVEL.§LVON >= quantity).values({§LEVEL.§LVON: §LEVEL.§LVON - quantity, §LEVEL.§LVRES: §LEVEL.§LVRES - quantity})
    else:
        moved = moved.values({§LEVEL.§LVRES: §LEVEL.§LVRES - quantity})
    if session.execute(moved, execution_options={"synchronize_session": False}).rowcount == 0:
        session.rollback()
        return False
    session.add(§LEDGER(§LGITEM=item, §LGKIND="§RELEASE", §LGQTY=-quantity, §LGSTAMP=now§LEDGERREFNOTE))
    if sell:
        session.add(§LEDGER(§LGITEM=item, §LGKIND="§SALE", §LGQTY=-quantity, §LGSTAMP=now§LEDGERREFNOTE))
    session.commit()
    return True


def expire_holds(session: Session, item: int | None = None) -> int:
    """Gives back every hold (of one item, or of all) whose time has run out; the number of holds closed."""
    now = §NOW
    query = select(§HOLD).where(§HOLD.§HSTATUS == "§HELD", §HOLD.§HEXPIRES <= now)
    if item is not None:
        query = query.where(§HOLD.§HITEM == item)
    closed = 0
    for hold in session.scalars(query).all():
        if resolve_hold(session, hold, "§EXPIRED", now, False):
            closed += 1
    return closed


def close_hold(session: Session, id, status: str, sell: bool) -> dict:
    hold = session.get(§HOLD, id)
    if hold is None:
        raise ApiError(404, "§UNKNOWNTITLE", problem_type="§UNKNOWNTYPE", detail="§W_RESMISSING")
    now = §NOW
    item = hold.§HITEM
    if hold.§HSTATUS == "§HELD" and hold.§HEXPIRES <= now:
        resolve_hold(session, hold, "§EXPIRED", now, False)
        raise stock_closed()
    if hold.§HSTATUS != "§HELD":
        raise stock_closed()
    if not resolve_hold(session, hold, status, now, sell):
        still = session.scalar(select(§HOLD.§HKEY).where(§HOLD.§HKEY == id, §HOLD.§HSTATUS == "§HELD"))
        raise stock_conflict("§W_INCONSISTENT") if still is not None else stock_closed()
    return {"reservation": id, "status": status, "level": stock_level(session, item)}
"""";

    private const string RouteText = """"
@router.get("§ROUTE/§AVAILABLEROUTE"§READ)
def stock_available(item: §ITEMANN | None = Query(None), session: Session = Depends(get_session)§USER):
    """What an item has: on hand, reserved, available (on hand less reserved), the reorder level and whether it is at or under it."""
    if item is None:
        raise stock_invalid("§W_NEEDSITEM")
    if session.scalar(select(§ITEMS.§ITEMKEY).where(§ITEMS.§ITEMKEY == item)) is None:
        raise ApiError(404, "§UNKNOWNTITLE", problem_type="§UNKNOWNTYPE", detail="§W_ITEMMISSING")
    return stock_level(session, item)


@router.get("§ROUTE/§LOWROUTE"§READ)
def stock_low(session: Session = Depends(get_session)§USER):
    """The items whose available stock (on hand less reserved) is at or under the reorder level, the lowest first."""
    available = §LEVEL.§LVON - §LEVEL.§LVRES
    rows = session.execute(select(§LEVEL.§LVITEM, §LEVEL.§LVON, §LEVEL.§LVRES, §REORDERVALUE).where(available <= §REORDERVALUE).order_by(available, §LEVEL.§LVITEM)).all()
    return [{"item": item, "onHand": on_hand, "reserved": reserved, "available": on_hand - reserved, "reorderLevel": reorder} for item, on_hand, reserved, reorder in rows]


@router.get("§ROUTE/§CHECKROUTE"§READ)
def stock_check(session: Session = Depends(get_session)§USER):
    """Works the level of every item out again from the ledger (receipts, sales, returns and adjustments for what is on hand; reservations and releases for what is reserved) and lists the items where the table differs."""
    sums: dict = {}
    for item, kind, total in session.execute(select(§LEDGER.§LGITEM, §LEDGER.§LGKIND, func.sum(§LEDGER.§LGQTY)).group_by(§LEDGER.§LGITEM, §LEDGER.§LGKIND)).all():
        on_hand, reserved = sums.get(item, (0, 0))
        if kind in ("§RECEIPT", "§SALE", "§RETURN", "§ADJUSTMENT"):
            on_hand += int(total or 0)
        elif kind in ("§RESERVATION", "§RELEASE"):
            reserved += int(total or 0)
        sums[item] = (on_hand, reserved)
    levels = session.execute(select(§LEVEL.§LVITEM, §LEVEL.§LVON, §LEVEL.§LVRES)).all()
    differences = []
    known = set()
    for item, on_hand, reserved in levels:
        known.add(item)
        ledger_on_hand, ledger_reserved = sums.get(item, (0, 0))
        if ledger_on_hand != on_hand or ledger_reserved != reserved:
            differences.append({"item": item, "onHand": on_hand, "ledgerOnHand": ledger_on_hand, "reserved": reserved, "ledgerReserved": ledger_reserved})
    for item, (ledger_on_hand, ledger_reserved) in sums.items():
        if item not in known and (ledger_on_hand != 0 or ledger_reserved != 0):
            differences.append({"item": item, "onHand": 0, "ledgerOnHand": ledger_on_hand, "reserved": 0, "ledgerReserved": ledger_reserved})
    return {"levels": len(levels), "mismatches": len(differences), "differences": differences}


@router.post("§ROUTE/§RECEIVEROUTE", status_code=201§WRITE)
def stock_receive(body: StockRequest, session: Session = Depends(get_session)§USER):
    """Puts stock on hand (a receipt, or a return when kind is return): the level goes up and a ledger row says why. The first receipt of an item adds its level row."""
    item, quantity, reference = read_stock(body, True)
    kind = (body.kind or "§RECEIPT").strip().lower()
    if kind not in ("§RECEIPT", "§RETURN"):
        raise stock_invalid("§W_KIND")
    if session.scalar(select(§ITEMS.§ITEMKEY).where(§ITEMS.§ITEMKEY == item)) is None:
        raise stock_invalid("§W_ITEMMISSING")
    changed = session.execute(update(§LEVEL).where(§LEVEL.§LVITEM == item).values({§LEVEL.§LVON: §LEVEL.§LVON + quantity}), execution_options={"synchronize_session": False}).rowcount
    if changed == 0:
        session.add(§LEVEL(§LVITEM=item, §LVON=quantity, §LVRES=0§REORDERINIT))
    session.add(§LEDGER(§LGITEM=item, §LGKIND=kind, §LGQTY=quantity, §LGSTAMP=§NOW§LEDGERREFREF))
    try:
        session.commit()
    except IntegrityError:
        session.rollback()
        raise stock_conflict()
    return stock_level(session, item)


@router.post("§ROUTE/§RESERVEROUTE", status_code=201§WRITE)
def stock_reserve(body: StockRequest, session: Session = Depends(get_session)§USER):
    """Holds stock for a buyer: one guarded update takes the quantity out of what is available (on hand less reserved), so two buyers never take the last item; the hold lasts the minutes asked (default §MINUTES)."""
    item, quantity, reference = read_stock(body, True)
    if body.minutes is not None and body.minutes <= 0:
        raise stock_invalid("§W_MINUTES")
    expire_holds(session, item)
    now = §NOW
    expires = now + timedelta(minutes=body.minutes or §MINUTES)
    taken = session.execute(update(§LEVEL).where(§LEVEL.§LVITEM == item, §LEVEL.§LVON - §LEVEL.§LVRES >= quantity).values({§LEVEL.§LVRES: §LEVEL.§LVRES + quantity}), execution_options={"synchronize_session": False}).rowcount
    if taken == 0:
        session.rollback()
        raise ApiError(409, "§SHORTTITLE", problem_type="§SHORTTYPE", detail="§W_SHORT")
    hold = §HOLD(§HITEM=item, §HQTY=quantity, §HSTATUS="§HELD", §HCREATED=now, §HEXPIRES=expires§HOLDREF)
    session.add(hold)
    session.add(§LEDGER(§LGITEM=item, §LGKIND="§RESERVATION", §LGQTY=quantity, §LGSTAMP=now§LEDGERREFREF§LEDGEREXPIRES))
    session.commit()
    return {"reservation": hold.§HKEY, "item": item, "quantity": quantity, "expiresOn": expires.isoformat(), "level": stock_level(session, item)}


@router.post("§ROUTE/§COMMITROUTE/{id}"§WRITE)
def stock_commit(id: §HKEYANN, session: Session = Depends(get_session)§USER):
    """Turns a hold into a sale: the quantity leaves what is on hand and what is reserved, and the ledger gets a release row and a sale row. A hold that is not held any more, or has run out, is refused (409)."""
    return close_hold(session, id, "§COMMITTED", True)


@router.post("§ROUTE/§RELEASEROUTE/{id}"§WRITE)
def stock_release(id: §HKEYANN, session: Session = Depends(get_session)§USER):
    """Gives a hold back: the quantity is available again."""
    return close_hold(session, id, "§RELEASED", False)


@router.post("§ROUTE/§EXPIREROUTE"§WRITE)
def stock_expire(session: Session = Depends(get_session)§USER):
    """Gives back every hold whose time has run out; the number of holds closed."""
    return {"expired": expire_holds(session)}


@router.post("§ROUTE/§ADJUSTROUTE"§WRITE)
def stock_adjust(body: StockRequest, session: Session = Depends(get_session)§USER):
    """A stock-take: the counted quantity becomes what is on hand and a ledger row (an adjustment of the difference, which may be zero) says so. A count below what is reserved is refused."""
    item, _, reference = read_stock(body, False)
    if body.counted is None or body.counted < 0:
        raise stock_invalid("§W_COUNT")
    counted = body.counted
    if session.scalar(select(§ITEMS.§ITEMKEY).where(§ITEMS.§ITEMKEY == item)) is None:
        raise stock_invalid("§W_ITEMMISSING")
    stored = session.execute(select(§LEVEL.§LVON, §LEVEL.§LVRES).where(§LEVEL.§LVITEM == item)).first()
    on_hand, reserved = (stored[0], stored[1]) if stored is not None else (0, 0)
    if counted < reserved:
        raise ApiError(409, "§BELOWTITLE", problem_type="§BELOWTYPE", detail="§W_BELOW")
    if stored is None:
        session.add(§LEVEL(§LVITEM=item, §LVON=counted, §LVRES=0§REORDERINIT))
    elif session.execute(update(§LEVEL).where(§LEVEL.§LVITEM == item, §LEVEL.§LVON == on_hand, §LEVEL.§LVRES == reserved).values({§LEVEL.§LVON: counted}), execution_options={"synchronize_session": False}).rowcount == 0:
        session.rollback()
        raise stock_conflict()
    session.add(§LEDGER(§LGITEM=item, §LGKIND="§ADJUSTMENT", §LGQTY=counted - on_hand, §LGSTAMP=§NOW§LEDGERREFSTOCKTAKE))
    try:
        session.commit()
    except IntegrityError:
        session.rollback()
        raise stock_conflict()
    return {"item": item, "counted": counted, "difference": counted - on_hand, "level": stock_level(session, item)}
"""";
}
