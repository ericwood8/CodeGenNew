namespace CodeGenNew.Core;

/// <summary> <c>tests/test_queue.py</c> of the Python API (PY_TestQueue), beside the <c>conftest.py</c> the approval tests share: three pending rows (<see cref="QueueTestSeed"/>) are added to the scratch database before
/// each test and removed after it, and the cases play the routes: the claim takes the oldest rows in turn, a row is completed once, a failed row goes back to pending until the retries are used, Retry, a row that is not
/// there, and a stale claim. The words are <see cref="QueueWords"/>, as in the other stacks. </summary>
public static class PythonQueueTests
{
    /// <summary> Whether the tests are written: <c>PythonTests</c>, a queue the plan accepts and a schema the seed can be made from, over a database the shared conftest can open (not SQL Server). </summary>
    public static bool Applies(DatabaseModel database, ProjectSettings project) =>
        project.PythonTests && database.Dialect != SqlDialect.SqlServer && Seeds(database, project).Count > 0;

    public static IReadOnlyList<QueueTestSeed> Seeds(DatabaseModel database, ProjectSettings project) =>
        database.Tables.Select(t => QueuePlan.For(t, project)).OfType<QueueTable>().OrderBy(q => q.Table.TableName, StringComparer.OrdinalIgnoreCase)
            .Select(q => QueueTestSeed.For(q, database, project, out _)).OfType<QueueTestSeed>().ToList();

    public static string Conftest(ProjectSettings project, SqlDialect dialect) => PythonApprovalTests.Conftest(project, dialect);

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var seed = Seeds(database, project)[0];
        var q = seed.Queue;
        var main = PythonTable.Of(q.Table, project);
        string Json(ColumnModel c) => JsonNames.Camel(c.Name);
        string List(IReadOnlyList<string> statements) => string.Join(",\n", statements.Select(s => "    " + PythonNames.Str(s)));
        string by = q.ClaimedBy is null ? "" : $"    assert first.json()[\"{Json(q.ClaimedBy)}\"] == \"w1\" and second.json()[\"{Json(q.ClaimedBy)}\"] == \"w2\"\n";
        string error = q.LastError is null ? "" : $"        assert failed.json()[\"{Json(q.LastError)}\"] == \"boom\"\n";
        string auth = project.ApiAuth ? "from app.auth import CurrentUser, require_user\n" : "";
        string signIn = project.ApiAuth ? "    app.dependency_overrides[require_user] = lambda: CurrentUser(name=\"zz_worker\", role=None, owner_id=None)\n" : "";
        string signOut = project.ApiAuth ? "    app.dependency_overrides.pop(require_user, None)\n" : "";
        string text = Text;
        foreach (var (token, value) in new (string, string)[]
        {
            ("§AUTHIMPORT\n", auth), ("§SIGNIN", signIn), ("§SIGNOUT", signOut), ("§BYASSERT", by), ("§ERRORASSERT", error),
            ("§SETUP", List(seed.Setup)), ("§CLEANUP", List(seed.Cleanup)), ("§STALESQL", PythonNames.Str(seed.StaleSql)), ("§ROUTE", PythonNames.Str(main.Route)),
            ("§K1", seed.Keys[0].ToString()), ("§K2", seed.Keys[1].ToString()), ("§K3", seed.Keys[2].ToString()), ("§MAX", project.QueueMaxRetries.ToString()),
            ("§ID", Json(q.Key)), ("§STATUS", Json(q.Status)), ("§RETRY", Json(q.Retry)), ("§PROCESSED", Json(q.Processed)), ("§CLAIMEDON", Json(q.Claimed)),
            ("§T_STATE", QueueWords.WrongStateType), ("§W_NOTCLAIMED", PythonNames.Str(QueueWords.NotClaimed)), ("§W_NOTFAILED", PythonNames.Str(QueueWords.NotFailed)),
            ("§PENDING", QueueStatus.Pending), ("§CLAIMED", QueueStatus.Claimed), ("§DONE", QueueStatus.Done), ("§FAILED", QueueStatus.Failed),
            ("§CONFLICT", HttpStatus.Conflict.ToString()), ("§NOTFOUND", HttpStatus.NotFound.ToString())
        }.OrderByDescending(v => v.Item1.Length))
            text = text.Replace(token, value);
        return text + "\n";
    }

    private const string Text = """"
"""The work queue through the routes: the claim, complete, fail and retry, a row that is not there and a stale claim. Written by PY_TestQueue; regenerated with the tables."""
import pytest
from fastapi.testclient import TestClient

§AUTHIMPORT
from conftest import run

SETUP = [
§SETUP,
]
CLEANUP = [
§CLEANUP,
]
STALE = §STALESQL
ROUTE = §ROUTE
K1, K2, K3 = §K1, §K2, §K3
MAX_RETRIES = §MAX
NOT_CLAIMED = §W_NOTCLAIMED
NOT_FAILED = §W_NOTFAILED


@pytest.fixture
def api(application):
    """A client over a scratch database that holds three pending rows of its own, the oldest of the table, removed after the test."""
    app, engine = application
    run(engine, CLEANUP)
    run(engine, SETUP)
§SIGNIN    client = TestClient(app)
    yield client, engine
§SIGNOUT    run(engine, CLEANUP)


def test_a_claim_takes_the_oldest_pending_rows_in_turn(api):
    client, _ = api
    first = client.post(ROUTE + "/claim?worker=w1")
    second = client.post(ROUTE + "/claim?worker=w2")
    assert first.status_code == 200 and first.json()["§ID"] == K1 and first.json()["§STATUS"] == "§CLAIMED"
    assert first.json()["§CLAIMEDON"] is not None
    assert second.status_code == 200 and second.json()["§ID"] == K2
§BYASSERT    stats = client.get(ROUTE + "/stats").json()
    assert stats["claimed"] >= 2 and stats["pending"] >= 1 and stats["oldestPendingId"] == K3


def test_a_row_is_completed_once(api):
    client, _ = api
    client.post(ROUTE + "/claim")
    done = client.post(f"{ROUTE}/{K1}/complete")
    assert done.status_code == 200 and done.json()["§STATUS"] == "§DONE" and done.json()["§PROCESSED"] is not None
    again = client.post(f"{ROUTE}/{K1}/complete")
    assert again.status_code == §CONFLICT
    assert again.json()["type"].endswith("§T_STATE") and again.json()["detail"] == NOT_CLAIMED
    assert client.post(f"{ROUTE}/{K2}/complete").status_code == §CONFLICT, "a row that nobody claimed cannot be completed"


def test_a_failed_row_goes_back_to_pending_until_the_retries_are_used_and_retry_puts_it_back(api):
    client, _ = api
    for attempt in range(1, MAX_RETRIES + 1):
        claimed = client.post(ROUTE + "/claim?worker=w1")
        assert claimed.json()["§ID"] == K1
        failed = client.post(f"{ROUTE}/{K1}/fail", json={"error": "boom"})
        assert failed.status_code == 200
        assert failed.json()["§RETRY"] == attempt
        assert failed.json()["§STATUS"] == ("§FAILED" if attempt == MAX_RETRIES else "§PENDING")
§ERRORASSERT    assert failed.json()["§PROCESSED"] is not None and failed.json()["§CLAIMEDON"] is None
    assert any(row["§ID"] == K1 for row in client.get(ROUTE + "/failed").json())

    retried = client.post(f"{ROUTE}/{K1}/retry")
    assert retried.status_code == 200 and retried.json()["§STATUS"] == "§PENDING" and retried.json()["§RETRY"] == 0
    again = client.post(f"{ROUTE}/{K1}/retry")
    assert again.status_code == §CONFLICT and again.json()["detail"] == NOT_FAILED
    assert client.post(f"{ROUTE}/{K1}/fail").status_code == §CONFLICT, "a row that is not claimed cannot be failed"


@pytest.mark.parametrize("action", ["complete", "fail", "retry", "renew"])
def test_a_row_that_is_not_there_is_not_found(api, action):
    client, _ = api
    assert client.post(f"{ROUTE}/{K3 + 500000}/{action}").status_code == §NOTFOUND


def test_a_renewed_claim_is_not_released_and_only_a_claimed_row_can_be_renewed(api):
    client, engine = api
    assert client.post(ROUTE + "/claim").json()["§ID"] == K1
    run(engine, [STALE])
    renewed = client.post(f"{ROUTE}/{K1}/renew")
    assert renewed.status_code == 200 and renewed.json()["§STATUS"] == "§CLAIMED"
    assert client.post(ROUTE + "/claim").json()["§ID"] == K2, "the renewed claim stays with its worker"
    assert client.post(f"{ROUTE}/{K1}/complete").status_code == 200
    late = client.post(f"{ROUTE}/{K1}/renew")
    assert late.status_code == §CONFLICT and late.json()["detail"] == NOT_CLAIMED, "a finished row has no claim to renew"
    assert client.post(f"{ROUTE}/{K3}/renew").status_code == §CONFLICT, "a pending row has no claim to renew"


def test_a_claim_nobody_completed_goes_back_to_pending(api):
    client, engine = api
    assert client.post(ROUTE + "/claim").json()["§ID"] == K1
    run(engine, [STALE])
    client.get(ROUTE + "/stats")
    assert client.post(ROUTE + "/claim").json()["§ID"] == K1
"""";
}
