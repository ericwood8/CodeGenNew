namespace CodeGenNew.Core;

/// <summary> <c>tests/test_inbound_&lt;table&gt;.py</c> of the Python API (PY_TestInbound), beside the <c>conftest.py</c> the approval tests share: the cases of the generated ASP.NET tests (<see cref="ApiInboundTests"/>) played through the routes
/// of <c>PY_Inbound</c> over the scratch database. The tests set the shared secret in the environment themselves and remove the events they wrote (their bodies hold a marker) before and after each test. </summary>
public static class PythonInboundTests
{
    public const string Marker = "zz-inbound-marker";

    /// <summary> Whether the tests are written: <c>PythonTests</c> and an inbound table the plan accepts, over a database the shared conftest can open (not SQL Server). </summary>
    public static bool Applies(DatabaseModel database, ProjectSettings project) =>
        project.PythonTests && DialectInfo.For(database.Dialect).RunsPythonTests && Tables(database, project).Count > 0;

    public static IReadOnlyList<InboundShape> Tables(DatabaseModel database, ProjectSettings project) =>
        database.Tables.Select(t => TableShapes.For(t, project).Inbound).OfType<InboundShape>().OrderBy(q => q.Table.TableName, StringComparer.OrdinalIgnoreCase).ToList();

    public static string Conftest(ProjectSettings project, SqlDialect dialect) => PythonApprovalTests.Conftest(project, dialect);

    public static string FileName(InboundShape t, ProjectSettings project) => $"tests/test_inbound_{PythonTable.Of(t.Table, project).Module}.py";

    public static string File(InboundShape t, DatabaseModel database, ProjectSettings project)
    {
        var main = PythonTable.Of(t.Table, project);
        var di = DialectInfo.For(database.Dialect);
        string body = JsonNames.Camel(t.Body.Name), processed = JsonNames.Camel(t.Processed.Name), key = JsonNames.Camel(t.Key.Name);
        int limit = ApiInboundTests.Limit(t, project);
        string cleanup = $"DELETE FROM {di.QuoteTable(t.Table.SchemaName, t.Table.DbTableName)} WHERE {di.Quote(t.Body.DbName)} LIKE '%{Marker}%'";
        string auth = project.ApiAuth ? "from app.auth import CurrentUser, require_user\n" : "";
        string signIn = project.ApiAuth ? "    app.dependency_overrides[require_user] = lambda: CurrentUser(name=\"zz_worker\", role=None, owner_id=None)\n" : "";
        string signOut = project.ApiAuth ? "    app.dependency_overrides.pop(require_user, None)\n" : "";
        string testSource = InboundPlan.TestSource(project);
        string source = t.Source is null ? "" : $"    assert row[\"{JsonNames.Camel(t.Source.Name)}\"] == \"{testSource}\"\n";
        string path = t.Source is null ? "/receive" : "/receive/" + testSource;
        var own = InboundPlan.SourceSecrets(project).FirstOrDefault();
        string ownConstants = own.Source is null ? "" : $"OWN_SOURCE = \"{own.Source}\"\nOWN_SECRET_NAME = \"{own.Secret}\"\nOWN_SECRET = \"test-own-secret-zz\"\n";
        string ownTests = own.Source is null ? "" : OwnTestsText.Replace("§OWNSOURCEASSERT", t.Source is null ? "" : $"    assert rows[0][\"{JsonNames.Camel(t.Source.Name)}\"] == OWN_SOURCE, \"the sender is kept as it is configured\"\n");
        string eventTest = t.EventId is null ? "" : """"


def test_an_event_id_is_kept_once(api):
    client, _ = api
    event = "zz-evt-" + MARKER
    first = post(client, signed("/receive", message(event)))
    second = post(client, signed("/receive", message(event)))
    header = post(client, signed("/receive", message(None), event=event))
    assert first.json()["duplicate"] is False
    assert second.json()["duplicate"] is True and second.json()["id"] == first.json()["id"]
    assert header.json()["duplicate"] is True, "the id in the header counts the same"
    assert len([r for r in unprocessed(client) if MARKER in r["§BODY"]]) == 1
"""";
        string signedTest = project.ApiAuth ? """"


def test_without_a_sign_in_the_worker_routes_answer_401(api):
    client, _ = api
    client.app.dependency_overrides.pop(require_user, None)
    assert client.get(ROUTE + "/unprocessed").status_code == 401
    assert client.post(ROUTE + "/1/processed").status_code == 401
"""" : "";
        int clientLimit = Math.Min(t.BodyLength > 0 ? t.BodyLength : int.MaxValue, project.InboundClientMaxBytes);
        string clientSource = t.Source is null ? "" : $"    assert rows[0][\"{JsonNames.Camel(t.Source.Name)}\"] == \"{InboundWords.ClientSource}\"\n";
        string clientTests = !t.Client ? "" : ClientTestsText.Replace("§CLIENTSOURCEASSERT", clientSource).Replace("§CLIENTLIMIT", (clientLimit + 1).ToString())
            .Replace("§CLIENTRATE", project.InboundClientPerMinute.ToString()).Replace("§T_RATE", InboundWords.RateLimitedType.Split('/').Last());
        return TemplateText.FillAll(Text, new (string, string)[]
        {
            ("§CLIENTIMPORT\n", t.Client ? "from app.routes.inbound import reset_client_limit\n" : ""), ("§CLIENTENABLED", t.Client ? "True" : "False"), ("§UNPROCLABEL", InboundScreens.UnprocessedLabel), ("§OWNCONSTANTS\n", ownConstants), ("§OWNTESTS", ownTests), ("§CLIENTTESTS", clientTests), ("§CLIENTRESET", t.Client ? "    reset_client_limit()\n" : ""),
            ("§AUTHIMPORT\n", auth), ("§SIGNIN", signIn), ("§SIGNOUT", signOut), ("§SOURCEASSERT", source), ("§PATH", path), ("§EVENTTEST", eventTest), ("§SIGNEDTEST", signedTest),
            ("§BODY", body), ("§PROCESSEDFIELD", processed), ("§KEYFIELD", key), ("§LIMIT", (limit + 1).ToString()), ("§ROUTE", PythonNames.Str(main.Route)), ("§CLEANUP", PythonNames.Str(cleanup)),
            ("§SECRETNAME", PythonNames.Str(project.InboundSecret)), ("§HEADER", PythonNames.Str(InboundWords.SignatureHeader)), ("§EVENTHEADER", PythonNames.Str(InboundWords.EventHeader)),
            ("§MARKER", PythonNames.Str(Marker)), ("§T_EXPIRED", InboundWords.ExpiredType.Split('/').Last()), ("§T_LARGE", InboundWords.TooLargeType.Split('/').Last())
        }) + "\n";
    }

    private const string Text = """"
"""The inbound routes of one table: a bad, missing or old signature is refused and stores nothing, a signed message is kept and listed as unprocessed, marking it processed takes it out of the list, a body over the limit
is refused. Written by PY_TestInbound; regenerated with the tables."""
import hashlib
import hmac
import json
import os
import time

import pytest
from fastapi.testclient import TestClient

§AUTHIMPORT
from conftest import run
§CLIENTIMPORT

ROUTE = §ROUTE
CLEANUP = [§CLEANUP]
SECRET = "test-secret-zz"
SECRET_NAME = §SECRETNAME
SIGNATURE_HEADER = §HEADER
EVENT_HEADER = §EVENTHEADER
MARKER = §MARKER
§OWNCONSTANTS


@pytest.fixture
def api(application, monkeypatch):
    """A client over a scratch database, the shared secret set in the environment, and the events these tests write removed before and after."""
    app, engine = application
    monkeypatch.setenv(SECRET_NAME, SECRET)
    run(engine, CLEANUP)
§CLIENTRESET§SIGNIN    client = TestClient(app)
    yield client, engine
§SIGNOUT§CLIENTRESET    run(engine, CLEANUP)


def message(event):
    return json.dumps({"id": event, "marker": MARKER})


def signed(path, text, key=SECRET, at=None, event=None):
    stamp = int(time.time()) if at is None else at
    digest = hmac.new(key.encode(), f"{stamp}.".encode() + text.encode(), hashlib.sha256).hexdigest()
    headers = {SIGNATURE_HEADER: f"t={stamp},v1={digest}", "Content-Type": "application/json"}
    if event:
        headers[EVENT_HEADER] = event
    return ROUTE + path, text, headers


def post(client, built):
    path, text, headers = built
    return client.post(path, content=text.encode(), headers=headers)


def unprocessed(client):
    response = client.get(ROUTE + "/unprocessed")
    assert response.status_code == 200, response.text
    return response.json()


def test_a_message_with_no_signature_a_wrong_one_or_an_old_one_is_refused_and_stores_nothing(api):
    client, _ = api
    unsigned = client.post(ROUTE + "/receive", content=message(None).encode(), headers={"Content-Type": "application/json"})
    assert unsigned.status_code == 401
    assert post(client, signed("/receive", message(None), key="not-the-secret")).status_code == 401
    old = post(client, signed("/receive", message(None), at=int(time.time()) - 3600))
    assert old.status_code == 400 and old.json()["type"].endswith("§T_EXPIRED")
    assert not [r for r in unprocessed(client) if MARKER in r["§BODY"]]


def test_a_signed_message_is_kept_listed_as_unprocessed_and_marked_processed(api):
    client, _ = api
    text = message(None)
    received = post(client, signed("§PATH", text))
    assert received.status_code == 200 and received.json()["received"] is True, received.text
    rows = [r for r in unprocessed(client) if MARKER in r["§BODY"]]
    assert len(rows) == 1, "the event is listed as unprocessed"
    row = rows[0]
    assert row["§BODY"] == text, "the raw body is kept as it came"
§SOURCEASSERT    identity = row["§KEYFIELD"]
    assert identity == received.json()["id"]
    done = client.post(f"{ROUTE}/{identity}/processed")
    assert done.status_code == 200 and done.json()["processed"] is True
    assert not [r for r in unprocessed(client) if MARKER in r["§BODY"]], "a processed event is no longer listed"
    again = client.post(f"{ROUTE}/{identity}/processed")
    assert again.status_code == 200 and again.json()["alreadyProcessed"] is True, "marking it again changes nothing"
    assert client.post(f"{ROUTE}/2147483647/processed").status_code == 404
§EVENTTEST

def test_a_body_over_the_limit_is_refused(api):
    client, _ = api
    response = post(client, signed("/receive", "x" * §LIMIT))
    assert response.status_code == 413 and response.json()["type"].endswith("§T_LARGE")


def test_the_status_counts_the_events_that_wait(api):
    client, _ = api
    before = client.get(ROUTE + "/status").json()
    received = post(client, signed("/receive", message(None)))
    after = client.get(ROUTE + "/status").json()
    assert after["unprocessed"] == before["unprocessed"] + 1, "the new event waits"
    assert after["total"] == before["total"] + 1
    assert after["oldestUnprocessedId"] is not None
    assert after["summary"].startswith("§UNPROCLABEL")
    assert after["collect"]["enabled"] is §CLIENTENABLED
    assert client.post(f"{ROUTE}/{received.json()['id']}/processed").status_code == 200
    assert client.get(ROUTE + "/status").json()["unprocessed"] == before["unprocessed"], "a processed event no longer waits"
§CLIENTTESTS§OWNTESTS§SIGNEDTEST
"""";

    private const string OwnTestsText = """"


def test_a_sender_with_its_own_secret_is_checked_against_that_secret_only(api, monkeypatch):
    client, _ = api
    monkeypatch.setenv(OWN_SECRET_NAME, OWN_SECRET)
    path = "/receive/" + OWN_SOURCE.upper()
    assert post(client, signed(path, message(None))).status_code == 401, "the shared secret does not work for this sender"
    sent = post(client, signed(path, message(None), key=OWN_SECRET))
    assert sent.status_code == 200 and sent.json()["received"] is True, sent.text
    rows = [r for r in unprocessed(client) if MARKER in r["§BODY"]]
    assert len(rows) == 1
§OWNSOURCEASSERT    monkeypatch.delenv(OWN_SECRET_NAME)
    assert post(client, signed(path, message(None), key=OWN_SECRET)).status_code == 503, "a sender whose secret is not set is not served"
"""";

    private const string ClientTestsText = """"


def test_a_client_message_is_kept_with_no_signature_and_the_answer_holds_nothing(api):
    client, _ = api
    sent = client.post(ROUTE + "/collect", content=message(None).encode(), headers={"Content-Type": "application/json"})
    assert sent.status_code == 202 and sent.content == b"", sent.text
    rows = [r for r in unprocessed(client) if MARKER in r["§BODY"]]
    assert len(rows) == 1, "the message is listed as unprocessed"
§CLIENTSOURCEASSERT    assert client.post(ROUTE + "/collect", content=b"").status_code == 400
    assert client.post(ROUTE + "/collect", content=("x" * §CLIENTLIMIT).encode()).status_code == 413


def test_a_caller_over_the_limit_of_requests_a_minute_is_refused_with_retry_after(api):
    client, _ = api
    if time.time() % 60 > 52:
        time.sleep(8)
    reset_client_limit()
    for number in range(§CLIENTRATE):
        allowed = client.post(ROUTE + "/collect", content=message(None).encode())
        assert allowed.status_code == 202, f"request {number + 1} is inside the limit"
    refused = client.post(ROUTE + "/collect", content=message(None).encode())
    assert refused.status_code == 429 and "retry-after" in refused.headers
    assert refused.json()["type"].endswith("§T_RATE")
"""";
}
