using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> PY_Dashboard: the dashboard endpoint of the Python stack, with the widgets and statements every stack shares (DashboardPlan). </summary>
[TestClass]
public class PythonDashboardTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static DatabaseModel Database() => new() { DatabaseName = "Acme", SchemaName = "dbo", Tables = [Sample.Holiday(), Sample.NaturalKey()] };

    private static async Task<string> Run(string template, DatabaseModel database, params (string, string)[] values)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), database, Project(values));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    [TestMethod]
    public async Task The_endpoint_answers_the_plans_widgets_with_their_statements_and_keeps_the_answer_for_a_minute()
    {
        var database = Database();
        var plan = DashboardPlan.Build(database, Project());
        Assert.IsNotEmpty(plan.Widgets);

        string py = await Run("PY_Dashboard_v1.tt", database);

        Expect.Contains(py, "@@@FILE app/routes/dashboard.py@@@");
        Expect.Contains(py, "@router.get(\"/api/dashboard\")");
        Expect.Contains(py, "def dashboard(table: str | None = Query(None), session: Session = Depends(get_session)):");
        foreach (var widget in plan.Widgets)
        {
            Expect.Contains(py, PythonNames.Str(widget.Id));
            Expect.Contains(py, PythonNames.Str(widget.Sql.Replace(":", "\\:")));
        }
        Expect.Contains(py, "CACHE_FOR = 60.0");
        Expect.Contains(py, "\"isMoney\": d.is_money,");
        Expect.Contains(py, "# a table that has no strip is not cached, so a caller cannot grow the cache with names of its own");
        Expect.DoesNotContain(py, "app.auth");
        Expect.DoesNotContain(py, "READ_RULES");
    }

    [TestMethod]
    public async Task A_role_that_may_not_read_a_table_gets_a_copy_without_its_widgets()
    {
        string py = await Run("PY_Dashboard_v1.tt", Database(), ("ApiAuth", "true"), ("TableReadRoles", "Holiday:Hr"));

        Expect.Contains(py, "from app.auth import CurrentUser, require_user");
        Expect.Contains(py, "\"holiday\": (\"Administrator\", \"Hr\"),");
        Expect.DoesNotContain(py, "\"country\":");
        Expect.Contains(py, "user: CurrentUser = Depends(require_user)");
        Expect.Contains(py, "widgets = [w for w in load(session, table) if may_read(user, w[\"table\"])]");
        Expect.Contains(py, "return roles is None or user.role in roles");
    }

    [TestMethod]
    public async Task The_module_lists_the_dashboard_router_only_when_the_project_has_a_dashboard()
    {
        string with = await Run("PY_Mod_v1.tt", Database());
        Expect.Contains(with, "from app.routes import dashboard");
        Expect.Contains(with, "    dashboard.router,");

        string without = await Run("PY_Mod_v1.tt", Database(), ("Dashboard", "false"));
        Expect.DoesNotContain(without, "dashboard");
    }

    [TestMethod]
    public void The_dashboard_flag_adds_the_python_template_to_the_plan_and_off_takes_it_away()
    {
        CollectionAssert.Contains(Project().ImpliedPlanTemplates.ToArray(), "PY_Dashboard");
        CollectionAssert.DoesNotContain(Project(("Dashboard", "false")).ImpliedPlanTemplates.ToArray(), "PY_Dashboard");
    }
}
