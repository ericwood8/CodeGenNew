using System.Text;

namespace CodeGenNew.Core;

/// <summary> The Python (FastAPI) counterpart of <see cref="TenantWriter"/>: <c>app/routes/tenancy.py</c>, the tenant of a sign-in, <c>GET /api/auth/tenants</c> and <c>POST /api/auth/switch-tenant</c> for a person in several tenants, the
/// check that a token's tenant is still switched on, and the helpers the tables' routers call to show, change and stamp only the caller's tenant (the rules and sentences of <see cref="TenantPlan"/> and <see cref="TenantWords"/>, the same as the ASP.NET API).
/// The tables are described with SQLAlchemy's lightweight <c>table()</c>, so it does not matter whether a model of them was generated. </summary>
public static class PythonTenantWriter
{
    /// <summary> Whether the project asks for tenants over a Python API: <c>TenantTable</c> with sign-in. The EF-only rule of the ASP.NET API does not apply: the routers filter by hand. </summary>
    public static bool Applies(ProjectSettings project) => TenantPlan.HasApiTenancy(project);

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var tenantTable = TenantPlan.TenantTableOf(database, project)!;
        var key = TenantPlan.KeyOf(tenantTable)!;
        var membership = TenantPlan.MembershipOf(database, project);
        var (userPlan, _) = UserTable.Resolve(database, project);
        var userOwned = userPlan is null ? null : TenantPlan.OwnedBy(userPlan.Table, project);
        var active = tenantTable.IsActiveColumn;
        var host = TenantPlan.HostColumnOf(database, project);
        var text = tenantTable.Columns.FirstOrDefault(c => c.IsStringColumn && !c.IsPrimaryKey && (c.Name.EqualsIgnoreCase("Name") || c.Name.EqualsIgnoreCase("Title")));
        string Str(string value) => PythonNames.Str(value);

        var o = new StringBuilder();
        o.Append(Shared.Replace("§HOSTIMPORT", host is null ? "" : "import ipaddress\n").Replace("§REQUESTIMPORT", host is null ? "" : ", Request").Replace("§FUNC", host is null ? "" : "func, "));
        o.Append(Tables(tenantTable, key, active, text, host, membership, userPlan, userOwned));
        if (host is not null)
            o.Append(Fill(HostLookup, ("§HOST_TYPE", TenantWords.HostType), ("§HOST_TEXT", TenantWords.Host)));
        var ownedTables = TenantPlan.OwnedTables(database, project);
        if (ownedTables.Count > 0)
            o.Append($"\nTENANT_COLUMN = {Str(ownedTables[0].Column.DbName)}\nTENANT_OWNED = frozenset({{{string.Join(", ", ownedTables.Select(t => Str(t.Table.DbTableName)))}}})\n");

        string textExpression = text is null ? "cast(tenants.c[TENANT_KEY], String)" : "tenants.c[TENANT_TEXT]";
        if (active is null)
            o.Append("\n\ndef ensure_active(tenant_id: int) -> None:\n    \"\"\"The tenant table has no active flag, so a token never outlives its tenant.\"\"\"\n");
        else
            o.Append(Fill(ActiveCheck, ("§INACTIVE_TEXT", TenantWords.Inactive)));

        if (membership is not null && userPlan is not null)
        {
            o.Append(Fill(Members, ("§HOSTLOGIN", host is null ? "" : HostLoginLines + "\n"), ("§TEXT", textExpression), ("§ROLE", membership.Role is null ? "null()" : "members.c[MEMBER_ROLE]"), ("§ACTIVEWHERE", active is null ? "" : ".where(tenants.c[TENANT_ACTIVE].is_(True))"),
                ("§NOTMEMBER_TYPE", TenantWords.NotMemberType), ("§NOMEMBER_TEXT", Str(TenantWords.NotMember))));
            o.Append(Fill(Attach, ("§ROLEVALUE", membership.Role is null ? "" : ", MEMBER_ROLE: ADMIN_ROLE")));
            o.Append(NewPerson(userPlan, project, membership, null));
            o.Append(TenantPlan.PeopleOf(database, project) is null ? PeoplePassThrough : PeopleScoped);
            o.Append(Fill(Routes, ("§CURRENT", "user.tenant_id"), ("§REQUESTPARAM", host is null ? "" : ", request: Request"), ("§HOSTSWITCH", host is null ? "" : HostSwitchLines + "\n")));
        }
        else if (userOwned is not null && userPlan is not null)
        {
            o.Append(Fill(OwnColumn, ("§HOSTLOGIN", host is null ? "" : HostOwnLogin + "\n"), ("§HOSTCHECK", host is null ? "" : HostOwnCheck + "\n"), ("§ACTIVEWHERE", active is null ? "" : ", tenants.c[TENANT_ACTIVE].is_(True)"), ("§NOTMEMBER_TYPE", TenantWords.NotMemberType), ("§NOMEMBER_TEXT", Str(TenantWords.NotMember))));
            o.Append("\n\ndef attach_creator(session: Session, user: CurrentUser, tenant_id: int) -> None:\n    \"\"\"The people belong to a tenant by their own row, so a new tenant has no first administrator to attach.\"\"\"\n");
            o.Append("\n\ndef attach_admin(session: Session, person_id: int | None, tenant_id: int, admin: dict[str, Any] | None = None, password: str | None = None) -> None:\n    \"\"\"The people belong to a tenant by their own row, so an onboarded tenant has no person to attach by key; a new first administrator is saved with the tenant.\"\"\"\n    if admin is not None:\n        new_person(session, admin, password, tenant_id)\n");
            o.Append(NewPerson(userPlan, project, null, userOwned));
        }
        else
        {
            o.Append("\n\ndef login_tenant(session: Session, user: DirectoryUser, requested: int | None, host: str | None = None) -> tuple[int | None, str | None]:\n    \"\"\"The sign-in works in no tenant: no membership table is named and the people are not tenant-owned.\"\"\"\n    return None, None\n");
            o.Append("\n\ndef attach_admin(session: Session, person_id: int | None, tenant_id: int, admin: dict[str, Any] | None = None, password: str | None = None) -> None:\n    \"\"\"No membership table is named, so an onboarded tenant has no first administrator to attach.\"\"\"\n    if admin is not None:\n        raise ApiError(400, \"Bad request\", detail=\"The tenant settings name no way to tie a new person to a tenant.\")\n");
            o.Append("\n\ndef attach_creator(session: Session, user: CurrentUser, tenant_id: int) -> None:\n    \"\"\"No membership table is named, so a new tenant has no first administrator to attach.\"\"\"\n");
        }
        return o.ToString();
    }

    /// <summary> <c>new_person</c>: the first administrator of an onboarded tenant as a new person, saved with the tenant (the user table's own model and schema, the password hashed the way <c>set-password</c> does). </summary>
    private static string NewPerson(UserTablePlan? userPlan, ProjectSettings project, TenantMembership? membership, TenantOwned? userOwned)
    {
        if (userPlan is null)
            return "";
        var table = PythonTable.Of(userPlan.Table, project);
        string FieldOf(ColumnModel column) => table.Fields.First(f => f.Column == column).Name;
        string tenantLink = userOwned is null ? "" : $"    person.{FieldOf(userOwned.Column)} = tenant_id\n";
        string adminFlag = membership?.Role is null && userPlan.Admin is { } flag ? $"    person.{FieldOf(flag)} = True\n" : "";
        string memberLine = membership is null ? "" : $"    session.execute(members.insert().values({{MEMBER_USER: person.{table.Key.Name}, MEMBER_TENANT: tenant_id{(membership.Role is null ? "" : ", MEMBER_ROLE: ADMIN_ROLE")}}}))\n";
        return Fill(NewPersonText, ("§MODULE", table.Module), ("§CLASS", table.Class), ("§SCHEMA", table.Schema), ("§NAMEFIELD", FieldOf(userPlan.UserName)), ("§HASHFIELD", table.HashField!.Name),
            ("§TENANTLINK", tenantLink), ("§ADMINFLAG", adminFlag), ("§MEMBERLINE", memberLine));
    }

    private const string PeopleScoped = """"


def only_members(statement, key_column, user: CurrentUser):
    """The people of the caller's tenant (every person for a platform administrator); a caller in no tenant sees none."""
    if is_platform(user):
        return statement
    if user.tenant_id is None:
        return statement.where(false())
    return statement.where(key_column.in_(select(members.c[MEMBER_USER]).where(members.c[MEMBER_TENANT] == user.tenant_id)))


def refuse_non_member(session: Session, person_id, user: CurrentUser) -> None:
    """A person of another tenant is not found."""
    if is_platform(user):
        return
    if user.tenant_id is None or session.scalar(select(members.c[MEMBER_USER]).where(members.c[MEMBER_USER] == person_id, members.c[MEMBER_TENANT] == user.tenant_id)) is None:
        raise ApiError(404, "Not found.")


def attach_person(session: Session, person_id, user: CurrentUser) -> None:
    """A person added inside a tenant belongs to it."""
    if user.tenant_id is None:
        return
    session.execute(members.insert().values({MEMBER_USER: person_id, MEMBER_TENANT: user.tenant_id}))
    session.commit()


def detach_person(session: Session, person_id, user: CurrentUser) -> None:
    """A person removed inside a tenant leaves it, in the transaction that removes them (a person of another tenant too is refused by the foreign key)."""
    if user.tenant_id is None:
        return
    session.execute(members.delete().where(members.c[MEMBER_USER] == person_id, members.c[MEMBER_TENANT] == user.tenant_id))
"""";

    private const string PeoplePassThrough = """"


def only_members(statement, key_column, user: CurrentUser):
    """The membership rows cannot be made for a person added inside a tenant, so the people are shown to every tenant."""
    return statement


def refuse_non_member(session: Session, person_id, user: CurrentUser) -> None:
    return None


def attach_person(session: Session, person_id, user: CurrentUser) -> None:
    return None


def detach_person(session: Session, person_id, user: CurrentUser) -> None:
    return None
"""";

    private const string NewPersonText = """"


def new_person(session: Session, admin: dict[str, Any], password: str | None, tenant_id: int) -> None:
    """Saves the first administrator as a new person, in the transaction that made the tenant: a short password, a missing value or a user name that someone has is a 400 and the tenant is not saved."""
    from pydantic import ValidationError

    from app.auth import hash_password
    from app.models.§MODULE import §CLASS
    from app.schemas.§MODULE import §SCHEMA
    from app.users import MINIMUM_PASSWORD_LENGTH

    if len(password or "") < MINIMUM_PASSWORD_LENGTH:
        raise ApiError(400, "Password too short", detail=f"The first administrator needs a password of at least {MINIMUM_PASSWORD_LENGTH} characters.")
    try:
        body = §SCHEMA.model_validate(admin)
    except ValidationError:
        raise ApiError(400, "Bad request", detail="The first administrator is missing a value or has one that is wrong.")
    name = (body.§NAMEFIELD or "").strip()
    if not name or session.scalar(select(users.c[USER_KEY]).where(users.c[USER_NAME] == name)) is not None:
        raise ApiError(400, "User name", detail="The first administrator needs a user name that no one has; send adminId for a person who exists.")
    person = §CLASS(**body.columns())
    person.§NAMEFIELD = name
    person.§HASHFIELD = hash_password(password)
§TENANTLINK§ADMINFLAG    session.add(person)
    session.flush()
§MEMBERLINE
"""";

    private static string Tables(TableModel tenantTable, ColumnModel key, ColumnModel? active, ColumnModel? text, ColumnModel? host, TenantMembership? membership, UserTablePlan? userPlan, TenantOwned? userOwned)
    {
        string Str(string value) => PythonNames.Str(value);
        var o = new StringBuilder("\n");
        o.Append($"TENANT_KEY = {Str(key.DbName)}\n");
        if (text is not null)
            o.Append($"TENANT_TEXT = {Str(text.DbName)}\n");
        if (active is not null)
            o.Append($"TENANT_ACTIVE = {Str(active.DbName)}\n");
        if (host is not null)
            o.Append($"TENANT_HOST = {Str(host.DbName)}\n");
        o.Append("tenants = " + LightTable(tenantTable, new[] { key, text, active, host }) + "\n");
        if (userPlan is not null && (membership is not null || userOwned is not null))
        {
            var userColumns = new List<ColumnModel?> { userPlan.UserName, userPlan.Table.PrimaryKeyColumns.First() };
            if (userOwned is not null && membership is null)
                userColumns.Add(userOwned.Column);
            o.Append($"USER_NAME = {Str(userPlan.UserName.DbName)}\nUSER_KEY = {Str(userPlan.Table.PrimaryKeyColumns.First().DbName)}\n");
            if (userOwned is not null && membership is null)
                o.Append($"USER_TENANT = {Str(userOwned.Column.DbName)}\n");
            o.Append("users = " + LightTable(userPlan.Table, userColumns) + "\n");
        }
        if (membership is not null)
        {
            o.Append($"MEMBER_USER = {Str(membership.User.DbName)}\nMEMBER_TENANT = {Str(membership.Tenant.DbName)}\n");
            if (membership.Role is not null)
                o.Append($"MEMBER_ROLE = {Str(membership.Role.DbName)}\n");
            o.Append("members = " + LightTable(membership.Table, new[] { membership.User, membership.Tenant, membership.Role }) + "\n");
        }
        return o.ToString();
    }

    /// <summary> <c>table("Name", column("A"), column("B"), schema="s")</c> for the given columns (null and repeated ones are left out); the schema is named only when it is not the database's default one. </summary>
    public static string LightTable(TableModel table, IEnumerable<ColumnModel?> columns)
    {
        bool schemaNamed = table.Dialect is SqlDialect.SqlServer or SqlDialect.PostgreSql && !table.SchemaName.EqualsIgnoreCase(table.Dialect == SqlDialect.SqlServer ? "dbo" : "public");
        var names = columns.OfType<ColumnModel>().Select(c => c.DbName).Distinct().ToList();
        return $"table({PythonNames.Str(table.DbTableName)}, {string.Join(", ", names.Select(n => $"column({PythonNames.Str(n)})"))}" + (schemaNamed ? $", schema={PythonNames.Str(table.SchemaName)}" : "") + ")";
    }

    private static string Fill(string text, params (string Token, string Value)[] values)
    {
        foreach (var (token, value) in values.OrderByDescending(v => v.Token.Length))
            text = text.Replace(token, value);
        return text;
    }

    private static readonly string Shared = Fill(""""
"""Tenants (TenantTable): the tenant of a sign-in, the tenants a person can switch between, and the helpers every router calls so a caller reads and writes only the rows of the tenant in their token.
The same rules and sentences as the ASP.NET API (API_Tenants). Generated by PY_Tenants."""
§HOSTIMPORTimport threading
import time
from datetime import datetime, timedelta, timezone
from typing import Any, NamedTuple

from fastapi import APIRouter, Depends§REQUESTIMPORT
from pydantic import BaseModel, ConfigDict, Field
from sqlalchemy import String, cast, column, false, §FUNCnull, select, table
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import Session

from app.auth import ADMIN_ROLE, EXPIRES_MINUTES, CurrentUser, DirectoryUser, create_token, require_user, session_response
from app.db import SessionLocal, get_session
from app.support import ApiError

PROBLEM_NO_TENANT = "§NOTENANT_TYPE"
PROBLEM_NOT_MEMBER = "§NOTMEMBER_TYPE"
PROBLEM_ONBOARDING = "§ONBOARD_TYPE"
PROBLEM_INACTIVE = "§INACTIVE_TYPE"
ACTIVE_SECONDS = 10
router = APIRouter()


def is_platform(user: CurrentUser) -> bool:
    """A platform administrator: the administrator role, working in no tenant. They see and create tenants."""
    return user.is_admin and user.tenant_id is None


def no_tenant() -> ApiError:
    return ApiError(403, "No tenant", problem_type=PROBLEM_NO_TENANT, detail="§NOTENANT_TEXT")


def not_member() -> ApiError:
    return ApiError(403, "Not a member", problem_type=PROBLEM_NOT_MEMBER, detail="§NOMEMBER_TEXT")


def only_platform() -> ApiError:
    return ApiError(403, "Platform administrators only", problem_type=PROBLEM_ONBOARDING, detail="§ONLYPLATFORM")


def never_deleted() -> ApiError:
    return ApiError(403, "Tenants are not deleted", problem_type=PROBLEM_ONBOARDING, detail="§NEVERDELETED")


def onboarded() -> ApiError:
    return ApiError(403, "Tenants are onboarded", problem_type=PROBLEM_ONBOARDING, detail="§CREATEBYONBOARDING")


def require_tenant(user: CurrentUser) -> int:
    """The caller's tenant; a caller who works in none cannot write a tenant-owned row."""
    if user.tenant_id is None:
        raise no_tenant()
    return user.tenant_id


def only_tenant(statement, tenant_column, user: CurrentUser):
    """The rows of the caller's tenant; a caller in no tenant sees none."""
    return statement.where(false()) if user.tenant_id is None else statement.where(tenant_column == user.tenant_id)


def only_own_tenant(statement, key_column, user: CurrentUser):
    """The tenant table: the caller's own tenant, or every tenant for a platform administrator."""
    return statement if is_platform(user) else only_tenant(statement, key_column, user)


def refuse_other_tenant(row_tenant, user: CurrentUser) -> None:
    """Another tenant's row is not found."""
    if user.tenant_id is None or row_tenant != user.tenant_id:
        raise ApiError(404, "Not found.")


def refuse_other_tenant_row(row_key, user: CurrentUser) -> None:
    if not is_platform(user) and row_key != user.tenant_id:
        raise ApiError(404, "Not found.")

"""",
        ("§NOTENANT_TYPE", TenantWords.NoTenantType), ("§NOTMEMBER_TYPE", TenantWords.NotMemberType), ("§ONBOARD_TYPE", TenantWords.OnboardingType), ("§INACTIVE_TYPE", TenantWords.InactiveType),
        ("§NOTENANT_TEXT", TenantWords.NoTenant), ("§NOMEMBER_TEXT", TenantWords.NotMember), ("§ONLYPLATFORM", TenantWords.OnlyPlatform), ("§NEVERDELETED", TenantWords.NeverDeleted),
        ("§CREATEBYONBOARDING", TenantWords.CreateByOnboarding));

    private const string ActiveCheck = """"


_known: dict[int, tuple[bool, float]] = {}
_known_lock = threading.Lock()


def ensure_active(tenant_id: int) -> None:
    """A token of a tenant that has been switched off is a 401. The answer for a tenant is remembered for ACTIVE_SECONDS, so a switch-off reaches a token in use after that long."""
    now = time.time()
    with _known_lock:
        known = _known.get(tenant_id)
    if known is None or now - known[1] >= ACTIVE_SECONDS:
        with SessionLocal() as session:
            alive = session.scalar(select(tenants.c[TENANT_KEY]).where(tenants.c[TENANT_KEY] == tenant_id, tenants.c[TENANT_ACTIVE].is_(True))) is not None
        with _known_lock:
            _known[tenant_id] = known = (alive, now)
    if not known[0]:
        raise ApiError(401, "Tenant switched off", problem_type=PROBLEM_INACTIVE, detail="§INACTIVE_TEXT")


def forget_tenant(tenant_id: int) -> None:
    """Forgets what is known of a tenant, so the next call asks the database."""
    with _known_lock:
        _known.pop(tenant_id, None)
"""";

    private const string Members = """"


class Member(NamedTuple):
    tenant_id: int
    text: str
    role: str | None


def members_of(session: Session, user_name: str) -> list[Member]:
    """The active tenants the person belongs to, by tenant id, with the role they have in each (None: their own role stays)."""
    person = session.scalar(select(users.c[USER_KEY]).where(users.c[USER_NAME] == user_name.strip()))
    if person is None:
        return []
    statement = (select(members.c[MEMBER_TENANT], §TEXT, §ROLE)
                 .join(tenants, tenants.c[TENANT_KEY] == members.c[MEMBER_TENANT])
                 .where(members.c[MEMBER_USER] == person)§ACTIVEWHERE
                 .order_by(members.c[MEMBER_TENANT]))
    return [Member(int(row[0]), "" if row[1] is None else str(row[1]), row[2]) for row in session.execute(statement).all()]


def login_tenant(session: Session, user: DirectoryUser, requested: int | None, host: str | None = None) -> tuple[int | None, str | None]:
    """The tenant (and the role in it) a sign-in works in: the one the login asks for, else the person's first. A person in no tenant (a platform administrator) signs in to none."""
§HOSTLOGIN    found = members_of(session, user.user_name)
    if not found:
        if requested is None:
            return None, None
        raise not_member()
    chosen = found[0] if requested is None else next((m for m in found if m.tenant_id == requested), None)
    if chosen is None:
        raise not_member()
    return chosen.tenant_id, chosen.role


class SwitchRequest(BaseModel):
    model_config = ConfigDict(populate_by_name=True)

    tenant_id: int | None = Field(None, alias="tenantId")
"""";

    private const string Routes = """"


@router.get("/api/auth/tenants")
def list_tenants(session: Session = Depends(get_session), user: CurrentUser = Depends(require_user)):
    return [{"id": m.tenant_id, "text": m.text, "role": m.role, "current": m.tenant_id == §CURRENT} for m in members_of(session, user.name)]


@router.post("/api/auth/switch-tenant")
def switch_tenant(body: SwitchRequest§REQUESTPARAM, session: Session = Depends(get_session), user: CurrentUser = Depends(require_user)):
    """A new token for the same person, working in another tenant they belong to."""
    member = None if body.tenant_id is None else next((m for m in members_of(session, user.name) if m.tenant_id == body.tenant_id), None)
    if member is None:
        raise not_member()
§HOSTSWITCH    person = DirectoryUser(user.name, "", member.role or user.role, user.owner_id)
    return session_response(person, member.tenant_id)
"""";

    private const string Attach = """"


def attach_creator(session: Session, user: CurrentUser, tenant_id: int) -> None:
    """Makes the person who created a tenant (a platform administrator who is a row of the user table) its first administrator; nothing happens for a caller who is not a row (the bootstrap administrator)."""
    person = session.scalar(select(users.c[USER_KEY]).where(users.c[USER_NAME] == user.name.strip()))
    if person is None:
        return
    try:
        session.execute(members.insert().values({MEMBER_USER: person, MEMBER_TENANT: tenant_id§ROLEVALUE}))
        session.commit()
    except IntegrityError:
        session.rollback()


def attach_admin(session: Session, person_id: int | None, tenant_id: int, admin: dict[str, Any] | None = None, password: str | None = None) -> None:
    """Makes a person the first administrator of a tenant, in the transaction that made it: the person who exists (person_id) or a new one (admin and password), not both. A person the user table does not know is a 400 and the tenant is not saved."""
    if admin is not None:
        if person_id is not None:
            raise ApiError(400, "One first administrator", detail="Send adminId (a person who exists) or admin (a new person), not both.")
        new_person(session, admin, password, tenant_id)
        return
    if person_id is None:
        return
    if session.scalar(select(users.c[USER_KEY]).where(users.c[USER_KEY] == person_id)) is None:
        raise ApiError(400, "Bad request", detail="The tenant could not be saved (the person is unknown).")
    session.execute(members.insert().values({MEMBER_USER: person_id, MEMBER_TENANT: tenant_id§ROLEVALUE}))
"""";

    private const string HostLoginLines = """"
    on_host = host_tenant(host)
    if on_host is not None:
        if requested is not None and requested != on_host:
            raise other_host()
        requested = on_host
"""";

    private const string HostOwnLogin = """"
    on_host = host_tenant(host)
"""";

    private const string HostOwnCheck = """"
    if on_host is not None and on_host != tenant:
        raise other_host()
"""";

    private const string HostSwitchLines = """"
    on_host = host_tenant(request.url.hostname)
    if on_host is not None and on_host != body.tenant_id:
        raise other_host()
"""";

    private const string HostLookup = """"


HOST_SECONDS = 60
_hosts_lock = threading.Lock()
_hosts: dict[str, tuple[int | None, float]] = {}


def other_host() -> ApiError:
    return ApiError(403, "Another tenant's address", problem_type="§HOST_TYPE", detail="§HOST_TEXT")


def host_tenant(host: str | None) -> int | None:
    """The tenant a host name belongs to: the first label of the name (acme in acme.example.com) compared with the tenant table's host column. A host that names no tenant (localhost, an address) has none, and is not restricted. A name is remembered a minute."""
    name = (host or "").lower()
    dot = name.find(".")
    if dot <= 0:
        return None
    try:
        ipaddress.ip_address(name)
        return None
    except ValueError:
        pass
    label = name[:dot]
    now = time.time()
    with _hosts_lock:
        known = _hosts.get(label)
    if known is None or now - known[1] >= HOST_SECONDS:
        with SessionLocal() as session:
            found = session.scalar(select(tenants.c[TENANT_KEY]).where(func.lower(tenants.c[TENANT_HOST]) == label))
        with _hosts_lock:
            if len(_hosts) > 1000:
                _hosts.clear()
            _hosts[label] = known = (None if found is None else int(found), now)
    return known[0]


def forget_hosts() -> None:
    """Forgets the host names, so the next call asks the database (the generated tests change a tenant's host name)."""
    with _hosts_lock:
        _hosts.clear()


def check_host(request: Request, tenant_id: int) -> None:
    """A token that works in one tenant is refused (403) on the host of another. A caller in no tenant (a platform administrator) is not restricted."""
    on_host = host_tenant(request.url.hostname)
    if on_host is not None and on_host != tenant_id:
        raise other_host()
"""";

    private const string OwnColumn = """"


def login_tenant(session: Session, user: DirectoryUser, requested: int | None, host: str | None = None) -> tuple[int | None, str | None]:
    """The tenant of the person's own row goes into the token; a person of a switched-off tenant cannot sign in."""
§HOSTLOGIN    found = session.scalar(select(users.c[USER_TENANT]).where(users.c[USER_NAME] == user.user_name.strip()))
    if found is None:
        return None, None
    tenant = int(found)
    if session.scalar(select(tenants.c[TENANT_KEY]).where(tenants.c[TENANT_KEY] == tenant§ACTIVEWHERE)) is None:
        raise ApiError(403, "Not a member", problem_type="§NOTMEMBER_TYPE", detail="§NOMEMBER_TEXT")
§HOSTCHECK    return tenant, None
"""";
}
