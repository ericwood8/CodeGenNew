using System.Text;

namespace CodeGenNew.Core;

/// <summary> The Python (FastAPI) counterpart of <see cref="UsageWriter"/>: <c>app/routes/usage.py</c>, the usage ledger of every table in <c>UsageTables</c> with the same routes (<c>GET</c> the summary per month and
/// kind, <c>POST</c> for a platform administrator), the same sentences (<see cref="UsageWords"/>), the same answers and the same plan-limit reading (<c>UsageLimits</c>). <c>record_usage</c> is the only writer of an
/// event. A ledger table has no model, so each is a SQLAlchemy <c>Table</c> of the columns the routes use. </summary>
public static class PythonUsageWriter
{
    public static string File(IReadOnlyList<UsageShape> tables, DatabaseModel database, ProjectSettings project)
    {
        var specs = new StringBuilder();
        foreach (var t in tables)
        {
            var roll = UsagePlan.RollupFor(database, project, t);
            var tenantTable = database.Tables.First(x => x.TableName.EqualsIgnoreCase(t.TenantTable));
            var tenantKey = TenantPlan.KeyOf(tenantTable)!;
            var fill = UsagePlan.FillColumns(t);
            string Col(ColumnModel c, string type, bool key = false) => $"Column({PythonNames.Str(c.DbName)}, {type}{(key ? ", primary_key=True" : "")})";
            string columns = string.Join(", ", new[]
            {
                Col(t.Key, "Integer", key: true), Col(t.Tenant, "Integer"), Col(t.Kind, "String"), t.Quantity is null ? null : Col(t.Quantity, t.QuantityIsDecimal ? "Numeric" : "Integer"), Col(t.Occurred, "DateTime")
            }.Concat((fill ?? []).Select(f => Col(f.Column, f.Kind switch { TokenPlan.FillKind.Time => "DateTime", TokenPlan.FillKind.Flag => "Boolean", TokenPlan.FillKind.Number => "Integer", _ => "String" }))).OfType<string>());
            string fillValues = fill is null ? "None" : "lambda: {" + string.Join(", ", fill.Select(f => $"{PythonNames.Str(f.Column.DbName)}: " + f.Kind switch
            {
                TokenPlan.FillKind.Time => "datetime.now()",
                TokenPlan.FillKind.Flag => "False",
                TokenPlan.FillKind.Number => "0",
                TokenPlan.FillKind.Guid => "str(uuid.uuid4())",
                _ => "\"test\""
            })) + "}";
            specs.Append($"    UsageSpec(\n        route={PythonNames.Str(BlazorNames.ApiRoute(t.Table.TableName))}, name={PythonNames.Str(t.Table.TableName)},\n" +
                $"        table=Table({PythonNames.Str(t.Table.DbTableName)}, MetaData(), {columns}{Schema(t.Table)}),\n" +
                $"        tenants=Table({PythonNames.Str(tenantTable.DbTableName)}, MetaData(), Column({PythonNames.Str(tenantKey.DbName)}, Integer, primary_key=True){Schema(tenantTable)}),\n" +
                $"        key={PythonNames.Str(t.Key.DbName)}, tenant={PythonNames.Str(t.Tenant.DbName)}, tenant_key={PythonNames.Str(tenantKey.DbName)}, kind={PythonNames.Str(t.Kind.DbName)}, quantity={(t.Quantity is null ? "None" : PythonNames.Str(t.Quantity.DbName))}, " +
                $"occurred={PythonNames.Str(t.Occurred.DbName)},\n        decimal={(t.QuantityIsDecimal ? "True" : "False")}, kind_length={(t.Kind.CharacterLength > 0 ? t.Kind.CharacterLength.ToString() : "None")}, fill={fillValues},\n" +
                $"        limited=({string.Join(", ", UsagePlan.KeysOf(database, project, t).Select(k => PythonNames.Str(k)))}{(UsagePlan.KeysOf(database, project, t).Count == 1 ? "," : "")}), rollup={RollupSpec(roll)},\n    ),\n");
        }
        bool limits = project.UsageLimits.Length > 0;
        string allowedBlock = limits ? AllowedLimited : AllowedNone;
        bool refuse = project.UsageRefuse && limits;
        bool schedule = UsagePlan.HasSchedule(database, project);
        return TemplateText.FillAll(Template, new (string, string)[]
        {
            ("§SPECS", specs.ToString().TrimEnd('\n')), ("§ALLOWED", allowedBlock), ("§GATE", refuse ? GateText(database.Dialect) : ""), ("§ROLLUPDAYS", project.UsageRollupDays.ToString()), ("§SCHEDULE", schedule ? TemplateText.Fill(ScheduleText, ("§HOURS", project.UsageRollupHours.ToString())) : ""), ("§REFUSE", refuse ? RefuseText + "\n" : ""),
            ("§REFUSEROUTE", refuse ? RefuseRouteText + "\n" : "        return result\n"),
            ("§LIMITCONSTANT", refuse ? "PROBLEM_LIMIT = \"" + SubscriptionWords.LimitType + "\"\nLIMIT_TEXT = " + PythonNames.Str(SubscriptionWords.LimitText) + "\n" : ""), ("§SUBIMPORT", limits ? "from app.routes import subscriptions\n" : ""),
            ("§ADMINS_TITLE", PythonNames.Str(UsageWords.AdminsTitle)), ("§ADMINS_TEXT", PythonNames.Str(UsageWords.AdminsDetail)), ("§PLATFORM_TYPE", UsageWords.PlatformType),
            ("§PLATFORM_TITLE", PythonNames.Str(UsageWords.PlatformTitle)), ("§PLATFORM_TEXT", PythonNames.Str(UsageWords.PlatformDetail)), ("§NAMETENANT_TEXT", PythonNames.Str(UsageWords.NameTenant)),
            ("§NEEDSTENANT_TEXT", PythonNames.Str(UsageWords.NeedsTenant)), ("§NEEDSKIND_TEXT", PythonNames.Str(UsageWords.NeedsKind)), ("§KINDLONG_TEXT", PythonNames.Str(UsageWords.KindTooLong)),
            ("§QUANTITY_TEXT", PythonNames.Str(UsageWords.QuantityPositive)), ("§QUANTITYWHOLE_TEXT", PythonNames.Str(UsageWords.QuantityWhole)), ("§UNKNOWNTENANT_TEXT", PythonNames.Str(UsageWords.UnknownTenant)), ("§BADREQUEST", HttpStatus.BadRequest.ToString()),
            ("§FORBIDDEN", HttpStatus.Forbidden.ToString()), ("§NOTFOUND", HttpStatus.NotFound.ToString())
        });
    }

    private static string RollupSpec(UsageRollupShape? r) => r is null ? "None" :
        $"RollupSpec(table=Table({PythonNames.Str(r.Table.DbTableName)}, MetaData(), Column({PythonNames.Str(r.Key.DbName)}, Integer, primary_key=True), Column({PythonNames.Str(r.Tenant.DbName)}, Integer), " +
        $"Column({PythonNames.Str(r.Kind.DbName)}, String), Column({PythonNames.Str(r.Month.DbName)}, {(r.MonthIsDateOnly ? "Date" : "DateTime")}), Column({PythonNames.Str(r.Quantity.DbName)}, {(r.QuantityIsDecimal ? "Numeric" : "Integer")}){Schema(r.Table)}), " +
        $"key={PythonNames.Str(r.Key.DbName)}, tenant={PythonNames.Str(r.Tenant.DbName)}, kind={PythonNames.Str(r.Kind.DbName)}, month={PythonNames.Str(r.Month.DbName)}, quantity={PythonNames.Str(r.Quantity.DbName)}, date_only={(r.MonthIsDateOnly ? "True" : "False")})";

    private static string Schema(TableModel table) => DialectInfo.For(table.Dialect).NamesSchema(table.SchemaName) ? $", schema={PythonNames.Str(table.SchemaName)}" : "";

    private const string ScheduleText = """"


ROLLUP_EVERY_HOURS = §HOURS


def roll_up_all() -> None:
    """Rolls up every ledger that has monthly totals, each in a session of its own."""
    with SessionLocal() as session:
        for spec in SPECS:
            roll_up(session, None, spec)


async def roll_up_every() -> None:
    """Rolls the old events up every ROLLUP_EVERY_HOURS hours (UsageRollupHours); the first run is one interval after the start. Run it in one instance of the API only."""
    while True:
        await asyncio.sleep(ROLLUP_EVERY_HOURS * 3600)
        try:
            await asyncio.to_thread(roll_up_all)
        except Exception:
            logging.getLogger("usage").exception("The scheduled usage roll-up failed.")
"""";

    private const string RefuseText = """"
    with _gate(session, spec, tenant):
        return _record(session, tenant, kind, quantity, occurred_on, extra, spec)


def _record(session: Session, tenant: int, kind: str, quantity: Decimal | int, occurred_on: datetime | None, extra: dict[str, Any] | None, spec: UsageSpec) -> dict[str, Any]:
    if occurred_on is None or month_start(occurred_on) == month_start():
        most = allowed(session, tenant, kind, spec)
        if most is not None:
            had = used_this_month(session, tenant, kind, spec)
            if had + quantity > most:
                session.rollback()
                return {"id": None, "used": had, "allowed": most, "over": True, "refused": True}
"""";

    /// <summary> The lock a refusing record takes so that two requests cannot both read the month before either writes: the tenant's row is locked until the commit (PostgreSQL, MySQL, SQL Server), and SQLite, which has one
    /// process, takes a lock of its own. </summary>
    private static string GateText(SqlDialect dialect)
    {
        string head = "\nimport threading\nfrom contextlib import contextmanager\n\n";
        if (dialect == SqlDialect.Sqlite)
            return head + "_GATE = threading.Lock()\n\n\n@contextmanager\ndef _gate(session: Session, spec: UsageSpec, tenant: int) -> Iterator[None]:\n    \"\"\"Lets one request at a time check a plan limit and write the event (SQLite has one process).\"\"\"\n    with _GATE:\n        yield\n";
        string statement = dialect == SqlDialect.SqlServer
            ? "select(spec.tenants.c[spec.tenant_key]).where(spec.tenants.c[spec.tenant_key] == tenant).with_hint(spec.tenants, \"WITH (UPDLOCK, ROWLOCK)\", \"mssql\")"
            : "select(spec.tenants.c[spec.tenant_key]).where(spec.tenants.c[spec.tenant_key] == tenant).with_for_update()";
        return head + "@contextmanager\ndef _gate(session: Session, spec: UsageSpec, tenant: int) -> Iterator[None]:\n    \"\"\"Locks the tenant's row until the commit, so two requests cannot both read the month before either writes.\"\"\"\n    session.execute(" + statement + ")\n    try:\n        yield\n    except Exception:\n        session.rollback()\n        raise\n";
    }

    private const string RefuseRouteText = """"
        if result.get("refused"):
            raise ApiError(402, "Plan limit reached", problem_type=PROBLEM_LIMIT, detail=LIMIT_TEXT.format(kind, result["allowed"], result["used"]))
        return result
"""";

    private const string AllowedNone = """"
def allowed(session: Session, tenant: int, kind: str, spec: "UsageSpec | None" = None) -> int | None:
    return None
"""";

    private const string AllowedLimited = """"
def allowed(session: Session, tenant: int, kind: str, spec: "UsageSpec | None" = None) -> int | None:
    """What the tenant's plan allows of a kind this month, or None when it is unlimited (a kind this table does not count for a limit, a tenant with no plan, no limit row)."""
    if kind not in (spec or SPECS[0]).limited:
        return None
    plan = subscriptions.current_state(tenant, session)["plan"]
    return None if plan is None else subscriptions.allowed(session, plan, kind)
"""";

    private const string Template = """"
"""The usage ledger tables (UsageTables): an append-only table of billable events written only by record_usage, summed per month and kind. The same routes, sentences and answers as the ASP.NET API (API_Usage).
GET reads the events of the caller's tenant (an administrator), POST records an event for a tenant (a platform administrator). Nothing edits or deletes an event. Generated by PY_Usage."""
import asyncio
import logging
import uuid
from dataclasses import dataclass
from datetime import date, datetime, timedelta, timezone
from decimal import Decimal
from typing import Any, Callable, Iterator

from fastapi import APIRouter, Depends, Query
from pydantic import BaseModel
from sqlalchemy import Boolean, Column, Date, DateTime, Integer, MetaData, Numeric, String, Table, delete, func, insert, select, update
from sqlalchemy.orm import Session

from app.auth import CurrentUser, require_user
from app.db import SessionLocal, get_session
§SUBIMPORT
from app.routes.tenancy import is_platform
from app.support import ApiError

PROBLEM_PLATFORM = "§PLATFORM_TYPE"
§LIMITCONSTANT

class UsageBody(BaseModel):
    """What a platform administrator sends to record usage for a tenant."""

    tenantId: int | None = None
    kind: str | None = None
    quantity: Decimal | None = None
    occurredOn: datetime | None = None


@dataclass(frozen=True)
class RollupSpec:
    """The monthly total table of the ledger: one row per tenant, kind and month (month is a date when date_only)."""

    table: Any
    key: str
    tenant: str
    kind: str
    month: str
    quantity: str
    date_only: bool

    def col(self, name: str) -> Any:
        return self.table.c[name]

    def first_day(self, year: int, month: int) -> Any:
        return date(year, month, 1) if self.date_only else datetime(year, month, 1)


@dataclass(frozen=True)
class UsageSpec:
    """One table of UsageTables: the columns the routes use (quantity is None when each event is one unit; fill gives the other required columns an example value, or is None when it cannot)."""

    route: str
    name: str
    table: Any
    tenants: Any
    key: str
    tenant: str
    tenant_key: str
    kind: str
    quantity: str | None
    occurred: str
    decimal: bool
    kind_length: int | None
    fill: Callable[[], dict[str, Any]] | None
    limited: tuple[str, ...]
    rollup: RollupSpec | None

    def col(self, name: str) -> Any:
        return self.table.c[name]


SPECS = [
§SPECS
]

§ALLOWED
§GATE

def _now() -> datetime:
    return datetime.now(timezone.utc).replace(tzinfo=None)


def month_start(at: datetime | None = None) -> datetime:
    """The first moment of the month (UTC) that contains at."""
    moment = at or _now()
    return datetime(moment.year, moment.month, 1)


def _units(spec: UsageSpec) -> Any:
    return func.coalesce(func.sum(spec.col(spec.quantity)), 0) if spec.quantity else func.count()


def _whole(spec: UsageSpec, value: Any) -> Any:
    return value if spec.decimal else int(value)


def used_this_month(session: Session, tenant: int, kind: str, spec: UsageSpec | None = None) -> Decimal | int:
    """The units of a kind the tenant used this month (UTC). The plan limit of a limited kind reads this."""
    spec = spec or SPECS[0]
    start = month_start()
    end = datetime(start.year + (start.month == 12), start.month % 12 + 1, 1)
    statement = select(_units(spec)).where(spec.col(spec.tenant) == tenant, spec.col(spec.kind) == kind, spec.col(spec.occurred) >= start, spec.col(spec.occurred) < end)
    return _whole(spec, session.scalar(statement) or 0)


def record_usage(session: Session, tenant: int, kind: str, quantity: Decimal | int = 1, occurred_on: datetime | None = None, extra: dict[str, Any] | None = None, spec: UsageSpec | None = None) -> dict[str, Any]:
    """Records an event: the only way a row gets into the ledger. The event is written (the ledger is what happened), unless the project refuses use past the plan (UsageRefuse: then refused is true and nothing is written); the answer says whether the tenant is now over its plan."""
    spec = spec or SPECS[0]
    if quantity <= 0:
        raise ValueError("A usage event counts at least one unit.")
§REFUSE    values: dict[str, Any] = {spec.tenant: tenant, spec.kind: kind, spec.occurred: occurred_on or _now()}
    if spec.quantity:
        values[spec.quantity] = quantity if spec.decimal else int(quantity)
    if spec.fill is not None:
        values.update(spec.fill())
    values.update(extra or {})
    result = session.execute(insert(spec.table).values(values))
    session.commit()
    used = used_this_month(session, tenant, kind, spec)
    most = allowed(session, tenant, kind, spec)
    return {"id": result.inserted_primary_key[0], "used": used, "allowed": most, "over": most is not None and used > most}


def remaining(session: Session, tenant: int, kind: str, spec: UsageSpec | None = None) -> Decimal | int | None:
    """How many more units of a kind the tenant may use this month before its plan is passed; None when it is unlimited. Call it before the work a unit pays for."""
    most = allowed(session, tenant, kind, spec)
    return None if most is None else max(0, most - used_this_month(session, tenant, kind, spec))


def summary(session: Session, spec: UsageSpec, tenant: int, kind: str | None, since: datetime | None, until: datetime | None) -> list[dict[str, Any]]:
    """The tenant's events summed per month and kind, newest month first, with the months that were rolled up into the monthly totals."""
    occurred = spec.col(spec.occurred)
    year, month = func.extract("year", occurred), func.extract("month", occurred)
    statement = select(year, month, spec.col(spec.kind), _units(spec), func.count()).where(spec.col(spec.tenant) == tenant)
    if kind and kind.strip():
        statement = statement.where(spec.col(spec.kind) == kind)
    if since is not None:
        statement = statement.where(occurred >= since)
    if until is not None:
        statement = statement.where(occurred < until)
    rows = session.execute(statement.group_by(year, month, spec.col(spec.kind))).all()
    ordered = sorted(rows, key=lambda r: (-int(r[0]), -int(r[1]), str(r[2])))
    lines = {(int(r[0]), int(r[1]), str(r[2])): [_whole(spec, r[3]), int(r[4])] for r in ordered}
    if spec.rollup is not None:
        roll = spec.rollup
        totals = session.execute(select(roll.col(roll.month), roll.col(roll.kind), roll.col(roll.quantity)).where(roll.col(roll.tenant) == tenant)).all()
        for month_value, total_kind, total in totals:
            start = datetime(month_value.year, month_value.month, 1)
            if (kind and kind.strip() and total_kind != kind) or (since is not None and start < since) or (until is not None and datetime(start.year + (start.month == 12), start.month % 12 + 1, 1) > until):
                continue
            key = (start.year, start.month, str(total_kind))
            if key in lines:
                lines[key][0] = lines[key][0] + _whole(spec, total)
            else:
                lines[key] = [_whole(spec, total), 0]
    return [{"month": f"{y:04d}-{m:02d}", "kind": k, "quantity": values[0], "events": values[1]} for (y, m, k), values in sorted(lines.items(), key=lambda item: (-item[0][0], -item[0][1], item[0][2]))]


ROLLUP_DAYS = §ROLLUPDAYS


def rollup_before(now: datetime | None = None) -> datetime:
    """The first day of the month before which events are rolled up: the month that contains today minus ROLLUP_DAYS. Never later than the start of this month."""
    return month_start((now or _now()) - timedelta(days=ROLLUP_DAYS))


def roll_up(session: Session, before: datetime | None = None, spec: UsageSpec | None = None) -> dict[str, int]:
    """Adds the events before the day (default rollup_before, never later than this month) into the monthly totals and removes them, a thousand at a time, each batch in one transaction:
    an event is removed only when it was counted."""
    spec = spec or SPECS[0]
    roll = spec.rollup
    if roll is None:
        return {"events": 0, "months": 0}
    cutoff = before if before is not None and before < month_start() else rollup_before()
    occurred = spec.col(spec.occurred)
    units = spec.col(spec.quantity) if spec.quantity else func.count()
    events = months = 0
    while True:
        columns = [spec.col(spec.key), spec.col(spec.tenant), spec.col(spec.kind), occurred] + ([spec.col(spec.quantity)] if spec.quantity else [])
        batch = session.execute(select(*columns).where(occurred < cutoff).order_by(spec.col(spec.key)).limit(1000)).all()
        if not batch:
            break
        totals: dict[tuple[int, str, int, int], Any] = {}
        for row in batch:
            key = (int(row[1]), str(row[2]), row[3].year, row[3].month)
            totals[key] = totals.get(key, 0) + (row[4] if spec.quantity else 1)
        for (tenant, kind, year, month), added in totals.items():
            first = roll.first_day(year, month)
            found = session.execute(select(roll.col(roll.key), roll.col(roll.quantity)).where(roll.col(roll.tenant) == tenant, roll.col(roll.kind) == kind, roll.col(roll.month) == first)).first()
            if found is None:
                session.execute(insert(roll.table).values({roll.tenant: tenant, roll.kind: kind, roll.month: first, roll.quantity: added if spec.decimal else int(added)}))
            else:
                session.execute(update(roll.table).where(roll.col(roll.key) == found[0]).values({roll.quantity: found[1] + added if spec.decimal else int(found[1]) + int(added)}))
            months += 1
        session.execute(delete(spec.table).where(spec.col(spec.key).in_([int(row[0]) for row in batch])))
        session.commit()
        events += len(batch)
    return {"events": events, "months": months}
§SCHEDULE

router = APIRouter()


def add_routes(spec: UsageSpec) -> None:
    @router.get(spec.route, name=spec.name + "UsageSummary")
    def summary_route(tenant_id: int | None = Query(None, alias="tenantId"), kind: str | None = None, since: datetime | None = Query(None, alias="from"), until: datetime | None = Query(None, alias="to"),
                      session: Session = Depends(get_session), user: CurrentUser = Depends(require_user)):
        """An administrator reads the usage of their tenant (a platform administrator names one with ?tenantId=)."""
        if not user.is_admin:
            raise ApiError(§FORBIDDEN, §ADMINS_TITLE, detail=§ADMINS_TEXT)
        tenant = user.tenant_id if user.tenant_id is not None else tenant_id
        if tenant is None:
            raise ApiError(§BADREQUEST, "Bad request", detail=§NAMETENANT_TEXT)
        return summary(session, spec, tenant, kind, since, until)

    if spec.rollup is not None:

        @router.post(spec.route + "/roll-up", name=spec.name + "UsageRollUp")
        def roll_up_route(before: datetime | None = None, session: Session = Depends(get_session), user: CurrentUser = Depends(require_user)):
            """A platform administrator rolls the old events up into the monthly totals (?before= names a day: only events before it, and never this month's)."""
            if not is_platform(user):
                raise ApiError(§FORBIDDEN, §PLATFORM_TITLE, problem_type=PROBLEM_PLATFORM, detail=§PLATFORM_TEXT)
            return roll_up(session, before, spec)

    if spec.fill is None:
        return

    @router.post(spec.route, name=spec.name + "UsageRecord", status_code=201)
    def record_route(body: UsageBody | None = None, session: Session = Depends(get_session), user: CurrentUser = Depends(require_user)):
        """A platform administrator records usage for a tenant. 201 with what the month holds now and whether the tenant is over its plan."""
        if not is_platform(user):
            raise ApiError(§FORBIDDEN, §PLATFORM_TITLE, problem_type=PROBLEM_PLATFORM, detail=§PLATFORM_TEXT)
        if body is None or body.tenantId is None:
            raise ApiError(§BADREQUEST, "Bad request", detail=§NEEDSTENANT_TEXT)
        kind = (body.kind or "").strip()
        if not kind:
            raise ApiError(§BADREQUEST, "Bad request", detail=§NEEDSKIND_TEXT)
        if spec.kind_length is not None and len(kind) > spec.kind_length:
            raise ApiError(§BADREQUEST, "Bad request", detail=§KINDLONG_TEXT.format(spec.kind_length))
        quantity = 1 if body.quantity is None else body.quantity
        if quantity <= 0:
            raise ApiError(§BADREQUEST, "Bad request", detail=§QUANTITY_TEXT)
        if spec.quantity and not spec.decimal and quantity != int(quantity):
            raise ApiError(§BADREQUEST, "Bad request", detail=§QUANTITYWHOLE_TEXT)
        if session.scalar(select(spec.tenants.c[spec.tenant_key]).where(spec.tenants.c[spec.tenant_key] == body.tenantId).limit(1)) is None:
            raise ApiError(§NOTFOUND, "Not found", detail=§UNKNOWNTENANT_TEXT)
        occurred = body.occurredOn if body.occurredOn is None or body.occurredOn.tzinfo is None else body.occurredOn.astimezone(timezone.utc).replace(tzinfo=None)
        result = record_usage(session, body.tenantId, kind, quantity, occurred, None, spec)
§REFUSEROUTE

for _spec in SPECS:
    add_routes(_spec)
"""";
}
