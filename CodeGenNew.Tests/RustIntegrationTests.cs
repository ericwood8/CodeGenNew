using System.Data;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The integration tests the Rust stack writes for the generated crate (RustTests=true): the test of a table, the bodies of the requests, the shared helper, and the plan around them. Compiling and running
/// them needs a Rust toolchain; that check is a generated scratch crate run with cargo test. </summary>
[TestClass]
public class RustIntegrationTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static (string, string)[] On(params (string, string)[] more) => [("RustTests", "true"), ("DatabaseProvider", "Sqlite"), ("DatabaseName", "acme.db"), .. more];

    private static TableModel Status() => Sample.Table("Status",
    [
        Sample.Column("StatusId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Description", SqlDbType.VarChar, characters: 20, ordinal: 2)
    ]);

    private static TableModel Account()
    {
        var fk = Sample.ForeignKey("StatusId", "Status", "StatusId", "Description");
        return Sample.Table("Account",
        [
            Sample.Column("AccountId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Code", SqlDbType.VarChar, characters: 20, inUniqueIndex: true, ordinal: 2),
            Sample.Column("Name", SqlDbType.VarChar, characters: 50, ordinal: 3),
            Sample.Column("StatusId", SqlDbType.Int, ordinal: 4),
            Sample.Column("Balance", SqlDbType.Int, ordinal: 5)
        ], [fk], indexes: [new IndexModel("UX_Account_Code", true, ["Code"])]);
    }

    private static DatabaseModel Database(params TableModel[] tables) =>
        new() { DatabaseName = "Acme", SchemaName = "main", Dialect = SqlDialect.Sqlite, Tables = [.. tables] };

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
    public async Task Nothing_is_written_unless_the_project_asks_for_the_tests()
    {
        Assert.AreEqual("", (await Run("RS_Test_v1.tt", Account(), ("DatabaseProvider", "Sqlite"))).Trim());
        Assert.AreEqual("", (await Run("RS_TestBodies_v1.tt", Database(Account()), ("DatabaseProvider", "Sqlite"))).Trim());
        Assert.AreEqual("", (await Run("RS_EssentialTests_v1.tt", new object(), ("DatabaseProvider", "Sqlite"))).Trim());
        Assert.AreEqual("", (await Run("RS_TestJunction_v1.tt", AccountTag(), ("DatabaseProvider", "Sqlite"))).Trim());
        foreach (string template in new[] { "RS_TestDashboard_v1.tt", "RS_TestCsv_v1.tt", "RS_TestGates_v1.tt" })
            Assert.AreEqual("", (await Run(template, Database(Account(), Status(), Tag(), AccountTag()), ("DatabaseProvider", "Sqlite"), ("ApiCsv", "true"), ("ApiAuth", "true"), ("ChangeGates", "Account.StatusId; to 2; when Balance=0"))).Trim(), template);
    }

    [TestMethod]
    public async Task A_table_gets_a_module_that_adds_reads_changes_and_removes_a_row_over_its_routes()
    {
        string test = await Run("RS_Test_v1.tt", Account(), On());

        Expect.Contains(test, "@@@FILE tests/api/account.rs@@@");
        Expect.Contains(test, "const ROUTE: &str = \"/api/accounts\";");
        Expect.Contains(test, "test_bodies::account(api, 0).await");
        Expect.Contains(test, "async fn a_row_can_be_added_read_changed_and_removed()");
        Expect.Contains(test, "row[\"code\"] = after.clone();");
        Expect.Contains(test, "async fn a_row_that_is_not_there_answers_404()");
        Expect.Contains(test, "async fn the_search_sorts_by_each_column_it_offers()");
        Expect.Contains(test, "async fn a_row_can_be_cloned()");
        Expect.Contains(test, "async fn a_second_row_with_a_value_that_must_be_unique_is_refused()");
        Expect.DoesNotContain(test, "without_a_token");
        Expect.DoesNotContain(test, "text_longer_than_the_column_is_refused");
    }

    [TestMethod]
    public async Task Validation_and_sign_in_add_their_tests()
    {
        string test = await Run("RS_Test_v1.tt", Account(), On(("ApiValidation", "true"), ("ApiAuth", "true"), ("WriteRoles", "Administrator,Manager"), ("ReadRoles", "Administrator,Clerk")));

        Expect.Contains(test, "async fn text_longer_than_the_column_is_refused()");
        Expect.Contains(test, "\"x\".repeat(21)");
        Expect.Contains(test, "async fn without_a_token_the_list_answers_401()");
        Expect.Contains(test, "async fn a_role_the_write_rule_does_not_list_cannot_change_rows()");
        Expect.Contains(test, "api.in_role(\"Manager\")");
        Expect.Contains(test, "async fn a_role_the_read_rule_lists_can_read_the_list()");
        Expect.Contains(test, "api.in_role(\"Clerk\")");
    }

    [TestMethod]
    public async Task The_bodies_add_a_row_to_an_empty_parent_when_the_parent_has_an_api()
    {
        string both = await Run("RS_TestBodies_v1.tt", Database(Account(), Status()), On(("Dashboard", "false")));

        Expect.Contains(both, "@@@FILE tests/api/main.rs@@@");
        Expect.Contains(both, "mod account;\nmod status;");
        Expect.Contains(both, "pub fn account<'a>(api: &'a Client, depth: u8) -> Body<'a>");
        Expect.Contains(both, "parent_key(api, \"/api/status\", \"statusId\", json!(1), Some(status(api, depth + 1)), depth).await?");
        Expect.Contains(both, "\"code\": unique(\"Sample code\", 20),");
        Expect.Contains(both, "\"accountId\": 0,");
        Expect.Contains(both, "\"statusId\": parent_status_id,");
        Expect.Contains(both, "pub fn status<'a>(_api: &'a Client, _depth: u8) -> Body<'a>");

        string alone = await Run("RS_TestBodies_v1.tt", Database(Account()), On());
        Expect.Contains(alone, "json!(1), None, depth)");
    }

    private static TableModel Tag() => Sample.Table("Tag",
    [
        Sample.Column("TagId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.VarChar, characters: 30, ordinal: 2),
        Sample.Column("Colour", SqlDbType.VarChar, characters: 20, ordinal: 3)
    ]);

    private static TableModel AccountTag() => Sample.Table("AccountTag",
    [
        Sample.Column("AccountId", SqlDbType.Int, primaryKey: true, ordinal: 1),
        Sample.Column("TagId", SqlDbType.Int, primaryKey: true, ordinal: 2)
    ],
    [
        Sample.ForeignKey("AccountId", "Account", "AccountId", "Name"),
        Sample.ForeignKey("TagId", "Tag", "TagId", "Name")
    ]);

    [TestMethod]
    public async Task The_module_list_adds_the_junction_dashboard_csv_and_gate_tests_the_project_has()
    {
        var database = Database(Account(), Status(), Tag(), AccountTag());
        string plain = await Run("RS_TestBodies_v1.tt", database, On(("Dashboard", "false")));
        Expect.Contains(plain, "mod account_tag_junction;");
        Expect.DoesNotContain(plain, "dashboard_routes");
        Expect.DoesNotContain(plain, "csv_routes");
        Expect.DoesNotContain(plain, "gate_routes");

        string all = await Run("RS_TestBodies_v1.tt", database, On(("ApiAuth", "true"), ("ApiCsv", "true"), ("ChangeGates", "Account.StatusId; to 2; when Balance=0; start 1")));
        Expect.Contains(all, "mod csv_routes;");
        Expect.Contains(all, "mod dashboard_routes;");
        Expect.Contains(all, "mod gate_routes;");
    }

    [TestMethod]
    public async Task A_junction_gets_tests_of_its_list_link_and_unlink()
    {
        string test = await Run("RS_TestJunction_v1.tt", AccountTag(), On(("ApiAuth", "true"), ("WriteRoles", "Administrator,Manager"), ("ReadRoles", "Administrator,Clerk")));

        Expect.Contains(test, "@@@FILE tests/api/account_tag_junction.rs@@@");
        Expect.Contains(test, "const ROUTE: &str = \"/api/accounttags\";");
        Expect.Contains(test, "parent_key(api, \"/api/accounts\", \"accountId\", json!(1), Some(test_bodies::account(api, 1)), 0)");
        Expect.Contains(test, "parent_key(api, \"/api/tags\", \"tagId\", json!(1), Some(test_bodies::tag(api, 1)), 0)");
        Expect.Contains(test, "async fn a_target_can_be_linked_and_unlinked_and_either_can_be_repeated()");
        Expect.Contains(test, "async fn a_row_that_is_not_there_cannot_be_linked()");
        Expect.Contains(test, "async fn without_a_token_the_junction_answers_401()");
        Expect.Contains(test, "async fn a_role_the_write_rule_lists_can_link()");
        Expect.Contains(test, "api.in_role(\"Manager\")");
        Expect.Contains(test, "async fn a_role_the_read_rule_does_not_list_cannot_read_the_list()");

        string plain = await Run("RS_TestJunction_v1.tt", AccountTag(), On());
        Expect.DoesNotContain(plain, "without_a_token");
        Assert.AreEqual("", (await Run("RS_TestJunction_v1.tt", AccountTag(), On(("NoApiTables", "Tag")))).Trim(), "a side without an API leaves nothing to link");
    }

    [TestMethod]
    public async Task The_dashboard_tests_compare_the_widgets_with_the_plan()
    {
        var database = Database(Account(), Status());
        string test = await Run("RS_TestDashboard_v1.tt", database, On(("Dashboard", "true"), ("DashboardStrip", "true")));

        Expect.Contains(test, "@@@FILE tests/api/dashboard_routes.rs@@@");
        Expect.Contains(test, "const ROUTE: &str = \"/api/dashboard\";");
        Expect.Contains(test, "const PAGE: &[&str] = &[\"");
        Expect.Contains(test, "async fn a_table_without_a_strip_answers_no_widget()");
        Expect.DoesNotContain(test, "without_a_token");

        string signedIn = await Run("RS_TestDashboard_v1.tt", database, On(("ApiAuth", "true"), ("ReadRoles", "Administrator,Clerk")));
        Expect.Contains(signedIn, "async fn without_a_token_the_dashboard_answers_401()");
        Expect.Contains(signedIn, "const LIMITED: &[&str] = &[\"Account\", \"Status\"];");
        Expect.Contains(signedIn, "async fn a_role_the_read_rules_do_not_list_gets_no_widget_of_a_limited_table()");

        Assert.AreEqual("", (await Run("RS_TestDashboard_v1.tt", database, On(("Dashboard", "false")))).Trim());
    }

    [TestMethod]
    public async Task The_csv_tests_write_a_module_per_table_with_the_columns_the_import_reads()
    {
        string test = await Run("RS_TestCsv_v1.tt", Database(Account(), Status()), On(("ApiCsv", "true")));

        Expect.Contains(test, "@@@FILE tests/api/csv_routes.rs@@@");
        Expect.Contains(test, "mod account {");
        Expect.Contains(test, "const HEADER: &str = \"AccountId,Code,Name,StatusId,Balance\";");
        Expect.Contains(test, "const COLUMNS: &[(&str, &str)] = &[(\"Code\", \"code\"), (\"Name\", \"name\"), (\"StatusId\", \"statusId\"), (\"Balance\", \"balance\")];");
        Expect.Contains(test, "async fn the_export_has_the_header_and_the_rows_of_the_table()");
        Expect.Contains(test, "async fn an_import_adds_the_rows_of_the_file()");
        Expect.Contains(test, "async fn a_bad_row_adds_none_of_the_rows_of_the_file()");
        Expect.Contains(test, "async fn a_file_without_a_column_that_must_have_a_value_is_refused()");
        Expect.Contains(test, "async fn a_text_that_starts_like_a_formula_is_exported_with_an_apostrophe_and_imported_as_it_was()");
        Expect.DoesNotContain(test, "without_a_token");
        Expect.DoesNotContain(test, "only_an_administrator");

        string owned = await Run("RS_TestCsv_v1.tt", Database(Account(), Status()), On(("ApiCsv", "true"), ("ApiAuth", "true"), ("OwnedTables", "Account"), ("OwnerColumn", "Balance")));
        Expect.Contains(owned, "async fn without_a_token_the_csv_routes_answer_401()");
        Expect.Contains(owned, "async fn only_an_administrator_uses_the_csv_routes_of_this_table()");

        Assert.AreEqual("", (await Run("RS_TestCsv_v1.tt", Database(Account(), Status()), On())).Trim());
    }

    [TestMethod]
    public async Task The_gate_tests_set_the_condition_through_the_apis_and_check_the_409()
    {
        string test = await Run("RS_TestGates_v1.tt", Database(Account(), Status()), On(("ApiAuth", "true"), ("ChangeGates", "Account.StatusId; to 2; when Balance=0; unless Name=Free; start 1")));

        Expect.Contains(test, "@@@FILE tests/api/gate_routes.rs@@@");
        Expect.Contains(test, "mod account {");
        Expect.Contains(test, "async fn a_new_row_must_start_with_status_id_at_its_start_value()");
        Expect.Contains(test, "body[\"statusId\"] = json!(2);");
        Expect.Contains(test, "async fn status_id_is_held_by_its_gate()");
        Expect.Contains(test, "row[\"balance\"] = json!(0);");
        Expect.Contains(test, "row[\"name\"] = json!(\"Freex\");");
        Expect.Contains(test, "row[\"name\"] = json!(\"Free\");");
        Expect.Contains(test, "assert_eq!(held.status, StatusCode::CONFLICT");
        Expect.Contains(test, "/problems/change-not-allowed");

        string parent = await Run("RS_TestGates_v1.tt", Database(Account(), Status()), On(("ApiAuth", "true"), ("ChangeGates", "Account.Balance; to 1; when Status.Description=Bad")));
        Expect.Contains(parent, "set_other(&api, \"/api/status\", &segment(&row[\"statusId\"]), \"description\", json!(\"Bad\"), &mut undo).await?;");

        Assert.AreEqual("", (await Run("RS_TestGates_v1.tt", Database(Account(), Status()), On(("ApiAuth", "true")))).Trim());
    }

    [TestMethod]
    public async Task A_table_scoped_to_a_person_gets_tests_of_the_owner_rule()
    {
        string test = await Run("RS_Test_v1.tt", Account(), On(("ApiAuth", "true"), ("OwnedTables", "Account"), ("OwnerColumn", "Balance")));

        Expect.Contains(test, "const PLAIN_ROLE: &str = \"Member\";");
        Expect.Contains(test, "row[\"balance\"].as_i64()");
        Expect.Contains(test, "async fn a_person_sees_and_changes_only_the_rows_of_their_own_owner()");
        Expect.Contains(test, "async fn a_person_adds_rows_only_for_their_own_owner()");
        Expect.Contains(test, "api.in_owner(PLAIN_ROLE, owner + 1)");
        Expect.Contains(test, "/problems/not-your-row");

        string roles = await Run("RS_Test_v1.tt", Account(), On(("ApiAuth", "true"), ("OwnedTables", "Account"), ("OwnerColumn", "Balance"), ("WriteRoles", "Administrator,Manager"), ("ReadRoles", "Administrator,Manager,Clerk")));
        Expect.Contains(roles, "const PLAIN_ROLE: &str = \"Manager\";");

        string apart = await Run("RS_Test_v1.tt", Account(), On(("ApiAuth", "true"), ("OwnedTables", "Account"), ("OwnerColumn", "Balance"), ("WriteRoles", "Administrator,Manager"), ("ReadRoles", "Administrator,Clerk")));
        Expect.DoesNotContain(apart, "PLAIN_ROLE");

        string open = await Run("RS_Test_v1.tt", Account(), On(("ApiAuth", "true")));
        Expect.DoesNotContain(open, "PLAIN_ROLE");
    }

    [TestMethod]
    public void The_plain_role_is_one_every_rule_lists_besides_the_administrator()
    {
        var project = Project(("AuthAdminRole", "Boss"));
        Assert.AreEqual("Member", RustTestBodies.PlainRole(Project(), [], []));
        Assert.AreEqual("Guest", RustTestBodies.PlainRole(Project(("AuthAdminRole", "Member")), [], []));
        Assert.AreEqual("Clerk", RustTestBodies.PlainRole(project, ["Boss", "Clerk", "Temp"], ["Boss", "Clerk"]));
        Assert.AreEqual("Clerk", RustTestBodies.PlainRole(project, [], ["Boss", "Clerk"]));
        Assert.IsNull(RustTestBodies.PlainRole(project, ["Boss", "Clerk"], ["Boss", "Manager"]));
    }

    [TestMethod]
    public async Task The_helper_copies_the_sqlite_file_and_asks_for_a_url_for_the_other_databases()
    {
        string sqlite = await Run("RS_EssentialTests_v1.tt", new object(), On());
        Expect.Contains(sqlite, "@@@FILE tests/api/test_support.rs@@@");
        Expect.Contains(sqlite, "unwrap_or_else(|_| \"acme.db\".to_string())");
        Expect.Contains(sqlite, "std::fs::copy(&source, &copy)");
        Expect.DoesNotContain(sqlite, "Jwt__Key");

        string postgres = await Run("RS_EssentialTests_v1.tt", new object(), ("RustTests", "true"), ("DatabaseProvider", "PostgreSql"));
        Expect.Contains(postgres, "std::env::var(\"ACME_TEST_CONNECTION\")");
        Expect.Contains(postgres, "PGPASSWORD");
        Expect.DoesNotContain(postgres, "std::fs::copy");

        string mysql = await Run("RS_EssentialTests_v1.tt", new object(), ("RustTests", "true"), ("DatabaseProvider", "MySql"));
        Expect.Contains(mysql, "MYSQL_PWD");
    }

    [TestMethod]
    public async Task With_sign_in_the_helper_signs_in_as_a_bootstrap_administrator_or_a_named_user()
    {
        string bootstrap = await Run("RS_EssentialTests_v1.tt", new object(), On(("ApiAuth", "true")));
        Expect.Contains(bootstrap, "Bootstrap__AdminUserName");
        Expect.Contains(bootstrap, "pub fn in_role(&self, role: &str) -> Client");
        Expect.Contains(bootstrap, "ACME_TEST_USER");

        string users = await Run("RS_EssentialTests_v1.tt", new object(), On(("ApiAuth", "true"), ("UserTable", "Employee")));
        Expect.Contains(users, "the sign-in reads the user table");
        Expect.DoesNotContain(users, "settings().1.clone()");
    }

    [TestMethod]
    public void The_setting_adds_the_templates_a_dev_dependency_and_keeps_the_tests_out_of_a_plain_plan()
    {
        CollectionAssert.IsSubsetOf(new[] { "RS_Test", "RS_TestBodies", "RS_TestJunction" }, Project(("RustTests", "true")).ImpliedPlanTemplates.ToArray());
        CollectionAssert.DoesNotContain(Project().ImpliedPlanTemplates.ToArray(), "RS_Test");
        var plain = Project(("RustTests", "true"), ("Dashboard", "false")).ImpliedPlanTemplates.ToArray();
        CollectionAssert.DoesNotContain(plain, "RS_TestDashboard");
        CollectionAssert.DoesNotContain(plain, "RS_TestCsv");
        CollectionAssert.DoesNotContain(plain, "RS_TestGates");
        var every = Project(("RustTests", "true"), ("ApiCsv", "true"), ("ApiAuth", "true"), ("GuardedColumns", "Customer.Flag; by Employee.IsManager")).ImpliedPlanTemplates.ToArray();
        CollectionAssert.IsSubsetOf(new[] { "RS_TestDashboard", "RS_TestCsv", "RS_TestGates" }, every);
        Assert.IsFalse(TemplateCatalog.Discover(Repo.TemplatesDirectory).Where(t => t.Name.StartsWith("RS_Test")).Any(t => t.Config.InPlan));
    }

    [TestMethod]
    public async Task Cargo_gets_the_dev_dependency_only_with_the_tests()
    {
        Expect.Contains(await Run("RS_EssentialCargo_v1.tt", new object(), On()), "[dev-dependencies]\ntower = { version = \"0.5\", features = [\"util\"] }");
        Expect.DoesNotContain(await Run("RS_EssentialCargo_v1.tt", new object(), ("DatabaseProvider", "Sqlite")), "dev-dependencies");
    }
}
