using CodeGenNew.Core.Sql;
using CodeGenNew.Core.Stock;

namespace CodeGenNew.Core.Rust;

/// <summary> The Rust (axum, sqlx) counterpart of <see cref="StockWriter"/> (<c>StockTables</c>): the ledger table's router gets the stock routes (receive, reserve, commit, release, expire, adjust, available, low, check) and the level table's router
/// refuses a change of the quantities by hand. Every change of the level is one guarded UPDATE and a ledger row, in one transaction. The numbers are read and bound as 64-bit whole numbers (the columns are cast), the times as naive date-times. </summary>
public static class RustStockWriter
{
    /// <summary> The names the level table's save and delete checks need. </summary>
    public sealed record LevelNames(string Struct, string KeyType, string On, string Res);

    public static LevelNames? LevelFor(StockShape? stock, RustTable level, string keyType) =>
        stock is null ? null : new LevelNames(level.Struct, keyType, level.FieldOf(stock.OnHand).Name, level.FieldOf(stock.Reserved).Name);

    public const string LevelCreateLine = "    check_stock_new(&row)?;";
    public const string LevelUpdateLine = "    check_stock_changes(&pool, id, &row).await?;";
    public const string LevelDeleteLine = "    check_stock_delete(&pool, id).await?;";

    public static string RouteLines(string route, string layer) =>
        $"        .route(\"{route}/{StockPlan.AvailableRoute}\", get(stock_available){layer})\n"
        + $"        .route(\"{route}/{StockPlan.LowRoute}\", get(stock_low){layer})\n"
        + $"        .route(\"{route}/{StockPlan.CheckRoute}\", get(stock_check){layer})\n"
        + $"        .route(\"{route}/{StockPlan.ReceiveRoute}\", post(stock_receive){layer})\n"
        + $"        .route(\"{route}/{StockPlan.ReserveRoute}\", post(stock_reserve){layer})\n"
        + $"        .route(\"{route}/{StockPlan.CommitRoute}/{{id}}\", post(stock_commit){layer})\n"
        + $"        .route(\"{route}/{StockPlan.ReleaseRoute}/{{id}}\", post(stock_release){layer})\n"
        + $"        .route(\"{route}/{StockPlan.ExpireRoute}\", post(stock_expire){layer})\n"
        + $"        .route(\"{route}/{StockPlan.AdjustRoute}\", post(stock_adjust){layer})\n";

    public static string LevelCode(LevelNames n) => TemplateText.Fill(LevelText, [("§STRUCT", n.Struct), ("§KT", n.KeyType), ("§ON", n.On), ("§RES", n.Res), .. Words()]);

    private static (string, string)[] Words() =>
    [
        ("§INVALIDTITLE", StockWords.InvalidTitle), ("§INVALIDTYPE", StockWords.InvalidType), ("§UNKNOWNTITLE", StockWords.UnknownTitle), ("§UNKNOWNTYPE", StockWords.UnknownType), ("§SHORTTITLE", StockWords.ShortTitle), ("§SHORTTYPE", StockWords.ShortType),
        ("§CLOSEDTITLE", StockWords.ClosedTitle), ("§CLOSEDTYPE", StockWords.ClosedType), ("§CONFLICTTITLE", StockWords.ConflictTitle), ("§CONFLICTTYPE", StockWords.ConflictType), ("§MANAGEDTITLE", StockWords.ManagedTitle), ("§MANAGEDTYPE", StockWords.ManagedType),
        ("§BELOWTITLE", StockWords.BelowReservedTitle), ("§BELOWTYPE", StockWords.BelowReservedType), ("§W_NEEDSITEM", StockWords.NeedsItem), ("§W_ITEMMISSING", StockWords.ItemMissing), ("§W_QUANTITY", StockWords.QuantityPositive), ("§W_COUNT", StockWords.CountNeeded),
        ("§W_KIND", StockWords.KindBad), ("§W_MINUTES", StockWords.MinutesBad), ("§W_SHORT", StockWords.ShortStock), ("§W_RESMISSING", StockWords.ReservationMissing), ("§W_CLOSED", StockWords.ReservationClosed), ("§W_INCONSISTENT", StockWords.ReservationInconsistent),
        ("§W_BELOW", StockWords.BelowReserved), ("§W_CONFLICT", StockWords.Conflict), ("§W_MANAGED", StockWords.Managed), ("§W_INUSE", StockWords.InUse),
        ("§RECEIPT", StockPlan.Receipt), ("§SALE", StockPlan.Sale), ("§RETURN", StockPlan.Return), ("§ADJUSTMENT", StockPlan.Adjustment), ("§RESERVATION", StockPlan.Reservation), ("§RELEASE", StockPlan.Release),
        ("§HELD", StockPlan.Held), ("§COMMITTED", StockPlan.Committed), ("§RELEASED", StockPlan.Released), ("§EXPIRED", StockPlan.Expired),
    ];

    /// <summary> The helpers and the routes of the ledger table. </summary>
    public static string Code(StockShape s, RustTable ledger, ProjectSettings project, DialectInfo di)
    {
        var level = RustTable.Of(s.Level, project);
        var hold = RustTable.Of(s.Reservation.Table, project);
        var items = RustTable.Of(s.ItemTable, project);
        var l = s.Ledger;
        var h = s.Reservation;
        string Q(ColumnModel c) => di.Quote(c.DbName);
        string P(int n) => di.Placeholder(n);
        string big(string expression) => $"CAST({expression} AS {(di.Dialect == SqlDialect.MySql ? "SIGNED" : "BIGINT")})";
        string lvItem = Q(s.Item), lvOn = Q(s.OnHand), lvRes = Q(s.Reserved), lvReorder = s.Reorder is null ? "0" : Q(s.Reorder);
        string reorderBig = big(lvReorder);
        string lgItem = Q(l.Item), lgKind = Q(l.Kind), lgQty = Q(l.Quantity), lgStamp = Q(l.Stamp);
        string hKey = Q(h.Key), hItem = Q(h.Item), hQty = Q(h.Quantity), hStatus = Q(h.Status), hCreated = Q(h.Created), hExpires = Q(h.Expires);
        int refMax = new[] { l.Reference?.CharacterLength ?? 0, h.Reference?.CharacterLength ?? 0 }.Where(n => n > 0).DefaultIfEmpty(0).Min();

        // the ledger row: the item, the kind, the quantity and the time, then the reference and the time a hold ends when the table has those columns
        var ledgerColumns = new List<string> { lgItem, lgKind, lgQty, lgStamp };
        string ledgerBinds = "";
        if (l.Reference is not null)
        {
            ledgerColumns.Add(Q(l.Reference));
            ledgerBinds += ".bind(reference)";
        }
        if (l.Expires is not null)
        {
            ledgerColumns.Add(Q(l.Expires));
            ledgerBinds += ".bind(expires)";
        }
        string sqlLedger = $"INSERT INTO {ledger.Table} ({string.Join(", ", ledgerColumns)}) VALUES ({string.Join(", ", ledgerColumns.Select((_, i) => P(i + 1)))})";

        var holdColumns = new List<string> { hItem, hQty, hStatus, hCreated, hExpires };
        string holdBinds = ".bind(item).bind(quantity).bind(HELD).bind(now).bind(expires)";
        if (h.Reference is not null)
        {
            holdColumns.Add(Q(h.Reference));
            holdBinds += ".bind(reference.clone())";
        }
        string insertHold = $"INSERT INTO {hold.Table} ({string.Join(", ", holdColumns)}) VALUES ({string.Join(", ", holdColumns.Select((_, i) => P(i + 1)))})";
        string holdReference = h.Reference is null ? "''" : Q(h.Reference);
        string holdSelect = $"SELECT {big(hKey)}, {big(hItem)}, {big(hQty)}, {hStatus}, {hExpires}, {holdReference} FROM {hold.Table}";
        string newHold = di.SupportsReturning
            ? $"let id: i64 = sqlx::query_scalar::<Db, i64>({RustTable.Raw(insertHold + " RETURNING " + big(hKey))}){holdBinds}.fetch_one(&mut *tx).await?;"
            : $"let id: i64 = sqlx::query({RustTable.Raw(insertHold)}){holdBinds}.execute(&mut *tx).await?.last_insert_id() as i64;";
        string claim = h.Resolved is null
            ? $"UPDATE {hold.Table} SET {hStatus} = {P(1)} WHERE {hKey} = {P(2)} AND {hStatus} = {P(3)}"
            : $"UPDATE {hold.Table} SET {hStatus} = {P(1)}, {Q(h.Resolved)} = {P(2)} WHERE {hKey} = {P(3)} AND {hStatus} = {P(4)}";
        string claimBinds = h.Resolved is null ? ".bind(status).bind(hold.key).bind(HELD)" : ".bind(status).bind(now).bind(hold.key).bind(HELD)";
        string newLevel = s.Reorder is null
            ? $"INSERT INTO {level.Table} ({lvItem}, {lvOn}, {lvRes}) VALUES ({P(1)}, {P(2)}, 0)"
            : $"INSERT INTO {level.Table} ({lvItem}, {lvOn}, {lvRes}, {lvReorder}) VALUES ({P(1)}, {P(2)}, 0, 0)";

        return TemplateText.FillAll(CodeText, [
            ("§SQL_LEVEL_ALL", RustTable.Raw($"SELECT {big(lvItem)}, {big(lvOn)}, {big(lvRes)} FROM {level.Table}")),
            ("§SQL_LEVEL", RustTable.Raw($"SELECT {big(lvOn)}, {big(lvRes)}, {reorderBig} FROM {level.Table} WHERE {lvItem} = {P(1)}")),
            ("§SQL_ITEM", RustTable.Raw($"SELECT COUNT(*) FROM {items.Table} WHERE {Q(s.ItemKey)} = {P(1)}")),
            ("§SQL_LOW", RustTable.Raw($"SELECT {big(lvItem)}, {big(lvOn)}, {big(lvRes)}, {reorderBig} FROM {level.Table} WHERE {lvOn} - {lvRes} <= {lvReorder} ORDER BY {lvOn} - {lvRes}, {lvItem}")),
            ("§SQL_SUMS", RustTable.Raw($"SELECT {big(lgItem)}, {lgKind}, {big($"SUM({lgQty})")} FROM {ledger.Table} GROUP BY {lgItem}, {lgKind}")),
            ("§SQL_ADD_ON", RustTable.Raw($"UPDATE {level.Table} SET {lvOn} = {lvOn} + {P(1)} WHERE {lvItem} = {P(2)}")),
            ("§SQL_NEW_LEVEL", RustTable.Raw(newLevel)),
            ("§SQL_LEDGER", RustTable.Raw(sqlLedger)),
            ("§SQL_RESERVE", RustTable.Raw($"UPDATE {level.Table} SET {lvRes} = {lvRes} + {P(1)} WHERE {lvItem} = {P(2)} AND {lvOn} - {lvRes} >= {P(3)}")),
            ("§SQL_CLAIM", RustTable.Raw(claim)),
            ("§SQL_SELL", RustTable.Raw($"UPDATE {level.Table} SET {lvOn} = {lvOn} - {P(1)}, {lvRes} = {lvRes} - {P(2)} WHERE {lvItem} = {P(3)} AND {lvRes} >= {P(4)} AND {lvOn} >= {P(5)}")),
            ("§SQL_GIVE", RustTable.Raw($"UPDATE {level.Table} SET {lvRes} = {lvRes} - {P(1)} WHERE {lvItem} = {P(2)} AND {lvRes} >= {P(3)}")),
            ("§SQL_HOLD_DUE_ITEM", RustTable.Raw($"{holdSelect} WHERE {hStatus} = {P(1)} AND {hExpires} <= {P(2)} AND {hItem} = {P(3)}")),
            ("§SQL_HOLD_DUE", RustTable.Raw($"{holdSelect} WHERE {hStatus} = {P(1)} AND {hExpires} <= {P(2)}")),
            ("§SQL_HOLD", RustTable.Raw($"{holdSelect} WHERE {hKey} = {P(1)}")),
            ("§SQL_STILL", RustTable.Raw($"SELECT COUNT(*) FROM {hold.Table} WHERE {hKey} = {P(1)} AND {hStatus} = {P(2)}")),
            ("§SQL_STORED", RustTable.Raw($"SELECT {big(lvOn)}, {big(lvRes)} FROM {level.Table} WHERE {lvItem} = {P(1)}")),
            ("§SQL_ADJUST", RustTable.Raw($"UPDATE {level.Table} SET {lvOn} = {P(1)} WHERE {lvItem} = {P(2)} AND {lvOn} = {P(3)} AND {lvRes} = {P(4)}")),
            ("§LEDGERBINDS", ledgerBinds), ("§CLAIMBINDS", claimBinds), ("§NEWHOLD", newHold),
            ("§CLIP", refMax > 0 ? $"text.chars().take({refMax}).collect::<String>()" : "text.to_string()"), ("§MINUTES", project.StockHoldMinutes.ToString()),
            ("§HOLDREFNOTE", ""), .. Words()]);
    }

    private const string LevelText = """"
fn check_stock_new(row: &§STRUCT) -> ApiResult<()> {
    if row.§ON != 0 || row.§RES != 0 {
        return Err(ApiError::Rejected(400, "§MANAGEDTYPE", "§MANAGEDTITLE", "§W_MANAGED".into()));
    }
    Ok(())
}

/// An update does not change either quantity.
async fn check_stock_changes(pool: &Pool, id: §KT, row: &§STRUCT) -> ApiResult<()> {
    if let Some(stored) = repo::get_by_id(pool, id).await? {
        if stored.§ON != row.§ON || stored.§RES != row.§RES {
            return Err(ApiError::Rejected(400, "§MANAGEDTYPE", "§MANAGEDTITLE", "§W_MANAGED".into()));
        }
    }
    Ok(())
}

/// A level that still has stock or reservations is not deleted.
async fn check_stock_delete(pool: &Pool, id: §KT) -> ApiResult<()> {
    if let Some(stored) = repo::get_by_id(pool, id).await? {
        if stored.§ON != 0 || stored.§RES != 0 {
            return Err(ApiError::Rejected(409, "§MANAGEDTYPE", "§MANAGEDTITLE", "§W_INUSE".into()));
        }
    }
    Ok(())
}

"""";

    private const string CodeText = """"
const HELD: &str = "§HELD";

#[derive(serde::Deserialize)]
struct StockRequest {
    item: Option<i64>,
    quantity: Option<i64>,
    counted: Option<i64>,
    reference: Option<String>,
    minutes: Option<i64>,
    kind: Option<String>,
}

#[derive(serde::Deserialize)]
struct ItemQuery {
    item: Option<i64>,
}

struct HoldRow {
    key: i64,
    item: i64,
    quantity: i64,
    status: String,
    expires: chrono::NaiveDateTime,
    reference: Option<String>,
}

type HoldTuple = (i64, i64, i64, String, chrono::NaiveDateTime, Option<String>);

fn hold_row(row: HoldTuple) -> HoldRow {
    HoldRow { key: row.0, item: row.1, quantity: row.2, status: row.3, expires: row.4, reference: row.5 }
}

fn stock_invalid(detail: &str) -> ApiError {
    ApiError::Rejected(400, "§INVALIDTYPE", "§INVALIDTITLE", detail.into())
}

fn stock_closed(detail: &str) -> ApiError {
    ApiError::Rejected(409, "§CLOSEDTYPE", "§CLOSEDTITLE", detail.into())
}

fn stock_conflict(detail: &str) -> ApiError {
    ApiError::Rejected(409, "§CONFLICTTYPE", "§CONFLICTTITLE", detail.into())
}

/// The item, the quantity (above zero when the route needs one) and the reference of a request, or the problem.
fn read_stock(body: &StockRequest, needs_quantity: bool) -> ApiResult<(i64, i64, Option<String>)> {
    let Some(item) = body.item else {
        return Err(stock_invalid("§W_NEEDSITEM"));
    };
    let quantity = body.quantity.unwrap_or(0);
    if needs_quantity && quantity <= 0 {
        return Err(stock_invalid("§W_QUANTITY"));
    }
    let text = body.reference.as_deref().unwrap_or("").trim();
    let reference = if text.is_empty() { None } else { Some(§CLIP) };
    Ok((item, quantity, reference))
}

/// What an item has: on hand, reserved, available (on hand less reserved), the reorder level and whether it is at or under it.
async fn stock_level(pool: &Pool, item: i64) -> ApiResult<serde_json::Value> {
    let (on_hand, reserved, reorder) = sqlx::query_as::<Db, (i64, i64, i64)>(§SQL_LEVEL).bind(item).fetch_optional(pool).await?.unwrap_or((0, 0, 0));
    let available = on_hand - reserved;
    let low = available <= reorder;
    Ok(serde_json::json!({ "item": item, "onHand": on_hand, "reserved": reserved, "available": available, "reorderLevel": reorder, "low": low }))
}

async fn item_exists(pool: &Pool, item: i64) -> ApiResult<bool> {
    Ok(sqlx::query_scalar::<Db, i64>(§SQL_ITEM).bind(item).fetch_one(pool).await? > 0)
}

async fn add_ledger(tx: &mut sqlx::Transaction<'_, Db>, item: i64, kind: &str, quantity: i64, stamp: chrono::NaiveDateTime, reference: Option<&str>, expires: Option<chrono::NaiveDateTime>) -> ApiResult<()> {
    let _ = (&reference, &expires);
    sqlx::query(§SQL_LEDGER).bind(item).bind(kind).bind(quantity).bind(stamp)§LEDGERBINDS.execute(&mut **tx).await?;
    Ok(())
}

/// Closes one hold that is still held: committed (the stock leaves: a sale), released or expired (the stock is available again). Only one caller moves a hold out of held; the level moves with a guarded update and the ledger gets the rows, in one transaction.
async fn resolve_hold(pool: &Pool, hold: &HoldRow, status: &str, now: chrono::NaiveDateTime, sell: bool) -> ApiResult<bool> {
    let note = hold.reference.clone().unwrap_or_else(|| format!("reservation {}", hold.key));
    let mut tx = pool.begin().await?;
    let claimed = sqlx::query(§SQL_CLAIM)§CLAIMBINDS.execute(&mut *tx).await?.rows_affected();
    if claimed == 0 {
        tx.rollback().await?;
        return Ok(false);
    }
    let moved = if sell {
        sqlx::query(§SQL_SELL).bind(hold.quantity).bind(hold.quantity).bind(hold.item).bind(hold.quantity).bind(hold.quantity).execute(&mut *tx).await?.rows_affected()
    } else {
        sqlx::query(§SQL_GIVE).bind(hold.quantity).bind(hold.item).bind(hold.quantity).execute(&mut *tx).await?.rows_affected()
    };
    if moved == 0 {
        tx.rollback().await?;
        return Ok(false);
    }
    add_ledger(&mut tx, hold.item, "§RELEASE", -hold.quantity, now, Some(&note), None).await?;
    if sell {
        add_ledger(&mut tx, hold.item, "§SALE", -hold.quantity, now, Some(&note), None).await?;
    }
    tx.commit().await?;
    Ok(true)
}

/// Gives back every hold (of one item, or of all) whose time has run out; the number of holds closed.
async fn expire_holds(pool: &Pool, item: Option<i64>) -> ApiResult<i64> {
    let now = chrono::Local::now().naive_local();
    let due: Vec<HoldTuple> = match item {
        Some(item) => sqlx::query_as::<Db, HoldTuple>(§SQL_HOLD_DUE_ITEM).bind(HELD).bind(now).bind(item).fetch_all(pool).await?,
        None => sqlx::query_as::<Db, HoldTuple>(§SQL_HOLD_DUE).bind(HELD).bind(now).fetch_all(pool).await?,
    };
    let mut closed = 0;
    for row in due {
        if resolve_hold(pool, &hold_row(row), "§EXPIRED", now, false).await? {
            closed += 1;
        }
    }
    Ok(closed)
}

async fn close_hold(pool: &Pool, id: i64, status: &str, sell: bool) -> ApiResult<Json<serde_json::Value>> {
    let Some(row) = sqlx::query_as::<Db, HoldTuple>(§SQL_HOLD).bind(id).fetch_optional(pool).await? else {
        return Err(ApiError::Rejected(404, "§UNKNOWNTYPE", "§UNKNOWNTITLE", "§W_RESMISSING".into()));
    };
    let hold = hold_row(row);
    let now = chrono::Local::now().naive_local();
    if hold.status == HELD && hold.expires <= now {
        resolve_hold(pool, &hold, "§EXPIRED", now, false).await?;
        return Err(stock_closed("§W_CLOSED"));
    }
    if hold.status != HELD {
        return Err(stock_closed("§W_CLOSED"));
    }
    if !resolve_hold(pool, &hold, status, now, sell).await? {
        let still = sqlx::query_scalar::<Db, i64>(§SQL_STILL).bind(id).bind(HELD).fetch_one(pool).await?;
        return Err(if still > 0 { stock_conflict("§W_INCONSISTENT") } else { stock_closed("§W_CLOSED") });
    }
    let level = stock_level(pool, hold.item).await?;
    Ok(Json(serde_json::json!({ "reservation": id, "status": status, "level": level })))
}

/// What an item has: on hand, reserved, available (on hand less reserved), the reorder level and whether it is at or under it.
async fn stock_available(State(pool): State<Pool>, Query(query): Query<ItemQuery>) -> ApiResult<Json<serde_json::Value>> {
    let Some(item) = query.item else {
        return Err(stock_invalid("§W_NEEDSITEM"));
    };
    if !item_exists(&pool, item).await? {
        return Err(ApiError::Rejected(404, "§UNKNOWNTYPE", "§UNKNOWNTITLE", "§W_ITEMMISSING".into()));
    }
    Ok(Json(stock_level(&pool, item).await?))
}

/// The items whose available stock (on hand less reserved) is at or under the reorder level, the lowest first.
async fn stock_low(State(pool): State<Pool>) -> ApiResult<Json<serde_json::Value>> {
    let rows = sqlx::query_as::<Db, (i64, i64, i64, i64)>(§SQL_LOW).fetch_all(&pool).await?;
    let list: Vec<serde_json::Value> = rows.into_iter().map(|(item, on_hand, reserved, reorder)| serde_json::json!({ "item": item, "onHand": on_hand, "reserved": reserved, "available": on_hand - reserved, "reorderLevel": reorder })).collect();
    Ok(Json(serde_json::Value::Array(list)))
}

/// Works the level of every item out again from the ledger (receipts, sales, returns and adjustments for what is on hand; reservations and releases for what is reserved) and lists the items where the table differs.
async fn stock_check(State(pool): State<Pool>) -> ApiResult<Json<serde_json::Value>> {
    let mut sums: std::collections::HashMap<i64, (i64, i64)> = std::collections::HashMap::new();
    for (item, kind, total) in sqlx::query_as::<Db, (i64, String, i64)>(§SQL_SUMS).fetch_all(&pool).await? {
        let entry = sums.entry(item).or_insert((0, 0));
        if ["§RECEIPT", "§SALE", "§RETURN", "§ADJUSTMENT"].contains(&kind.as_str()) {
            entry.0 += total;
        } else if ["§RESERVATION", "§RELEASE"].contains(&kind.as_str()) {
            entry.1 += total;
        }
    }
    let levels = sqlx::query_as::<Db, (i64, i64, i64)>(§SQL_LEVEL_ALL).fetch_all(&pool).await?;
    let mut differences: Vec<serde_json::Value> = Vec::new();
    let mut known = std::collections::HashSet::new();
    for (item, on_hand, reserved) in &levels {
        known.insert(*item);
        let (ledger_on_hand, ledger_reserved) = sums.get(item).copied().unwrap_or((0, 0));
        if ledger_on_hand != *on_hand || ledger_reserved != *reserved {
            differences.push(serde_json::json!({ "item": item, "onHand": on_hand, "ledgerOnHand": ledger_on_hand, "reserved": reserved, "ledgerReserved": ledger_reserved }));
        }
    }
    for (item, (ledger_on_hand, ledger_reserved)) in &sums {
        if !known.contains(item) && (*ledger_on_hand != 0 || *ledger_reserved != 0) {
            differences.push(serde_json::json!({ "item": item, "onHand": 0, "ledgerOnHand": ledger_on_hand, "reserved": 0, "ledgerReserved": ledger_reserved }));
        }
    }
    Ok(Json(serde_json::json!({ "levels": levels.len(), "mismatches": differences.len(), "differences": differences })))
}

/// Puts stock on hand (a receipt, or a return when kind is return): the level goes up and a ledger row says why. The first receipt of an item adds its level row.
async fn stock_receive(State(pool): State<Pool>, ApiJson(body): ApiJson<StockRequest>) -> ApiResult<Response> {
    let (item, quantity, reference) = read_stock(&body, true)?;
    let kind = body.kind.as_deref().unwrap_or("§RECEIPT").trim().to_lowercase();
    if kind != "§RECEIPT" && kind != "§RETURN" {
        return Err(stock_invalid("§W_KIND"));
    }
    if !item_exists(&pool, item).await? {
        return Err(stock_invalid("§W_ITEMMISSING"));
    }
    let now = chrono::Local::now().naive_local();
    let mut tx = pool.begin().await?;
    let changed = sqlx::query(§SQL_ADD_ON).bind(quantity).bind(item).execute(&mut *tx).await?.rows_affected();
    if changed == 0 {
        sqlx::query(§SQL_NEW_LEVEL).bind(item).bind(quantity).execute(&mut *tx).await.map_err(|_| stock_conflict("§W_CONFLICT"))?;
    }
    add_ledger(&mut tx, item, &kind, quantity, now, reference.as_deref(), None).await?;
    tx.commit().await?;
    Ok(created(format!("available?item={}", item), stock_level(&pool, item).await?))
}

/// Holds stock for a buyer: one guarded update takes the quantity out of what is available (on hand less reserved), so two buyers never take the last item; the hold lasts the minutes asked (default §MINUTES).
async fn stock_reserve(State(pool): State<Pool>, ApiJson(body): ApiJson<StockRequest>) -> ApiResult<Response> {
    let (item, quantity, reference) = read_stock(&body, true)?;
    if body.minutes.is_some_and(|m| m <= 0) {
        return Err(stock_invalid("§W_MINUTES"));
    }
    expire_holds(&pool, Some(item)).await?;
    let now = chrono::Local::now().naive_local();
    let expires = now + chrono::Duration::minutes(body.minutes.unwrap_or(§MINUTES));
    let mut tx = pool.begin().await?;
    let taken = sqlx::query(§SQL_RESERVE).bind(quantity).bind(item).bind(quantity).execute(&mut *tx).await?.rows_affected();
    if taken == 0 {
        tx.rollback().await?;
        return Err(ApiError::Rejected(409, "§SHORTTYPE", "§SHORTTITLE", "§W_SHORT".into()));
    }
    §NEWHOLD
    add_ledger(&mut tx, item, "§RESERVATION", quantity, now, reference.as_deref(), Some(expires)).await?;
    tx.commit().await?;
    let level = stock_level(&pool, item).await?;
    Ok(created(format!("reserve/{}", id), serde_json::json!({ "reservation": id, "item": item, "quantity": quantity, "expiresOn": expires.format("%Y-%m-%dT%H:%M:%S").to_string(), "level": level })))
}

/// Turns a hold into a sale: the quantity leaves what is on hand and what is reserved, and the ledger gets a release row and a sale row. A hold that is not held any more, or has run out, is refused (409).
async fn stock_commit(State(pool): State<Pool>, Path(id): Path<i64>) -> ApiResult<Json<serde_json::Value>> {
    close_hold(&pool, id, "§COMMITTED", true).await
}

/// Gives a hold back: the quantity is available again.
async fn stock_release(State(pool): State<Pool>, Path(id): Path<i64>) -> ApiResult<Json<serde_json::Value>> {
    close_hold(&pool, id, "§RELEASED", false).await
}

/// Gives back every hold whose time has run out; the number of holds closed.
async fn stock_expire(State(pool): State<Pool>) -> ApiResult<Json<serde_json::Value>> {
    Ok(Json(serde_json::json!({ "expired": expire_holds(&pool, None).await? })))
}

/// A stock-take: the counted quantity becomes what is on hand and a ledger row (an adjustment of the difference, which may be zero) says so. A count below what is reserved is refused.
async fn stock_adjust(State(pool): State<Pool>, ApiJson(body): ApiJson<StockRequest>) -> ApiResult<Json<serde_json::Value>> {
    let (item, _, reference) = read_stock(&body, false)?;
    let Some(counted) = body.counted.filter(|c| *c >= 0) else {
        return Err(stock_invalid("§W_COUNT"));
    };
    if !item_exists(&pool, item).await? {
        return Err(stock_invalid("§W_ITEMMISSING"));
    }
    let stored = sqlx::query_as::<Db, (i64, i64)>(§SQL_STORED).bind(item).fetch_optional(&pool).await?;
    let found = stored.is_some();
    let (on_hand, reserved) = stored.unwrap_or((0, 0));
    if counted < reserved {
        return Err(ApiError::Rejected(409, "§BELOWTYPE", "§BELOWTITLE", "§W_BELOW".into()));
    }
    let now = chrono::Local::now().naive_local();
    let mut tx = pool.begin().await?;
    if !found {
        sqlx::query(§SQL_NEW_LEVEL).bind(item).bind(counted).execute(&mut *tx).await.map_err(|_| stock_conflict("§W_CONFLICT"))?;
    } else if counted != on_hand {
        let changed = sqlx::query(§SQL_ADJUST).bind(counted).bind(item).bind(on_hand).bind(reserved).execute(&mut *tx).await?.rows_affected();
        if changed == 0 {
            tx.rollback().await?;
            return Err(stock_conflict("§W_CONFLICT"));
        }
    }
    let note = reference.unwrap_or_else(|| "stock-take".to_string());
    add_ledger(&mut tx, item, "§ADJUSTMENT", counted - on_hand, now, Some(&note), None).await?;
    tx.commit().await?;
    Ok(Json(serde_json::json!({ "item": item, "counted": counted, "difference": counted - on_hand, "level": stock_level(&pool, item).await? })))
}

"""";
}
