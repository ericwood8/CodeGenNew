using System.Text;
using CodeGenNew.Core.Naming;
using CodeGenNew.Core.Sql;
using CodeGenNew.Core.Totals;

namespace CodeGenNew.Core.Python;

/// <summary> <c>tests/test_totals.py</c> of the Python API (PY_TestTotals), beside the <c>conftest.py</c> the other tests share: the cases add a header and lines through the routes and remove them after the test. They
/// play the totals a client writes being replaced, the lines total following every line added or removed, the payable amount following the prepaid amount and the check route finding nothing wrong. The words are
/// <see cref="DocumentTotalsWords"/>. </summary>
public static class PythonTotalsTests
{
    /// <summary> Whether the tests are written: <c>PythonTests</c>, a header and a line table the tests can fill, over a database the shared conftest can open (not SQL Server). </summary>
    public static bool Applies(DatabaseModel database, ProjectSettings project) =>
        project.PythonTests && DialectInfo.For(database.Dialect).RunsPythonTests && DocumentTotalsTestPlan.For(database, project) is not null;

    public static string Conftest(ProjectSettings project, SqlDialect dialect) => PythonApprovalTests.Conftest(project, dialect);

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var plan = DocumentTotalsTestPlan.For(database, project)!;
        var s = plan.Shape;
        string Json(ColumnModel c) => JsonNames.Camel(c.Name);
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');
        string headerKey = Json(s.Key), lineKey = Json(s.Lines.PrimaryKeyColumns[0]), lines = Json(s.Columns.Lines!), amount = Json(s.LineAmount);
        var owned = s.Columns.Owned().Select(p => Json(p.Column)).ToList();

        L("\"\"\"The document totals through the routes: the totals a client writes are replaced by the ones worked out from the rows, the lines total follows every line added or removed"
            + (plan.TestsPrepaid ? ", the payable amount follows the prepaid amount" : "") + " and the check route finds nothing wrong. Written by PY_TestTotals; regenerated with the tables.\"\"\"");
        L("import json");
        L();
        L("import pytest");
        L("from fastapi.testclient import TestClient");
        L();
        if (project.ApiAuth)
        {
            L("from app.auth import CurrentUser, require_user");
            L();
        }
        L($"HEADERS = {PythonNames.Str(PythonTable.Of(s.Header, project).Route)}");
        L($"LINES = {PythonNames.Str(PythonTable.Of(s.Lines, project).Route)}");
        L($"HEADER = json.loads({PythonNames.Str(plan.HeaderBody)})");
        L($"LINE = json.loads({PythonNames.Str(plan.LineBody)})");
        L($"OWNED = [{string.Join(", ", owned.Select(PythonNames.Str))}]");
        L();
        L();
        L("class Rows:");
        L("    \"\"\"Adds headers and lines and remembers them, so the test can take them out again.\"\"\"");
        L();
        L("    def __init__(self, client):");
        L("        self.client = client");
        L("        self.headers = []");
        L("        self.lines = []");
        L();
        L("    def header(self, **values):");
        L("        made = self.client.post(HEADERS, json={**HEADER, **values})");
        L("        assert made.status_code == 201, made.text");
        L($"        key = made.json()[\"{headerKey}\"]");
        L("        self.headers.append(key)");
        L("        return key");
        L();
        L("    def line(self, header, amount):");
        L($"        made = self.client.post(LINES, json={{**LINE, \"{Json(s.LineKey)}\": header, \"{amount}\": amount}})");
        L("        assert made.status_code == 201, made.text");
        L($"        key = made.json()[\"{lineKey}\"]");
        L("        self.lines.append(key)");
        L("        return key");
        L();
        L("    def read(self, header):");
        L("        return self.client.get(f\"{HEADERS}/{header}\").json()");
        L();
        L("    def remove(self):");
        L("        for key in self.lines:");
        L("            self.client.delete(f\"{LINES}/{key}\")");
        L("        for key in self.headers:");
        L("            self.client.delete(f\"{HEADERS}/{key}\")");
        L();
        L();
        L("@pytest.fixture");
        L("def rows(application):");
        L("    \"\"\"A client over a scratch database; the rows a test adds are removed after it.\"\"\"");
        L("    app, _ = application");
        if (project.ApiAuth)
            L("    app.dependency_overrides[require_user] = lambda: CurrentUser(name=\"zz_totals\", role=None, owner_id=None)");
        L("    made = Rows(TestClient(app))");
        L("    yield made");
        L("    made.remove()");
        if (project.ApiAuth)
            L("    app.dependency_overrides.pop(require_user, None)");
        L();
        L();
        L("def test_the_totals_a_client_writes_are_replaced_by_the_worked_out_ones(rows):");
        L("    key = rows.header(**{name: 999 for name in OWNED})");
        L("    row = rows.read(key)");
        L("    for name in OWNED:");
        L("        assert abs(float(row[name])) < 0.001, f\"{name} is worked out from the rows, not taken from the client\"");
        L();
        L();
        L("def test_the_lines_total_follows_every_line_added_and_removed(rows):");
        L("    key = rows.header()");
        L("    rows.line(key, 10)");
        L("    second = rows.line(key, 5.5)");
        L($"    assert abs(float(rows.read(key)[\"{lines}\"]) - 15.5) < 0.001");
        L($"    assert rows.client.delete(f\"{{LINES}}/{{second}}\").status_code in (200, 204)");
        L($"    assert abs(float(rows.read(key)[\"{lines}\"]) - 10) < 0.001");
        if (plan.TestsPrepaid)
        {
            string prepaid = Json(s.Columns.Prepaid!), payable = Json(s.Columns.Payable!);
            L();
            L();
            L("def test_the_payable_amount_follows_the_prepaid_amount(rows):");
            L("    key = rows.header()");
            L("    rows.line(key, 10)");
            L("    row = rows.read(key)");
            L($"    before = float(row[\"{payable}\"])");
            L($"    row[\"{prepaid}\"] = 5");
            L("    saved = rows.client.put(f\"{HEADERS}/{key}\", json=row)");
            L("    assert saved.status_code in (200, 204), saved.text");
            L($"    assert abs(float(rows.read(key)[\"{payable}\"]) - (before - 5)) < 0.001");
        }
        L();
        L();
        L("def test_the_check_route_finds_nothing_wrong(rows):");
        L("    key = rows.header()");
        L("    rows.line(key, 10)");
        L($"    answer = rows.client.get(HEADERS + \"/{DocumentTotalsWords.CheckRoute}\", params={{\"id\": key}})");
        L("    assert answer.status_code == 200, answer.text");
        L("    assert answer.json()[\"checked\"] == 1 and answer.json()[\"differing\"] == 0, answer.json()");
        return o.ToString();
    }
}
