using static CodeGenNew.Core.TemplateText;
using System.Text;

namespace CodeGenNew.Core;

/// <summary> The Python (FastAPI) counterpart of <see cref="SubscriptionWriter"/>: <c>app/routes/subscriptions.py</c>, the check that pauses a tenant's writes while its subscription is not active (called from <c>require_user</c>),
/// the plan limits on creates, <c>GET /api/subscription</c> and <c>PUT /api/subscription/{tenant_id}</c>. The same rules, sentences and answers as the ASP.NET API. The tables are described with SQLAlchemy's lightweight
/// <c>table()</c>, so it does not matter whether a model of them was generated (none is: the subscription tables have no ordinary API). </summary>
public static class PythonSubscriptionWriter
{
    /// <summary> Whether the project asks for subscriptions over a Python API: <c>SubscriptionTable</c> with the tenants of the Python API. </summary>
    public static bool Applies(ProjectSettings project) => SubscriptionFeature.IsEnabled(project) && PythonTenantWriter.Applies(project);

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var subscription = SubscriptionFeature.SubscriptionOf(database, project)!;
        var limit = SubscriptionFeature.LimitTableOf(database, project);
        var limited = limit is null ? [] : SubscriptionFeature.LimitedTables(database, project);
        var tenantTable = TenantPlan.TenantTableOf(database, project)!;
        string Str(string value) => PythonNames.Str(value);

        var o = new StringBuilder();
        o.Append(FillAll(Head, ("§INACTIVE_TYPE", SubscriptionWords.InactiveType), ("§LIMIT_TYPE", SubscriptionWords.LimitType), ("§PLATFORM_TYPE", SubscriptionWords.OnlyPlatformType), ("§TOOSMALL_TYPE", SubscriptionWords.PlanTooSmallType)));
        o.Append($"\nACTIVE_STATUSES = ({string.Join(", ", SubscriptionFeature.ActiveStatuses(project).Select(s => Str(s.ToLowerInvariant())))},)\n");
        o.Append($"REQUIRED = {(project.SubscriptionRequired ? "True" : "False")}\n");
        o.Append($"S_KEY = {Str(subscription.Key.DbName)}\nS_TENANT = {Str(subscription.Tenant.DbName)}\nS_PLAN = {Str(subscription.Plan.DbName)}\nS_STATUS = {Str(subscription.Status.DbName)}\n");
        var typed = new List<string> { $"column(S_KEY, Integer)", "column(S_TENANT, Integer)", "column(S_PLAN, Integer)", "column(S_STATUS, String)" };
        if (subscription.PeriodEnd is not null)
        {
            o.Append($"S_END = {Str(subscription.PeriodEnd.DbName)}\n");
            typed.Add("column(S_END, DateTime)");
        }
        if (subscription.PeriodStart is not null)
        {
            o.Append($"S_START = {Str(subscription.PeriodStart.DbName)}\n");
            typed.Add("column(S_START, DateTime)");
        }
        if (subscription.Gateway is not null)
        {
            o.Append($"S_GATEWAY = {Str(subscription.Gateway.DbName)}\n");
            typed.Add("column(S_GATEWAY, String)");
        }
        o.Append($"subscriptions = table({Str(subscription.Table.DbTableName)}, {string.Join(", ", typed)}{Schema(subscription.Table)})\n");
        o.Append($"P_KEY = {Str(subscription.PlanKey.DbName)}\n");
        if (subscription.PlanName is not null)
            o.Append($"P_NAME = {Str(subscription.PlanName.DbName)}\n");
        var planColumns = new List<string> { "column(P_KEY, Integer)" };
        if (subscription.PlanName is not null)
            planColumns.Add("column(P_NAME, String)");
        if (subscription.PlanPrice is not null)
        {
            o.Append($"P_PRICE = {Str(subscription.PlanPrice.DbName)}\n");
            planColumns.Add("column(P_PRICE, Numeric)");
        }
        if (subscription.PlanPeriod is not null)
        {
            o.Append($"P_PERIOD = {Str(subscription.PlanPeriod.DbName)}\n");
            planColumns.Add("column(P_PERIOD, String)");
        }
        o.Append($"plans = table({Str(subscription.PlanTable.DbTableName)}, {string.Join(", ", planColumns)}{Schema(subscription.PlanTable)})\n");
        if (limit is not null)
        {
            o.Append($"L_PLAN = {Str(limit.Plan.DbName)}\nL_KEY = {Str(limit.Key.DbName)}\nL_VALUE = {Str(limit.Value.DbName)}\n");
            o.Append($"plan_limits = table({Str(limit.Table.DbTableName)}, column(L_PLAN, Integer), column(L_KEY, String), column(L_VALUE, Integer){Schema(limit.Table)})\n");
        }
        o.Append($"T_KEY = {Str(TenantPlan.KeyOf(tenantTable)!.DbName)}\n");
        o.Append($"tenant_rows = table({Str(tenantTable.DbTableName)}, column(T_KEY, Integer){Schema(tenantTable)})\n");

        o.Append("\n# the limits: the name of each, the table whose rows of a tenant are counted, the column that says whose they are and the soft-delete flag (None: every row counts)\nLIMITS = {\n");
        foreach (var l in limited)
            o.Append($"    {Str(l.LimitKey)}: (table({Str(l.Table.DbTableName)}, column({Str(l.Tenant.DbName)}, Integer){(l.Deleted is null ? "" : $", column({Str(l.Deleted.DbName)}, Boolean)")}{Schema(l.Table)}), {Str(l.Tenant.DbName)}, {(l.Deleted is null ? "None" : Str(l.Deleted.DbName))}),\n");
        o.Append("}\n");
        o.Append("\n# the paths that add a row to a limited table: the create, its clone and its import; the membership table is added through the members route\nCREATE_PATHS = (\n");
        foreach (var l in limited)
            o.Append($"    ({Str(l.CreatePath.ToLowerInvariant())}, {Str(l.LimitKey)}, {(l.IsMembership ? "True" : "False")}),\n");
        o.Append(")\n");

        string choose = project.SubscriptionPlanChoice
            ? FillAll(ChoosePlan, ("§ADMIN_TEXT", Str(SubscriptionWords.OnlyAdministrator)), ("§NEEDSPLAN_TEXT", Str(SubscriptionWords.NeedsPlan)), ("§UNKNOWNPLAN_TEXT", Str(SubscriptionWords.UnknownPlan)),
                ("§TOOSMALL_TEXT", Str(SubscriptionWords.PlanTooSmallText)), ("§STARTVALUE", subscription.PeriodStart is null ? "" : ", S_START: datetime.now(timezone.utc).replace(tzinfo=None)"))
            : "";
        string renew = subscription.PeriodEnd is null ? ""
            : FillAll(RenewPeriod, ("§PLATFORM_TEXT", Str(SubscriptionWords.OnlyPlatform)), ("§RENEWDAYS_TEXT", Str(SubscriptionWords.RenewDays)), ("§NOSUBSCRIPTION_TEXT", Str(SubscriptionWords.NoSubscriptionToRenew)),
                ("§DAYS", project.SubscriptionPeriodDays.ToString()), ("§RENEWSTART", subscription.PeriodStart is null ? "" : "    values[S_START] = start\n"));
        if (choose.Length > 0)
            choose = "\n" + choose + "\n";
        if (renew.Length > 0)
            renew = "\n" + renew + "\n";
        o.Append(FillAll(Body, ("§CHOOSE", choose), ("§RENEW", renew), ("§CANCHOOSE", project.SubscriptionPlanChoice ? "True" : "False"), ("§TOOSMALL_TYPE", SubscriptionWords.PlanTooSmallType), ("§INACTIVE_TEXT", Str(SubscriptionWords.InactiveText)), ("§TRIAL_TEXT", Str(SubscriptionWords.TrialEndedText)), ("§NONE_TEXT", Str(SubscriptionWords.NoSubscriptionText)),
            ("§LIMIT_TEXT", Str(SubscriptionWords.LimitText)), ("§PLATFORM_TEXT", Str(SubscriptionWords.OnlyPlatform)), ("§ADMIN_TEXT", Str(SubscriptionWords.OnlyAdministrator)),
            ("§NEEDS_TEXT", Str(SubscriptionWords.NeedsPlanAndStatus)), ("§UNKNOWNPLAN_TEXT", Str(SubscriptionWords.UnknownPlan)), ("§UNKNOWNTENANT_TEXT", Str(SubscriptionWords.UnknownTenant)),
            ("§ENDREAD", subscription.PeriodEnd is null ? "null()" : "subscriptions.c[S_END]"), ("§GATEWAYREAD", subscription.Gateway is null ? "null()" : "subscriptions.c[S_GATEWAY]"),
            ("§PLANNAME", subscription.PlanName is null ? "cast(plans.c[P_KEY], String)" : "plans.c[P_NAME]"),
            ("§STARTVALUE", subscription.PeriodStart is null ? "" : ", S_START: datetime.now(timezone.utc).replace(tzinfo=None)"),
            ("§ENDUPDATE", subscription.PeriodEnd is null ? "" : "    if body.periodEnd is not None:\n        values[S_END] = body.periodEnd\n"),
            ("§GATEWAYUPDATE", subscription.Gateway is null ? "" : "    if body.gatewayId is not None:\n        values[S_GATEWAY] = body.gatewayId.strip()\n"),
            ("§PRICEREAD", subscription.PlanPrice is null ? "null()" : "plans.c[P_PRICE]"), ("§PERIODREAD", subscription.PlanPeriod is null ? "null()" : "plans.c[P_PERIOD]"),
            ("§LIMITHELPERS", limit is null ? NoLimitHelpers : LimitHelpers)));
        if (SubscriptionFeature.HasWebhook(database, project))
            o.Append(FillAll(Webhook, ("§SECRETNAME", project.SubscriptionWebhookSecret!), ("§HEADER", SubscriptionWords.WebhookHeader), ("§TOLERANCE", SubscriptionWords.WebhookToleranceSeconds.ToString()),
                ("§SIGNATURE_TYPE", SubscriptionWords.WebhookSignatureType), ("§EXPIRED_TYPE", SubscriptionWords.WebhookExpiredType), ("§WEBHOOKPATH", SubscriptionWords.WebhookPath),
                ("§SIGNATURE_TEXT", SubscriptionWords.WebhookSignatureText), ("§EXPIRED_TEXT", SubscriptionWords.WebhookExpiredText), ("§UNCONFIGURED_TEXT", SubscriptionWords.WebhookUnconfiguredText),
                ("§BODY_TEXT", SubscriptionWords.WebhookBodyText), ("§UNKNOWN_TEXT", SubscriptionWords.WebhookUnknownText),
                ("§ENDVALUE", subscription.PeriodEnd is null ? "" : WebhookEnd.Replace("§BODY_TEXT", SubscriptionWords.WebhookBodyText))));
        return o.ToString();
    }

    private const string WebhookEnd = """
    end = message.get("periodEnd")
    if isinstance(end, str) and end.strip():
        try:
            parsed = datetime.fromisoformat(end.strip().replace("Z", "+00:00"))
            values[S_END] = parsed if parsed.tzinfo is None else parsed.astimezone(timezone.utc).replace(tzinfo=None)
        except ValueError:
            raise ApiError(400, "Bad request", detail="§BODY_TEXT")

""";

    private static string Schema(TableModel table)
    {
        bool schemaNamed = DialectInfo.For(table.Dialect).NamesSchema(table.SchemaName);
        return schemaNamed ? $", schema={PythonNames.Str(table.SchemaName)}" : "";
    }

    private const string Head = """"
"""Plans, subscriptions and entitlements (SubscriptionTable): a tenant's writes pause while its subscription is not active, a create past a plan limit is refused, and the subscription is read and changed through
GET /api/subscription and PUT /api/subscription/{tenant_id}. The same rules and sentences as the ASP.NET API (API_Subscriptions). Generated by PY_Subscriptions."""
import hashlib
import hmac
import json
import os
import threading
import time
from datetime import datetime, timedelta, timezone
from typing import Any

from fastapi import APIRouter, Depends, Request
from pydantic import BaseModel
from sqlalchemy import Boolean, DateTime, Integer, Numeric, String, cast, column, func, null, select, table
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import Session

from app.auth import CurrentUser, require_user
from app.db import SessionLocal, get_session
from app.routes.tenancy import is_platform, no_tenant
from app.support import ApiError

PROBLEM_INACTIVE = "§INACTIVE_TYPE"
PROBLEM_LIMIT = "§LIMIT_TYPE"
PROBLEM_PLATFORM = "§PLATFORM_TYPE"
PROBLEM_TOOSMALL = "§TOOSMALL_TYPE"
STATE_SECONDS = 10
router = APIRouter()
public_router = APIRouter()   # the routes that need no sign-in: the payment gateway's webhook, when the project names a secret for it
"""";

    private const string LimitHelpers = """"
def allowed(session: Session, plan: int, key: str) -> int | None:
    """What the plan allows for a limit, or None when it is unlimited (no row, or a NULL value)."""
    value = session.scalar(select(plan_limits.c[L_VALUE]).where(plan_limits.c[L_PLAN] == plan, plan_limits.c[L_KEY] == key).limit(1))
    return None if value is None else int(value)


def used(session: Session, tenant: int, key: str) -> int:
    """What the tenant has of the limit: its rows of the table, not counting a soft-deleted row."""
    source, tenant_column, deleted_column = LIMITS[key]
    statement = select(func.count()).select_from(source).where(source.c[tenant_column] == tenant)
    if deleted_column is not None:
        statement = statement.where(source.c[deleted_column].is_(False))
    return int(session.scalar(statement) or 0)


def limit_key(path: str) -> str | None:
    """The limit a create route is counted against, or None when the path adds no row to a limited table."""
    for route, key, membership in CREATE_PATHS:
        if path == route:
            return key
        if not membership and (path == route + "/import" or (path.startswith(route + "/") and path.endswith("/clone"))):
            return key
    return None


def limits_of(session: Session, tenant: int, plan: int | None) -> list[dict[str, Any]]:
    return [{"key": key, "allowed": None if plan is None else allowed(session, plan, key), "used": used(session, tenant, key)} for key in LIMITS]


def allowed_by_plan(session: Session) -> dict[tuple[int, str], int | None]:
    """What each plan allows of each limit, keyed by plan and limit name."""
    statement = select(plan_limits.c[L_PLAN], plan_limits.c[L_KEY], plan_limits.c[L_VALUE]).where(plan_limits.c[L_KEY].in_(list(LIMITS)))
    return {(int(plan), key): None if value is None else int(value) for plan, key, value in session.execute(statement)}
"""";

    private const string NoLimitHelpers = """"
def allowed(session: Session, plan: int, key: str) -> int | None:
    return None


def used(session: Session, tenant: int, key: str) -> int:
    return 0


def limit_key(path: str) -> str | None:
    return None


def limits_of(session: Session, tenant: int, plan: int | None) -> list[dict[str, Any]]:
    return []


def allowed_by_plan(session: Session) -> dict[tuple[int, str], int | None]:
    return {}
"""";

    private const string Body = """"

_known: dict[int, tuple[dict[str, Any], float]] = {}
_known_lock = threading.Lock()


def _now() -> datetime:
    return datetime.now(timezone.utc).replace(tzinfo=None)


def evaluate(status: str, end: datetime | None, now: datetime) -> tuple[bool, str | None]:
    """The rule: a status outside ACTIVE_STATUSES pauses writes, and a trialing subscription also stops at its period end."""
    if status.strip().lower() not in ACTIVE_STATUSES:
        return False, §INACTIVE_TEXT.format(status)
    if end is not None and end.replace(tzinfo=None) < now and status.strip().lower() == "trialing":
        return False, §TRIAL_TEXT.format(end.strftime("%Y-%m-%d"))
    return True, None


def read_state(session: Session, tenant: int) -> dict[str, Any]:
    row = session.execute(select(subscriptions.c[S_PLAN], subscriptions.c[S_STATUS], §ENDREAD, §GATEWAYREAD).where(subscriptions.c[S_TENANT] == tenant)
                          .order_by(subscriptions.c[S_KEY].desc()).limit(1)).first()
    if row is None:
        return {"exists": False, "plan": None, "status": None, "end": None, "gateway": None, "writable": not REQUIRED, "why": §NONE_TEXT if REQUIRED else None}
    writable, why = evaluate(str(row[1]), row[2], _now())
    return {"exists": True, "plan": None if row[0] is None else int(row[0]), "status": str(row[1]), "end": row[2], "gateway": row[3], "writable": writable, "why": why}


def current_state(tenant: int) -> dict[str, Any]:
    """The tenant's subscription (its newest row), remembered for STATE_SECONDS so a status change reaches a request in use after that long unless forget_state is called."""
    now = time.time()
    with _known_lock:
        known = _known.get(tenant)
    if known is None or now - known[1] >= STATE_SECONDS:
        with SessionLocal() as session:
            state = read_state(session, tenant)
        with _known_lock:
            _known[tenant] = known = (state, now)
    return known[0]


def forget_state(tenant: int) -> None:
    with _known_lock:
        _known.pop(tenant, None)


§LIMITHELPERS


def check_write(request: Request, tenant: int) -> None:
    """Called by require_user for a caller who works in a tenant: a paused subscription refuses a write (402), and a create that would pass a plan limit is refused (402). Reads, sign-in and the subscription routes are never paused."""
    if request.method not in ("POST", "PUT", "PATCH", "DELETE"):
        return
    path = request.url.path.rstrip("/").lower()
    if path.startswith("/api/subscription") or (path.startswith("/api/auth") and not path.startswith("/api/auth/members")):
        return
    state = current_state(tenant)
    if not state["writable"]:
        raise ApiError(402, "Subscription not active", problem_type=PROBLEM_INACTIVE, detail=state["why"])
    key = limit_key(path) if request.method == "POST" else None
    if key is not None and state["plan"] is not None:
        with SessionLocal() as session:
            most = allowed(session, state["plan"], key)
            if most is not None:
                have = used(session, tenant, key)
                if have >= most:
                    raise ApiError(402, "Plan limit reached", problem_type=PROBLEM_LIMIT, detail=§LIMIT_TEXT.format(key, most, have))


def plans_of(session: Session, current: int | None) -> list[dict[str, Any]]:
    """Every plan, cheapest first (a plan with no price last), with what it allows of each limit; current marks the tenant's own."""
    rows = session.execute(select(plans.c[P_KEY], §PLANNAME, §PRICEREAD, §PERIODREAD)).all()
    values = allowed_by_plan(session)
    ordered = sorted(rows, key=lambda row: (row[2] is None, float(row[2] or 0), row[0]))
    return [{"planId": int(row[0]), "name": "" if row[1] is None else str(row[1]), "price": None if row[2] is None else float(row[2]), "period": None if row[3] is None else str(row[3]),
             "current": row[0] == current, "limits": [{"key": key, "allowed": values.get((int(row[0]), key))} for key in LIMITS]} for row in ordered]


def view(session: Session, tenant: int) -> dict[str, Any]:
    state = read_state(session, tenant)
    listed = plans_of(session, state["plan"])
    current = next((plan for plan in listed if plan["current"]), None)
    return {"hasSubscription": state["exists"], "planId": state["plan"], "planName": None if current is None else current["name"], "planPrice": None if current is None else current["price"],
            "planPeriod": None if current is None else current["period"], "status": state["status"], "periodEnd": state["end"], "gatewayId": state["gateway"], "writable": state["writable"],
            "reason": state["why"], "limits": limits_of(session, tenant, state["plan"]), "plans": listed, "canChoose": §CANCHOOSE}


@router.get("/api/subscription")
def get_subscription(session: Session = Depends(get_session), user: CurrentUser = Depends(require_user)):
    """The subscription of the caller's tenant for its administrator: the plan, the status and, for each limit, what the plan allows and what the tenant has."""
    if not user.is_admin:
        raise ApiError(403, "Administrators only", detail=§ADMIN_TEXT)
    if user.tenant_id is None:
        raise no_tenant()
    return view(session, user.tenant_id)


§CHOOSE
class SubscriptionChange(BaseModel):
    """The body of PUT /api/subscription/{tenant_id}: what to change. A tenant with no subscription needs a planId and a status."""
    planId: int | None = None
    status: str | None = None
    periodEnd: datetime | None = None
    gatewayId: str | None = None


@router.put("/api/subscription/{tenant_id}")
def put_subscription(tenant_id: int, body: SubscriptionChange, session: Session = Depends(get_session), user: CurrentUser = Depends(require_user)):
    """Sets the plan, status, period end or gateway id of a tenant's subscription, making the row when the tenant has none. Only a platform administrator may."""
    if not is_platform(user):
        raise ApiError(403, "Platform administrators only", problem_type=PROBLEM_PLATFORM, detail=§PLATFORM_TEXT)
    if session.scalar(select(tenant_rows.c[T_KEY]).where(tenant_rows.c[T_KEY] == tenant_id).limit(1)) is None:
        raise ApiError(404, "Not found", detail=§UNKNOWNTENANT_TEXT)
    if body.planId is not None and session.scalar(select(plans.c[P_KEY]).where(plans.c[P_KEY] == body.planId).limit(1)) is None:
        raise ApiError(400, "Bad request", detail=§UNKNOWNPLAN_TEXT)
    existing = session.scalar(select(subscriptions.c[S_KEY]).where(subscriptions.c[S_TENANT] == tenant_id).order_by(subscriptions.c[S_KEY].desc()).limit(1))
    values: dict[str, Any] = {}
    if body.planId is not None:
        values[S_PLAN] = body.planId
    if body.status is not None and body.status.strip():
        values[S_STATUS] = body.status.strip()
§ENDUPDATE§GATEWAYUPDATE    try:
        if existing is None:
            if body.planId is None or not (body.status or "").strip():
                raise ApiError(400, "Bad request", detail=§NEEDS_TEXT)
            session.execute(subscriptions.insert().values({**values, S_TENANT: tenant_id§STARTVALUE}))
        elif values:
            session.execute(subscriptions.update().where(subscriptions.c[S_KEY] == existing).values(values))
        session.commit()
    except IntegrityError:
        session.rollback()
        raise ApiError(400, "Bad request", detail="The subscription could not be saved (a value is too long or missing).")
    forget_state(tenant_id)
    return view(session, tenant_id)
§RENEW
"""";

    private const string ChoosePlan = """"


class PlanChoice(BaseModel):
    """The body of PUT /api/subscription/plan: the plan the tenant moves to."""
    planId: int | None = None


@router.put("/api/subscription/plan")
def choose_plan(body: PlanChoice, session: Session = Depends(get_session), user: CurrentUser = Depends(require_user)):
    """The tenant's administrator moves the tenant to a plan of the list (SubscriptionPlanChoice=true): at once, with no payment step. A plan that allows less than the tenant already has of a limit is refused (409)."""
    if not user.is_admin:
        raise ApiError(403, "Administrators only", detail=§ADMIN_TEXT)
    if user.tenant_id is None:
        raise no_tenant()
    tenant = user.tenant_id
    if body.planId is None:
        raise ApiError(400, "Bad request", detail=§NEEDSPLAN_TEXT)
    if session.scalar(select(plans.c[P_KEY]).where(plans.c[P_KEY] == body.planId).limit(1)) is None:
        raise ApiError(400, "Bad request", detail=§UNKNOWNPLAN_TEXT)
    for key in LIMITS:
        most = allowed(session, body.planId, key)
        have = used(session, tenant, key)
        if most is not None and have > most:
            raise ApiError(409, "Plan too small", problem_type=PROBLEM_TOOSMALL, detail=§TOOSMALL_TEXT.format(key, most, have))
    existing = session.scalar(select(subscriptions.c[S_KEY]).where(subscriptions.c[S_TENANT] == tenant).order_by(subscriptions.c[S_KEY].desc()).limit(1))
    try:
        if existing is None:
            status = "active" if "active" in ACTIVE_STATUSES else ACTIVE_STATUSES[0]
            session.execute(subscriptions.insert().values({S_TENANT: tenant, S_PLAN: body.planId, S_STATUS: status§STARTVALUE}))
        else:
            session.execute(subscriptions.update().where(subscriptions.c[S_KEY] == existing).values({S_PLAN: body.planId}))
        session.commit()
    except IntegrityError:
        session.rollback()
        raise ApiError(400, "Bad request", detail="The subscription could not be saved (a value is too long or missing).")
    forget_state(tenant)
    return view(session, tenant)
"""";

    private const string RenewPeriod = """"


class RenewRequest(BaseModel):
    """The body of POST /api/subscription/{tenant_id}/renew: the days to add (blank: the project's period)."""
    days: int | None = None


@router.post("/api/subscription/{tenant_id}/renew")
def renew_subscription(tenant_id: int, body: RenewRequest | None = None, session: Session = Depends(get_session), user: CurrentUser = Depends(require_user)):
    """A platform administrator renews a tenant's period: the new period starts when the old one ends (or now, when it already ended) and lasts the given days (default §DAYS). The status is not changed."""
    if not is_platform(user):
        raise ApiError(403, "Platform administrators only", problem_type=PROBLEM_PLATFORM, detail=§PLATFORM_TEXT)
    days = §DAYS if body is None or body.days is None else body.days
    if days < 1 or days > 3660:
        raise ApiError(400, "Bad request", detail=§RENEWDAYS_TEXT)
    row = session.execute(select(subscriptions.c[S_KEY], subscriptions.c[S_END]).where(subscriptions.c[S_TENANT] == tenant_id).order_by(subscriptions.c[S_KEY].desc()).limit(1)).first()
    if row is None:
        raise ApiError(404, "Not found", detail=§NOSUBSCRIPTION_TEXT)
    now = _now()
    end = row[1]
    if end is not None and end.tzinfo is not None:
        end = end.astimezone(timezone.utc).replace(tzinfo=None)
    start = end if end is not None and end > now else now
    values: dict[str, Any] = {S_END: start + timedelta(days=days)}
§RENEWSTART    session.execute(subscriptions.update().where(subscriptions.c[S_KEY] == row[0]).values(values))
    session.commit()
    forget_state(tenant_id)
    return view(session, tenant_id)
"""";

    private const string Webhook = """"


WEBHOOK_SECRET_NAME = "§SECRETNAME"
WEBHOOK_HEADER = "§HEADER"
WEBHOOK_TOLERANCE = §TOLERANCE
PROBLEM_SIGNATURE = "§SIGNATURE_TYPE"
PROBLEM_EXPIRED = "§EXPIRED_TYPE"
_events: dict[str, float] = {}


def _remember_event(event: str) -> bool:
    """True the first time an event id is seen in ten minutes."""
    now = time.time()
    with _known_lock:
        for old in [key for key, seen in _events.items() if now - seen > 600]:
            del _events[old]
        if event in _events:
            return False
        _events[event] = now
    return True


@public_router.post("§WEBHOOKPATH")
async def webhook(request: Request):
    """The payment gateway tells the API a subscription's new status. The message is signed (X-Gateway-Signature: t=unix seconds,v1=hex of the HMAC-SHA256 of "t.body" with the shared secret in the environment variable
    named by WEBHOOK_SECRET_NAME), must be no older than WEBHOOK_TOLERANCE seconds, and is applied once per event id; the body is { id?, gatewayId, status, periodEnd? }. Open to anyone, since the signature is the check."""
    secret = os.environ.get(WEBHOOK_SECRET_NAME, "")
    if not secret:
        raise ApiError(503, "Webhook not configured", detail="§UNCONFIGURED_TEXT")
    raw = await request.body()
    parts = dict(item.strip().split("=", 1) for item in request.headers.get(WEBHOOK_HEADER, "").split(",") if "=" in item)
    try:
        signed_at = int(parts.get("t", ""))
    except ValueError:
        signed_at = None
    expected = hmac.new(secret.encode(), f"{signed_at}.".encode() + raw, hashlib.sha256).hexdigest()
    if signed_at is None or not hmac.compare_digest(expected, parts.get("v1", "")):
        raise ApiError(401, "Bad signature", problem_type=PROBLEM_SIGNATURE, detail="§SIGNATURE_TEXT")
    if abs(time.time() - signed_at) > WEBHOOK_TOLERANCE:
        raise ApiError(400, "Message expired", problem_type=PROBLEM_EXPIRED, detail="§EXPIRED_TEXT")
    try:
        message = json.loads(raw)
    except ValueError:
        message = None
    if not isinstance(message, dict):
        message = {}
    gateway = str(message.get("gatewayId") or "").strip()
    status = str(message.get("status") or "").strip()
    if not gateway or not status:
        raise ApiError(400, "Bad request", detail="§BODY_TEXT")
    event = message.get("id")
    if event and not _remember_event(str(event)):
        return {"applied": False, "duplicate": True}
    values: dict[str, Any] = {S_STATUS: status}
§ENDVALUE    with SessionLocal() as session:
        row = session.execute(select(subscriptions.c[S_KEY], subscriptions.c[S_TENANT]).where(subscriptions.c[S_GATEWAY] == gateway).order_by(subscriptions.c[S_KEY].desc()).limit(1)).first()
        if row is None:
            raise ApiError(404, "Not found", detail="§UNKNOWN_TEXT")
        session.execute(subscriptions.update().where(subscriptions.c[S_KEY] == row[0]).values(values))
        session.commit()
    forget_state(int(row[1]))
    return {"applied": True, "duplicate": False}
"""";
}
