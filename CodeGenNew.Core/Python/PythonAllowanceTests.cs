using System.Text;
using CodeGenNew.Core.Allowance;
using CodeGenNew.Core.Naming;
using CodeGenNew.Core.Sql;

namespace CodeGenNew.Core.Python;

/// <summary> <c>tests/test_allowance.py</c> of the Python API (PY_TestAllowance), beside the <c>conftest.py</c> the other tests share: the cases add rows through the routes and remove them after the test. They play
/// the amount worked out from the base and the percentage, the percentage from the base and the amount, the three refusals and, with the document part, the totals. The words are <see cref="AllowanceChargeWords"/>. </summary>
public static class PythonAllowanceTests
{
    /// <summary> Whether the tests are written: <c>PythonTests</c>, a table the tests can fill, over a database the shared conftest can open (not SQL Server). </summary>
    public static bool Applies(DatabaseModel database, ProjectSettings project) =>
        project.PythonTests && DialectInfo.For(database.Dialect).RunsPythonTests && AllowanceTestPlan.For(database, project) is not null;

    public static string Conftest(ProjectSettings project, SqlDialect dialect) => PythonApprovalTests.Conftest(project, dialect);

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var plan = AllowanceTestPlan.For(database, project)!;
        var s = plan.Shape;
        string Json(ColumnModel c) => JsonNames.Camel(c.Name);
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');
        string key = Json(s.Table.PrimaryKeyColumns[0]);

        L("\"\"\"The allowances and charges through the routes: the amount is worked out from the base and the percentage (or the percentage from the amount), a negative value, a percentage with no base and a row with"
            + " nothing to work from are refused" + (s.Document is null ? "" : "; the totals of a document add up") + ". Written by PY_TestAllowance; regenerated with the tables.\"\"\"");
        L("import json");
        L("import uuid");
        L();
        L("import pytest");
        L("from fastapi.testclient import TestClient");
        L();
        if (project.ApiAuth)
        {
            L("from app.auth import CurrentUser, require_user");
            L();
        }
        L($"ROUTE = {PythonNames.Str(PythonTable.Of(s.Table, project).Route)}");
        L($"ROW = json.loads({PythonNames.Str(plan.Body)})");
        L($"INVALID = \"{AllowanceChargeWords.InvalidType}\"");
        L();
        L();
        L("class Rows:");
        L("    \"\"\"Adds rows and remembers them, so the test can take them out again.\"\"\"");
        L();
        L("    def __init__(self, client):");
        L("        self.client = client");
        L("        self.rows = []");
        if (s.Document is not null)
            L("        self.document = 800000 + uuid.uuid4().int % 100000");
        L();
        L("    def add(self, charge, base, percentage, amount" + (s.Line is null ? "" : ", line=1") + "):");
        L($"        body = {{**ROW, \"{Json(s.Flag)}\": charge, \"{Json(s.Base)}\": base, \"{Json(s.Percent)}\": percentage, \"{Json(s.Amount)}\": amount}}");
        if (s.Document is not null)
            L($"        body[\"{Json(s.Document)}\"] = self.document");
        if (s.Line is not null)
            L($"        body[\"{Json(s.Line)}\"] = line");
        L("        made = self.client.post(ROUTE, json=body)");
        L("        if made.status_code == 201:");
        L($"            self.rows.append(made.json()[\"{key}\"])");
        L("        return made");
        L();
        L("    def read(self, key):");
        L("        return self.client.get(f\"{ROUTE}/{key}\").json()");
        L();
        L("    def remove(self):");
        L("        for key in self.rows:");
        L("            self.client.delete(f\"{ROUTE}/{key}\")");
        L();
        L();
        L("@pytest.fixture");
        L("def rows(application):");
        L("    \"\"\"A client over a scratch database; the rows a test adds are removed after it.\"\"\"");
        L("    app, _ = application");
        if (project.ApiAuth)
            L("    app.dependency_overrides[require_user] = lambda: CurrentUser(name=\"zz_allowance\", role=None, owner_id=None)");
        L("    made = Rows(TestClient(app))");
        L("    yield made");
        L("    made.remove()");
        if (project.ApiAuth)
            L("    app.dependency_overrides.pop(require_user, None)");
        L();
        L();
        L("def test_the_amount_is_worked_out_from_the_base_and_the_percentage(rows):");
        L("    made = rows.add(False, 200, 10, 1)");
        L("    assert made.status_code == 201, made.text");
        L($"    assert abs(float(rows.read(rows.rows[-1])[\"{Json(s.Amount)}\"]) - 20) < 0.001, \"the amount sent is ignored when a percentage is given\"");
        L();
        L();
        L("def test_the_percentage_is_worked_out_from_the_base_and_the_amount(rows):");
        L("    made = rows.add(True, 200, 0, 50)");
        L("    assert made.status_code == 201, made.text");
        L("    row = rows.read(rows.rows[-1])");
        L($"    assert abs(float(row[\"{Json(s.Percent)}\"]) - 25) < 0.001 and abs(float(row[\"{Json(s.Amount)}\"]) - 50) < 0.001");
        L($"    assert row[\"{Json(s.Flag)}\"] is True, \"the amount stays positive, the flag says it is a charge\"");
        foreach (var (name, args) in new[] { ("a_negative_value_is_refused", "False, -5, 10, 1"), ("a_percentage_without_a_base_is_refused", "False, 0, 10, 0"), ("a_row_with_nothing_to_work_an_amount_from_is_refused", "False, 0, 0, 0") })
        {
            L();
            L();
            L($"def test_{name}(rows):");
            L($"    refused = rows.add({args})");
            L("    assert refused.status_code == 400 and INVALID in refused.text, refused.text");
            L("    assert rows.rows == []");
        }
        if (s.Document is not null)
        {
            L();
            L();
            L("def test_the_totals_of_a_document_add_up_the_allowances_and_the_charges(rows):");
            L("    for charge, base, percentage, amount in ((False, 200, 10, 0), (False, 0, 0, 5), (True, 0, 0, 60)):");
            L("        assert rows.add(charge, base, percentage, amount).status_code == 201");
            L($"    answer = rows.client.get(ROUTE + \"/{AllowanceChargeWords.TotalsRoute}\", params={{\"document\": rows.document}})");
            L("    assert answer.status_code == 200, answer.text");
            L("    totals = answer.json()");
            L("    assert (abs(float(totals[\"allowances\"]) - 25) < 0.001 and abs(float(totals[\"charges\"]) - 60) < 0.001 and abs(float(totals[\"net\"]) - 35) < 0.001 and totals[\"rows\"] == 3), totals");
            L($"    assert rows.client.get(ROUTE + \"/{AllowanceChargeWords.TotalsRoute}\").status_code == 400");
            if (s.Line is not null)
            {
                L();
                L();
                L("def test_the_totals_of_one_line_count_only_the_rows_of_that_line(rows):");
                L("    assert rows.add(False, 200, 10, 0, line=1).status_code == 201");
                L("    assert rows.add(True, 0, 0, 60, line=2).status_code == 201");
                L($"    answer = rows.client.get(ROUTE + \"/{AllowanceChargeWords.TotalsRoute}\", params={{\"document\": rows.document, \"line\": 1}})");
                L("    assert answer.status_code == 200, answer.text");
                L("    totals = answer.json()");
                L("    assert abs(float(totals[\"allowances\"]) - 20) < 0.001 and float(totals[\"charges\"]) == 0 and totals[\"rows\"] == 1, totals");
            }
        }
        return o.ToString();
    }
}
