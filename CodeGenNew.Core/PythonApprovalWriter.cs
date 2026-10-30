using System.Text;

namespace CodeGenNew.Core;

/// <summary> The Python (FastAPI) counterpart of <see cref="ApprovalWriter"/>: <c>app/routes/approvals.py</c>, the request, approve, reject and withdraw routes, <c>GET toapprove</c> and <c>GET {id}/approvals</c> of every
/// table in <c>RowApprovals</c>, over the same side table, with the same sentences (<see cref="ApprovalWords"/>), problem types and status codes. The routes are written once for all tables; each table adds one
/// <c>ApprovalSpec</c> that names its model, key and side-table attributes. </summary>
public static class PythonApprovalWriter
{
    public static string File(ApprovalPlan plan, ProjectSettings project)
    {
        var user = plan.User!;
        var userTable = PythonTable.Of(user.Table, project);
        string Attr(TableModel table, ColumnModel column) => PythonTable.Of(table, project).FieldOf(column).Name;
        var imports = new SortedSet<string>(StringComparer.Ordinal) { $"from app.models.{userTable.Module} import {userTable.Class}" };
        var approvers = plan.Approvers;
        string mayDecide = MayDecideFlag;
        string sqlImport = "literal, select, update";
        if (approvers is not null)
        {
            var approverTable = PythonTable.Of(approvers.Table, project);
            imports.Add($"from app.models.{approverTable.Module} import {approverTable.Class}");
            string rows = $"{approverTable.Class}.{Attr(approvers.Table, approvers.Person)} == person.key";
            string forTable = approvers.ForTable is { } named ? $"\n    query = query.where(or_({approverTable.Class}.{Attr(approvers.Table, named)}.is_(None), {approverTable.Class}.{Attr(approvers.Table, named)} == \"\", {approverTable.Class}.{Attr(approvers.Table, named)} == table))" : "";
            mayDecide = MayDecideRows.Replace("§APPROVER", approverTable.Class).Replace("§APERSON", rows).Replace("§AFORTABLE", forTable);
            sqlImport = "literal, or_, select, update";
        }
        var specs = new StringBuilder();
        bool tenanted = false;
        foreach (var t in plan.Tables)
        {
            var main = PythonTable.Of(t.Table, project);
            var owned = TenantPlan.HasApiTenancy(project) ? TenantPlan.OwnedBy(t.Table, project) : null;
            tenanted |= owned is not null;
            var side = PythonTable.Of(t.Side, project);
            imports.Add($"from app.models.{main.Module} import {main.Class}");
            imports.Add($"from app.models.{side.Module} import {side.Class}");
            specs.Append($"    ApprovalSpec(\n        table=\"{t.Table.TableName}\", route=\"{main.Route}\", row={main.Class}, row_key={main.Class}.{main.Key.Name}, side={side.Class},\n" +
                $"        id=\"{Attr(t.Side, t.SideKey)}\", link=\"{Attr(t.Side, t.Link)}\", status=\"{Attr(t.Side, t.Status)}\", revision=\"{Attr(t.Side, t.Revision)}\", reason=\"{Attr(t.Side, t.Reason)}\",\n" +
                $"        requested_by=\"{Attr(t.Side, t.RequestedBy)}\", requested_on=\"{Attr(t.Side, t.RequestedOn)}\", decided_by=\"{Attr(t.Side, t.DecidedBy)}\", decided_on=\"{Attr(t.Side, t.DecidedOn)}\",\n" +
                $"        decision_note=\"{Attr(t.Side, t.DecisionNote)}\",\n" + (owned is null ? "" : $"        tenant={main.Class}.{main.FieldOf(owned.Column).Name},\n") + "    ),\n");
        }
        if (tenanted)
            imports.Add("from app.routes.tenancy import only_tenant");
        string adminSelect = user.Admin is { } adminColumn ? $"{userTable.Class}.{Attr(user.Table, adminColumn)}" : "literal(False)";
        string text = Template;
        foreach (var (token, value) in new (string, string)[]
        {
            ("§IMPORTS", string.Join("\n", imports)), ("§SPECS", specs.ToString().TrimEnd('\n')),
            ("§USER", userTable.Class), ("§UNAME", Attr(user.Table, user.UserName)), ("§MAYDECIDE", mayDecide), ("§SQLIMPORT", sqlImport), ("§UKEY", Attr(user.Table, user.Table.PrimaryKeyColumns[0])), ("§UFLAG", plan.Approvers is null ? $"{userTable.Class}.{Attr(user.Table, user.Role!)}" : "literal(False)"), ("§UADMIN", adminSelect),
            ("§T_UNKNOWN", ApprovalWriter.RequesterUnknownType), ("§T_NOTAPPROVER", ApprovalWriter.NotApproverType), ("§T_FOUREYES", ApprovalWriter.FourEyesType),
            ("§T_STATE", ApprovalWriter.WrongStateType), ("§T_REFUSED", ApprovalWriter.RefusedType),
            ("§W_UNKNOWN", PythonNames.Str(ApprovalWords.RequesterUnknown)), ("§W_NOTAPPROVER", PythonNames.Str(ApprovalWords.NotApprover)), ("§W_NOTREQUESTER", PythonNames.Str(ApprovalWords.NotRequester)),
            ("§W_FOUREYES", PythonNames.Str(ApprovalWords.FourEyes)), ("§W_ALREADY", PythonNames.Str(ApprovalWords.AlreadyWaiting)), ("§W_NOTHING", PythonNames.Str(ApprovalWords.NothingWaiting)),
            ("§W_MEANWHILE", PythonNames.Str(ApprovalWords.DecidedMeanwhile)), ("§W_JUSTNOW", PythonNames.Str(ApprovalWords.DecidedJustNow)),
            ("§PENDING", ApprovalStatus.Pending.ToString()), ("§APPROVED", ApprovalStatus.Approved.ToString()), ("§REJECTED", ApprovalStatus.Rejected.ToString()), ("§WITHDRAWN", ApprovalStatus.Withdrawn.ToString()),
            ("§FORBIDDEN", HttpStatus.Forbidden.ToString()), ("§CONFLICT", HttpStatus.Conflict.ToString()), ("§NOTFOUND", HttpStatus.NotFound.ToString())
        }.OrderByDescending(v => v.Item1.Length))
            text = text.Replace(token, value);
        return text;
    }

    private const string MayDecideFlag = """"
def may_decide(session: Session, person: Person, table: str) -> bool:
    return person.may_decide
"""";

    private const string MayDecideRows = """"
def may_decide(session: Session, person: Person, table: str) -> bool:
    """An administrator, or a person with a row in the approvers table for this table."""
    if person.may_decide:
        return True
    query = select(literal(1)).select_from(§APPROVER).where(§APERSON)§AFORTABLE
    return session.execute(query.limit(1)).first() is not None
"""";

    private const string Template = """"
"""The row approvals (RowApprovals): for each table a request, an approval or rejection by someone else, a withdrawal, the requests waiting for the signed-in approver and the history of a row, over the
<Table>Approval side table, one row per request cycle. The same routes, sentences and problem types as the ASP.NET API (API_Approvals). Generated by PY_Approvals."""
from collections.abc import Callable
from dataclasses import dataclass
from datetime import datetime
from typing import Any

from fastapi import APIRouter, Depends
from pydantic import BaseModel
from sqlalchemy import §SQLIMPORT
from sqlalchemy.orm import Session

from app.auth import CurrentUser, require_user
from app.db import get_session
§IMPORTS
from app.support import ApiError

PROBLEM_UNKNOWN = "§T_UNKNOWN"
PROBLEM_NOT_APPROVER = "§T_NOTAPPROVER"
PROBLEM_FOUR_EYES = "§T_FOUREYES"
PROBLEM_WRONG_STATE = "§T_STATE"
PROBLEM_REFUSED = "§T_REFUSED"

PENDING, APPROVED, REJECTED, WITHDRAWN = §PENDING, §APPROVED, §REJECTED, §WITHDRAWN
NAMES = {PENDING: "Pending", APPROVED: "Approved", REJECTED: "Rejected", WITHDRAWN: "Withdrawn"}


class ApprovalNote(BaseModel):
    """What a caller sends with a request (reason) or a decision (note)."""

    reason: str | None = None
    note: str | None = None


class ApprovalView(BaseModel):
    """One request cycle of a row, as the routes answer it."""

    id: int
    rowId: int
    revision: int
    status: str
    reason: str | None = None
    requestedBy: str
    requestedOn: datetime
    decidedBy: str | None = None
    decidedOn: datetime | None = None
    decisionNote: str | None = None


@dataclass
class ApprovalEvent:
    """What happened, handed to the project's hooks. session is the one the route saves with, so a change made in on_decided is saved in the decision's transaction."""

    session: Session
    table: str
    row_id: int
    action: str
    person: str
    revision: int


class ApprovalHooks:
    """The two places a project adds its own rules. validate runs before a request, an approval, a rejection or a withdrawal and must have no side effects: a sentence refuses it (409).
    on_decided runs after an approval or rejection is written and before it is committed. Set them at start-up (from app.routes.approvals import hooks)."""

    validate: Callable[[ApprovalEvent], str | None] | None = None
    on_decided: Callable[[ApprovalEvent], None] | None = None


hooks = ApprovalHooks()


@dataclass(frozen=True)
class ApprovalSpec:
    """One table of RowApprovals: its model and key, its side model and the attribute names of the side table's columns."""

    table: str
    route: str
    row: Any
    row_key: Any
    side: Any
    id: str
    link: str
    status: str
    revision: str
    reason: str
    requested_by: str
    requested_on: str
    decided_by: str
    decided_on: str
    decision_note: str
    tenant: Any = None

    def col(self, name: str) -> Any:
        return getattr(self.side, getattr(self, name))

    def get(self, cycle: Any, name: str) -> Any:
        return getattr(cycle, getattr(self, name))


SPECS = [
§SPECS
]


@dataclass(frozen=True)
class Person:
    """The signed-in person as the user table knows them: may_decide is an administrator or a person whose role flag is set; key is their key in the user table."""

    name: str
    may_decide: bool
    key: int


def find_person(session: Session, user: CurrentUser) -> Person | None:
    if not user.name:
        return None
    row = session.execute(select(§USER.§UNAME.label("name"), §USER.§UKEY.label("key"), §UFLAG.label("flag"), §UADMIN.label("admin")).where(§USER.§UNAME == user.name)).first()
    if row is None:
        return None
    return Person(row.name, bool(row.flag) or bool(row.admin) or user.is_admin, row.key)


def require_person(session: Session, user: CurrentUser) -> Person:
    person = find_person(session, user)
    if person is None:
        raise ApiError(§FORBIDDEN, "Not allowed", problem_type=PROBLEM_UNKNOWN, detail=§W_UNKNOWN)
    return person


§MAYDECIDE


def require_approver(session: Session, user: CurrentUser, spec: ApprovalSpec) -> Person:
    person = require_person(session, user)
    if not may_decide(session, person, spec.table):
        raise ApiError(§FORBIDDEN, "Not allowed", problem_type=PROBLEM_NOT_APPROVER, detail=§W_NOTAPPROVER)
    return person


def wrong_state(detail: str) -> ApiError:
    return ApiError(§CONFLICT, "Wrong state", problem_type=PROBLEM_WRONG_STATE, detail=detail)


def view(spec: ApprovalSpec, cycle: Any) -> ApprovalView:
    return ApprovalView(
        id=spec.get(cycle, "id"), rowId=spec.get(cycle, "link"), revision=spec.get(cycle, "revision"), status=NAMES.get(spec.get(cycle, "status"), "Unknown"),
        reason=spec.get(cycle, "reason"), requestedBy=spec.get(cycle, "requested_by"), requestedOn=spec.get(cycle, "requested_on"),
        decidedBy=spec.get(cycle, "decided_by"), decidedOn=spec.get(cycle, "decided_on"), decisionNote=spec.get(cycle, "decision_note"))


def visible(spec: ApprovalSpec, user: CurrentUser) -> Any:
    """The keys of the rows the caller may reach: a tenant-owned table shows only the caller's tenant's rows (the side table has no tenant column of its own)."""
    keys = select(spec.row_key)
    return keys if spec.tenant is None else only_tenant(keys, spec.tenant, user)


def require_row(session: Session, user: CurrentUser, spec: ApprovalSpec, row_id: int) -> None:
    if spec.tenant is not None and session.scalar(visible(spec, user).where(spec.row_key == row_id)) is None:
        raise ApiError(§NOTFOUND, "Not found.")


def latest(session: Session, spec: ApprovalSpec, row_id: int) -> Any:
    return session.scalars(select(spec.side).where(spec.col("link") == row_id).order_by(spec.col("revision").desc())).first()


def validate(session: Session, spec: ApprovalSpec, row_id: int, action: str, person: str, revision: int) -> None:
    if hooks.validate is not None and (refusal := hooks.validate(ApprovalEvent(session, spec.table, row_id, action, person, revision))):
        raise ApiError(§CONFLICT, "Refused", problem_type=PROBLEM_REFUSED, detail=refusal)


def request_approval(session: Session, user: CurrentUser, spec: ApprovalSpec, row_id: int, body: ApprovalNote | None) -> ApprovalView:
    person = require_person(session, user)
    if session.scalar(visible(spec, user).where(spec.row_key == row_id)) is None:
        raise ApiError(§NOTFOUND, "Not found.")
    current = latest(session, spec, row_id)
    if current is not None and spec.get(current, "status") == PENDING:
        raise wrong_state(§W_ALREADY)
    revision = (spec.get(current, "revision") if current is not None else 0) + 1
    validate(session, spec, row_id, "request", person.name, revision)
    cycle = spec.side(**{
        spec.link: row_id, spec.status: PENDING, spec.reason: body.reason if body else None, spec.requested_by: person.name,
        spec.requested_on: datetime.now(), spec.revision: revision,
    })
    session.add(cycle)
    session.commit()
    session.refresh(cycle)
    return view(spec, cycle)


def decide(session: Session, user: CurrentUser, spec: ApprovalSpec, row_id: int, body: ApprovalNote | None, approve: bool) -> ApprovalView:
    person = require_approver(session, user, spec)
    require_row(session, user, spec, row_id)
    current = latest(session, spec, row_id)
    if current is None or spec.get(current, "status") != PENDING:
        raise wrong_state(§W_NOTHING)
    if spec.get(current, "requested_by").lower() == person.name.lower():
        raise ApiError(§FORBIDDEN, "Not allowed", problem_type=PROBLEM_FOUR_EYES, detail=§W_FOUREYES)
    action = "approve" if approve else "reject"
    revision = spec.get(current, "revision")
    validate(session, spec, row_id, action, person.name, revision)
    changed = session.execute(update(spec.side).where(spec.col("id") == spec.get(current, "id"), spec.col("status") == PENDING).values({
        spec.status: APPROVED if approve else REJECTED, spec.decided_by: person.name, spec.decided_on: datetime.now(), spec.decision_note: (body.note if body else None) or None,
    })).rowcount
    if changed == 0:
        session.rollback()
        raise wrong_state(§W_MEANWHILE)
    if hooks.on_decided is not None:
        hooks.on_decided(ApprovalEvent(session, spec.table, row_id, action, person.name, revision))
    session.commit()
    session.expire_all()
    return view(spec, latest(session, spec, row_id))


def withdraw(session: Session, user: CurrentUser, spec: ApprovalSpec, row_id: int) -> ApprovalView:
    person = require_person(session, user)
    require_row(session, user, spec, row_id)
    current = latest(session, spec, row_id)
    if current is None or spec.get(current, "status") != PENDING:
        raise wrong_state(§W_NOTHING)
    if spec.get(current, "requested_by").lower() != person.name.lower():
        raise ApiError(§FORBIDDEN, "Not allowed", problem_type=PROBLEM_NOT_APPROVER, detail=§W_NOTREQUESTER)
    validate(session, spec, row_id, "withdraw", person.name, spec.get(current, "revision"))
    changed = session.execute(update(spec.side).where(spec.col("id") == spec.get(current, "id"), spec.col("status") == PENDING).values({
        spec.status: WITHDRAWN, spec.decided_by: person.name, spec.decided_on: datetime.now(),
    })).rowcount
    if changed == 0:
        session.rollback()
        raise wrong_state(§W_JUSTNOW)
    session.commit()
    session.expire_all()
    return view(spec, latest(session, spec, row_id))


def to_approve(session: Session, user: CurrentUser, spec: ApprovalSpec) -> list[ApprovalView]:
    person = require_approver(session, user, spec)
    query = select(spec.side).where(spec.col("status") == PENDING, spec.col("requested_by") != person.name)
    if spec.tenant is not None:
        query = query.where(spec.col("link").in_(visible(spec, user)))
    waiting = session.scalars(query.order_by(spec.col("requested_on"))).all()
    return [view(spec, cycle) for cycle in waiting]


def history(session: Session, user: CurrentUser, spec: ApprovalSpec, row_id: int) -> list[ApprovalView]:
    require_row(session, user, spec, row_id)
    cycles = session.scalars(select(spec.side).where(spec.col("link") == row_id).order_by(spec.col("revision").desc())).all()
    return [view(spec, cycle) for cycle in cycles]


router = APIRouter()


def add_routes(spec: ApprovalSpec) -> None:
    """The routes of one table. The fixed path toapprove is registered before the paths with an id, and PY_Mod lists this router before the tables' routers (their /{id} would read toapprove as a key)."""

    @router.get(spec.route + "/toapprove", response_model=list[ApprovalView], name=spec.table + "ToApprove")
    def to_approve_route(session: Session = Depends(get_session), user: CurrentUser = Depends(require_user)):
        return to_approve(session, user, spec)

    @router.get(spec.route + "/{id}/approvals", response_model=list[ApprovalView], name=spec.table + "Approvals")
    def history_route(id: int, session: Session = Depends(get_session), user: CurrentUser = Depends(require_user)):
        return history(session, user, spec, id)

    @router.post(spec.route + "/{id}/request", response_model=ApprovalView, name=spec.table + "Request")
    def request_route(id: int, body: ApprovalNote | None = None, session: Session = Depends(get_session), user: CurrentUser = Depends(require_user)):
        return request_approval(session, user, spec, id, body)

    @router.post(spec.route + "/{id}/approve", response_model=ApprovalView, name=spec.table + "Approve")
    def approve_route(id: int, body: ApprovalNote | None = None, session: Session = Depends(get_session), user: CurrentUser = Depends(require_user)):
        return decide(session, user, spec, id, body, True)

    @router.post(spec.route + "/{id}/reject", response_model=ApprovalView, name=spec.table + "Reject")
    def reject_route(id: int, body: ApprovalNote | None = None, session: Session = Depends(get_session), user: CurrentUser = Depends(require_user)):
        return decide(session, user, spec, id, body, False)

    @router.post(spec.route + "/{id}/withdraw", response_model=ApprovalView, name=spec.table + "Withdraw")
    def withdraw_route(id: int, session: Session = Depends(get_session), user: CurrentUser = Depends(require_user)):
        return withdraw(session, user, spec, id)


for _spec in SPECS:
    add_routes(_spec)
"""";
}
