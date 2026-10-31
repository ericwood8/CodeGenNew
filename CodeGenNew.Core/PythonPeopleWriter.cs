using System.Text;

namespace CodeGenNew.Core;

/// <summary> <c>app/people.py</c> of the Python (FastAPI) API: <c>GET</c> and <c>POST /api/auth/first-person</c> (no sign-in), <c>PUT /api/auth/password</c> and, with a membership table, the members of the caller's
/// tenant under <c>/api/auth/members</c>. The same routes, answers and sentences as the ASP.NET API (<see cref="PeoplePlan"/>, <see cref="PeopleWords"/>). The tables are described with SQLAlchemy's lightweight <c>table()</c>. </summary>
public static class PythonPeopleWriter
{
    public static string File(DatabaseModel database, ProjectSettings project)
    {
        if (PeoplePlan.For(database, project) is not { } plan)
            return "";
        string Str(string value) => PythonNames.Str(value);
        var o = new StringBuilder();
        void Add(string text) => o.Append(text).Append((char)10);
        bool members = plan.HasMembers;
        var membership = plan.Membership;
        var tenantTable = plan.TenantTable;
        var soft = tenantTable is null ? null : TenantPlan.DeletedFlagOf(tenantTable);
        var active = tenantTable?.IsActiveColumn;

        var tenantNames = new List<string> { "members", "tenants", "MEMBER_USER", "MEMBER_TENANT", "TENANT_KEY" };
        if (membership?.Role is not null) tenantNames.Add("MEMBER_ROLE");
        if (active is not null) tenantNames.Add("TENANT_ACTIVE");
        if (soft is not null) tenantNames.Add("TENANT_DELETED");
        if (members) tenantNames.Add("no_tenant");
        tenantNames.Sort(StringComparer.OrdinalIgnoreCase);

        Add(Fill(Head, ("§TENANTIMPORT", members ? $"from app.routes.tenancy import {string.Join(", ", tenantNames)}\n" : ""),
            ("§ROLEIMPORT", members ? ", PROBLEM_ROLE_NOT_ALLOWED" : "")));
        o.Append($"\nPERSON_KEY = {Str(plan.Key.DbName)}\nPERSON_LOGIN = {Str(plan.User.UserName.DbName)}\n");
        if (plan.NameColumn is not null)
            o.Append($"PERSON_NAME = {Str(plan.NameColumn.DbName)}\n");
        o.Append("people = " + PythonTenantWriter.LightTable(plan.User.Table, plan.UserColumns) + "\n");
        Add(Models);
        Add(Fill(Routes, ("§FIRSTPERSON_TYPE", ProblemTypes.FirstPersonInvalid), ("§TAKEN_TYPE", ProblemTypes.FirstPersonTaken), ("§PASSWORD_TYPE", ProblemTypes.PasswordInvalid), ("§NOTYOURS_TYPE", ProblemTypes.PasswordNotYours),
            ("§FIRSTPERSON_TITLE", Str(PeopleWords.FirstPersonInvalidTitle)), ("§TAKEN_TITLE", Str(PeopleWords.FirstPersonTakenTitle)), ("§TAKEN_DETAIL", Str(PeopleWords.FirstPersonTakenDetail)),
            ("§PASSWORD_TITLE", Str(PeopleWords.PasswordInvalidTitle)), ("§NOTYOURS_TITLE", Str(PeopleWords.PasswordNotYoursTitle)), ("§NOTYOURS_DETAIL", Str(PeopleWords.PasswordNotYoursDetail)),
            ("§FIRSTPATH", PeopleWords.FirstPersonPath), ("§PASSWORDPATH", PeopleWords.PasswordPath)));
        if (plan.First is { } first)
        {
            string tenantPart = !members ? "" : Fill(FirstMember, ("§ACTIVE", active is null ? "" : "tenants.c[TENANT_ACTIVE].is_(True), "), ("§DELETED", soft is null ? "" : "tenants.c[TENANT_DELETED].is_(False), "),
                ("§ROLEVALUE", membership!.Role is null ? "" : ", MEMBER_ROLE: ADMIN_ROLE"));
            Add(Fill(FirstText, ("§VALUES", string.Join(", ", first.Values.Select(v => $"{Str(v.Column.DbName)}: {Value(v)}"))), ("§FIRSTMEMBER", tenantPart.Length == 0 ? "" : tenantPart + (char)10),("§LOGINVALUE", Value(first.Values[0])),
                ("§FIRSTPATH", PeopleWords.FirstPersonPath), ("§FIRSTPERSON_TITLE", Str(PeopleWords.FirstPersonInvalidTitle)), ("§FIRSTPERSON_TYPE", ProblemTypes.FirstPersonInvalid),
                ("§TAKEN_TITLE", Str(PeopleWords.FirstPersonTakenTitle)), ("§TAKEN_TYPE", ProblemTypes.FirstPersonTaken), ("§TAKEN_DETAIL", Str(PeopleWords.FirstPersonTakenDetail))));
        }
        if (members)
        {
            string text = plan.NameColumn is null ? "login" : "f\"{name} ({login})\" if name else login";
            string nameSelect = plan.NameColumn is null ? "" : ", people.c[PERSON_NAME]";
            string nameRead = plan.NameColumn is null ? "None" : "row[3]";
            string nameReadCandidate = plan.NameColumn is null ? "None" : "row[2]";
            string membersText = Members + (membership!.Role is null ? "" : RoleRoute) + RemoveRoute;
            Add(Fill(membersText, ("§NAMESELECT", nameSelect), ("§NAMEREAD2", nameReadCandidate), ("§NAMEREAD", nameRead), ("§TEXT", text), ("§ROLESELECT", membership.Role is null ? "null()" : "members.c[MEMBER_ROLE]"),
                ("§ROLEKEY", membership.Role is null ? "" : "\"role\": row[2],"), ("§ROLEVALUE", membership.Role is null ? "" : ", MEMBER_ROLE: body.role or None"),
                ("§ROLETYPE", membership.Role is null ? "" : "\n    role: str | None = None"), ("§EXISTS_TYPE", ProblemTypes.MemberExists), ("§SELF_TYPE", ProblemTypes.MemberSelf),
                ("§ADMINS_TITLE", Str(PeopleWords.MembersAdminsOnlyTitle)), ("§ADMINS_DETAIL", Str(PeopleWords.MembersAdminsOnlyDetail)), ("§EXISTS_TITLE", Str(PeopleWords.MemberExistsTitle)),
                ("§EXISTS_DETAIL", Str(PeopleWords.MemberExistsDetail)), ("§SELF_TITLE", Str(PeopleWords.MemberSelfTitle)), ("§SELF_DETAIL", Str(PeopleWords.MemberSelfDetail)),
                ("§MEMBERSPATH", PeopleWords.MembersPath), ("§CANDIDATESPATH", PeopleWords.CandidatesPath)));
        }
        return o.ToString();
    }

    private static string Value(FirstPersonValue v) => v.Kind switch
    {
        FirstPersonKind.Name => v.Length is { } n ? $"name[:{n}]" : "name",
        FirstPersonKind.Hash => "hash_password(password)",
        FirstPersonKind.True => "True",
        FirstPersonKind.False => "False",
        FirstPersonKind.AdminRole => "ADMIN_ROLE",
        FirstPersonKind.Zero => "0",
        _ => "datetime.now()"
    };

    private static string Fill(string text, params (string Token, string Value)[] values)
    {
        foreach (var (token, value) in values.OrderByDescending(v => v.Token.Length))
            text = text.Replace(token, value);
        return text;
    }

    private const string Head = """"
"""The people routes: the first person of an empty user table (no sign-in), a person's new password and, with tenants, the members of the caller's tenant.
The same routes, answers and sentences as the ASP.NET API. Generated by PY_UserDirectory."""
from datetime import datetime

from fastapi import APIRouter, Depends, Response
from pydantic import BaseModel, ConfigDict, Field
from sqlalchemy import column, func, null, select, table
from sqlalchemy.orm import Session

from app.auth import ADMIN_ROLE, CurrentUser, hash_password, require_user§ROLEIMPORT
from app.db import get_session
from app.support import ApiError
from app.users import MINIMUM_PASSWORD_LENGTH, set_password
§TENANTIMPORT
public_router = APIRouter()
router = APIRouter()
"""";

    private const string Models = """"


class FirstPersonBody(BaseModel):
    model_config = ConfigDict(populate_by_name=True)

    user_name: str | None = Field(None, alias="userName")
    password: str | None = None


class PasswordBody(BaseModel):
    model_config = ConfigDict(populate_by_name=True)

    user_name: str | None = Field(None, alias="userName")
    password: str | None = None
"""";

    private const string Routes = """"


@public_router.get("§FIRSTPATH")
def first_person_state(session: Session = Depends(get_session)):
    """Whether the user table is empty, so the sign-in offers to create the first administrator."""
    return {"open": no_people(session)}


def no_people(session: Session) -> bool:
    return session.scalar(select(func.count()).select_from(people)) == 0


@router.put("§PASSWORDPATH", status_code=204)
def change_password(body: PasswordBody, session: Session = Depends(get_session), user: CurrentUser = Depends(require_user)):
    """A new password: an administrator for anyone, anyone else only for their own name."""
    name = (body.user_name or "").strip()
    if not name or len(body.password or "") < MINIMUM_PASSWORD_LENGTH:
        raise ApiError(400, §PASSWORD_TITLE, problem_type="§PASSWORD_TYPE")
    if user.name.lower() != name.lower() and not user.is_admin:
        raise ApiError(403, §NOTYOURS_TITLE, problem_type="§NOTYOURS_TYPE", detail=§NOTYOURS_DETAIL)
    if not set_password(session, name, body.password):
        raise ApiError(404, "Not found.")
    return Response(status_code=204)
"""";

    private const string FirstText = """"


@public_router.post("§FIRSTPATH", status_code=204)
def create_first_person(body: FirstPersonBody, session: Session = Depends(get_session)):
    """Adds the first person, the administrator, while the user table is empty; whoever reaches an empty system first claims it."""
    name = (body.user_name or "").strip()
    password = body.password or ""
    if not name or len(password) < MINIMUM_PASSWORD_LENGTH:
        raise ApiError(400, §FIRSTPERSON_TITLE, problem_type="§FIRSTPERSON_TYPE")
    if not no_people(session):
        raise ApiError(409, §TAKEN_TITLE, problem_type="§TAKEN_TYPE", detail=§TAKEN_DETAIL)
    session.execute(people.insert().values({§VALUES}))
    person_id = session.scalar(select(people.c[PERSON_KEY]).where(people.c[PERSON_LOGIN] == §LOGINVALUE))
§FIRSTMEMBER    session.commit()
    return Response(status_code=204)
"""";

    private const string FirstMember = """"
    first = session.scalar(select(tenants.c[TENANT_KEY]).where(§ACTIVE§DELETEDtenants.c[TENANT_KEY].is_not(None)).order_by(tenants.c[TENANT_KEY]).limit(1))
    if first is not None:
        session.execute(members.insert().values({MEMBER_USER: person_id, MEMBER_TENANT: first§ROLEVALUE}))
"""";

    private const string Members = """"


def require_tenant_admin(user: CurrentUser) -> int:
    """The tenant an administrator manages: the one in their token."""
    if not user.is_admin:
        raise ApiError(403, §ADMINS_TITLE, problem_type=PROBLEM_ROLE_NOT_ALLOWED, detail=§ADMINS_DETAIL)
    if user.tenant_id is None:
        raise no_tenant()
    return user.tenant_id


def person_text(login, name) -> str:
    login = login or ""
    return §TEXT


@router.get("§MEMBERSPATH")
def list_members(session: Session = Depends(get_session), user: CurrentUser = Depends(require_user)):
    tenant = require_tenant_admin(user)
    statement = (
        select(people.c[PERSON_KEY], people.c[PERSON_LOGIN], §ROLESELECT§NAMESELECT)
        .select_from(members.join(people, members.c[MEMBER_USER] == people.c[PERSON_KEY]))
        .where(members.c[MEMBER_TENANT] == tenant)
    )
    rows = session.execute(statement).all()
    answer = [{"personId": int(row[0]), "login": row[1] or "", "person": person_text(row[1], §NAMEREAD), §ROLEKEY} for row in rows]
    return sorted(answer, key=lambda m: m["person"].lower())


@router.get("§CANDIDATESPATH")
def list_candidates(session: Session = Depends(get_session), user: CurrentUser = Depends(require_user)):
    """The people who are not yet in the caller's tenant."""
    tenant = require_tenant_admin(user)
    inside = select(members.c[MEMBER_USER]).where(members.c[MEMBER_TENANT] == tenant)
    rows = session.execute(select(people.c[PERSON_KEY], people.c[PERSON_LOGIN]§NAMESELECT).where(people.c[PERSON_KEY].not_in(inside))).all()
    answer = [{"id": int(row[0]), "name": person_text(row[1], §NAMEREAD2)} for row in rows]
    return sorted(answer, key=lambda c: c["name"].lower())


class MemberBody(BaseModel):
    model_config = ConfigDict(populate_by_name=True)

    person_id: int | None = Field(None, alias="personId")§ROLETYPE


def in_tenant(session: Session, person_id: int, tenant: int) -> bool:
    return session.scalar(select(members.c[MEMBER_USER]).where(members.c[MEMBER_USER] == person_id, members.c[MEMBER_TENANT] == tenant)) is not None


@router.post("§MEMBERSPATH", status_code=204)
def add_member(body: MemberBody, session: Session = Depends(get_session), user: CurrentUser = Depends(require_user)):
    tenant = require_tenant_admin(user)
    if body.person_id is None or session.scalar(select(people.c[PERSON_KEY]).where(people.c[PERSON_KEY] == body.person_id)) is None:
        raise ApiError(404, "Not found.")
    if in_tenant(session, body.person_id, tenant):
        raise ApiError(409, §EXISTS_TITLE, problem_type="§EXISTS_TYPE", detail=§EXISTS_DETAIL)
    session.execute(members.insert().values({MEMBER_USER: body.person_id, MEMBER_TENANT: tenant§ROLEVALUE}))
    session.commit()
    return Response(status_code=204)
"""";

    private const string RoleRoute = """"


@router.put("§MEMBERSPATH/{person_id}", status_code=204)
def change_member_role(person_id: int, body: MemberBody, session: Session = Depends(get_session), user: CurrentUser = Depends(require_user)):
    tenant = require_tenant_admin(user)
    result = session.execute(members.update().where(members.c[MEMBER_USER] == person_id, members.c[MEMBER_TENANT] == tenant).values({MEMBER_ROLE: body.role or None}))
    session.commit()
    if result.rowcount == 0:
        raise ApiError(404, "Not found.")
    return Response(status_code=204)
"""";

    private const string RemoveRoute = """"


@router.delete("§MEMBERSPATH/{person_id}", status_code=204)
def remove_member(person_id: int, session: Session = Depends(get_session), user: CurrentUser = Depends(require_user)):
    tenant = require_tenant_admin(user)
    login = session.scalar(select(people.c[PERSON_LOGIN]).where(people.c[PERSON_KEY] == person_id))
    if login is not None and login.lower() == user.name.lower():
        raise ApiError(403, §SELF_TITLE, problem_type="§SELF_TYPE", detail=§SELF_DETAIL)
    result = session.execute(members.delete().where(members.c[MEMBER_USER] == person_id, members.c[MEMBER_TENANT] == tenant))
    session.commit()
    if result.rowcount == 0:
        raise ApiError(404, "Not found.")
    return Response(status_code=204)
"""";
}
