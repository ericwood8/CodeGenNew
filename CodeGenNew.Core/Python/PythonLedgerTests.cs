using CodeGenNew.Core.Payment;
using CodeGenNew.Core.Sql;

namespace CodeGenNew.Core.Python;

/// <summary> <c>tests/test_ledger.py</c> of the Python API (PY_TestLedger), beside the <c>conftest.py</c> the approval tests share: the cases record rows through <c>POST record</c> against a document of their own and play the
/// ledger rules (<see cref="PaymentWords"/>). A ledger keeps its rows, so they stay in the scratch database. </summary>
public static class PythonLedgerTests
{
    /// <summary> Whether the tests are written: <c>PythonTests</c>, a ledger the tests can fill, over a database the shared conftest can open (not SQL Server). </summary>
    public static bool Applies(DatabaseModel database, ProjectSettings project) =>
        project.PythonTests && DialectInfo.For(database.Dialect).RunsPythonTests && LedgerTestPlan.For(database, project) is not null;

    public static string Conftest(ProjectSettings project, SqlDialect dialect) => PythonApprovalTests.Conftest(project, dialect);

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var plan = LedgerTestPlan.For(database, project)!;
        string auth = project.ApiAuth ? "from app.auth import CurrentUser, require_user\n" : "";
        string signIn = project.ApiAuth ? "    app.dependency_overrides[require_user] = lambda: CurrentUser(name=\"zz_ledger\", role=None, owner_id=None)\n" : "";
        string signOut = project.ApiAuth ? "    app.dependency_overrides.pop(require_user, None)\n" : "";
        var names = new (string, string)[]
        {
            ("§AUTHIMPORT\n", auth), ("§SIGNIN", signIn), ("§SIGNOUT", signOut),
            ("§DOCROUTE", PythonNames.Str(PythonTable.Of(plan.Shape.DocumentTable, project).Route)), ("§ROWROUTE", PythonNames.Str(PythonTable.Of(plan.Shape.Table, project).Route)),
            ("§DOCBODY", PythonNames.Str(plan.DocumentBody)), ("§ROWBODY", PythonNames.Str(plan.RowBody)),
            ("§DOCKEY", plan.DocumentKeyJson), ("§TOTAL", plan.TotalJson), ("§ROWKEY", plan.KeyJson), ("§DOC", plan.DocumentJson), ("§KIND", plan.KindJson), ("§STATUS", plan.StatusJson),
            ("§AMOUNT", plan.AmountJson), ("§GATEWAY", plan.GatewayJson), ("§IDEM", plan.IdempotencyJson),
            ("§T_REFUND", PaymentWords.RefundOverType), ("§T_CAPTURE", PaymentWords.CaptureOverType), ("§T_DUP", PaymentWords.LedgerDuplicateType), ("§T_INVALID", PaymentWords.InvalidType),
            ("§RECORD", LedgerPlan.RecordRoute), ("§PAID", LedgerPlan.PaidRoute),
        };
        return TemplateText.FillAll(Text, names) + "\n";
    }

    private const string Text = """"
"""The ledger through the route: a row is recorded against a document and the paid amount is worked out from the succeeded rows, a refund is negative and never more than has been paid, a capture never more than the total, a retried call answers with the stored row and a key or a gateway id is recorded once. Written by PY_TestLedger; regenerated with the tables. A ledger keeps its rows, so each test adds a document of its own."""
import json
import uuid

import pytest
from fastapi.testclient import TestClient

§AUTHIMPORT
DOCUMENTS = §DOCROUTE
ROWS = §ROWROUTE
DOCUMENT = json.loads(§DOCBODY)
ROW = json.loads(§ROWBODY)


class Ledger:
    def __init__(self, client):
        self.client = client

    def document(self, total):
        made = self.client.post(DOCUMENTS, json={**DOCUMENT, "§TOTAL": total})
        assert made.status_code == 201, made.text
        return made.json()["§DOCKEY"]

    def record(self, document, kind, amount, status="succeeded", gateway=None, key=None):
        tag = uuid.uuid4().hex
        body = {**ROW, "§DOC": document, "§KIND": kind, "§STATUS": status, "§AMOUNT": amount, "§GATEWAY": gateway or "g" + tag, "§IDEM": key or "k" + tag}
        return self.client.post(ROWS + "/§RECORD", json=body)

    def paid(self, document):
        answer = self.client.get(ROWS + "/§PAID", params={"document": document})
        assert answer.status_code == 200, answer.text
        return answer.json()


@pytest.fixture
def ledger(application):
    """A client over a scratch database."""
    app, _ = application
§SIGNIN    yield Ledger(TestClient(app))
§SIGNOUT

def test_what_a_document_has_been_paid_is_worked_out_from_the_succeeded_rows(ledger):
    document = ledger.document(300)
    first = ledger.record(document, "Authorize", 300)
    assert first.status_code == 201, first.text
    assert first.json()["§KIND"] == "authorize", "the kind is written in lower case"
    assert ledger.record(document, "capture", 200).status_code == 201
    assert ledger.record(document, "capture", 40, "pending").status_code == 201
    assert ledger.record(document, "refund", -50).status_code == 201
    paid = ledger.paid(document)
    assert (float(paid["captured"]), float(paid["refunded"]), float(paid["paid"]), float(paid["remaining"]), paid["settled"]) == (200.0, 50.0, 150.0, 150.0, False)


def test_a_refund_is_negative_and_never_more_than_has_been_paid_and_a_capture_never_more_than_the_total(ledger):
    document = ledger.document(100)
    assert ledger.record(document, "refund", 10).status_code == 400, "a refund is a negative row"
    assert ledger.record(document, "capture", -10).status_code == 400
    assert ledger.record(document, "capture", 60).status_code == 201
    too_much = ledger.record(document, "refund", -61)
    assert too_much.status_code == 400 and "§T_REFUND" in too_much.text, too_much.text
    over = ledger.record(document, "capture", 41)
    assert over.status_code == 400 and "§T_CAPTURE" in over.text, over.text
    assert ledger.record(document, "capture", 40).status_code == 201
    assert ledger.paid(document)["settled"] is True


def test_a_retried_call_answers_with_the_stored_row_and_a_key_or_gateway_id_is_recorded_once(ledger):
    document = ledger.document(100)
    first = ledger.record(document, "capture", 30, key="retry-" + uuid.uuid4().hex)
    assert first.status_code == 201, first.text
    stored = first.json()
    retry = ledger.record(document, "capture", 30, key=stored["§IDEM"])
    assert retry.status_code == 200 and retry.json()["§ROWKEY"] == stored["§ROWKEY"], retry.text
    assert float(ledger.paid(document)["captured"]) == 30.0, "the retry wrote nothing"
    other = ledger.record(document, "capture", 5, key=stored["§IDEM"])
    assert other.status_code == 409 and "§T_DUP" in other.text, other.text
    assert ledger.record(document, "capture", 5, gateway=stored["§GATEWAY"]).status_code == 409


def test_a_row_is_never_changed_or_deleted(ledger):
    document = ledger.document(100)
    row = ledger.record(document, "capture", 10).json()
    assert ledger.client.put(f"{ROWS}/{row['§ROWKEY']}", json=row).status_code == 405
    assert ledger.client.delete(f"{ROWS}/{row['§ROWKEY']}").status_code == 405
"""";
}
