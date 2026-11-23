using CodeGenNew.Core.Sql;
using CodeGenNew.Core.Stock;

namespace CodeGenNew.Core.Python;

/// <summary> <c>tests/test_stock.py</c> of the Python API (PY_TestStock), beside the <c>conftest.py</c> the approval tests share: the cases receive, reserve, commit, release and count stock of an item of their own through the routes of the stock ledger
/// and play the rules (<see cref="StockWords"/>). A ledger keeps its rows, so they stay in the scratch database. </summary>
public static class PythonStockTests
{
    public static bool Applies(DatabaseModel database, ProjectSettings project) =>
        project.PythonTests && DialectInfo.For(database.Dialect).RunsPythonTests && StockTestPlan.For(database, project) is not null;

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var plan = StockTestPlan.For(database, project)!;
        string auth = project.ApiAuth ? "from app.auth import CurrentUser, require_user\n" : "";
        string signIn = project.ApiAuth ? "    app.dependency_overrides[require_user] = lambda: CurrentUser(name=\"zz_stock\", role=None, owner_id=None)\n" : "";
        string signOut = project.ApiAuth ? "    app.dependency_overrides.pop(require_user, None)\n" : "";
        var names = new (string, string)[]
        {
            ("§AUTHIMPORT\n", auth), ("§SIGNIN", signIn), ("§SIGNOUT", signOut),
            ("§ITEMROUTE", PythonNames.Str(plan.ItemRoute)), ("§LEDGERROUTE", PythonNames.Str(plan.LedgerRoute)), ("§ITEMBODY", PythonNames.Str(plan.ItemBody)), ("§ITEMKEY", plan.ItemKeyJson), ("§LEDGERITEM", plan.LedgerItemJson), ("§LEDGERKEY", plan.LedgerKeyJson),
            ("§T_SHORT", StockWords.ShortType), ("§T_CLOSED", StockWords.ClosedType), ("§T_BELOW", StockWords.BelowReservedType), ("§T_UNKNOWN", StockWords.UnknownType),
        };
        return TemplateText.FillAll(Text, names) + "\n";
    }

    private const string Text = """"
"""Stock through the routes of the ledger: a receipt puts stock on hand, a reservation takes it out of what is available with one guarded update, a commit turns a hold into a sale, a release gives it back and a stock-take writes an adjustment row."""
import json

import pytest
from fastapi.testclient import TestClient

§AUTHIMPORT
ITEMS = §ITEMROUTE
LEDGER = §LEDGERROUTE
ITEM = json.loads(§ITEMBODY)


class Stock:
    def __init__(self, client):
        self.client = client

    def item(self):
        made = self.client.post(ITEMS, json=ITEM)
        assert made.status_code == 201, made.text
        return made.json()["§ITEMKEY"]

    def post(self, route, body=None):
        return self.client.post(f"{LEDGER}/{route}", json=body or {})

    def level(self, item):
        answer = self.client.get(f"{LEDGER}/available", params={"item": item})
        assert answer.status_code == 200, answer.text
        body = answer.json()
        return body["onHand"], body["reserved"], body["available"]


@pytest.fixture
def stock(application):
    """A client over a scratch database."""
    app, _ = application
§SIGNIN    yield Stock(TestClient(app))
§SIGNOUT

def test_a_receipt_a_reservation_and_a_commit_move_the_level_and_write_the_ledger(stock):
    item = stock.item()
    assert stock.post("receive", {"item": item, "quantity": 10}).status_code == 201
    hold = stock.post("reserve", {"item": item, "quantity": 4})
    assert hold.status_code == 201, hold.text
    assert stock.level(item) == (10, 4, 6)
    sold = stock.post(f"commit/{hold.json()['reservation']}")
    assert sold.status_code == 200, sold.text
    assert stock.level(item) == (6, 0, 6), "a sale takes the quantity from on hand and from reserved"
    assert stock.post(f"commit/{hold.json()['reservation']}").status_code == 409, "a hold is committed once"


def test_the_last_item_goes_to_one_buyer_and_a_release_gives_a_hold_back(stock):
    item = stock.item()
    stock.post("receive", {"item": item, "quantity": 3})
    first = stock.post("reserve", {"item": item, "quantity": 3})
    assert first.status_code == 201, first.text
    refused = stock.post("reserve", {"item": item, "quantity": 1})
    assert refused.status_code == 409 and "§T_SHORT" in refused.text, refused.text
    assert stock.post(f"release/{first.json()['reservation']}").status_code == 200
    assert stock.level(item) == (3, 0, 3)
    closed = stock.post(f"commit/{first.json()['reservation']}")
    assert closed.status_code == 409 and "§T_CLOSED" in closed.text, closed.text
    assert stock.post("commit/987654").status_code == 404


def test_a_stock_take_writes_an_adjustment_and_a_count_below_what_is_reserved_is_refused(stock):
    item = stock.item()
    stock.post("receive", {"item": item, "quantity": 10})
    stock.post("reserve", {"item": item, "quantity": 4})
    below = stock.post("adjust", {"item": item, "counted": 3})
    assert below.status_code == 409 and "§T_BELOW" in below.text, below.text
    counted = stock.post("adjust", {"item": item, "counted": 9})
    assert counted.status_code == 200 and counted.json()["difference"] == -1, counted.text
    assert stock.level(item) == (9, 4, 5)
    checked = stock.client.get(f"{LEDGER}/check").json()
    assert all(row["item"] != item for row in checked["differences"]), "the level agrees with the ledger"


def test_a_ledger_row_is_never_changed_or_deleted(stock):
    item = stock.item()
    stock.post("receive", {"item": item, "quantity": 1})
    rows = stock.client.get(LEDGER, params={"search": "", "pageSize": 200}).json()
    rows = rows if isinstance(rows, list) else rows.get("items") or rows.get("data") or []
    row = next(r for r in rows if r["§LEDGERITEM"] == item)
    key = row["§LEDGERKEY"]
    assert stock.client.put(f"{LEDGER}/{key}", json=row).status_code == 405
    assert stock.client.delete(f"{LEDGER}/{key}").status_code == 405
"""";
}
