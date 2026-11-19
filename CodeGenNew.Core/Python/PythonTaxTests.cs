using System.Text;
using CodeGenNew.Core.Naming;
using CodeGenNew.Core.Sql;
using CodeGenNew.Core.Tax;

namespace CodeGenNew.Core.Python;

/// <summary> <c>tests/test_tax.py</c> of the Python API (PY_TestTax), beside the <c>conftest.py</c> the approval tests share: the cases add categories and lines through the routes and remove them after the
/// test. They play the copied rate and the tax, the rate kept when the category's rate changes, an unknown category, and with the validity and document parts the validity and the subtotals. The words are
/// <see cref="TaxWords"/>. </summary>
public static class PythonTaxTests
{
    /// <summary> Whether the tests are written: <c>PythonTests</c>, a line table the tests can fill, over a database the shared conftest can open (not SQL Server). </summary>
    public static bool Applies(DatabaseModel database, ProjectSettings project) =>
        project.PythonTests && DialectInfo.For(database.Dialect).RunsPythonTests && TaxTestPlan.For(database, project) is not null;

    public static string Conftest(ProjectSettings project, SqlDialect dialect) => PythonApprovalTests.Conftest(project, dialect);

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var plan = TaxTestPlan.For(database, project)!;
        var s = plan.Shape;
        string Json(ColumnModel c) => JsonNames.Camel(c.Name);
        string Q(string text) => PythonNames.Str(text);
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');
        string auth = project.ApiAuth ? "from app.auth import CurrentUser, require_user\n" : "";

        L("\"\"\"The tax lines through the routes: a line carries the rate of its category and the tax worked out from it, keeps the rate when the category's rate changes, and refuses a category that does not exist"
            + (s.ValidFrom is null ? "" : " or is not in force") + (s.Document is null ? "" : "; the subtotals of a document add up by category and rate") + ". Written by PY_TestTax; regenerated with the tables.\"\"\"");
        L("import json");
        L("import uuid");
        L();
        L("import pytest");
        L("from fastapi.testclient import TestClient");
        L();
        if (auth.Length > 0)
            L(auth.TrimEnd('\n'));
        L($"LINES = {Q(PythonTable.Of(s.Table, project).Route)}");
        L($"CATEGORIES = {Q(PythonTable.Of(s.CategoryTable, project).Route)}");
        L($"CATEGORY = json.loads({Q(plan.CategoryBody)})");
        L($"LINE = json.loads({Q(plan.LineBody)})");
        L($"UNKNOWN = \"{TaxWords.UnknownType}\"");
        if (s.ValidFrom is not null)
            L($"NOT_IN_FORCE = \"{TaxWords.NotInForceType}\"");
        L();
        L();
        L("class Rows:");
        L("    \"\"\"Adds categories and lines and remembers them, so the test can take them out again.\"\"\"");
        L();
        L("    def __init__(self, client):");
        L("        self.client = client");
        L("        self.lines = []");
        L("        self.categories = []");
        if (s.Document is not null)
            L("        self.document = 800000 + uuid.uuid4().int % 100000");
        L();
        L("    def category(self, rate, valid_from=None, valid_to=None):");
        L($"        body = {{**CATEGORY, \"{Json(s.CategoryRate)}\": rate}}");
        if (s.ValidFrom is not null)
        {
            L($"        body[\"{Json(s.ValidFrom)}\"] = valid_from or \"{TaxTestPlan.Day(s.ValidFrom, "2000-01-01")}\"");
            if (s.ValidTo is not null)
                L($"        body[\"{Json(s.ValidTo)}\"] = valid_to");
        }
        foreach (var c in TaxTestPlan.UniqueTextColumns(s))
            L($"        body[\"{Json(c)}\"] = uuid.uuid4().hex[:{(c.CharacterLength > 0 ? Math.Min(8, c.CharacterLength) : 8)}]");
        L("        made = self.client.post(CATEGORIES, json=body)");
        L("        assert made.status_code == 201, made.text");
        L($"        key = made.json()[\"{Json(s.CategoryKey)}\"]");
        L("        self.categories.append(key)");
        L("        return key");
        L();
        L("    def line(self, category, total, day=None):");
        L($"        body = {{**LINE, \"{Json(s.Category)}\": category, \"{Json(s.Taxable)}\": total, \"{Json(s.Rate)}\": 99, \"{Json(s.Tax)}\": 1}}");
        if (s.Day is not null)
            L($"        if day:\n            body[\"{Json(s.Day)}\"] = day\n        else:\n            body[\"{Json(s.Day)}\"] = \"{TaxTestPlan.Day(s.Day, "2024-06-01")}\"");
        if (s.Document is not null)
            L($"        body[\"{Json(s.Document)}\"] = self.document");
        L("        made = self.client.post(LINES, json=body)");
        L("        if made.status_code == 201:");
        L($"            self.lines.append(made.json()[\"{Json(s.Key)}\"])");
        L("        return made");
        L();
        L("    def read(self, key):");
        L("        return self.client.get(f\"{LINES}/{key}\").json()");
        L();
        L("    def remove(self):");
        L("        for key in self.lines:");
        L("            self.client.delete(f\"{LINES}/{key}\")");
        L("        for key in self.categories:");
        L("            self.client.delete(f\"{CATEGORIES}/{key}\")");
        L();
        L();
        L("@pytest.fixture");
        L("def rows(application):");
        L("    \"\"\"A client over a scratch database; the rows a test adds are removed after it.\"\"\"");
        L("    app, _ = application");
        if (project.ApiAuth)
            L("    app.dependency_overrides[require_user] = lambda: CurrentUser(name=\"zz_tax\", role=None, owner_id=None)");
        L("    made = Rows(TestClient(app))");
        L("    yield made");
        L("    made.remove()");
        if (project.ApiAuth)
            L("    app.dependency_overrides.pop(require_user, None)");
        L();
        L();
        L("def test_a_line_carries_the_rate_of_its_category_and_the_tax_worked_out_from_it(rows):");
        L("    category = rows.category(0.2)");
        L("    made = rows.line(category, 50)");
        L("    assert made.status_code == 201, made.text");
        L("    row = rows.read(rows.lines[-1])");
        L($"    assert float(row[\"{Json(s.Rate)}\"]) == 0.2, \"the rate is the category's, not the one sent\"");
        L($"    assert float(row[\"{Json(s.Tax)}\"]) == 10, \"the tax is the taxable amount times the rate over 100\"");
        L();
        L();
        L("def test_a_line_keeps_its_rate_when_the_rate_of_its_category_changes(rows):");
        L("    category = rows.category(0.2)");
        L("    assert rows.line(category, 100).status_code == 201");
        L("    key = rows.lines[-1]");
        L($"    stored = rows.client.get(f\"{{CATEGORIES}}/{{category}}\").json()");
        L($"    assert rows.client.put(f\"{{CATEGORIES}}/{{category}}\", json={{**stored, \"{Json(s.CategoryRate)}\": 0.25}}).status_code in (200, 204)");
        L("    line = rows.read(key)");
        L($"    assert rows.client.put(f\"{{LINES}}/{{key}}\", json={{**line, \"{Json(s.Taxable)}\": 200}}).status_code in (200, 204)");
        L("    line = rows.read(key)");
        L($"    assert float(line[\"{Json(s.Rate)}\"]) == 0.2, \"the rate the line was written with stays\"");
        L($"    assert float(line[\"{Json(s.Tax)}\"]) == 40");
        L("    assert rows.line(category, 100).status_code == 201");
        L($"    assert float(rows.read(rows.lines[-1])[\"{Json(s.Rate)}\"]) == 0.25, \"a new line takes the category's new rate\"");
        L();
        L();
        L("def test_a_line_with_a_category_that_does_not_exist_is_refused(rows):");
        L("    refused = rows.line(2000000000, 10)");
        L("    assert refused.status_code == 400 and UNKNOWN in refused.text, refused.text");
        L("    assert rows.lines == []");
        if (s.ValidFrom is not null && s.Day is not null)
        {
            L();
            L();
            L("def test_a_category_outside_its_validity_is_refused(rows):");
            L($"    category = rows.category(0.2, \"{TaxTestPlan.Day(s.ValidFrom, "2024-01-01")}\", " + (s.ValidTo is null ? "None" : $"\"{TaxTestPlan.Day(s.ValidTo, "2024-12-31")}\"") + ")");
            L($"    assert rows.line(category, 10, \"{TaxTestPlan.Day(s.Day, "2024-06-01")}\").status_code == 201");
            L($"    before = rows.line(category, 10, \"{TaxTestPlan.Day(s.Day, "2023-12-31")}\")");
            L("    assert before.status_code == 400 and NOT_IN_FORCE in before.text, before.text");
            if (s.ValidTo is not null)
            {
                L($"    after = rows.line(category, 10, \"{TaxTestPlan.Day(s.Day, "2025-01-01")}\")");
                L("    assert after.status_code == 400 and NOT_IN_FORCE in after.text, after.text");
            }
        }
        if (s.Document is not null)
        {
            L();
            L();
            L("def test_the_subtotals_of_a_document_add_up_by_category_and_rate(rows):");
            L("    twenty, five = rows.category(0.2), rows.category(0.05)");
            L("    for category, amount in ((twenty, 100), (twenty, 50), (five, 200)):");
            L("        assert rows.line(category, amount).status_code == 201");
            L("    answer = rows.client.get(LINES + \"/tax-subtotals\", params={\"document\": rows.document})");
            L("    assert answer.status_code == 200, answer.text");
            L("    by = {row[\"category\"]: row for row in answer.json()}");
            L("    assert len(by) == 2");
            L("    assert float(by[twenty][\"taxable\"]) == 150 and float(by[twenty][\"tax\"]) == 30 and by[twenty][\"lines\"] == 2");
            L("    assert float(by[five][\"tax\"]) == 10");
            L("    assert rows.client.get(LINES + \"/tax-subtotals\").status_code == 400");
        }
        return o.ToString();
    }
}
