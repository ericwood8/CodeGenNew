using static CodeGenNew.Core.TemplateText;
using System.Text;

namespace CodeGenNew.Core;

/// <summary> The Python (FastAPI) counterpart of <see cref="FeatureFlagWriter"/>: <c>app/routes/features.py</c> with <c>all_flags</c>, <c>is_enabled</c>, <c>GET /api/features</c> and <c>PUT /api/features/{name}</c>. The same rules,
/// sentences and answers as the ASP.NET API. The tables are described with SQLAlchemy's lightweight <c>table()</c>, so no model of them is needed (none is generated: the flag tables have no ordinary API). </summary>
public static class PythonFeatureWriter
{
    private const string NL = "\n";

    /// <summary> The plan as the Python API serves it: the tenant column counts only when the Python API has tenants, the person column only with a sign-in that carries the owner id. </summary>
    public static FeatureFlagPlan? PlanOf(DatabaseModel database, ProjectSettings project)
    {
        if (FeatureFlagTables.Resolve(database, project).Plan is not { } plan)
            return null;
        var tenant = PythonTenantWriter.Applies(project) ? plan.OverrideTenant : null;
        return tenant is null && plan.OverridePerson is null ? null : plan with { OverrideTenant = tenant, PlanLink = tenant is not null && PythonSubscriptionWriter.Applies(project) ? plan.PlanLink : null };
    }

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var plan = PlanOf(database, project)!;
        var tenantTable = plan.OverrideTenant is null ? null : TenantPlan.TenantTableOf(database, project);
        var user = plan.OverridePerson is null ? null : UserTable.Resolve(database, project).Plan!;
        string Str(string value) => PythonNames.Str(value);
        string Schema(TableModel table) => DialectInfo.For(table.Dialect).NamesSchema(table.SchemaName) ? $", schema={Str(table.SchemaName)}" : "";

        var o = new StringBuilder();
        o.Append(FillAll(Head, ("§PATH", FeatureFlagTables.Path), ("§PLANIMPORT", plan.PlanLink is null ? "" : "from app.routes.subscriptions import read_state" + NL)));
        o.Append($"\nF_KEY = {Str(plan.FlagKey.DbName)}\nF_NAME = {Str(plan.FlagName.DbName)}\nF_DEFAULT = {Str(plan.FlagDefault.DbName)}\n");
        o.Append($"flags = table({Str(plan.Flag.DbTableName)}, column(F_KEY, Integer), column(F_NAME, String), column(F_DEFAULT, Boolean){Schema(plan.Flag)})\n");
        o.Append($"O_FLAG = {Str(plan.OverrideFlag.DbName)}\nO_ON = {Str(plan.OverrideEnabled.DbName)}\n");
        var columns = new List<string> { "column(O_FLAG, Integer)", "column(O_ON, Boolean)" };
        if (plan.OverrideTenant is not null)
        {
            o.Append($"O_TENANT = {Str(plan.OverrideTenant.DbName)}\n");
            columns.Add("column(O_TENANT, Integer)");
        }
        if (plan.OverridePerson is not null)
        {
            o.Append($"O_PERSON = {Str(plan.OverridePerson.DbName)}\n");
            columns.Add("column(O_PERSON, Integer)");
        }
        o.Append($"overrides = table({Str(plan.Override.DbTableName)}, {string.Join(", ", columns)}{Schema(plan.Override)})\n");
        if (tenantTable is not null)
        {
            o.Append($"T_KEY = {Str(TenantPlan.KeyOf(tenantTable)!.DbName)}\ntenant_rows = table({Str(tenantTable.DbTableName)}, column(T_KEY, Integer){Schema(tenantTable)})\n");
        }
        if (user is not null)
        {
            o.Append($"U_KEY = {Str(user.Table.PrimaryKeyColumns[0].DbName)}\nperson_rows = table({Str(user.Table.DbTableName)}, column(U_KEY, Integer){Schema(user.Table)})\n");
        }

        string tenantCondition, personCondition;
        if (plan.OverrideTenant is not null && plan.OverridePerson is not null)
        {
            tenantCondition = "and_(overrides.c[O_TENANT] == tenant, overrides.c[O_PERSON].is_(None))";
            personCondition = "and_(overrides.c[O_PERSON] == person, overrides.c[O_TENANT].is_(None))";
        }
        else
        {
            tenantCondition = plan.OverrideTenant is not null ? "overrides.c[O_TENANT] == tenant" : "false()";
            personCondition = plan.OverridePerson is not null ? "overrides.c[O_PERSON] == person" : "false()";
        }

        string planTable = "", planRead = "";
        if (plan.PlanLink is { } link)
        {
            planTable = $"PF_FLAG = {Str(link.Flag.DbName)}{NL}PF_PLAN = {Str(link.Plan.DbName)}{NL}plan_flags = table({Str(link.Table.DbTableName)}, column(PF_FLAG, Integer), column(PF_PLAN, Integer){Schema(link.Table)}){NL}";
            planRead = PlanRead + NL;
        }

        string working = TenantPlan.IsEnabled(project) && PythonTenantWriter.Applies(project) ? "getattr(user, \"tenant_id\", None)" : "None";
        string tenantArm = plan.OverrideTenant is null ? "" : "    if tenant_target is not None:\n        return and_(overrides.c[O_FLAG] == flag" + (plan.OverridePerson is null ? "" : ", overrides.c[O_PERSON].is_(None)") + ", overrides.c[O_TENANT] == tenant_target)\n";
        string personArm = plan.OverridePerson is null ? "" : "    if person_target is not None:\n        return and_(overrides.c[O_FLAG] == flag" + (plan.OverrideTenant is null ? "" : ", overrides.c[O_TENANT].is_(None)") + ", overrides.c[O_PERSON] == person_target)\n";
        string tenantValues = plan.OverrideTenant is null ? "" : "            if tenant_target is not None:\n                values[O_TENANT] = tenant_target\n";
        string personValues = plan.OverridePerson is null ? "" : "            if person_target is not None:\n                values[O_PERSON] = person_target\n";
        string tenantCheck = tenantTable is null ? "" : "    if tenant_target is not None and session.scalar(select(tenant_rows.c[T_KEY]).where(tenant_rows.c[T_KEY] == tenant_target).limit(1)) is None:\n        raise ApiError(404, \"Not found\", detail=" + Str(SubscriptionWords.UnknownTenant) + ")\n";
        string personCheck = user is null ? "" : "    if person_target is not None and session.scalar(select(person_rows.c[U_KEY]).where(person_rows.c[U_KEY] == person_target).limit(1)) is None:\n        raise ApiError(404, \"Not found\", detail=" + Str(FeatureFlagTables.UnknownPersonText) + ")\n";

        o.Append(planTable);
        o.Append(FillAll(Body, ("§PLANREAD", planRead), ("§TENANTCONDITION", tenantCondition), ("§PERSONCONDITION", personCondition), ("§WORKING", working), ("§SECONDS", project.FeatureCacheSeconds.ToString()),
            ("§OWNER", plan.OverridePerson is null ? "None" : "user.owner_id"), ("§HASTENANT", plan.OverrideTenant is null ? "False" : "True"), ("§HASPERSON", plan.OverridePerson is null ? "False" : "True"),
            ("§TENANTARM", tenantArm), ("§PERSONARM", personArm), ("§TENANTVALUES", tenantValues), ("§PERSONVALUES", personValues), ("§TENANTCHECK", tenantCheck), ("§PERSONCHECK", personCheck),
            ("§UNKNOWN_TEXT", Str(FeatureFlagTables.UnknownText)), ("§ADMIN_TEXT", Str(FeatureFlagTables.OnlyAdministratorText)), ("§SCOPE_TEXT", Str(FeatureFlagTables.NeedsScopeText)),
            ("§PLATFORM_TEXT", Str(FeatureFlagTables.OnlyPlatformText)), ("§NOTARGET_TEXT", Str(FeatureFlagTables.NoTargetText)), ("§PATH", FeatureFlagTables.Path)));
        return o.ToString();
    }

    private const string PlanRead = """"
    if tenant is not None:
        plan = read_state(session, tenant)["plan"]
        if plan is not None:
            for (flag,) in session.execute(select(plan_flags.c[PF_FLAG]).where(plan_flags.c[PF_PLAN] == plan)):
                if int(flag) in result:
                    result[int(flag)][1] = True
"""";

    private const string Head = """"
"""Feature flags kept in two tables (FeatureTables): a flag is on or off by default, a tenant's override replaces the default and a person's override replaces both. GET §PATH and PUT §PATH/{name}.
The same rules and sentences as the ASP.NET API (API_Features). Generated by PY_Features."""
import time
from typing import Any

from fastapi import APIRouter, Depends
from pydantic import BaseModel
from sqlalchemy import Boolean, Integer, String, and_, column, false, select, table
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import Session

from app.auth import CurrentUser, require_user
from app.db import get_session
§PLANIMPORTfrom app.support import ApiError

router = APIRouter()
"""";

    private const string Body = """"


CACHE_SECONDS = §SECONDS
_cache: dict[tuple[int | None, int | None], tuple[float, dict[str, bool]]] = {}


def clear_cache() -> None:
    """Forgets what was read, for a change made to the tables by other means than the routes."""
    _cache.clear()


def all_flags(session: Session, tenant: int | None, person: int | None) -> dict[str, bool]:
    """Every flag by name with its value for the tenant and the person (either may be None): the default, then the plan's, then the tenant's override, then the person's.
    What was read is kept for FeatureCacheSeconds (zero reads the tables on every call); a change through the feature routes forgets it at once."""
    if CACHE_SECONDS > 0:
        hit = _cache.get((tenant, person))
        if hit is not None and time.monotonic() - hit[0] < CACHE_SECONDS:
            return dict(hit[1])
    read = _read_flags(session, tenant, person)
    if CACHE_SECONDS > 0:
        _cache[(tenant, person)] = (time.monotonic(), read)
    return dict(read)


def _read_flags(session: Session, tenant: int | None, person: int | None) -> dict[str, bool]:
    result ={int(row[0]): [str(row[1]), bool(row[2])] for row in session.execute(select(flags.c[F_KEY], flags.c[F_NAME], flags.c[F_DEFAULT]))}
§PLANREAD    if tenant is not None:
        for flag, on in session.execute(select(overrides.c[O_FLAG], overrides.c[O_ON]).where(§TENANTCONDITION)):
            if int(flag) in result:
                result[int(flag)][1] = bool(on)
    if person is not None:
        for flag, on in session.execute(select(overrides.c[O_FLAG], overrides.c[O_ON]).where(§PERSONCONDITION)):
            if int(flag) in result:
                result[int(flag)][1] = bool(on)
    return {name: on for name, on in result.values()}


def is_enabled(session: Session, name: str, user: CurrentUser) -> bool:
    """Whether the named flag is on for the caller of the request; a name that is not a flag is off: `if is_enabled(session, "new-invoice-screen", user): ...`"""
    return all_flags(session, §WORKING, §OWNER).get(name, False)


@router.get("§PATH")
def get_features(session: Session = Depends(get_session), user: CurrentUser = Depends(require_user)):
    """Every flag by name as the caller sees it (their tenant's and their own overrides applied)."""
    return all_flags(session, §WORKING, §OWNER)


class FeatureChange(BaseModel):
    """The body of PUT §PATH/{name}: on or off (null removes the override) for a tenant or a person."""
    enabled: bool | None = None
    tenantId: int | None = None
    personId: int | None = None


def _target(flag: int, tenant_target: int | None, person_target: int | None):
    """The rows of the flag that belong to the target."""
§TENANTARM§PERSONARM    return false()


@router.put("§PATH/{name}")
def put_feature(name: str, body: FeatureChange, session: Session = Depends(get_session), user: CurrentUser = Depends(require_user)):
    """Switches a flag for the caller's tenant (an administrator of the tenant), or for the tenant or person named in the body (a platform administrator)."""
    if not user.is_admin:
        raise ApiError(403, "Administrators only", detail=§ADMIN_TEXT)
    flag = session.scalar(select(flags.c[F_KEY]).where(flags.c[F_NAME] == name).limit(1))
    if flag is None:
        raise ApiError(404, "Not found", detail=§UNKNOWN_TEXT)
    working: int | None = §WORKING
    tenant_target: int | None = None
    person_target: int | None = None
    if working is not None:
        if not §HASTENANT or body.personId is not None or (body.tenantId is not None and body.tenantId != working):
            raise ApiError(403, "Platform administrators only", detail=§PLATFORM_TEXT)
        tenant_target = working
    else:
        if (not §HASTENANT and body.tenantId is not None) or (not §HASPERSON and body.personId is not None):
            raise ApiError(400, "Bad request", detail=§NOTARGET_TEXT)
        if (not §HASTENANT and not §HASPERSON) or (body.tenantId is None) == (body.personId is None):
            raise ApiError(400, "Bad request", detail=§SCOPE_TEXT)
        tenant_target = body.tenantId
        person_target = body.personId
§TENANTCHECK§PERSONCHECK    where = _target(int(flag), tenant_target, person_target)
    exists = session.scalar(select(overrides.c[O_FLAG]).where(where).limit(1)) is not None
    try:
        if body.enabled is None:
            session.execute(overrides.delete().where(where))
        elif exists:
            session.execute(overrides.update().where(where).values({O_ON: body.enabled}))
        else:
            values: dict[str, Any] = {O_FLAG: int(flag), O_ON: body.enabled}
§TENANTVALUES§PERSONVALUES            session.execute(overrides.insert().values(values))
        session.commit()
    except IntegrityError:
        session.rollback()
        raise ApiError(400, "Bad request", detail="The feature could not be saved.")
    clear_cache()
    return all_flags(session, tenant_target if tenant_target is not None else §WORKING, person_target if person_target is not None else §OWNER)
"""";
}
