using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> WriteRoles and TableWriteRoles (item 110): the rule in Core, the REST routes, the OData routes and the Angular screens that hide the write buttons. </summary>
[TestClass]
public class WriteRolesTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static readonly (string, string) Api = ("ApiAuth", "true");

    private static async Task<string> Run(string template, object model, params (string, string)[] values)
    {
        var project = Project(values);
        var result = model switch
        {
            TableModel table => await Repo.Cache.RunAsync(Repo.Template(template), table, project),
            DatabaseModel database => await Repo.Cache.RunAsync(Repo.Template(template), database, project),
            _ => await Repo.Cache.RunAsync(Repo.Template(template), project)
        };
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    [TestMethod]
    public void The_rule_lists_the_roles_with_the_administrator_first_and_only_with_ApiAuth()
    {
        CollectionAssert.AreEqual(Array.Empty<string>(), WriteRolePlan.For(Project(Api), "Holiday").ToArray());
        CollectionAssert.AreEqual(Array.Empty<string>(), WriteRolePlan.For(Project(("WriteRoles", "Manager")), "Holiday").ToArray());
        CollectionAssert.AreEqual(new[] { "Administrator", "Manager" }, WriteRolePlan.For(Project(Api, ("WriteRoles", "Manager")), "Holiday").ToArray());
        CollectionAssert.AreEqual(new[] { "Chief", "Manager" }, WriteRolePlan.For(Project(Api, ("AuthAdminRole", "Chief"), ("WriteRoles", " Manager , chief ")), "Holiday").ToArray(), "a role the administrator role repeats is listed once");
    }

    [TestMethod]
    public void A_table_entry_replaces_the_project_wide_roles_and_an_empty_entry_lets_everyone_write()
    {
        var project = Project(Api, ("WriteRoles", "Manager"), ("TableWriteRoles", "Holiday:Hr,Payroll; country : ;LeaveType:Administrator"));

        CollectionAssert.AreEqual(new[] { "Administrator", "Hr", "Payroll" }, WriteRolePlan.For(project, "holiday").ToArray());
        CollectionAssert.AreEqual(Array.Empty<string>(), WriteRolePlan.For(project, "Country").ToArray(), "Table: alone means any signed-in person");
        CollectionAssert.AreEqual(new[] { "Administrator" }, WriteRolePlan.For(project, "LeaveType").ToArray());
        CollectionAssert.AreEqual(new[] { "Administrator", "Manager" }, WriteRolePlan.For(project, "Department").ToArray(), "a table with no entry uses WriteRoles");
    }

    [TestMethod]
    public void The_literal_quotes_each_role_for_the_language()
    {
        Assert.AreEqual("\"Administrator\", \"Manager\"", WriteRolePlan.Literal(["Administrator", "Manager"]));
        Assert.AreEqual("'Administrator', 'O\\'Neil'", WriteRolePlan.Literal(["Administrator", "O'Neil"], '\''));
    }

    [TestMethod]
    public async Task A_table_with_a_rule_names_its_roles_in_its_API_class_and_other_projects_get_nothing()
    {
        string api = await Run("API_Crud_v1.tt", Sample.Holiday(), Api, ("WriteRoles", "Manager"));
        Expect.Contains(api, "protected override IReadOnlyList<string> WriteRoles => [\"Administrator\", \"Manager\"];");

        string table = await Run("API_Crud_v1.tt", Sample.Holiday(), Api, ("WriteRoles", "Manager"), ("TableWriteRoles", "Holiday:Hr"));
        Expect.Contains(table, "WriteRoles => [\"Administrator\", \"Hr\"];");

        Expect.DoesNotContain(await Run("API_Crud_v1.tt", Sample.Holiday(), Api), "WriteRoles");
        Expect.DoesNotContain(await Run("API_Crud_v1.tt", Sample.Holiday(), ("WriteRoles", "Manager")), "WriteRoles");
        Expect.DoesNotContain(await Run("API_Crud_v1.tt", Sample.Holiday(), Api, ("WriteRoles", "Manager"), ("TableWriteRoles", "Holiday:")), "WriteRoles");
    }

    [TestMethod]
    public async Task The_REST_base_refuses_a_write_from_another_role_on_create_update_delete_and_clone_only()
    {
        string withAuth = await Run("API_EssentialCrudApi_v1.tt", new object(), Api);

        Expect.Contains(withAuth, "protected virtual IReadOnlyList<string> WriteRoles => [];");
        Assert.AreEqual(7, withAuth.Split(".RequireWriteRoles(WriteRoles)").Length - 1, "create, update, delete and clone of CrudApi, create, update and delete of NameActiveCrudApi");
        foreach (string read in new[] { "Get{plural}\")", "Get{singular}ById\")", "Get{plural}Lookup\")", "Get{plural}ByName\")" })
            Expect.DoesNotContain(CutAround(withAuth, "WithName($\"" + read), ".RequireWriteRoles");

        string without = await Run("API_EssentialCrudApi_v1.tt", new object());
        Expect.DoesNotContain(without, "RequireWriteRoles");
        Expect.DoesNotContain(without, "WriteRoles");
    }

    private static string CutAround(string text, string anchor)
    {
        int at = text.IndexOf(anchor, StringComparison.Ordinal);
        Assert.IsTrue(at >= 0, "missing " + anchor);
        int end = text.IndexOf(';', at);
        return text.Substring(at, end - at);
    }

    [TestMethod]
    public async Task The_sign_in_essentials_write_the_gate_and_the_problem_type()
    {
        string jwt = await Run("API_EssentialJwt_v1.tt", new object(), Api);

        Expect.Contains(jwt, "public static class RoleGate");
        Expect.Contains(jwt, "public static bool Allows(ClaimsPrincipal caller, IReadOnlyList<string> roles) => roles.Count == 0 || roles.Any(caller.IsInRole);");
        Expect.Contains(jwt, "type: \"" + ProblemTypes.RoleNotAllowed + "\"");
        Expect.Contains(jwt, "statusCode: 403");
        Expect.Contains(jwt, "public static TBuilder RequireWriteRoles<TBuilder>(this TBuilder builder, IReadOnlyList<string> roles)");
    }

    [TestMethod]
    public async Task The_OData_write_routes_refuse_another_role_and_the_reads_do_not()
    {
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [Sample.Holiday(), Sample.NaturalKey()] };
        string odata = await Run("API_OData_v1.tt", database, ("OData", "true"), Api, ("ContextName", "AcmeContext"), ("TableWriteRoles", "Holiday:Hr"));

        Expect.Contains(odata, "public virtual IReadOnlyList<string> WriteRoles => [];");
        Expect.Contains(odata, "public sealed class HolidayODataRules : ODataRules<Holiday, AcmeContext>");
        Expect.Contains(odata, "public override IReadOnlyList<string> WriteRoles => [\"Administrator\", \"Hr\"];");
        Expect.DoesNotContain(odata, "CountryODataRules");
        Assert.AreEqual(4, odata.Split(".RequireWriteRoles(rules.WriteRoles)").Length - 1, "post, put, patch and delete");
        Expect.Contains(odata, "}).WithODataPathFactory(pathOf).RequireWriteRoles(rules.WriteRoles);");

        string open = await Run("API_OData_v1.tt", database, ("OData", "true"), ("TableWriteRoles", "Holiday:Hr"));
        Expect.DoesNotContain(open, "RequireWriteRoles");
        Expect.DoesNotContain(open, "override IReadOnlyList<string> WriteRoles");
    }

    [TestMethod]
    public async Task The_dashboard_endpoint_leaves_out_the_widgets_of_tables_the_role_may_not_read()
    {
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [Sample.Holiday(), Sample.NaturalKey()] };
        string dashboard = await Run("API_Dashboard_v1.tt", database, ("Dashboard", "true"), Api, ("ContextName", "AcmeContext"), ("TableReadRoles", "Holiday:Hr"));

        Expect.Contains(dashboard, "using System.Security.Claims;");
        Expect.Contains(dashboard, "[\"Holiday\"] = [\"Administrator\", \"Hr\"],");
        Expect.DoesNotContain(dashboard, "[\"Country\"]");
        Expect.Contains(dashboard, "RoleGate.Allows(caller, roles)");

        string open = await Run("API_Dashboard_v1.tt", database, ("Dashboard", "true"), Api, ("ContextName", "AcmeContext"));
        Expect.DoesNotContain(open, "ReadRules");
        Expect.DoesNotContain(open, "ClaimsPrincipal");
    }

    [TestMethod]
    public async Task The_Rust_dashboard_leaves_out_the_widgets_of_tables_the_role_may_not_read()
    {
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [Sample.Holiday(), Sample.NaturalKey()] };
        string dashboard = await Run("RS_Dashboard_v1.tt", database, ("Dashboard", "true"), Api, ("TableReadRoles", "Holiday:Hr"));

        Expect.Contains(dashboard, "use crate::auth::CurrentUser;");
        Expect.Contains(dashboard, "(\"Holiday\", &[\"Administrator\", \"Hr\"]),");
        Expect.DoesNotContain(dashboard, "(\"Country\"");
        Expect.Contains(dashboard, "result.widgets.retain(|w| may_read(&user, &w.table));");

        string open = await Run("RS_Dashboard_v1.tt", database, ("Dashboard", "true"), Api);
        Expect.DoesNotContain(open, "READ_RULES");
        Expect.DoesNotContain(open, "CurrentUser");
    }

    [TestMethod]
    public async Task Grouping_with_apply_is_refused_as_a_bad_request_because_the_package_cannot_write_the_answer()
    {
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [Sample.Holiday()] };
        string odata = await Run("API_OData_v1.tt", database, ("OData", "true"));

        Expect.Contains(odata, "validation.AllowedQueryOptions = AllowedQueryOptions.Supported & ~AllowedQueryOptions.Apply;");
    }

    [TestMethod]
    public async Task The_role_gate_runs_before_the_validator_on_a_write()
    {
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [Sample.Holiday()] };
        string odata = await Run("API_OData_v1.tt", database, ("OData", "true"), Api, ("ApiValidation", "true"), ("WriteRoles", "Manager"));

        Expect.Contains(odata, ".RequireWriteRoles(rules.WriteRoles).AddEndpointFilter<ValidationFilter<T>>()");

        string rest = await Run("API_EssentialCrudApi_v1.tt", new object(), Api, ("ApiValidation", "true"));
        Expect.Contains(rest, "        .RequireWriteRoles(WriteRoles)\n        .AddEndpointFilter<ValidationFilter<TEntity>>()");
    }

    [TestMethod]
    public async Task The_Angular_screen_hides_the_write_buttons_from_a_role_that_may_not_write()
    {
        string screen = await Run("TS_Component_v1.tt", Sample.Holiday(), Api, ("Auth", "true"), ("WriteRoles", "Manager"));

        Expect.Contains(screen, "import { AuthService } from '../../services/auth.service';");
        Expect.Contains(screen, "private readonly auth = inject(AuthService);");
        Expect.Contains(screen, "get canWrite(): boolean { return this.auth.hasRole(['Administrator', 'Manager']); }");
        Expect.Contains(screen, "<button *ngIf=\"canWrite\" (click)=\"add()\"");
        Expect.Contains(screen, "<button *ngIf=\"canWrite\" (click)=\"edit(");
        Expect.Contains(screen, "<button *ngIf=\"canWrite\" (click)=\"delete(");

        string open = await Run("TS_Component_v1.tt", Sample.Holiday(), Api, ("Auth", "true"));
        Expect.DoesNotContain(open, "canWrite");
        Expect.DoesNotContain(open, "AuthService");

        string noSignIn = await Run("TS_Component_v1.tt", Sample.Holiday(), Api, ("WriteRoles", "Manager"));
        Expect.DoesNotContain(noSignIn, "canWrite");
    }

    [TestMethod]
    public async Task The_OData_screen_and_the_master_detail_screen_hide_them_too()
    {
        string odata = await Run("TS_Component_v1.tt", Sample.Holiday(), ("OData", "true"), Api, ("Auth", "true"), ("WriteRoles", "Manager"));
        Expect.Contains(odata, "<button *ngIf=\"canWrite\" (click)=\"add()\"");
        Expect.Contains(odata, "<button *ngIf=\"canWrite\" (click)=\"delete(");

        string detail = await Run("TS_DetailMasterComponent_v1.tt", Sample.TimeSheetWithEmployeeAndDetail(), Api, ("Auth", "true"), ("WriteRoles", "Manager"));
        Expect.Contains(detail, "get canWrite(): boolean { return this.auth.hasRole(['Administrator', 'Manager']); }");
        Expect.Contains(detail, "<button *ngIf=\"canWrite\" (click)=\"add()\"");
    }

    [TestMethod]
    public void The_read_rule_has_its_own_settings_and_lists_the_administrator_first()
    {
        var project = Project(Api, ("ReadRoles", "Hr"), ("TableReadRoles", "Salary:Payroll; Holiday:"));

        CollectionAssert.AreEqual(new[] { "Administrator", "Hr" }, WriteRolePlan.ReadFor(project, "Department").ToArray());
        CollectionAssert.AreEqual(new[] { "Administrator", "Payroll" }, WriteRolePlan.ReadFor(project, "salary").ToArray());
        CollectionAssert.AreEqual(Array.Empty<string>(), WriteRolePlan.ReadFor(project, "Holiday").ToArray());
        CollectionAssert.AreEqual(Array.Empty<string>(), WriteRolePlan.For(project, "Department").ToArray(), "the write rule is separate");
        CollectionAssert.AreEqual(Array.Empty<string>(), WriteRolePlan.ReadFor(Project(("ReadRoles", "Hr")), "Department").ToArray(), "no ApiAuth, no rule");
        Assert.AreEqual("Roles", ProjectSettingGroups.TabOf("ReadRoles"));
        Assert.AreEqual("Roles", ProjectSettingGroups.TabOf("TableWriteRoles"));
    }

    [TestMethod]
    public async Task The_REST_reads_refuse_another_role_except_the_lookup()
    {
        string api = await Run("API_Crud_v1.tt", Sample.Holiday(), Api, ("ReadRoles", "Hr"));
        Expect.Contains(api, "protected override IReadOnlyList<string> ReadRoles => [\"Administrator\", \"Hr\"];");
        Expect.DoesNotContain(api, "WriteRoles");
        Expect.DoesNotContain(await Run("API_Crud_v1.tt", Sample.Holiday(), Api), "ReadRoles");

        string withAuth = await Run("API_EssentialCrudApi_v1.tt", new object(), Api);
        Expect.Contains(withAuth, "protected virtual IReadOnlyList<string> ReadRoles => [];");
        Assert.AreEqual(7, withAuth.Split(".RequireReadRoles(ReadRoles)").Length - 1, "list, one row and find by name of CrudApi; list, active list, one row and find by name of NameActiveCrudApi");
        Expect.DoesNotContain(CutAround(withAuth, "WithName($\"Get{plural}Lookup\")"), "RequireReadRoles");
        Expect.DoesNotContain(await Run("API_EssentialCrudApi_v1.tt", new object()), "RequireReadRoles");

        string jwt = await Run("API_EssentialJwt_v1.tt", new object(), Api);
        Expect.Contains(jwt, "public static TBuilder RequireReadRoles<TBuilder>(this TBuilder builder, IReadOnlyList<string> roles)");
    }

    [TestMethod]
    public async Task The_search_and_the_OData_reads_refuse_another_role()
    {
        string search = await Run("API_Search_v1.tt", Sample.Holiday(), Api, ("TableReadRoles", "Holiday:Hr,Payroll"));
        Expect.Contains(search, "        .RequireReadRoles([\"Administrator\", \"Hr\", \"Payroll\"])\n        .Produces");
        Expect.DoesNotContain(await Run("API_Search_v1.tt", Sample.Holiday(), Api), "RequireReadRoles");

        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [Sample.Holiday(), Sample.NaturalKey()] };
        string odata = await Run("API_OData_v1.tt", database, ("OData", "true"), Api, ("ContextName", "AcmeContext"), ("TableReadRoles", "Holiday:Hr"));
        Expect.Contains(odata, "public sealed class HolidayODataRules : ODataRules<Holiday, AcmeContext>");
        Expect.Contains(odata, "public override IReadOnlyList<string> ReadRoles => [\"Administrator\", \"Hr\"];");
        Assert.AreEqual(1, odata.Split(".RequireReadRoles(rules.ReadRoles)").Length - 1, "the read by key");
        Expect.Contains(odata, "if (!RoleGate.Allows(http.User, rules.ReadRoles))");
        Expect.Contains(odata, "throw new ODataRoleRefusedException();");
        Expect.Contains(odata, "catch (ODataRoleRefusedException)");
        string noAuth = await Run("API_OData_v1.tt", database, ("OData", "true"), ("TableReadRoles", "Holiday:Hr"));
        Expect.DoesNotContain(noAuth, "RequireReadRoles");
        Expect.DoesNotContain(noAuth, "ODataRoleRefusedException");
    }

    [TestMethod]
    public async Task The_routes_and_the_menu_hide_a_screen_from_a_role_that_may_not_read_it()
    {
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [Sample.Holiday(), Sample.NaturalKey()] };
        string routes = await Run("TS_Screens_v1.tt", database, ("Auth", "true"), Api, ("TableReadRoles", "Holiday:Hr"));

        Expect.Contains(routes, "label: 'Holiday', component: HolidayComponent, roles: ['Administrator', 'Hr'] }");
        Expect.Contains(routes, "const canRead: CanActivateFn = (route) => {");
        Expect.Contains(routes, "import { AuthService } from './services/auth.service';");
        Expect.Contains(routes, "canActivate: [canRead], data: { roles: s.roles }");
        Expect.Contains(routes, "const first = screens.find((s) => !s.roles || auth.hasRole(s.roles));");

        string open = await Run("TS_Screens_v1.tt", database, ("Auth", "true"), Api);
        Expect.DoesNotContain(open, "canRead");
        Expect.Contains(open, "canActivate: [authGuard]");

        string shell = await Run("TS_EssentialShell_v1.tt", new object(), ("Auth", "true"));
        Expect.Contains(shell, "protected get screens() {");
        Expect.Contains(shell, "this.auth.hasRole(roles)");
        Expect.Contains(await Run("TS_EssentialShell_v1.tt", new object()), "protected readonly screens = screens;");
    }

    [TestMethod]
    public async Task The_generated_spec_checks_that_Add_New_is_shown_to_a_listed_role_and_hidden_from_another()
    {
        string screen = await Run("TS_Component_v1.tt", Sample.Holiday(), Api, ("Auth", "true"), ("WriteRoles", "Manager"));

        Expect.Contains(screen, "describe('HolidayComponent write roles', () => {");
        Expect.Contains(screen, "sessionStorage.setItem('acme.auth'");
        Expect.Contains(screen, "expect(hasAdd(await open('Manager'))).toBe(true);");
        Expect.Contains(screen, "expect(hasAdd(await open('NoSuchRole'))).toBe(false);");
        Expect.DoesNotContain(await Run("TS_Component_v1.tt", Sample.Holiday(), Api, ("Auth", "true")), "write roles");

        string detail = await Run("TS_DetailMasterComponent_v1.tt", Sample.TimeSheetWithEmployeeAndDetail(), Api, ("Auth", "true"), ("WriteRoles", "Manager"));
        Expect.Contains(detail, "write roles', () => {");
    }

    [TestMethod]
    public async Task The_API_tests_sign_in_with_a_role_and_check_the_rules()
    {
        string tests = await Run("API_Test_v1.tt", Sample.Holiday(), Api, ("WriteRoles", "Manager"), ("ReadRoles", "Hr"));

        Expect.Contains(tests, "public async Task A_role_the_write_rule_does_not_list_cannot_change_rows()");
        Expect.Contains(tests, "ApiFixture.ClientInRole(\"NoSuchRole\")");
        Expect.Contains(tests, "ApiFixture.ClientInRole(\"Manager\")");
        Expect.Contains(tests, "public async Task A_role_the_read_rule_does_not_list_cannot_read_rows()");
        Expect.Contains(tests, "ApiFixture.ClientInRole(\"Hr\")");
        Expect.Contains(tests, ProblemTypes.RoleNotAllowed);

        string open = await Run("API_Test_v1.tt", Sample.Holiday(), Api);
        Expect.DoesNotContain(open, "ClientInRole");

        string fixture = await Run("API_EssentialTests_v1.tt", new object(), Api, ("ApiTests", "true"));
        Expect.Contains(fixture, "public static HttpClient ClientInRole(string role)");
        Expect.Contains(fixture, "new(\"role\", role)");
        Expect.Contains(fixture, "_jwtKey = Guid.NewGuid()");
    }

    [TestMethod]
    public async Task The_Angular_auth_service_can_say_whether_a_person_has_one_of_the_roles()
    {
        string auth = await Run("TS_EssentialAuth_v1.tt", new object(), ("Auth", "true"));

        Expect.Contains(auth, "hasRole(roles: readonly string[]): boolean {");
        Expect.Contains(auth, "return !!role && roles.includes(role);");
        Expect.Contains(auth, "says whether the signed-in person has one of the roles");
    }
}
