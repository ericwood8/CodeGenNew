namespace CodeGenNew.Core;

/// <summary> <c>tests/test_rate_documents.py</c> of the Python API (PY_TestRates), beside the rate tests: a document is written with the rate in force on its date, keeps it while its currency and date stay, gets the
/// amount times the rate as its converted amount, ignores a rate and converted amount sent by the caller on an update, and is refused when its currency has no rate. The words are <see cref="RateWords"/>. </summary>
public static class PythonRateDocumentTests
{
    public static bool Applies(DatabaseModel database, ProjectSettings project) =>
        project.PythonTests && DialectInfo.For(database.Dialect).RunsPythonTests && RateDocumentTestPlan.For(database, project) is not null;

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var plan = RateDocumentTestPlan.For(database, project)!;
        var s = plan.Shape;
        string Json(ColumnModel c) => JsonNames.Camel(c.Name);
        string? d1 = plan.DocumentDay("2024-01-01"), d2 = plan.DocumentDay("2024-03-01");
        string auth = project.ApiAuth ? "from app.auth import CurrentUser, require_user\n" : "";
        string signIn = project.ApiAuth ? "    app.dependency_overrides[require_user] = lambda: CurrentUser(name=\"zz_rates\", role=None, owner_id=None)\n" : "";
        string signOut = project.ApiAuth ? "    app.dependency_overrides.pop(require_user, None)\n" : "";
        string setDay = s.Day is null ? "" : $"        if day:\n            body[\"{Json(s.Day)}\"] = day\n";
        string rate = Json(s.Rate), baseAmount = Json(s.BaseAmount);
        string[] lines = s.Day is null
            ? [$"    assert close(number(written[\"{rate}\"]) * 100, number(written[\"{baseAmount}\"])), \"the amount times the rate\""]
            : [$"    assert close(number(written[\"{rate}\"]), 1.1), \"the rate in force on the date\"",
               $"    assert close(number(written[\"{baseAmount}\"]), 110), \"the amount in the base currency\"",
               $"    moved = rows.client.put(route, json={{**written, \"{Json(s.Day)}\": \"{d2}\"}})",
               "    assert moved.status_code in (200, 204), moved.text",
               "    again = rows.client.get(route).json()",
               $"    assert close(number(again[\"{rate}\"]), 1.2), \"a new date takes the rate of that date\"",
               $"    assert close(number(again[\"{baseAmount}\"]), 120)"];
        string dated = string.Join('\n', lines) + "\n";
        return TemplateText.FillAll(Text, new (string, string)[]
        {
            ("§AUTHIMPORT\n", auth), ("§SIGNIN", signIn), ("§SIGNOUT", signOut), ("§SETDAY", setDay), ("§DATED\n", dated),
            ("§ROUTE", PythonNames.Str(PythonTable.Of(s.Table, project).Route)), ("§RATES", PythonNames.Str(PythonTable.Of(s.Rates.Table, project).Route)),
            ("§RATEBODY", PythonNames.Str(plan.RateBody)), ("§BODY", PythonNames.Str(plan.Body)),
            ("§KEY", Json(s.Key)), ("§CUR", Json(s.Currency)), ("§RATE", Json(s.Rate)), ("§AMOUNT", Json(s.Amount)), ("§BASEAMOUNT", Json(s.BaseAmount)), ("§DAY", s.Day is null ? "" : Json(s.Day)),
            ("§RKEY", Json(s.Rates.Key)), ("§RFROM", Json(s.Rates.From)), ("§RTO", Json(s.Rates.To)), ("§RRATE", Json(s.Rates.Rate)), ("§RDAY", Json(s.Rates.Day)),
            ("§BASE", s.BaseCurrency), ("§DAY1", d1 is null ? "None" : "\"" + d1 + "\""),
            ("§RD1", plan.RateDay("2024-01-01")), ("§RD2", plan.RateDay("2024-03-01")),
            ("§T_NEEDED", RateWords.NeededType)
        }) + "\n";
    }

    private const string Text = """"
"""A document of a rate table through the route: written with the rate in force on its date, kept while its currency and date stay, the amount times the rate as its converted amount, and refused when its currency has no rate. Written by PY_TestRates; regenerated with the tables."""
import json
import uuid

import pytest
from fastapi.testclient import TestClient

§AUTHIMPORT
ROUTE = §ROUTE
RATES = §RATES
RATE = json.loads(§RATEBODY)
DOCUMENT = json.loads(§BODY)
BASE = "§BASE"
DAY1 = §DAY1
NEEDED = "§T_NEEDED"


def number(value):
    return float(value)


def close(left, right):
    return abs(left - right) < 0.005


class Rows:
    """Adds rates of one currency no other run uses and documents in it, and remembers them, so the test can take them out again."""

    def __init__(self, client):
        self.client = client
        self.currency = "T" + uuid.uuid4().hex.upper()[:2]
        self.rates = []
        self.documents = []

    def rate(self, currency, value, day):
        made = self.client.post(RATES, json={**RATE, "§RFROM": currency, "§RTO": BASE, "§RRATE": value, "§RDAY": day})
        assert made.status_code == 201, made.text
        self.rates.append(made.json()["§RKEY"])

    def document(self, currency, amount, day=None):
        body = {**DOCUMENT, "§CUR": currency, "§AMOUNT": amount, "§RATE": 99, "§BASEAMOUNT": 1}
§SETDAY        made = self.client.post(ROUTE, json=body)
        if made.status_code == 201:
            self.documents.append(made.json()["§KEY"])
        return made

    def remove(self):
        for key in reversed(self.documents):
            self.client.delete(f"{ROUTE}/{key}")
        for key in reversed(self.rates):
            self.client.delete(f"{RATES}/{key}")


@pytest.fixture
def rows(application):
    """A client over a scratch database; the rates and documents a test adds are removed after it."""
    app, _ = application
§SIGNIN    made = Rows(TestClient(app))
    yield made
    made.remove()
§SIGNOUT

def test_a_document_is_written_with_the_rate_in_force_and_keeps_it(rows):
    rows.rate(rows.currency, 1.1, "§RD1")
    rows.rate(rows.currency, 1.2, "§RD2")
    first = rows.document(" " + rows.currency.lower() + " ", 100, DAY1)
    assert first.status_code == 201, first.text
    written = first.json()
    route = f"{ROUTE}/{written['§KEY']}"
    assert written["§CUR"] == rows.currency, "the currency is trimmed and upper case"
§DATED
    changed = rows.client.put(route, json={**written, "§AMOUNT": 200, "§RATE": 5})
    assert changed.status_code in (200, 204), changed.text
    kept = rows.client.get(route).json()
    assert number(kept["§RATE"]) != 5, "the rate is not the caller's"
    assert close(number(kept["§RATE"]) * 200, number(kept["§BASEAMOUNT"])), "the amount times the rate"

    home = rows.document(BASE.lower(), 50, DAY1)
    assert home.status_code == 201, home.text
    assert close(number(home.json()["§RATE"]), 1), "the base currency has rate 1"
    assert close(number(home.json()["§BASEAMOUNT"]), 50)


def test_a_document_in_a_currency_with_no_rate_is_refused(rows):
    none = rows.document(rows.currency, 100, DAY1)
    assert none.status_code == 400 and NEEDED in none.text, none.text
    blank = rows.document("  ", 100, DAY1)
    assert blank.status_code == 400, blank.text
    assert rows.documents == [], "nothing was kept"
"""";
}
