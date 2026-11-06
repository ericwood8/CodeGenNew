using System.Text;

namespace CodeGenNew.Core;

/// <summary> <c>tests/test_tokens.py</c> of the Python API (PY_TestTokens), beside the <c>conftest.py</c> the other tests share: the cases of the generated ASP.NET tests (<see cref="ApiTokenTests"/>) played through the
/// routes over a scratch database. Each test inserts rows of its own with plain SQL (the hash of a token it makes up) and removes them after it. Written for each token table whose other columns the generator can
/// fill (<see cref="TokenPlan.TestColumns"/>), over SQLite, PostgreSQL or MySQL. </summary>
public static class PythonTokenTests
{
    public static bool Applies(DatabaseModel database, ProjectSettings project) =>
        project.PythonTests && project.ApiAuth && DialectInfo.For(database.Dialect).RunsPythonTests && Tables(database, project).Count > 0;

    public static IReadOnlyList<TokenShape> Tables(DatabaseModel database, ProjectSettings project) =>
        database.Tables.Select(t => TableShapes.For(t, project).Token).OfType<TokenShape>().Where(t => TokenPlan.TestColumns(t) is not null).OrderBy(t => t.Table.TableName, StringComparer.OrdinalIgnoreCase).ToList();

    public static string Conftest(ProjectSettings project, SqlDialect dialect) => PythonApprovalTests.Conftest(project, dialect);

    /// <summary> The file name of a table's tests. </summary>
    public static string FileName(TokenShape t) => $"tests/test_tokens_{new string(t.Table.TableName.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray())}.py";

    public static string File(TokenShape t, DatabaseModel database, ProjectSettings project)
    {
        var o = new StringBuilder();
        o.Append(FillAll(Head, ("§ADMINROLE", PythonNames.Str(project.AuthAdminRole))));
        o.Append(ForTable(t, database, project));
        return o.ToString();
    }

    private static string FillAll(string text, params (string, string)[] values) => TemplateText.FillAll(text, values);

    private static string ForTable(TokenShape t, DatabaseModel database, ProjectSettings project)
    {
        var di = DialectInfo.For(database.Dialect);
        var main = PythonTable.Of(t.Table, project);
        string Q(ColumnModel c) => di.Quote(c.DbName);
        string Json(ColumnModel c) => JsonNames.Camel(c.Name);
        string table = di.QuoteTable(t.Table.SchemaName, t.Table.DbTableName);
        var fill = TokenPlan.TestColumns(t)!;

        var columns = new List<(string Column, string Param)> { (Q(t.Hash), "hash"), (Q(t.Expiry), "expires") };
        var values = new List<string> { "\"hash\": digest", "\"expires\": datetime.now() + timedelta(hours=hours)" };
        foreach (var end in t.Ends.Where(c => c.IsBooleanColumn))
        {
            columns.Add((Q(end), "ended_" + end.DbName));
            values.Add($"\"ended_{end.DbName}\": False");
        }
        if (t.Person is not null)
        {
            columns.Add((Q(t.Person), "person"));
            values.Add("\"person\": person");
        }
        if (t.Family is not null)
        {
            columns.Add((Q(t.Family), "family"));
            values.Add("\"family\": family if family is not None else uuid.uuid4().hex");
        }
        for (int i = 0; i < fill.Count; i++)
        {
            columns.Add((Q(fill[i].Column), "fill" + i));
            values.Add($"\"fill{i}\": " + fill[i].Kind switch
            {
                TokenPlan.FillKind.Time => "datetime.now()",
                TokenPlan.FillKind.Flag => "False",
                TokenPlan.FillKind.Number => "0",
                TokenPlan.FillKind.Guid => "str(uuid.uuid4())",
                _ => "\"test\""
            });
        }
        string insert = $"INSERT INTO {table} ({string.Join(", ", columns.Select(c => c.Column))}) VALUES ({string.Join(", ", columns.Select(c => c.Param.StartsWith("fill") ? Template(c.Column, ":" + c.Param) : ":" + c.Param))})";
        // a column the routes do not manage copies the first row of the table when there is one (a check constraint or a parent row the generator cannot guess), else the example value
        string Template(string column, string fallback) => $"COALESCE((SELECT copied.c FROM (SELECT {column} AS c FROM {table} WHERE {Q(t.Key)} = (SELECT MIN({Q(t.Key)}) FROM {table})) copied), {fallback})";
        string find = $"SELECT {Q(t.Key)} FROM {table} WHERE {Q(t.Hash)} = :hash";
        string delete = $"DELETE FROM {table} WHERE {Q(t.Key)} = :id";
        string personSql = "";
        if (t.Person is not null)
        {
            var userTable = database.Tables.First(x => x.TableName.EqualsIgnoreCase(t.PersonTable!));
            var userKey = userTable.Columns.First(c => c.Name.EqualsIgnoreCase(t.PersonKey!));
            personSql = $"SELECT MIN({Q(userKey)}) FROM {di.QuoteTable(userTable.SchemaName, userTable.DbTableName)}";
        }
        string key = Json(t.Key), hashJson = Json(t.Hash), person = t.Person is null ? "" : Json(t.Person), family = t.Family is null ? "" : Json(t.Family);
        string familyValue = t.Family is null ? "" : t.Family.IsIntegerColumn ? "random.randint(1_000_000, 2_000_000_000)" : t.Family.IsGuidColumn ? "str(uuid.uuid4())" : "uuid.uuid4().hex";

        var o = new StringBuilder();
        o.Append(FillAll(TableHead,
            ("§NAME", t.Table.TableName), ("§ROUTE", PythonNames.Str(main.Route)), ("§INSERT", PythonNames.Str(insert)), ("§FIND", PythonNames.Str(find)), ("§DELETE", PythonNames.Str(delete)),
            ("§PERSONSQL", personSql.Length == 0 ? "None" : PythonNames.Str(personSql)), ("§VALUES", string.Join(", ", values)),
            ("§PERSONNEEDED", t.Person is not null && !t.Person.IsNullable ? "True" : "False")));
        o.Append(FillAll(Cases, ("§NAME", t.Table.TableName), ("§KEY", key), ("§HASH", hashJson), ("§T_SPEND", TokenWords.SpendType), ("§T_ROLE", ProblemTypes.RoleNotAllowed),
            ("§SPENDUNSIGNED", t.Anonymous ? "" : "    assert client.post(ROUTE + \"/spend\", json={\"token\": new_token()}).status_code == 401\n")));
        if (t.Anonymous)
            o.Append(FillAll(AnonymousCase));
        if (t.Ends.Count >= 2)
            o.Append(FillAll(TwoEndsCase, ("§KEY", key), ("§USED", Json(t.Spent!)), ("§REVOKED", Json(t.Revoked!))));
        if (t.Family is not null)
            o.Append(FillAll(FamilyCase, ("§FAMILYVALUE", familyValue), ("§FAMILY", family)));
        if (t.Person is not null)
            o.Append(FillAll(PersonCase, ("§PERSON", person), ("§KEY", key)));
        return o.ToString();
    }

    private const string Head = """"
"""The session and token routes through the application: a call without a sign-in, the administrator's list and revoke, a token spent once, an expired or unknown or blank one, and the cases of a family, an anonymous spend and a person's tokens. Written by PY_TestTokens; regenerated with the tables."""
import hashlib
import random
import uuid
from datetime import datetime, timedelta

import pytest
from fastapi.testclient import TestClient
from sqlalchemy import text

# nothing of the application is imported here: the engine is made when app.db is first imported, which must be after the conftest chose the database
ADMIN_ROLE = §ADMINROLE


def new_token() -> str:
    return "test-" + uuid.uuid4().hex


def digest_of(token: str) -> str:
    return hashlib.sha256(token.encode("utf-8")).hexdigest()


def client_in(app, role):
    """A client signed in with the role (None: no sign-in at all)."""
    from app.auth import CurrentUser, require_user

    if role is None:
        app.dependency_overrides.pop(require_user, None)
    else:
        app.dependency_overrides[require_user] = lambda: CurrentUser(name="zz_tokens", role=role, owner_id=None)
    return TestClient(app)

"""";

    private const string TableHead = """"

# ===== §NAME =====
ROUTE = §ROUTE
INSERT_SQL = §INSERT
FIND_SQL = §FIND
DELETE_SQL = §DELETE
PERSON_SQL = §PERSONSQL
PERSON_NEEDED = §PERSONNEEDED


class Rows:
    """The rows a test added to the scratch database; removed after it."""

    def __init__(self, engine):
        self.engine = engine
        self.ids = []

    def person(self):
        if PERSON_SQL is None:
            return None
        with self.engine.connect() as connection:
            found = connection.execute(text(PERSON_SQL)).scalar()
        if found is None and PERSON_NEEDED:
            pytest.skip("the user table has no row to hold a token for")
        return found

    def add(self, token, hours=1, person=None, family=None, upper=False):
        person = person if person is not None else self.person()
        digest = digest_of(token).upper() if upper else digest_of(token)
        with self.engine.begin() as connection:
            connection.execute(text(INSERT_SQL), {§VALUES})
            found = connection.execute(text(FIND_SQL), {"hash": digest}).scalar()
        self.ids.append(found)
        return found

    def close(self):
        with self.engine.begin() as connection:
            for id in self.ids:
                connection.execute(text(DELETE_SQL), {"id": id})


@pytest.fixture
def api(application):
    app, engine = application
    rows = Rows(engine)
    yield app, rows
    rows.close()
    from app.auth import require_user

    app.dependency_overrides.pop(require_user, None)

"""";

    private const string Cases = """"

def test_§NAME_without_a_sign_in_the_routes_answer_401(api):
    app, _ = api
    client = client_in(app, None)
    assert client.get(ROUTE).status_code == 401
    assert client.post(ROUTE + "/1/revoke").status_code == 401
§SPENDUNSIGNED

def test_§NAME_a_role_that_is_not_the_administrators_can_neither_list_nor_revoke(api):
    app, _ = api
    client = client_in(app, "NoSuchRole")
    assert client.get(ROUTE).status_code == 403
    revoke = client.post(ROUTE + "/1/revoke")
    assert revoke.status_code == 403 and revoke.json()["type"].endswith("§T_ROLE")


def test_§NAME_a_token_is_spent_once(api):
    app, rows = api
    token = new_token()
    id = rows.add(token)
    client = client_in(app, ADMIN_ROLE)
    first = client.post(ROUTE + "/spend", json={"token": token})
    assert first.status_code == 200 and first.json()["id"] == id, first.text
    second = client.post(ROUTE + "/spend", json={"token": token})
    assert second.status_code == 400 and second.json()["type"].endswith("§T_SPEND"), second.text


def test_§NAME_an_expired_token_and_one_that_is_not_known_cannot_be_spent(api):
    app, rows = api
    token = new_token()
    rows.add(token, hours=-1)
    client = client_in(app, ADMIN_ROLE)
    assert client.post(ROUTE + "/spend", json={"token": token}).status_code == 400
    assert client.post(ROUTE + "/spend", json={"token": new_token()}).status_code == 400
    assert client.post(ROUTE + "/spend", json={"token": ""}).status_code == 400
    assert client.post(ROUTE + "/spend", json={}).status_code == 400


def test_§NAME_a_hash_stored_in_capitals_is_found(api):
    app, rows = api
    token = new_token()
    id = rows.add(token, upper=True)
    spent = client_in(app, ADMIN_ROLE).post(ROUTE + "/spend", json={"token": token})
    assert spent.status_code == 200 and spent.json()["id"] == id, spent.text


def test_§NAME_a_revoked_token_cannot_be_spent_and_the_list_never_shows_the_hash(api):
    app, rows = api
    token = new_token()
    id = rows.add(token)
    client = client_in(app, ADMIN_ROLE)
    revoked = client.post(f"{ROUTE}/{id}/revoke")
    assert revoked.status_code == 200 and revoked.json()["active"] is False, revoked.text
    assert client.post(f"{ROUTE}/{id}/revoke").status_code == 200, "revoking twice changes nothing more"
    assert client.post(ROUTE + "/spend", json={"token": token}).status_code == 400
    listed = client.get(ROUTE)
    assert listed.status_code == 200, listed.text
    assert any(row["§KEY"] == id for row in listed.json()), "the row is listed"
    assert not any("§HASH" in row for row in listed.json()), "the hash is never answered"
    assert client.post(ROUTE + "/2000000000/revoke").status_code == 404

"""";

    private const string TwoEndsCase = """"

def test_a_revoke_sets_the_revoked_column_and_a_spend_the_used_one(api):
    app, rows = api
    revoke_token, spend_token = new_token(), new_token()
    revoke_id = rows.add(revoke_token)
    spend_id = rows.add(spend_token)
    client = client_in(app, ADMIN_ROLE)
    assert client.post(f"{ROUTE}/{revoke_id}/revoke").status_code == 200
    assert client.post(ROUTE + "/spend", json={"token": spend_token}).status_code == 200
    listed = {row["§KEY"]: row for row in client.get(ROUTE).json()}
    assert bool(listed[revoke_id]["§REVOKED"]) and not bool(listed[revoke_id]["§USED"]), "a revoke sets the revoked column only"
    assert bool(listed[spend_id]["§USED"]) and not bool(listed[spend_id]["§REVOKED"]), "a spend sets the used column only"

"""";

    private const string AnonymousCase = """"

def test_a_token_is_spent_without_signing_in_and_a_wrong_one_is_refused_the_same_way(api):
    app, rows = api
    token = new_token()
    id = rows.add(token)
    client = client_in(app, None)
    assert client.post(ROUTE + "/spend", json={"token": new_token()}).status_code == 400
    first = client.post(ROUTE + "/spend", json={"token": token})
    assert first.status_code == 200 and first.json()["id"] == id, first.text
    assert client.post(ROUTE + "/spend", json={"token": token}).status_code == 400

"""";

    private const string FamilyCase = """"

def test_using_a_token_twice_ends_every_token_of_its_family(api):
    app, rows = api
    family = §FAMILYVALUE
    first, second = new_token(), new_token()
    rows.add(first, family=family)
    rows.add(second, family=family)
    client = client_in(app, ADMIN_ROLE)
    spent = client.post(ROUTE + "/spend", json={"token": first})
    assert spent.status_code == 200 and str(spent.json()["family"]) == str(family), spent.text
    assert client.post(ROUTE + "/spend", json={"token": first}).status_code == 400
    assert client.post(ROUTE + "/spend", json={"token": second}).status_code == 400, "the reuse ended the family"

"""";

    private const string PersonCase = """"

def test_revoking_a_person_ends_every_active_token_of_that_person(api):
    app, rows = api
    person = rows.person()
    first, second = new_token(), new_token()
    rows.add(first, person=person)
    rows.add(second, person=person)
    client = client_in(app, ADMIN_ROLE)
    revoked = client.post(f"{ROUTE}/revoke-person/{person}")
    assert revoked.status_code == 200 and revoked.json()["revoked"] >= 2, revoked.text
    assert client.post(ROUTE + "/spend", json={"token": first}).status_code == 400
    assert client.post(ROUTE + "/spend", json={"token": second}).status_code == 400
    mine = client.get(f"{ROUTE}?personId={person}")
    assert mine.status_code == 200 and all(row["§PERSON"] == person for row in mine.json()), mine.text

"""";
}
