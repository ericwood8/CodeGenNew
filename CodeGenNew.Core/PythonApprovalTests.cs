using System.Text;

namespace CodeGenNew.Core;

/// <summary> The pytest files of the row approvals of the Python API (PY_TestApprovals): <c>tests/conftest.py</c>, which opens the application over a scratch database, and <c>tests/test_approvals.py</c>, which seeds the
/// people and a row (<see cref="ApprovalTestSeed"/>) and plays the cycle through the routes: the same cases as the ASP.NET and WinUI3 tests, in the same words (<see cref="ApprovalWords"/>). </summary>
public static class PythonApprovalTests
{
    /// <summary> The environment variable that names a scratch copy of a PostgreSQL or MySQL database for the tests. </summary>
    public static string ConnectionVariable(ProjectSettings project)
    {
        string name = project.ProjectName ?? "MyApp";
        return new string(name.ToUpperInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()) + "_TEST_CONNECTION";
    }

    public static string Conftest(ProjectSettings project, SqlDialect dialect)
    {
        string connection = ConnectionVariable(project);
        string open = dialect switch
        {
            SqlDialect.Sqlite => Fill(SqliteOpen, ("§FILE", PythonNames.Str(project.DatabaseName ?? "")), ("§PREFIX", new string((project.ProjectName ?? "app").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray()))),
            SqlDialect.SqlServer => "    pytest.skip(\"the approval tests run over SQLite, PostgreSQL or MySQL\")\n    folder = None",
            _ => Fill(UrlOpen, ("§VARIABLE", connection))
        };
        return Fill(ConftestText, ("§OPEN", open), ("§CLEAN", dialect == SqlDialect.Sqlite ? "    engine.dispose()\n    shutil.rmtree(folder, ignore_errors=True)" : "    engine.dispose()")) + "\n";
    }

    public static string Tests(ApprovalPlan plan, ApprovalTestSeed seed, ProjectSettings project)
    {
        var table = seed.Table;
        var main = PythonTable.Of(table.Table, project);
        string Setup() => string.Join(",\n", seed.Setup.Select(s => "    " + PythonNames.Str(s)));
        string Cleanup() => string.Join(",\n", seed.Cleanup.Select(s => "    " + PythonNames.Str(s)));
        var o = new StringBuilder();
        o.Append(Fill(Head,
            ("§TENANTELSE", seed.TenantSql is null ? "" : "ELSEWHERE = 2147483000\n"),
            ("§TENANTSQL", seed.TenantSql is null ? "" : $"TENANT_SQL = {PythonNames.Str(seed.TenantSql)}\n"),
            ("§TENANTLOOKUP", seed.TenantSql is null ? "" : "    with engine.connect() as connection:\n        tenant = connection.execute(text(TENANT_SQL)).scalar()\n    who[\"tenant\"] = tenant\n"),
            ("§TENANTARG", seed.TenantSql is null ? "" : ", tenant_id=who[\"tenant\"]"),
            ("§ELSEPARAM", seed.TenantSql is null ? "" : ", elsewhere=False"),
            ("§ELSEBODY", seed.TenantSql is null ? "" : "        who[\"tenant\"] = ELSEWHERE if elsewhere else tenant\n"),
            ("§TENANTIMPORT", seed.TenantSql is null ? "" : "from sqlalchemy import text\n"),
            ("§SETUP", Setup()), ("§CLEANUP", Cleanup()), ("§ROUTE", PythonNames.Str(main.Route)), ("§ROW", seed.RowKey.ToString()), ("§TABLE", PythonNames.Str(table.Table.TableName)),
            ("§ANN", ApprovalTestSeed.Requester), ("§BOB", ApprovalTestSeed.Approver), ("§CY", ApprovalTestSeed.AnyTableApprover)));
        o.Append(Fill(Cases,
            ("§ANN", ApprovalTestSeed.Requester), ("§BOB", ApprovalTestSeed.Approver), ("§CY", ApprovalTestSeed.AnyTableApprover),
            ("§T_UNKNOWN", ApprovalWriter.RequesterUnknownType), ("§T_NOTAPPROVER", ApprovalWriter.NotApproverType), ("§T_FOUREYES", ApprovalWriter.FourEyesType), ("§T_REFUSED", ApprovalWriter.RefusedType),
            ("§W_NOTAPPROVER", PythonNames.Str(ApprovalWords.NotApprover)), ("§W_NOTREQUESTER", PythonNames.Str(ApprovalWords.NotRequester)), ("§W_FOUREYES", PythonNames.Str(ApprovalWords.FourEyes)),
            ("§W_ALREADY", PythonNames.Str(ApprovalWords.AlreadyWaiting)), ("§FORBIDDEN", HttpStatus.Forbidden.ToString()), ("§CONFLICT", HttpStatus.Conflict.ToString()), ("§NOTFOUND", HttpStatus.NotFound.ToString())));
        if (seed.TenantSql is not null)
            o.Append(Fill(TenantCase, ("§ANN", ApprovalTestSeed.Requester), ("§BOB", ApprovalTestSeed.Approver)));
        if (ApprovalTestSeed.HasOtherTablePerson(plan))
            o.Append(Fill(OtherTableCase, ("§CY", ApprovalTestSeed.AnyTableApprover), ("§DEE", ApprovalTestSeed.OtherTableApprover), ("§W_NOTAPPROVER", PythonNames.Str(ApprovalWords.NotApprover)), ("§FORBIDDEN", HttpStatus.Forbidden.ToString())));
        return o.ToString() + "\n";
    }

    private static string Fill(string text, params (string Token, string Value)[] values)
    {
        foreach (var (token, value) in values.OrderByDescending(v => v.Token.Length))
            text = text.Replace(token, value);
        return text;
    }

    private const string ConftestText = """"
"""The application over a scratch database, for the tests of the routes. Written by PY_TestApprovals; yours to change."""
import os
import shutil
import tempfile
import uuid
from pathlib import Path

import pytest
from sqlalchemy import text


@pytest.fixture(scope="session")
def application():
    """The application and its engine, over a scratch copy of the database (the original is never written)."""
    os.environ.setdefault("Jwt__Key", uuid.uuid4().hex + uuid.uuid4().hex)
§OPEN
    from app.db import engine
    from app.main import app

    yield app, engine
§CLEAN


def run(engine, statements):
    """Runs statements one after the other in one transaction."""
    with engine.begin() as connection:
        for statement in statements:
            connection.execute(text(statement))
"""";

    private const string SqliteOpen = """"
    source = Path(os.environ.get("DATABASE_FILE", §FILE))
    if not source.is_file():
        pytest.skip(f"there is no database file {source}: set DATABASE_FILE to the SQLite file the tests may copy")
    folder = tempfile.mkdtemp(prefix="§PREFIX_test_")
    copy = Path(folder) / "scratch.db"
    shutil.copy(source, copy)
    os.environ["DATABASE_FILE"] = str(copy)
"""";

    private const string UrlOpen = """"
    url = os.environ.get("§VARIABLE")
    if not url:
        pytest.skip("set §VARIABLE to the URL of a scratch copy of the database (the tests add and remove rows)")
    os.environ["DATABASE_URL"] = url
"""";

    private const string Head = """"
"""The row approvals through the routes: who may ask and decide, the four eyes, withdrawal, the history and the hooks. Written by PY_TestApprovals; regenerated with the tables."""
import pytest
from fastapi.testclient import TestClient
§TENANTIMPORT
from app.auth import CurrentUser, require_user
from app.routes import approvals

from conftest import run

SETUP = [
§SETUP,
]
CLEANUP = [
§CLEANUP,
]
ROUTE = §ROUTE
ROW = §ROW
TABLE = §TABLE
§TENANTELSE§TENANTSQL

@pytest.fixture
def call(application):
    """call(person, method, path, **kw): a request made as that person, over rows seeded for the test and removed after it."""
    app, engine = application
    run(engine, CLEANUP)
    run(engine, SETUP)
    who = {"name": ""}
§TENANTLOOKUP    app.dependency_overrides[require_user] = lambda: CurrentUser(name=who["name"], role=None, owner_id=None§TENANTARG)
    client = TestClient(app)

    def make(person, method, path§ELSEPARAM, **kw):
        who["name"] = person
§ELSEBODY        return client.request(method, ROUTE + path, **kw)

    yield make
    app.dependency_overrides.pop(require_user, None)
    run(engine, CLEANUP)


def waiting_rows(reply):
    return [r["rowId"] for r in reply.json()]

"""";

    private const string Cases = """"

def test_a_person_who_is_not_in_the_user_table_cannot_ask(call):
    reply = call("zz_stranger", "POST", f"/{ROW}/request", json={})
    assert reply.status_code == §FORBIDDEN
    assert reply.json()["type"].endswith("§T_UNKNOWN")


def test_a_row_that_does_not_exist_is_not_found(call):
    assert call("§ANN", "POST", f"/{ROW + 500000}/request", json={}).status_code == §NOTFOUND


def test_a_request_waits_for_a_decision_and_a_second_one_is_refused(call):
    reply = call("§ANN", "POST", f"/{ROW}/request", json={"reason": "please"})
    assert reply.status_code == 200
    assert reply.json()["status"] == "Pending" and reply.json()["reason"] == "please" and reply.json()["revision"] == 1
    again = call("§ANN", "POST", f"/{ROW}/request", json={})
    assert again.status_code == §CONFLICT
    assert again.json()["detail"] == §W_ALREADY
    assert len(call("§ANN", "GET", f"/{ROW}/approvals").json()) == 1


def test_only_an_approver_decides_and_never_on_their_own_request(call):
    call("§ANN", "POST", f"/{ROW}/request", json={})
    refused = call("§ANN", "POST", f"/{ROW}/approve", json={})
    assert refused.status_code == §FORBIDDEN
    assert refused.json()["type"].endswith("§T_NOTAPPROVER") and refused.json()["detail"] == §W_NOTAPPROVER
    assert call("§ANN", "GET", "/toapprove").status_code == §FORBIDDEN

    waiting = call("§BOB", "GET", "/toapprove")
    assert waiting.status_code == 200 and ROW in waiting_rows(waiting)
    not_requester = call("§BOB", "POST", f"/{ROW}/withdraw")
    assert not_requester.status_code == §FORBIDDEN and not_requester.json()["detail"] == §W_NOTREQUESTER
    assert call("§ANN", "POST", f"/{ROW}/withdraw").json()["status"] == "Withdrawn"

    call("§BOB", "POST", f"/{ROW}/request", json={})
    four_eyes = call("§BOB", "POST", f"/{ROW}/approve", json={})
    assert four_eyes.status_code == §FORBIDDEN
    assert four_eyes.json()["type"].endswith("§T_FOUREYES") and four_eyes.json()["detail"] == §W_FOUREYES
    assert ROW not in waiting_rows(call("§BOB", "GET", "/toapprove"))


def test_a_decision_is_written_once_and_the_history_shows_every_cycle(call):
    call("§ANN", "POST", f"/{ROW}/request", json={})
    call("§ANN", "POST", f"/{ROW}/withdraw")
    call("§ANN", "POST", f"/{ROW}/request", json={})

    decided = call("§CY", "POST", f"/{ROW}/approve", json={"note": "fine"})
    assert decided.status_code == 200
    assert decided.json()["status"] == "Approved" and decided.json()["decidedBy"] == "§CY" and decided.json()["decisionNote"] == "fine"
    assert call("§CY", "POST", f"/{ROW}/reject", json={}).status_code == §CONFLICT

    history = call("§ANN", "GET", f"/{ROW}/approvals").json()
    assert [cycle["status"] for cycle in history] == ["Approved", "Withdrawn"]


def test_a_hook_can_refuse_a_request_and_sees_a_decision(call):
    approvals.hooks.validate = lambda event: "Not today." if event.action == "request" else None
    try:
        refused = call("§ANN", "POST", f"/{ROW}/request", json={})
    finally:
        approvals.hooks.validate = None
    assert refused.status_code == §CONFLICT
    assert refused.json()["type"].endswith("§T_REFUSED") and refused.json()["detail"] == "Not today."

    call("§ANN", "POST", f"/{ROW}/request", json={})
    seen = []
    approvals.hooks.on_decided = lambda event: seen.append((event.action, event.person))
    try:
        call("§BOB", "POST", f"/{ROW}/reject", json={})
    finally:
        approvals.hooks.on_decided = None
    assert seen == [("reject", "§BOB")]
"""";

    private const string TenantCase = """"


def test_another_tenant_cannot_ask_decide_withdraw_or_read_the_approvals_of_a_row(call):
    assert call("§ANN", "POST", f"/{ROW}/request", json={"reason": "please"}).status_code == 200
    assert call("§ANN", "POST", f"/{ROW}/request", json={}, elsewhere=True).status_code == 404, "another tenant's row cannot be asked for"
    assert call("§BOB", "POST", f"/{ROW}/approve", json={}, elsewhere=True).status_code != 200, "another tenant's cycle cannot be approved"
    assert call("§ANN", "POST", f"/{ROW}/withdraw", elsewhere=True).status_code != 200, "another tenant's cycle cannot be withdrawn"
    history = call("§ANN", "GET", f"/{ROW}/approvals", elsewhere=True)
    assert history.status_code == 404 or history.json() == [], "another tenant is shown no history"
    waiting = call("§BOB", "GET", "/toapprove", elsewhere=True)
    assert waiting.status_code != 200 or ROW not in waiting_rows(waiting), "another tenant's waiting request is not listed"
    assert ROW in waiting_rows(call("§BOB", "GET", "/toapprove")), "the tenant's own approver sees the request"
    assert call("§BOB", "POST", f"/{ROW}/approve", json={"note": "fine"}).status_code == 200
"""";

    private const string OtherTableCase = """"


def test_a_row_for_another_table_does_not_let_a_person_decide_this_one(call):
    call("§CY", "POST", f"/{ROW}/request", json={})
    refused = call("§DEE", "POST", f"/{ROW}/approve", json={})
    assert refused.status_code == §FORBIDDEN and refused.json()["detail"] == §W_NOTAPPROVER
    assert call("§DEE", "GET", "/toapprove").status_code == §FORBIDDEN
"""";
}
