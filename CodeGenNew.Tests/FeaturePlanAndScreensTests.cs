using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using CodeGenNew.TemplateEngine;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests;

/// <summary> FeaturePlanTable (the flags a plan switches on) and FeatureScreens (the screens a flag hides), over a SQLite copy of the tables of <c>Schemas/features.sql</c>. </summary>
[TestClass]
public class FeaturePlanAndScreensTests
{
    private static readonly string[] Tables =
    [
        "CREATE TABLE Plan (PlanId INTEGER PRIMARY KEY AUTOINCREMENT, Name VARCHAR(40) NOT NULL, MonthlyPrice DECIMAL(10,2) NOT NULL DEFAULT 0, BillingPeriod VARCHAR(20) NOT NULL DEFAULT 'month')",
        "CREATE TABLE Subscription (SubscriptionId INTEGER PRIMARY KEY AUTOINCREMENT, TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId), PlanId INTEGER NOT NULL REFERENCES Plan (PlanId), Status VARCHAR(20) NOT NULL, CurrentPeriodStart DATETIME NULL, CurrentPeriodEnd DATETIME NULL, GatewaySubscriptionId VARCHAR(80) NULL)",
        "CREATE TABLE Feature (FeatureId INTEGER PRIMARY KEY AUTOINCREMENT, Name VARCHAR(60) NOT NULL UNIQUE, Description VARCHAR(200) NULL, DefaultEnabled BOOLEAN NOT NULL DEFAULT 0)",
        "CREATE TABLE FeatureOverride (FeatureOverrideId INTEGER PRIMARY KEY AUTOINCREMENT, FeatureId INTEGER NOT NULL REFERENCES Feature (FeatureId), TenantId INTEGER NULL REFERENCES Tenant (TenantId), EmployeeId INTEGER NULL REFERENCES Employee (EmployeeId), IsEnabled BOOLEAN NOT NULL)",
        "CREATE TABLE PlanFeature (PlanFeatureId INTEGER PRIMARY KEY AUTOINCREMENT, PlanId INTEGER NOT NULL REFERENCES Plan (PlanId), FeatureId INTEGER NOT NULL REFERENCES Feature (FeatureId))"
    ];

    private static Dictionary<string, string> Values(Dictionary<string, string>? extra = null)
    {
        var values = new Dictionary<string, string>
        {
            ["Auth"] = "true", ["UserOwnerColumn"] = "EmployeeId", ["FeatureTables"] = "Feature,FeatureOverride", ["SubscriptionTable"] = "Subscription", ["PlanTable"] = "Plan",
            ["FeaturePlanTable"] = "PlanFeature", ["FeatureScreens"] = "Project:new-invoice-screen"
        };
        foreach (var (key, value) in extra ?? new Dictionary<string, string>())
            values[key] = value;
        return TenantPlanTests.Values(values);
    }

    private static async Task<DatabaseModel> ReadAsync(params string[] more)
    {
        string database = TenantPlanTests.CreateDatabase([.. Tables, .. more]);
        try
        {
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            return await provider.BuildAsync(database, "main");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(database);
        }
    }

    [TestMethod]
    public async Task The_plan_link_is_found_by_its_two_foreign_keys_and_gets_no_api_and_no_junction()
    {
        var database = await ReadAsync();
        var project = ProjectSettings.FromValues(Values());
        var (plan, problem) = FeatureFlagTables.Resolve(database, project);
        Assert.IsNull(problem);
        Assert.AreEqual("PlanFeature", plan!.PlanLink!.Table.TableName);
        Assert.AreEqual("FeatureId", plan.PlanLink.Flag.Name);
        Assert.AreEqual("PlanId", plan.PlanLink.Plan.Name);

        var link = database.Tables.Single(t => t.TableName == "PlanFeature");
        Assert.IsTrue(link.IsJunctionTable, "the shape of the table is a junction");
        Assert.IsFalse(link.HasCrudApi(project));
        Assert.IsTrue(FeatureFlagTables.IsPlanLinkTable(project, "PlanFeature"));
        Assert.IsFalse(JunctionPlan.HasPage(link, project), "no junction page for a table only the flag routes use");
        Assert.IsFalse(ProjectPlan.Tables(PlanTableSet.Junction, database, project).Any(t => t.TableName == "PlanFeature"));
    }

    [TestMethod]
    public async Task The_plan_link_needs_the_subscription_tables_and_two_whole_number_keys()
    {
        var database = await ReadAsync();
        StringAssert.Contains(FeatureFlagTables.Resolve(database, ProjectSettings.FromValues(Values(new() { ["SubscriptionTable"] = "", ["PlanTable"] = "" }))).Problem, "needs SubscriptionTable");
        StringAssert.Contains(FeatureFlagTables.Resolve(database, ProjectSettings.FromValues(Values(new() { ["FeaturePlanTable"] = "Nothing" }))).Problem, "not a table");
        StringAssert.Contains(FeatureFlagTables.Resolve(database, ProjectSettings.FromValues(Values(new() { ["FeaturePlanTable"] = "Project" }))).Problem, "needs exactly one whole-number foreign key");
        Assert.IsNull(FeatureFlagTables.Resolve(database, ProjectSettings.FromValues(Values(new() { ["FeaturePlanTable"] = "" }))).Plan!.PlanLink);
    }

    [TestMethod]
    public async Task Every_stack_reads_the_plan_of_the_tenant()
    {
        var database = await ReadAsync();
        var project = ProjectSettings.FromValues(Values());
        StringAssert.Contains(FeatureFlagWriter.File(database, project, "ShopContext", []), "SubscriptionState.OfAsync(context, working, cancellationToken)).PlanId");
        StringAssert.Contains(PythonFeatureWriter.File(database, project), "read_state(session, tenant)[\"plan\"]");
        StringAssert.Contains(RustFeatureWriter.File(database, project), "crate::subscriptions::read_state(pool, tenant)");
        var without = ProjectSettings.FromValues(Values(new() { ["FeaturePlanTable"] = "" }));
        Assert.IsFalse(FeatureFlagWriter.File(database, without, "ShopContext", []).Contains("SubscriptionState"));
    }

    [TestMethod]
    public void A_screen_a_flag_hides_is_named_by_its_table_and_only_with_the_flag_tables()
    {
        var project = ProjectSettings.FromValues(Values(new() { ["FeatureScreens"] = "Project:new-invoice-screen, Organization : beta" }));
        Assert.AreEqual("new-invoice-screen", FeatureScreens.FlagOf(project, "Project"));
        Assert.AreEqual("beta", FeatureScreens.FlagOf(project, "organization"));
        Assert.IsNull(FeatureScreens.FlagOf(project, "Country"));
        Assert.IsNull(FeatureScreens.FlagOf(ProjectSettings.FromValues(Values(new() { ["FeatureTables"] = "" })), "Project"));
        Assert.IsTrue(FeatureScreens.Reads(project));
        Assert.IsFalse(FeatureScreens.Reads(ProjectSettings.FromValues(Values(new() { ["Auth"] = "false" }))));
    }

    [TestMethod]
    public async Task A_field_a_flag_hides_is_named_by_table_column_and_flag_and_only_with_the_flag_tables()
    {
        var database = await ReadAsync();
        var project = ProjectSettings.FromValues(Values(new() { ["FeatureFields"] = "Project.Description:beta-notes, Project.Budget : beta-notes, Organization.Name:other" }));
        Assert.AreEqual("beta-notes", FeatureScreens.FieldFlagOf(project, "project", "description"));
        Assert.IsNull(FeatureScreens.FieldFlagOf(project, "Project", "Name"));
        Assert.IsNull(FeatureScreens.FieldFlagOf(ProjectSettings.FromValues(Values(new() { ["FeatureFields"] = "Project.Description:beta-notes", ["FeatureTables"] = "" })), "Project", "Description"));

        var form = FormModel.For(database.Tables.Single(t => t.TableName == "Project"), project);
        CollectionAssert.AreEqual(new[] { "beta-notes" }, form.FeatureFlags.ToArray(), "a flag that hides two fields is listed once");
        Assert.AreEqual("beta-notes", form.FeatureOf(form.Columns.Single(c => c.Name == "Budget")));
        Assert.IsNull(form.FeatureOf(form.Columns.Single(c => c.Name == "Name")));
        Assert.AreEqual(0, form.FeatureIndex("beta-notes"));
    }

    private async Task<string> FormOf(string template, string table, Dictionary<string, string> extra)
    {
        var database = await ReadAsync();
        var values = new Dictionary<string, string> { ["FeatureFields"] = "Project.Description:beta-notes", ["Stacks"] = "api,react,angular,blazor,uno,winui3" };
        foreach (var (key, value) in extra)
            values[key] = value;
        var project = ProjectSettings.FromValues(Values(values));
        var result = await Repo.Cache.RunAsync(Repo.Template(template), database.Tables.Single(t => t.TableName == table), project);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.Replace("\r\n", "\n");
    }

    [TestMethod]
    public async Task Every_front_end_shows_a_field_in_its_form_only_while_its_flag_is_on()
    {
        string react = await FormOf("TSX_Page_v1.tt", "Project", []);
        StringAssert.Contains(react, "const featureFlag0 = useFeature('beta-notes');");
        StringAssert.Contains(react, "{featureFlag0 && (");
        StringAssert.Contains(react, "import { useFeature } from '../api/features';");

        string angular = await FormOf("TS_Component_v1.tt", "Project", []);
        StringAssert.Contains(angular, "features.enabled('beta-notes')");
        StringAssert.Contains(angular, "protected readonly features = inject(FeatureService);");

        string blazor = await FormOf("BLZ_Page_v1.tt", "Project", []);
        StringAssert.Contains(blazor, "@inject FeatureFlags Features");
        StringAssert.Contains(blazor, "@if (Features.Enabled(\"beta-notes\"))");

        string uno = await FormOf("UNO_DetailScreen_v1.tt", "Project", []);
        StringAssert.Contains(uno, "Visibility=\"{x:Bind ViewModel.FeatureVisibility('beta-notes')}\"");
        StringAssert.Contains(uno, "if (FeatureOn(\"beta-notes\"))");
        StringAssert.Contains(uno, "_api.Features.Enabled(flag)");

        string winui3 = await FormOf("WinUI3_DetailScreen_v1.tt", "Project", []);
        StringAssert.Contains(winui3, "Visibility=\"{x:Bind ViewModel.FeatureVisibility('beta-notes')}\"");
        StringAssert.Contains(winui3, "DesktopFeatures.Enabled(flag)");

        string plain = await FormOf("TSX_Page_v1.tt", "Project", new() { ["FeatureFields"] = "" });
        Assert.IsFalse(plain.Contains("useFeature"), "without FeatureFields the form is unchanged");
    }

    [TestMethod]
    public async Task The_uno_app_reads_the_flags_from_the_api_and_the_winui3_app_reads_them_from_its_database()
    {
        var database = await ReadAsync();
        var project = ProjectSettings.FromValues(Values(new() { ["Stacks"] = "api,uno,winui3" }));
        Assert.IsTrue(FeatureScreenCode.DesktopReads(database, project));
        Assert.IsTrue(FeatureScreenCode.DesktopHasPage(database, project));
        CollectionAssert.IsSubsetOf(new[] { "UNO_Features", "WinUI3_Features" }, project.ImpliedPlanTemplates.ToList());

        string winui3 = FeatureScreenCode.DesktopFile(database, project, "ShopContext", []);
        StringAssert.Contains(winui3, "(await SubscriptionGuard.ReadAsync(context, working, includeLimits: false)).PlanId");
        StringAssert.Contains(winui3, "public static class DesktopFeatures");
        StringAssert.Contains(winui3, "FeatureFlags.AllAsync(context, user.TenantId, user.OwnerId)");
        var withoutPlan = ProjectSettings.FromValues(Values(new() { ["Stacks"] = "api,winui3", ["FeaturePlanTable"] = "" }));
        Assert.IsFalse(FeatureScreenCode.DesktopFile(database, withoutPlan, "ShopContext", []).Contains("SubscriptionGuard"));

        string uno = FeatureScreenCode.UnoFlags("Shop.Services");
        StringAssert.Contains(uno, "GetFromJsonAsync<Dictionary<string, bool>>(\"/api/features\")");
        Assert.IsFalse(FeatureScreenCode.DesktopReads(database, ProjectSettings.FromValues(Values(new() { ["Stacks"] = "api,uno" }))), "the WinUI3 app reads the flags only when it is a stack of the project");
    }

    [TestMethod]
    public async Task The_menus_of_the_uno_and_winui3_apps_hide_a_screen_while_its_flag_is_off_and_list_the_features_page()
    {
        var database = await ReadAsync();
        var values = new Dictionary<string, string> { ["Stacks"] = "api,uno,winui3" };
        var project = ProjectSettings.FromValues(Values(values));
        var uno = await Repo.Cache.RunAsync(Repo.Template("UNO_Screens_v1.tt"), database, project);
        Assert.IsTrue(uno.Success, string.Join(" | ", uno.Errors));
        string unoText = uno.GeneratedText!.Replace("\r\n", "\n");
        StringAssert.Contains(unoText, "(string Tag, string Label, string[]? Roles, string? Feature)[] Screens");
        StringAssert.Contains(unoText, "(\"Project\", \"Project\", null, \"new-invoice-screen\"),");
        StringAssert.Contains(unoText, "(\"Features\", \"Features\", [\"Administrator\"], null),");
        StringAssert.Contains(unoText, "if (feature is not null && !_api.Features.Enabled(feature))");
        StringAssert.Contains(unoText, "\"Features\" => new FeaturesPage(_api),");
        StringAssert.Contains(unoText, "public FeatureFlags Features => _features ??= new(http, session);");

        var winui3 = await Repo.Cache.RunAsync(Repo.Template("WinUI3_Screens_v1.tt"), database, project);
        Assert.IsTrue(winui3.Success, string.Join(" | ", winui3.Errors));
        string winText = winui3.GeneratedText!.Replace("\r\n", "\n");
        StringAssert.Contains(winText, "(feature is null || DesktopFeatures.Enabled(feature))");
        StringAssert.Contains(winText, "Tag = \"Features\"");
        StringAssert.Contains(winText, "\"Features\" => new FeaturesPage(_context),");

        var files = await Repo.Cache.RunAsync(Repo.Template("WinUI3_Features_v1.tt"), database, project);
        Assert.IsTrue(files.Success, string.Join(" | ", files.Errors));
        StringAssert.Contains(files.GeneratedText!, "Repositories/FeatureFlags.cs");
        StringAssert.Contains(files.GeneratedText!, "FeaturesPage.xaml.cs");
        var unoFiles = await Repo.Cache.RunAsync(Repo.Template("UNO_Features_v1.tt"), database, project);
        Assert.IsTrue(unoFiles.Success, string.Join(" | ", unoFiles.Errors));
        StringAssert.Contains(unoFiles.GeneratedText!, "Services/FeatureFlags.cs");
        StringAssert.Contains(unoFiles.GeneratedText!, "ApiSupport.ProblemDetailAsync");
    }

    [TestMethod]
    public async Task The_flags_are_kept_for_FeatureCacheSeconds_in_every_api_and_a_write_forgets_them()
    {
        var database = await ReadAsync();
        var on = ProjectSettings.FromValues(Values(new() { ["FeatureCacheSeconds"] = "30" }));
        var off = ProjectSettings.FromValues(Values());
        Assert.AreEqual(30, on.FeatureCacheSeconds);
        Assert.AreEqual(0, off.FeatureCacheSeconds);
        Assert.AreEqual(0, ProjectSettings.FromValues(Values(new() { ["FeatureCacheSeconds"] = "-5" })).FeatureCacheSeconds);

        string dotnet = FeatureFlagWriter.File(database, on, "ShopContext", []);
        StringAssert.Contains(dotnet, "CacheFor = TimeSpan.FromSeconds(30)");
        StringAssert.Contains(dotnet, "FeatureFlags.Clear();");
        StringAssert.Contains(FeatureFlagWriter.File(database, off, "ShopContext", []), "TimeSpan.FromSeconds(0)");
        StringAssert.Contains(PythonFeatureWriter.File(database, on), "CACHE_SECONDS = 30");
        StringAssert.Contains(PythonFeatureWriter.File(database, on), "    clear_cache()\n    return all_flags(");
        StringAssert.Contains(RustFeatureWriter.File(database, on), "const CACHE_SECONDS: u64 = 30;");
        StringAssert.Contains(RustFeatureWriter.File(database, on), "    clear_cache();\n    Ok(Json(");
    }

    [TestMethod]
    public async Task The_generated_tests_of_every_api_try_a_flag_a_plan_lists_and_the_tenants_override_over_it()
    {
        var database = await ReadAsync();
        var project = ProjectSettings.FromValues(Values(new() { ["ApiTests"] = "true", ["PythonTests"] = "true", ["RustTests"] = "true" }));
        StringAssert.Contains(ApiFeatureTests.ClassText(database, project), "A_flag_the_plan_lists_is_on_for_a_tenant_on_that_plan_and_the_tenants_override_wins");
        StringAssert.Contains(ApiFeatureTests.ClassText(database, project), "FeatureTestSupport.LinkPlanAsync(ApiFixture.Services, plan, flag)");
        StringAssert.Contains(FeatureFlagWriter.File(database, project, "ShopContext", []), "public static async Task LinkPlanAsync(");
        StringAssert.Contains(PythonFeatureTests.File(database, project), "def test_a_flag_the_plan_lists_is_on_for_a_tenant_on_that_plan_and_the_tenants_override_wins(world, application):");
        StringAssert.Contains(RustFeatureTests.File(database, project), "async fn a_flag_the_plan_lists_is_on_for_a_tenant_on_that_plan_and_the_tenants_override_wins()");

        var without = ProjectSettings.FromValues(Values(new() { ["ApiTests"] = "true", ["PythonTests"] = "true", ["RustTests"] = "true", ["FeaturePlanTable"] = "" }));
        Assert.IsFalse(ApiFeatureTests.ClassText(database, without).Contains("the_plan_lists"));
        Assert.IsFalse(FeatureFlagWriter.File(database, without, "ShopContext", []).Contains("LinkPlanAsync"));
        Assert.IsFalse(PythonFeatureTests.File(database, without).Contains("the_plan_lists"));
        Assert.IsFalse(RustFeatureTests.File(database, without).Contains("the_plan_lists"));
    }

    [TestMethod]
    public async Task The_page_and_the_menu_entries_follow_the_tenant_column_and_the_flags()
    {
        var database = await ReadAsync();
        var project = ProjectSettings.FromValues(Values());
        Assert.IsTrue(FeatureScreens.HasPage(database, project));
        var menu = BlazorMenu.Entries(database, project);
        Assert.AreEqual("new-invoice-screen", menu.Single(e => e.Route == "project").Feature);
        Assert.IsTrue(menu.Any(e => e.Route == "features" && e.Roles.SequenceEqual([project.AuthAdminRole])));
        Assert.IsFalse(BlazorMenu.Entries(database, ProjectSettings.FromValues(Values(new() { ["FeatureTables"] = "" }))).Any(e => e.Route == "features" || e.Feature is not null));
        CollectionAssert.IsSubsetOf(new[] { "TSX_Features", "TS_Features", "BLZ_Features", "API_Features", "PY_Features", "RS_Features" }, project.ImpliedPlanTemplates.ToList());
    }

    [TestMethod]
    public async Task Every_front_end_leaves_a_column_out_of_its_grid_and_its_column_list_while_its_flag_is_off()
    {
        string react = await FormOf("TSX_Page_v1.tt", "Project", []);
        StringAssert.Contains(react, "const hiddenColumns = [...(featureFlag0 ? [] : ['Description'])];");
        StringAssert.Contains(react, "chosen.includes(name) && !hiddenColumns.includes(name)");
        StringAssert.Contains(react, "columnList.filter((column) => !hiddenColumns.includes(column.name))");

        string angular = await FormOf("TS_Component_v1.tt", "Project", []);
        StringAssert.Contains(angular, "() => [...(this.features.enabled('beta-notes') ? [] : ['Description'])]");
        StringAssert.Contains(await FormOf("TS_GridColumns_v1.tt", "Project", []), "!this.hidden().includes(name)");

        string blazor = await FormOf("BLZ_Page_v1.tt", "Project", []);
        StringAssert.Contains(blazor, "\"Description\" => !Features.Enabled(\"beta-notes\")");
        StringAssert.Contains(blazor, "chosen.Contains(name) && !FlagOff(name)");
        StringAssert.Contains(blazor, "AllColumns.Where(c => !FlagOff(c.Name))");

        foreach (var (template, source) in new[] { ("UNO_MasterScreen_v1.tt", "_api.Features.Enabled"), ("WinUI3_MasterScreen_v1.tt", "DesktopFeatures.Enabled") })
        {
            string desktop = await FormOf(template, "Project", []);
            StringAssert.Contains(desktop, "\"Description\" => " + source + "(\"beta-notes\")");
            StringAssert.Contains(desktop, "IsOffered(name) && ");
        }

        string plain = await FormOf("TSX_Page_v1.tt", "Project", new() { ["FeatureFields"] = "" });
        Assert.IsFalse(plain.Contains("hiddenColumns"), "without FeatureFields the grid is unchanged");
        Assert.IsFalse((await FormOf("BLZ_Page_v1.tt", "Project", new() { ["FeatureFields"] = "" })).Contains("FlagOff"));
    }

    [TestMethod]
    public async Task A_child_grid_of_a_master_detail_page_leaves_a_column_out_while_its_flag_is_off()
    {
        string database = TenantPlanTests.CreateDatabase(Tables);
        string output = Path.Combine(Path.GetTempPath(), "codegen_childflags_" + Guid.NewGuid().ToString("N"));
        try
        {
            async Task<string[]> Generate(string fields, string editable = "")
            {
                var values = Values(new() { ["FeatureFields"] = fields, ["DetailMasterTables"] = "Project", ["EditableChildren"] = editable, ["Stacks"] = "api,react,angular,blazor,uno,winui3", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = database });
                var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
                var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
                var report = await Repo.GenerateAsync(provider, new GenerateOptions
                {
                    Project = ProjectSettings.FromValues(values), Stacks = ["React", "Angular", "Blazor", "Uno", "WinUI3"], OutputDirectory = output, DatabaseName = database, Schema = "main", Essentials = true, Force = true
                });
                Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
                string Read(string name) => string.Concat(Directory.EnumerateFiles(output, name, SearchOption.AllDirectories).Where(f => !f.Contains("node_modules") && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)).Select(File.ReadAllText));
                return [Read("ProjectDetailMasterPage.tsx"), Read("project-detail-master.component.ts") + Read("project-detail-master.component.html"), Read("ProjectPage.razor"), Read("ProjectDetailMasterViewModel.cs"), string.Concat(Directory.EnumerateFiles(output, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)).Select(File.ReadAllText).Where(s => s.Contains("FeatureOn")))];
            }

            var flagged = await Generate("ProjectTask.Hours:beta-notes");
            StringAssert.Contains(flagged[0], "const projectTaskFlagHidden = [...(projectTaskFeature0 ? [] : ['hours'])];");
            StringAssert.Contains(flagged[0], "projectTaskColumns.filter((col) => !projectTaskFlagHidden.includes(col))");
            StringAssert.Contains(flagged[1], "get projectTaskShown(): string[]");
            StringAssert.Contains(flagged[2], "@if (Features.Enabled(\"beta-notes\")) { <th>Hours</th> }");
            StringAssert.Contains(flagged[3], "\"Hours\" => _api.Features.Enabled(\"beta-notes\")");
            StringAssert.Contains(flagged[3], "\"Hours\" => DesktopFeatures.Enabled(\"beta-notes\")");

            var editable = await Generate("ProjectTask.Hours:beta-notes", "Project.ProjectTask");
            StringAssert.Contains(editable[0], "const projectTaskFeature0 = useFeature('beta-notes');");
            StringAssert.Contains(editable[0], "{projectTaskFeature0 && (<th>Hours");
            StringAssert.Contains(editable[1], "<th *ngIf=\"features.enabled('beta-notes')\">Hours");
            StringAssert.Contains(editable[2], "@if (Features.Enabled(\"beta-notes\")) { <th>Hours");
            StringAssert.Contains(editable[4], "FeatureOn(\"beta-notes\")");
            StringAssert.Contains(editable[4], "public Func<string, bool> FeatureOn");

            var plain = await Generate("Project.Description:beta-notes");
            Assert.IsFalse(plain[0].Contains("FlagHidden"), "a flag on another table leaves the child grid unchanged");
            Assert.IsFalse(plain[1].Contains("projectTaskShown"));
            Assert.IsFalse(plain[2].Contains("<th>Hours</th> }"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(database);
            if (Directory.Exists(output))
                Directory.Delete(output, true);
        }
    }

    [TestMethod]
    public async Task The_odata_page_hides_a_field_and_its_column_like_the_ordinary_page()
    {
        string odata = await FormOf("TSX_Page_v1.tt", "Project", new() { ["OData"] = "true", ["ODataTables"] = "Project" });
        StringAssert.Contains(odata, "useFeature('beta-notes')");
        StringAssert.Contains(odata, "hiddenColumns");
    }

    private async Task<string> EssentialOf(string template, string refreshSeconds)
    {
        var database = await ReadAsync();
        var project = ProjectSettings.FromValues(Values(new() { ["Stacks"] = "api,react,angular,blazor,uno,winui3", ["FeatureRefreshSeconds"] = refreshSeconds }));
        var result = await Repo.Cache.RunAsync(Repo.Template(template), database, project);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.Replace("\r\n", "\n");
    }

    [TestMethod]
    public async Task With_a_refresh_interval_every_front_end_reads_the_flags_again_on_a_timer_and_without_one_it_does_not()
    {
        StringAssert.Contains(await EssentialOf("TSX_EssentialShell_v1.tt", "30"), "setInterval(() => { getFeatures().catch(() => undefined); }, 30000)");
        StringAssert.Contains(await EssentialOf("TS_Features_v1.tt", "30"), "setInterval(() => this.get().subscribe({ error: () => undefined }), 30000)");
        string blazor = await EssentialOf("BLZ_Features_v1.tt", "30");
        StringAssert.Contains(blazor, "new Timer(_ => _ = RefreshAsync(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30))");
        StringAssert.Contains(blazor, "public async Task RefreshAsync()");
        string uno = await EssentialOf("UNO_EssentialApp_v1.tt", "30");
        StringAssert.Contains(uno, "Interval = TimeSpan.FromSeconds(30)");
        StringAssert.Contains(uno, "if (await _api.Features.RefreshAsync())");
        StringAssert.Contains(await EssentialOf("WinUI3_EssentialMainWindow_v1.tt", "30"), "Interval = TimeSpan.FromSeconds(30)");

        foreach (string template in new[] { "TSX_EssentialShell_v1.tt", "TS_Features_v1.tt", "BLZ_Features_v1.tt", "UNO_EssentialApp_v1.tt", "WinUI3_EssentialMainWindow_v1.tt" })
        {
            string off = await EssentialOf(template, "0");
            Assert.IsFalse(off.Contains("FeatureRefreshSeconds") || off.Contains("flagTimer") || off.Contains("RefreshAsync"), template + " is unchanged without the setting");
        }
    }
}
