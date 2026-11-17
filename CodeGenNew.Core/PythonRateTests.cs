namespace CodeGenNew.Core;

/// <summary> <c>tests/test_rates.py</c> of the Python API (PY_TestRates), beside the <c>conftest.py</c> the approval tests share: the cases add rates through the route and remove them after the test. They play
/// the rule (trimmed upper case currencies, two different ones, a rate above zero, one rate per pair and day) and the lookup of the rate in force. The words are <see cref="RateWords"/>. </summary>
public static class PythonRateTests
{
    /// <summary> Whether the tests are written: <c>PythonTests</c>, a rate table the tests can fill, over a database the shared conftest can open (not SQL Server). </summary>
    public static bool Applies(DatabaseModel database, ProjectSettings project) =>
        project.PythonTests && DialectInfo.For(database.Dialect).RunsPythonTests && RateTestPlan.For(database, project) is not null;

    public static string Conftest(ProjectSettings project, SqlDialect dialect) => PythonApprovalTests.Conftest(project, dialect);

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var plan = RateTestPlan.For(database, project)!;
        var s = plan.Shape;
        string Json(ColumnModel c) => JsonNames.Camel(c.Name);
        string auth = project.ApiAuth ? "from app.auth import CurrentUser, require_user\n" : "";
        string signIn = project.ApiAuth ? "    app.dependency_overrides[require_user] = lambda: CurrentUser(name=\"zz_rates\", role=None, owner_id=None)\n" : "";
        string signOut = project.ApiAuth ? "    app.dependency_overrides.pop(require_user, None)\n" : "";
        var names = new (string, string)[]
        {
            ("§AUTHIMPORT\n", auth), ("§SIGNIN", signIn), ("§SIGNOUT", signOut),
            ("§ROUTE", PythonNames.Str(PythonTable.Of(s.Table, project).Route)), ("§BODY", PythonNames.Str(plan.Body)),
            ("§KEY", Json(s.Key)), ("§FROM", Json(s.From)), ("§TO", Json(s.To)), ("§RATE", Json(s.Rate)), ("§DAY", Json(s.Day)),
            ("§D1", plan.Day("2024-01-01")), ("§D2", plan.Day("2024-03-01")),
            ("§T_TAKEN", RateWords.TakenType), ("§T_INVALID", RateWords.InvalidType), ("§T_MISSING", RateWords.MissingType),
            ("§CONFLICT", HttpStatus.Conflict.ToString()), ("§D3", plan.Day("2024-03-15")), ("§HEADER", RateWords.InvertedHeader)
        };
        string text = TemplateText.FillAll(Text, names);
        if (project.RateInverse && RatePlan.InvertibleRate(s.Rate))
            text += "\n\n" + TemplateText.FillAll(InverseText, names);
        return text + "\n";
    }

    private const string Text = """"
"""The rate table through the route: currencies are trimmed and upper case and differ, a rate is above zero, a pair has one rate per day, and the rate in force on a day is the latest one on or before it. Written by PY_TestRates; regenerated with the tables."""
import json
import uuid

import pytest
from fastapi.testclient import TestClient

§AUTHIMPORT
ROUTE = §ROUTE
RATE = json.loads(§BODY)
TAKEN = "§T_TAKEN"
INVALID = "§T_INVALID"
MISSING = "§T_MISSING"


class Rows:
    """Adds rates of one pair of currencies no other run uses and remembers them, so the test can take them out again."""

    def __init__(self, client):
        self.client = client
        tag = uuid.uuid4().hex.upper()
        self.source, self.target = "T" + tag[:2], "Y" + tag[2:4]
        self.keys = []

    def save(self, source, target, rate, day):
        made = self.client.post(ROUTE, json={**RATE, "§FROM": source, "§TO": target, "§RATE": rate, "§DAY": day})
        if made.status_code == 201:
            self.keys.append(made.json()["§KEY"])
        return made

    def on(self, source, target, day=None):
        params = {"from": source, "to": target}
        if day:
            params["on"] = day
        return self.client.get(ROUTE + "/rate", params=params)

    def remove(self):
        for key in self.keys:
            self.client.delete(f"{ROUTE}/{key}")


@pytest.fixture
def rows(application):
    """A client over a scratch database; the rates a test adds are removed after it."""
    app, _ = application
§SIGNIN    made = Rows(TestClient(app))
    yield made
    made.remove()
§SIGNOUT

def test_a_rate_is_kept_trimmed_in_upper_case_and_once_per_pair_and_day(rows):
    first = rows.save("  " + rows.source.lower() + " ", rows.target, 1.5, "§D1")
    assert first.status_code == 201, first.text
    assert (first.json()["§FROM"], first.json()["§TO"]) == (rows.source, rows.target), "both are trimmed and upper case"

    same = rows.save(rows.source.lower(), rows.target, 1.6, "§D1")
    assert same.status_code == §CONFLICT and TAKEN in same.text, same.text
    assert rows.save(rows.target, rows.source, 0.66, "§D1").status_code == 201, "the opposite pair is another rate"
    later = rows.save(rows.source, rows.target, 1.7, "§D2")
    assert later.status_code == 201, later.text

    route = f"{ROUTE}/{later.json()['§KEY']}"
    assert rows.client.put(route, json={**later.json(), "§RATE": 1.8}).status_code in (200, 204), "a row keeps its own pair and day"
    clash = rows.client.put(route, json={**later.json(), "§DAY": "§D1"})
    assert clash.status_code == §CONFLICT and TAKEN in clash.text, clash.text


def test_a_rate_that_is_not_valid_is_refused(rows):
    same = rows.save(rows.source, rows.source, 1, "§D1")
    assert same.status_code == 400 and INVALID in same.text, same.text
    zero = rows.save(rows.source, rows.target, 0, "§D1")
    assert zero.status_code == 400 and INVALID in zero.text, zero.text
    assert rows.save(rows.source, rows.target, -2, "§D1").status_code == 400
    blank = rows.save("  ", rows.target, 1, "§D1")
    assert blank.status_code == 400 and INVALID in blank.text, blank.text
    assert rows.keys == []


def test_the_rate_in_force_is_the_latest_one_on_or_before_the_day(rows):
    assert rows.save(rows.source, rows.target, 1.5, "§D1").status_code == 201
    assert rows.save(rows.source, rows.target, 1.7, "§D2").status_code == 201
    assert rows.save(rows.target, rows.source, 0.66, "§D2").status_code == 201

    early = rows.on(rows.source.lower(), rows.target, "2023-12-31")
    assert early.status_code == 404 and MISSING in early.text, early.text
    assert float(rows.on(rows.source, rows.target, "2024-02-15").json()["§RATE"]) == 1.5, "a day between two rates gets the earlier one"
    assert float(rows.on(rows.source, rows.target, "2024-03-01").json()["§RATE"]) == 1.7, "the day a rate starts gets that rate"
    assert float(rows.on(rows.source, rows.target).json()["§RATE"]) == 1.7, "no day means today"
    assert float(rows.on(rows.target, rows.source, "2024-06-01").json()["§RATE"]) == 0.66, "the opposite pair has its own rates"
    assert rows.on(rows.source, "XXQ", "2024-06-01").status_code == 404
    assert rows.on(rows.source, rows.source).status_code == 400
    assert rows.client.get(ROUTE + "/rate", params={"from": rows.source}).status_code == 400
"""";

    private const string InverseText = """"

def test_a_newer_rate_of_the_opposite_pair_answers_as_one_divided_by_it(rows):
    assert rows.save(rows.source, rows.target, 1.1, "§D1").status_code == 201
    assert rows.save(rows.target, rows.source, 0.8, "§D1").status_code == 201
    direct = rows.on(rows.source, rows.target, "2024-02-01")
    assert float(direct.json()["§RATE"]) == 1.1, "on the same day the pair's own row wins"
    assert "§HEADER" not in direct.headers
    assert rows.save(rows.target, rows.source, 0.5, "§D3").status_code == 201
    inverse = rows.on(rows.source, rows.target, "2024-04-01")
    answer = inverse.json()
    assert float(answer["§RATE"]) == 2.0, "one divided by the newer opposite rate"
    assert (answer["§FROM"], answer["§TO"]) == (rows.source, rows.target)
    assert inverse.headers.get("§HEADER") == "true", "the answer says it was worked out"
"""";
}
