using CodeGenNew.Core.Naming;
using CodeGenNew.Core.Party;
using CodeGenNew.Core.Sql;

namespace CodeGenNew.Core.Python;

/// <summary> <c>tests/test_parties.py</c> of the Python API (PY_TestParties), beside the <c>conftest.py</c> the approval tests share: the cases add two parties and their identifiers through the routes and remove
/// them after the test. They play the rule (a scheme and value pair is held by one row, trimmed and compared without regard to case) and the search by identifier. The words are <see cref="PartyWords"/>. </summary>
public static class PythonPartyTests
{
    /// <summary> Whether the tests are written: <c>PythonTests</c>, a party table the tests can fill, over a database the shared conftest can open (not SQL Server). </summary>
    public static bool Applies(DatabaseModel database, ProjectSettings project) =>
        project.PythonTests && DialectInfo.For(database.Dialect).RunsPythonTests && PartyTestPlan.For(database, project) is not null;

    public static string Conftest(ProjectSettings project, SqlDialect dialect) => PythonApprovalTests.Conftest(project, dialect);

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var plan = PartyTestPlan.For(database, project)!;
        var s = plan.Shape;
        string Json(ColumnModel c) => JsonNames.Camel(c.Name);
        string auth = project.ApiAuth ? "from app.auth import CurrentUser, require_user\n" : "";
        string signIn = project.ApiAuth ? "    app.dependency_overrides[require_user] = lambda: CurrentUser(name=\"zz_parties\", role=None, owner_id=None)\n" : "";
        string signOut = project.ApiAuth ? "    app.dependency_overrides.pop(require_user, None)\n" : "";
        return TemplateText.FillAll(Text, new (string, string)[]
        {
            ("§AUTHIMPORT\n", auth), ("§SIGNIN", signIn), ("§SIGNOUT", signOut),
            ("§PARTYROUTE", PythonNames.Str(PythonTable.Of(s.Party, project).Route)), ("§IDROUTE", PythonNames.Str(PythonTable.Of(s.Identifiers, project).Route)),
            ("§PARTYBODY", PythonNames.Str(plan.PartyBody)), ("§IDBODY", PythonNames.Str(plan.IdentifierBody)),
            ("§PARTYKEY", Json(s.PartyKey)), ("§IDKEY", Json(s.IdentifierKey)), ("§LINK", Json(s.Link)), ("§SCHEME", Json(s.Scheme)), ("§VALUE", Json(s.Value)),
            ("§FIRST", PartyTestPlan.FirstScheme), ("§OTHER", PartyTestPlan.OtherScheme), ("§T_TAKEN", PartyWords.IdentifierTakenType),
            ("§CONFLICT", HttpStatus.Conflict.ToString())
        }) + "\n";
    }

    private const string Text = """"
"""The party table and its identifiers through the routes: a scheme and value pair is held by one row, and a party is found by an identifier. Written by PY_TestParties; regenerated with the tables."""
import json
import uuid

import pytest
from fastapi.testclient import TestClient

§AUTHIMPORT
PARTIES = §PARTYROUTE
IDENTIFIERS = §IDROUTE
PARTY = json.loads(§PARTYBODY)
IDENTIFIER = json.loads(§IDBODY)
TAKEN = "§T_TAKEN"


class Rows:
    """Adds parties and identifiers and remembers them, so the test can take them out again."""

    def __init__(self, client):
        self.client = client
        self.tag = uuid.uuid4().hex[:8].upper()
        self.parties = []
        self.identifiers = []

    def party(self):
        made = self.client.post(PARTIES, json=PARTY)
        assert made.status_code == 201, made.text
        self.parties.append(made.json()["§PARTYKEY"])
        return self.parties[-1]

    def identifier(self, owner, scheme, value):
        made = self.client.post(IDENTIFIERS, json={**IDENTIFIER, "§LINK": owner, "§SCHEME": scheme, "§VALUE": value})
        if made.status_code == 201:
            self.identifiers.append(made.json()["§IDKEY"])
        return made

    def found(self, **query):
        reply = self.client.get(PARTIES + "/by-identifier", params=query)
        assert reply.status_code == 200, reply.text
        return sorted(row["§PARTYKEY"] for row in reply.json())

    def remove(self):
        for key in self.identifiers:
            self.client.delete(f"{IDENTIFIERS}/{key}")
        for key in self.parties:
            self.client.delete(f"{PARTIES}/{key}")


@pytest.fixture
def rows(application):
    """A client over a scratch database; the rows a test adds are removed after it."""
    app, _ = application
§SIGNIN    made = Rows(TestClient(app))
    yield made
    made.remove()
§SIGNOUT

def test_a_scheme_and_value_pair_another_row_holds_is_refused(rows):
    acme, bolt = rows.party(), rows.party()
    held = "NL" + rows.tag
    first = rows.identifier(acme, "§FIRST", "  " + held + "  ")
    assert first.status_code == 201, first.text
    assert first.json()["§VALUE"] == held and first.json()["§SCHEME"] == "§FIRST", "both are trimmed"

    same = rows.identifier(bolt, "§FIRST", held)
    assert same.status_code == §CONFLICT and TAKEN in same.text, same.text
    assert rows.identifier(bolt, "§FIRST".lower(), held.lower()).status_code == §CONFLICT, "case does not make a new pair"
    assert rows.identifier(bolt, "§OTHER", held).status_code == 201, "the same value under another scheme is another pair"

    own = rows.identifier(bolt, "§FIRST", "DE" + rows.tag)
    assert own.status_code == 201, own.text
    route = f"{IDENTIFIERS}/{own.json()['§IDKEY']}"
    assert rows.client.put(route, json=own.json()).status_code in (200, 204), "a row keeps its own pair"
    clash = rows.client.put(route, json={**own.json(), "§VALUE": held.lower()})
    assert clash.status_code == §CONFLICT and TAKEN in clash.text, clash.text


def test_a_party_is_found_by_its_identifier(rows):
    acme, bolt = rows.party(), rows.party()
    held = "NL" + rows.tag
    assert rows.identifier(acme, "§FIRST", held).status_code == 201
    assert rows.identifier(bolt, "§OTHER", held).status_code == 201

    assert rows.found(value=held) == sorted([acme, bolt])
    assert rows.found(value=held, scheme="§FIRST") == [acme]
    assert rows.found(value="  " + held.lower() + " ", scheme="§FIRST".lower()) == [acme], "the search does not mind case or spaces"
    assert rows.found(value="XX" + rows.tag) == []
    assert rows.client.get(PARTIES + "/by-identifier").status_code == 400
    assert rows.client.get(PARTIES + "/by-identifier", params={"value": " "}).status_code == 400
"""";
}
