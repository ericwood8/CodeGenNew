using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> TokenTables (session and token tables: no ordinary API, an administrator's list without the hash, revoke, sign out a person everywhere, spend once). </summary>
[TestClass]
public class TokenPlanTests
{
    private static ColumnModel C(string name, SqlDbType type, bool nullable = false, bool primaryKey = false, int ordinal = 0, int? characters = null, bool identity = false) =>
        Sample.Column(name, type, nullable: nullable, primaryKey: primaryKey, ordinal: ordinal, characters: characters, identity: identity);

    private static TableModel Session(int hashLength = 64, bool expiryNullable = false, bool withRevoked = true, bool person = true) => Sample.Table("Session",
        new List<ColumnModel>
        {
            C("SessionId", SqlDbType.Int, primaryKey: true, ordinal: 1, identity: true), C("EmployeeId", SqlDbType.Int, ordinal: 2), C("TokenHash", SqlDbType.VarChar, ordinal: 3, characters: hashLength),
            C("CreatedOn", SqlDbType.DateTime, ordinal: 4), C("ExpiresOn", SqlDbType.DateTime, nullable: expiryNullable, ordinal: 5)
        }.Concat(withRevoked ? [C("RevokedOn", SqlDbType.DateTime, nullable: true, ordinal: 6)] : []).ToList(),
        person ? [new ForeignKeyModel { ConstraintName = "FK_s", ReferencingColumns = ["EmployeeId"], ReferencedSchema = "dbo", ReferencedTable = "Employee", ReferencedColumns = ["EmployeeId"] }] : []);

    private static TableModel Reset() => Sample.Table("PasswordReset",
        new List<ColumnModel>
        {
            C("PasswordResetId", SqlDbType.Int, primaryKey: true, ordinal: 1, identity: true), C("Email", SqlDbType.VarChar, ordinal: 2, characters: 80), C("CodeHash", SqlDbType.VarChar, ordinal: 3, characters: 64),
            C("ExpiresOn", SqlDbType.DateTime, ordinal: 4), C("IsUsed", SqlDbType.Bit, ordinal: 5)
        });

    private static TableModel Refresh() => Sample.Table("RefreshToken",
        [
            C("RefreshTokenId", SqlDbType.Int, primaryKey: true, ordinal: 1, identity: true), C("EmployeeId", SqlDbType.Int, ordinal: 2), C("TokenHash", SqlDbType.VarChar, ordinal: 3, characters: 64),
            C("FamilyId", SqlDbType.UniqueIdentifier, ordinal: 4), C("ExpiresOn", SqlDbType.DateTime, ordinal: 5), C("UsedOn", SqlDbType.DateTime, nullable: true, ordinal: 6), C("RevokedOn", SqlDbType.DateTime, nullable: true, ordinal: 7)
        ],
        [new ForeignKeyModel { ConstraintName = "FK_r", ReferencingColumns = ["EmployeeId"], ReferencedSchema = "dbo", ReferencedTable = "Employee", ReferencedColumns = ["EmployeeId"] }]);

    private static ProjectSettings Project(string tokens = "Session", Dictionary<string, string>? extra = null)
    {
        var values = new Dictionary<string, string> { ["AccessMode"] = "Ef", ["DatabaseProvider"] = "SqlServer", ["ApiAuth"] = "true", ["UserTable"] = "Employee", ["TokenTables"] = tokens };
        foreach (var (key, value) in extra ?? new Dictionary<string, string>())
            values[key] = value;
        return ProjectSettings.FromValues(values);
    }

    [TestMethod]
    public void The_setting_names_a_table_and_optionally_its_hash_expiry_and_revoked_columns()
    {
        var project = Project("Session, PasswordReset:CodeHash:ExpiresOn:IsUsed");

        CollectionAssert.AreEqual(new[] { ("Session", (string?)null, (string?)null, (string?)null, (string?)null), ("PasswordReset", "CodeHash", "ExpiresOn", "IsUsed", (string?)null) }, TokenPlan.Entries(project).ToArray());
        Assert.IsTrue(TokenPlan.IsListed("passwordreset", project));
        Assert.IsFalse(TokenPlan.IsListed("Customer", project));
    }

    [TestMethod]
    public void The_columns_are_found_by_name_and_the_person_by_the_foreign_key_to_the_user_table()
    {
        var token = TableShapes.For(Session(), Project()).Token!;

        Assert.AreEqual("TokenHash", token.Hash.Name);
        Assert.AreEqual("ExpiresOn", token.Expiry.Name);
        Assert.AreEqual("RevokedOn", token.Revoked!.Name);
        Assert.IsFalse(token.RevokedIsFlag);
        Assert.AreEqual("EmployeeId", token.Person!.Name);
        Assert.AreEqual("Employee", token.PersonTable);
        Assert.IsFalse(token.Shown.Any(c => c.Name == "TokenHash"), "the hash is never shown");
        Assert.IsNull(TableShapes.For(Session(person: false), Project()).Token!.Person);
        Assert.IsNull(TableShapes.For(Session(withRevoked: false), Project()).Token!.Revoked, "without a revoked column, ending a token sets its expiry");
    }

    [TestMethod]
    public void A_flag_named_in_the_setting_is_a_yes_no_revoked_column()
    {
        var token = TableShapes.For(Reset(), Project("PasswordReset:CodeHash:ExpiresOn:IsUsed")).Token!;

        Assert.IsTrue(token.RevokedIsFlag);
        Assert.AreEqual("IsUsed", token.Revoked!.Name);
    }

    [TestMethod]
    public void A_table_that_cannot_hold_a_token_is_refused_with_the_reason()
    {
        Assert.Contains("at least 64 characters", TokenPlan.Problem(Session(hashLength: 40), Project())!);
        Assert.Contains("NOT NULL", TokenPlan.Problem(Session(expiryNullable: true), Project())!);
        Assert.Contains("ApiAuth", TokenPlan.Problem(Session(), Project(extra: new() { ["ApiAuth"] = "false" }))!);
        Assert.Contains("AccessMode=Ef", TokenPlan.Problem(Session(), Project(extra: new() { ["AccessMode"] = "Routines" }))!);
        Assert.Contains("Missing", TokenPlan.Problem(Session(), Project("Session:Missing"))!);
        Assert.IsNull(TokenPlan.Problem(Session(), Project()));
        Assert.IsNull(TokenPlan.Problem(Reset(), Project("Session")), "a table the project does not list is not asked");
    }

    [TestMethod]
    public void A_token_table_gets_no_ordinary_api_and_is_aged_by_its_expiry()
    {
        var project = Project();

        Assert.IsFalse(Session().HasCrudApi(project));
        Assert.IsTrue(project.NoApi("Session", Session().LookupShape) == true);
        Assert.IsTrue(Reset().HasCrudApi(project), "a table the project does not list keeps its API");
    }

    [TestMethod]
    public void The_columns_the_routes_do_not_manage_are_given_a_value_for_the_generated_tests()
    {
        var token = TableShapes.For(Session(), Project()).Token!;

        CollectionAssert.AreEqual(new[] { "row.CreatedOn = DateTime.Now;" }, TokenPlan.RequiredFill(token)!.ToArray());
    }

    [TestMethod]
    public void The_token_templates_join_the_plan_with_ApiAuth_and_the_tests_with_ApiTests()
    {
        CollectionAssert.Contains(Project().ImpliedPlanTemplates.ToList(), "API_Token");
        CollectionAssert.DoesNotContain(Project().ImpliedPlanTemplates.ToList(), "API_TestTokens");
        CollectionAssert.Contains(Project(extra: new() { ["ApiTests"] = "true" }).ImpliedPlanTemplates.ToList(), "API_TestTokens");
        CollectionAssert.DoesNotContain(Project(extra: new() { ["ApiAuth"] = "false" }).ImpliedPlanTemplates.ToList(), "API_Token");
    }

    [TestMethod]
    public void The_setting_is_on_the_table_shapes_tab_and_has_a_hint()
    {
        Assert.AreEqual("Table shapes", ProjectSettingGroups.TabOf("TokenTables"));
        Assert.IsTrue(ProjectSettingsHints.All.ContainsKey("TokenTables"));
    }

    [TestMethod]
    public void The_routes_hash_the_token_end_a_row_with_one_guarded_update_and_never_answer_the_hash()
    {
        var token = TableShapes.For(Session(), Project()).Token!;
        string code = TokenWriter.File([token], Project(), "ShopContext", []);

        Assert.Contains("class SessionTokens", code);
        Assert.Contains("TokenHash.Of(token)", code);
        Assert.Contains("x.ExpiresOn > now && x.RevokedOn == null", code);
        Assert.Contains(".SetProperty(x => x.RevokedOn, now)", code);
        Assert.Contains("revoke-person/{personId}", code);
        Assert.Contains("app.MapPost(\"/api/sessions/spend\"", code);
        Assert.IsFalse(code.Contains("x.TokenHash,") || code.Contains("x.TokenHash }"), "the hash is not in an answer");
        Assert.Contains("class TokenApi", code);
        Assert.IsFalse(code.Contains('§'), "every token is filled");
    }

    [TestMethod]
    public void A_flag_is_set_and_tested_as_a_flag_and_a_table_without_one_ends_by_its_expiry()
    {
        string flag = TokenWriter.File([TableShapes.For(Reset(), Project("PasswordReset:CodeHash:ExpiresOn:IsUsed")).Token!], Project(), "ShopContext", []);
        string none = TokenWriter.File([TableShapes.For(Session(withRevoked: false), Project()).Token!], Project(), "ShopContext", []);

        Assert.Contains("x.ExpiresOn > now && !x.IsUsed", flag);
        Assert.Contains(".SetProperty(x => x.IsUsed, true)", flag);
        Assert.IsFalse(flag.Contains("revoke-person"), "no person column, no sign-out-everywhere route");
        Assert.Contains(".SetProperty(x => x.ExpiresOn, now)", none);
    }

    private static DatabaseModel Database(params TableModel[] tables) => new() { DatabaseName = "Shop", SchemaName = "dbo", Dialect = SqlDialect.SqlServer, Tables = tables.ToList() };

    [TestMethod]
    public void A_sign_in_front_end_gets_a_page_for_each_token_table_with_a_sign_out_when_a_person_is_named()
    {
        var project = Project(extra: new() { ["Auth"] = "true" });
        var pages = TokenScreens.Pages(Database(Session(), Reset()), Project("Session, PasswordReset:CodeHash:ExpiresOn:IsUsed", new() { ["Auth"] = "true" }));

        Assert.HasCount(2, pages);
        var session = pages.Single(p => p.Name == "SessionTokens");
        Assert.AreEqual("Sessions", session.Label);
        Assert.AreEqual("employeeId", session.PersonJson);
        Assert.IsFalse(session.Columns.Any(c => c.Json == "tokenHash"), "the hash is not a column of the page");
        Assert.IsNull(pages.Single(p => p.Name == "PasswordResetTokens").PersonJson);
        Assert.IsEmpty(TokenScreens.Pages(Database(Session()), Project()), "a front end without sign-in has no page");
        CollectionAssert.Contains(project.ImpliedPlanTemplates.ToList(), "TSX_Tokens");
        CollectionAssert.Contains(project.ImpliedPlanTemplates.ToList(), "TS_Tokens");
        CollectionAssert.Contains(project.ImpliedPlanTemplates.ToList(), "BLZ_Tokens");
        CollectionAssert.Contains(project.ImpliedPlanTemplates.ToList(), "UNO_Tokens");
        CollectionAssert.DoesNotContain(Project().ImpliedPlanTemplates.ToList(), "TSX_Tokens");
        Assert.IsTrue(BlazorMenu.Entries(Database(Session()), project).Any(e => e.Route == "session" && e.Roles.Contains("Administrator")));
    }

    [TestMethod]
    public void The_desktop_pages_ask_the_api_in_uno_and_the_database_in_winui3()
    {
        var project = Project(extra: new() { ["Auth"] = "true" });
        var page = TokenScreens.Pages(Database(Session()), project).Single();
        string uno = TokenDesktopCode.UnoPage("Shop.Views", "Shop.Services", page);
        string winui = TokenDesktopCode.DesktopPage("Shop.Views", "Shop.Repositories", "ShopContext", [], page);

        Assert.Contains("class SessionTokensPage", uno);
        Assert.Contains("revoke-person/", uno);
        Assert.Contains("x.ExpiresOn > now && x.RevokedOn == null", winui);
        Assert.Contains("ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedOn, now))", winui);
        Assert.IsFalse(winui.Contains("TokenHash"), "the hash is not read");
        Assert.IsFalse(uno.Contains('§') || winui.Contains('§'), "every token is filled");
        Assert.Contains("x:Class=\"Shop.Views.SessionTokensPage\"", TokenDesktopCode.Xaml("Shop.Views", page));
        Assert.Contains("AutomationProperties.Name=\"{x:Bind RevokeName}\"", TokenDesktopCode.Xaml("Shop.Views", page));
        Assert.Contains("public string RevokeName => \"Revoke \" + Key;", uno);
        Assert.Contains("public string RevokeName => \"Revoke \" + Key;", winui);
    }

    [TestMethod]
    public void The_python_and_rust_routes_are_written_with_the_same_sentences()
    {
        var token = TableShapes.For(Session(), Project()).Token!;
        string python = PythonTokenWriter.File([token], Project());
        string rust = RustTokenWriter.File([token], Project(), SqlDialect.Sqlite);

        Assert.Contains("hashlib.sha256", python);
        Assert.Contains("/revoke-person/{person_id}", python);
        Assert.Contains(TokenWords.SpendType, python);
        Assert.Contains("Sha256::digest", rust);
        Assert.Contains("/revoke-person/{person_id}", rust);
        Assert.Contains(TokenWords.SpendType, rust);
        Assert.IsFalse(python.Contains('§') || rust.Contains('§'), "every token is filled");
        Assert.DoesNotContain("\"TokenHash\", ", rust);
    }

    [TestMethod]
    public void A_table_with_a_used_and_a_revoked_column_tests_both_and_a_spend_sets_the_used_one()
    {
        var token = TableShapes.For(Refresh(), Project("RefreshToken")).Token!;
        string code = TokenWriter.File([token], Project(), "ShopContext", []);

        CollectionAssert.AreEqual(new[] { "UsedOn", "RevokedOn" }, token.Ends.Select(c => c.Name).ToArray());
        Assert.AreEqual("RevokedOn", token.Revoked!.Name);
        Assert.AreEqual("UsedOn", token.Spent!.Name);
        Assert.Contains("x.ExpiresOn > now && x.UsedOn == null && x.RevokedOn == null", code);
        Assert.Contains("ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedOn, now))", code);
        Assert.Contains("ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedOn, now))", code);
        var named = TableShapes.For(Refresh(), Project("RefreshToken:TokenHash:ExpiresOn:RevokedOn")).Token!;
        CollectionAssert.AreEqual(new[] { "RevokedOn" }, named.Ends.Select(c => c.Name).ToArray(), "a named column is the only one");
        var both = TableShapes.For(Refresh(), Project("RefreshToken:TokenHash:ExpiresOn:UsedOn+RevokedOn")).Token!;
        Assert.HasCount(2, both.Ends);
    }

    [TestMethod]
    public void A_family_column_ends_the_whole_family_when_a_used_token_comes_again()
    {
        var token = TableShapes.For(Refresh(), Project("RefreshToken")).Token!;
        string code = TokenWriter.File([token], Project(), "ShopContext", []);
        string python = PythonTokenWriter.File([token], Project());
        string rust = RustTokenWriter.File([token], Project(), SqlDialect.Sqlite);

        Assert.AreEqual("FamilyId", token.Family!.Name);
        Assert.Contains("found.Live && found.Family is not null", code);
        Assert.Contains("x.FamilyId == found.Family", code);
        Assert.Contains("Guid? Family", code);
        Assert.Contains("spec.family and values[spec.family] is not None", python);
        Assert.Contains("if expiry > now", rust);
        Assert.Contains("\"family\"", rust);
        Assert.IsFalse(code.Contains('§') || python.Contains('§') || rust.Contains('§'), "every token is filled");
        Assert.IsNull(TableShapes.For(Session(), Project()).Token!.Family);
    }

    [TestMethod]
    public void The_hash_is_looked_up_in_lower_and_upper_case()
    {
        var token = TableShapes.For(Session(), Project()).Token!;

        Assert.Contains("x.TokenHash == hash || x.TokenHash == upper", TokenWriter.File([token], Project(), "ShopContext", []));
        Assert.Contains("digest.upper()", PythonTokenWriter.File([token], Project()));
        Assert.Contains("digest.to_uppercase()", RustTokenWriter.File([token], Project(), SqlDialect.Sqlite));
    }

    [TestMethod]
    public void A_table_in_the_anonymous_list_spends_without_a_sign_in_and_must_be_a_token_table()
    {
        var project = Project("Session, RefreshToken", new() { ["TokenAnonymousSpend"] = "RefreshToken" });
        var tokens = new[] { TableShapes.For(Session(), project).Token!, TableShapes.For(Refresh(), project).Token! };
        string code = TokenWriter.File(tokens, project, "ShopContext", []);
        string rust = RustTokenWriter.File(tokens, project, SqlDialect.Sqlite);
        string python = PythonTokenWriter.File(tokens, project);

        Assert.IsFalse(tokens[0].Anonymous);
        Assert.IsTrue(tokens[1].Anonymous);
        Assert.Contains("app.MapPost(\"/api/refreshtokens/spend\", SpendRouteAsync).AllowAnonymous()", code);
        Assert.Contains("app.MapPost(\"/api/sessions/spend\", SpendRouteAsync).RequireAuthorization()", code);
        Assert.Contains("pub fn public_router()", rust);
        Assert.Contains("anonymous=True", python);
        Assert.IsEmpty(TokenPlan.Problems(Database(Session(), Refresh()), project));
        Assert.Contains("TokenAnonymousSpend: Customer is not in TokenTables", TokenPlan.Problems(Database(Session()), Project(extra: new() { ["TokenAnonymousSpend"] = "Customer" })).Single());
    }

    [TestMethod]
    public void The_python_and_rust_tests_cover_the_same_cases_and_only_for_a_table_the_generator_can_fill()
    {
        var project = Project("Session, RefreshToken", new() { ["TokenAnonymousSpend"] = "RefreshToken", ["PythonTests"] = "true", ["RustTests"] = "true", ["DatabaseProvider"] = "Sqlite" });
        var database = new DatabaseModel { DatabaseName = "Shop", SchemaName = "main", Dialect = SqlDialect.Sqlite, Tables = [Session(), Refresh(), Sample.Table("Employee", [C("EmployeeId", SqlDbType.Int, primaryKey: true, ordinal: 1, identity: true), C("Name", SqlDbType.VarChar, ordinal: 2, characters: 40)])] };
        var refresh = PythonTokenTests.Tables(database, project).Single(t => t.Table.TableName == "RefreshToken");
        string python = PythonTokenTests.File(refresh, database, project);
        string rust = RustTokenTests.File(refresh, database, project);

        Assert.HasCount(2, PythonTokenTests.Tables(database, project), "the user table is not a token table");
        Assert.Contains("def test_using_a_token_twice_ends_every_token_of_its_family", python);
        Assert.Contains("def test_a_token_is_spent_without_signing_in", python);
        Assert.Contains("def test_a_revoke_sets_the_revoked_column_and_a_spend_the_used_one", python);
        Assert.Contains("async fn using_a_token_twice_ends_every_token_of_its_family", rust);
        Assert.Contains("async fn a_revoke_sets_the_revoked_column_and_a_spend_the_used_one", rust);
        Assert.Contains("async fn revoking_a_person_ends_every_active_token_of_that_person", rust);
        Assert.IsFalse(python.Contains('§') || rust.Contains('§'), "every token is filled");
        Assert.IsFalse(PythonTokenTests.Applies(new DatabaseModel { DatabaseName = "Shop", SchemaName = "dbo", Dialect = SqlDialect.SqlServer, Tables = [Session()] }, Project(extra: new() { ["PythonTests"] = "true" })), "no Python tests over SQL Server");
        CollectionAssert.Contains(project.ImpliedPlanTemplates.ToList(), "PY_TestTokens");
        CollectionAssert.Contains(project.ImpliedPlanTemplates.ToList(), "RS_TestTokens");
    }
}
