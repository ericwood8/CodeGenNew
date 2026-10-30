using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests;

/// <summary> TreeTables (a table that refers to itself), SoftDeleteTables (rows flagged instead of removed) and EffectiveDatedTables (rows valid for a period). </summary>
[TestClass]
public class RowShapePlanTests
{
    private static string CreateDatabase()
    {
        string path = Path.Combine(Path.GetTempPath(), "codegen_shapes_" + Guid.NewGuid().ToString("N") + ".db");
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        string[] statements =
        [
            "CREATE TABLE Category (CategoryId INTEGER PRIMARY KEY AUTOINCREMENT, Name VARCHAR(60) NOT NULL, Description VARCHAR(200) NULL, SortOrder INTEGER NOT NULL DEFAULT 0, ParentCategoryId INTEGER NULL REFERENCES Category (CategoryId), IsDeleted BIT NOT NULL DEFAULT 0, DeletedOn DATETIME NULL, DeletedBy VARCHAR(50) NULL)",
            "CREATE TABLE Customer (CustomerId INTEGER PRIMARY KEY AUTOINCREMENT, Name VARCHAR(80) NOT NULL, Email VARCHAR(120) NULL, City VARCHAR(60) NULL, Notes VARCHAR(200) NULL, IsDeleted BIT NOT NULL DEFAULT 0)",
            "CREATE TABLE Item (ItemId INTEGER PRIMARY KEY AUTOINCREMENT, Name VARCHAR(80) NOT NULL, Sku VARCHAR(30) NOT NULL, Unit VARCHAR(10) NOT NULL DEFAULT 'each', Weight DECIMAL(9,2) NOT NULL DEFAULT 0)",
            "CREATE TABLE Price (PriceId INTEGER PRIMARY KEY AUTOINCREMENT, ItemId INTEGER NOT NULL REFERENCES Item (ItemId), Amount DECIMAL(10,2) NOT NULL, Currency VARCHAR(3) NOT NULL DEFAULT 'USD', ValidFrom DATE NOT NULL, ValidTo DATE NULL, Note VARCHAR(100) NULL)"
        ];
        foreach (string statement in statements)
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
        return path;
    }

    private static async Task<(bool Success, List<string> Errors, Func<string, string> Read, List<string> Files)> GenerateAsync(Dictionary<string, string>? extra = null, string stacks = "api")
    {
        string database = CreateDatabase();
        string output = Path.Combine(Path.GetTempPath(), "codegen_shapes_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            var values = new Dictionary<string, string>
            {
                ["ProjectName"] = "Shop", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["EnumTables"] = "none", ["Stacks"] = stacks, ["Dashboard"] = "false", ["AccessMode"] = "Ef",
                ["TreeTables"] = "Category:ParentCategoryId:3", ["SoftDeleteTables"] = "Category,Customer", ["EffectiveDatedTables"] = "Price:ValidFrom:ValidTo", ["ApiTests"] = "true", ["ApiHttp"] = "true"
            };
            foreach (var (key, value) in extra ?? new Dictionary<string, string>())
                values[key] = value;
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            var report = await Repo.GenerateAsync(provider, new GenerateOptions
            {
                Project = ProjectSettings.FromValues(values), Stacks = stacks.Split(","), OutputDirectory = output, DatabaseName = database, Schema = "main", Essentials = true
            });
            var files = Directory.GetFiles(output, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(output, f).Replace('\\', '/')).ToList();
            var contents = files.ToDictionary(f => f, f => File.ReadAllText(Path.Combine(output, f)).Replace("\r\n", "\n"));
            return (report.Success, report.Errors.ToList(), name => contents[files.Single(f => f.EndsWith(name))], files);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(database);
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    private static async Task<DatabaseModel> ReadAsync(Dictionary<string, string> values)
    {
        string database = CreateDatabase();
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

    private static ProjectSettings Project(Dictionary<string, string> extra)
    {
        var values = new Dictionary<string, string> { ["AccessMode"] = "Ef", ["DatabaseProvider"] = "Sqlite" };
        foreach (var (key, value) in extra)
            values[key] = value;
        return ProjectSettings.FromValues(values);
    }

    [TestMethod]
    public void The_settings_name_a_table_and_what_its_shape_needs()
    {
        var project = Project(new() { ["TreeTables"] = "Category, Comment:ParentId:4", ["SoftDeleteTables"] = "Customer,Order:IsRemoved", ["EffectiveDatedTables"] = "Price:ValidFrom:ValidTo:ItemId+CurrencyId" });

        CollectionAssert.AreEqual(new[] { ("Category", (string?)null, (string?)null), ("Comment", "ParentId", "4") }, TreePlan.Entries(project).ToArray());
        CollectionAssert.AreEqual(new[] { ("Customer", "IsDeleted"), ("Order", "IsRemoved") }, SoftDeletePlan.Entries(project).ToArray());
        CollectionAssert.AreEqual(new[] { ("Price", (string?)"ValidFrom", (string?)"ValidTo", (string?)"ItemId+CurrencyId") }, EffectiveDatePlan.Entries(project).ToArray());
        Assert.IsTrue(RowShapePlan.Any(project));
        Assert.IsFalse(RowShapePlan.Any(Project(new())));
        Assert.Contains("API_RowShapes", project.ImpliedPlanTemplates.ToList());
        Assert.DoesNotContain("API_RowShapes", Project(new()).ImpliedPlanTemplates.ToList());
    }

    [TestMethod]
    public async Task The_plans_find_the_parent_the_flag_and_the_groups_of_each_table()
    {
        var values = new Dictionary<string, string> { ["TreeTables"] = "Category", ["SoftDeleteTables"] = "Category,Customer", ["EffectiveDatedTables"] = "Price:ValidFrom:ValidTo", ["EnumTables"] = "none" };
        var database = await ReadAsync(values);
        var project = Project(values);
        var category = database.Tables.Single(t => t.TableName == "Category");

        var tree = TreePlan.For(category, project)!;
        Assert.AreEqual("ParentCategoryId", tree.Parent.Name, "the only foreign key to itself is the parent");
        Assert.AreEqual(0, tree.MaxDepth);
        Assert.AreEqual("Name", tree.Text!.Name);
        var soft = SoftDeletePlan.For(category, project)!;
        Assert.AreEqual("IsDeleted", soft.Flag.Name);
        Assert.AreEqual("DeletedOn", soft.DeletedOn!.Name);
        Assert.AreEqual("DeletedBy", soft.DeletedBy!.Name);
        Assert.IsNull(SoftDeletePlan.For(database.Tables.Single(t => t.TableName == "Customer"), project)!.DeletedOn, "the stamps are optional");
        var effective = EffectiveDatePlan.For(database.Tables.Single(t => t.TableName == "Price"), project)!;
        CollectionAssert.AreEqual(new[] { "ItemId" }, effective.Groups.Select(g => g.Name).ToArray(), "the groups are the foreign keys unless named");
        Assert.IsEmpty(RowShapePlan.Problems(database, project));
    }

    [TestMethod]
    public async Task A_table_that_cannot_have_the_shape_is_named_with_the_reason()
    {
        var database = await ReadAsync(new() { ["EnumTables"] = "none" });
        string Problems(Dictionary<string, string> values) => string.Join(" | ", RowShapePlan.Problems(database, Project(values)));

        Assert.Contains("no foreign key to its own key", Problems(new() { ["TreeTables"] = "Customer" }));
        Assert.Contains("must be a whole-number column that allows NULL", Problems(new() { ["TreeTables"] = "Category:Name" }));
        Assert.Contains("depth limit x must be a whole number", Problems(new() { ["TreeTables"] = "Category:ParentCategoryId:x" }));
        Assert.Contains("needs a yes/no column IsDeleted", Problems(new() { ["SoftDeleteTables"] = "Item" }));
        Assert.Contains("is in SingleRowTables", Problems(new() { ["SingleRowTables"] = "Price", ["EffectiveDatedTables"] = "Price:ValidFrom:ValidTo" }));
        Assert.Contains("needs the first and last day columns", Problems(new() { ["EffectiveDatedTables"] = "Price" }));
        Assert.Contains("same type", Problems(new() { ["EffectiveDatedTables"] = "Price:ValidFrom:Currency" }));
        Assert.Contains("Missing is not a table", Problems(new() { ["SoftDeleteTables"] = "Missing" }));
        var sqlServer = new DatabaseModel { DatabaseName = "Shop", SchemaName = "dbo", Dialect = SqlDialect.SqlServer, Tables = database.Tables };
        Assert.Contains("need AccessMode=Ef", string.Join(" | ", RowShapePlan.Problems(sqlServer, ProjectSettings.FromValues(new Dictionary<string, string> { ["AccessMode"] = "Routines", ["DatabaseProvider"] = "SqlServer", ["TreeTables"] = "Category" }))));
    }

    [TestMethod]
    public async Task A_row_shape_that_cannot_be_served_fails_the_run_with_the_reason()
    {
        var (success, errors, _, _) = await GenerateAsync(new() { ["TreeTables"] = "Customer" });

        Assert.IsFalse(success);
        Assert.IsTrue(errors.Any(e => e.Contains("no foreign key to its own key")), string.Join(" | ", errors));
    }

    [TestMethod]
    public async Task The_API_checks_each_save_and_delete_and_maps_the_extra_routes()
    {
        var (success, errors, read, _) = await GenerateAsync();

        Assert.IsTrue(success, string.Join(" | ", errors));
        string shapes = read("Apis/RowShapes.cs");
        foreach (string text in new[] { "public const int MaxDepth = 3;", "CheckTreeAsync", "ids.Contains(x.ParentCategoryId.Value)", "CheckChildrenAsync", "promote-children", "\"/api/categories/tree\"",
            "\"/api/categories/deleted\"", "\"/api/categories/{id}/restore\"", "\"/api/customers/deleted\"", "IgnoreQueryFilters()", "CheckEffectiveAsync", "\"/api/prices/current\"", "RowShapeApi" })
            Assert.Contains(text, shapes);
        Assert.Contains("x.ValidFrom <= to) && (x.ValidTo == null || x.ValidTo >= from)", shapes, "both ends are inclusive and an empty end is open");
        Assert.Contains("RowShapeApi.Register(app);", read("ApiRegistration.cs"));
        string category = read("Apis/CategoryApi.cs");
        Assert.Contains("CategoryShape.CheckSaveAsync(context, row, isNew)", category);
        Assert.Contains("CategoryShape.CheckDeleteAsync(context, id, null)", category);
        Assert.DoesNotContain("BeforeSaveAsync", read("Apis/CustomerApi.cs"), "a soft-delete table only changes the delete");
        Assert.Contains("PriceShape.CheckSaveAsync", read("Apis/PriceApi.cs"));
        Assert.DoesNotContain("BeforeDeleteAsync", read("Apis/PriceApi.cs"));
    }

    [TestMethod]
    public async Task The_Python_and_Rust_APIs_check_a_tree_and_map_its_routes()
    {
        var (success, errors, readPython, _) = await GenerateAsync(stacks: "python");
        Assert.IsTrue(success, string.Join(" | ", errors));
        string python = readPython("app/routes/category.py");
        foreach (string text in new[] { "TREE_MAX_DEPTH = 3", "def check_tree(session: Session, row_id, parent_id) -> None:", "check_tree(session, None, body.parent_category_id)",
            "check_tree(session, id, body.changes().get(\"parent_category_id\", row.parent_category_id))", "refuse_children(session, id)", "@router.get(\"/api/categories/tree\")",
            "@router.post(\"/api/categories/{id}/promote-children\")", "problem_type=\"/problems/tree-cycle\"", "problem_type=\"/problems/tree-has-children\"" })
            Assert.Contains(text, python);
        Assert.IsLessThan(python.IndexOf("/api/categories/{id}", StringComparison.Ordinal), python.IndexOf("/api/categories/tree", StringComparison.Ordinal), "the fixed path comes before the route of one row");
        Assert.DoesNotContain("check_tree", readPython("app/routes/customer.py"));

        var (rustSuccess, rustErrors, readRust, _) = await GenerateAsync(stacks: "rust");
        Assert.IsTrue(rustSuccess, string.Join(" | ", rustErrors));
        string rust = readRust("src/routes/category.rs");
        foreach (string text in new[] { "const TREE_MAX_DEPTH: i32 = 3;", "async fn check_tree(pool: &Pool, row_id: Option<i32>, parent: Option<i32>) -> ApiResult<()> {", "check_tree(&pool, None, row.parent_category_id).await?;",
            "check_tree(&pool, Some(id), row.parent_category_id).await?;", "refuse_children(&pool, id).await?;", ".route(\"/api/categories/tree\", get(tree))",
            ".route(\"/api/categories/{id}/promote-children\", post(promote_children))", "\"/problems/tree-cycle\"" })
            Assert.Contains(text, rust);
        Assert.DoesNotContain("check_tree", readRust("src/routes/customer.rs"));
    }

    [TestMethod]
    public async Task The_Python_and_Rust_APIs_hide_flagged_rows_and_flag_instead_of_deleting()
    {
        var (success, errors, readPython, _) = await GenerateAsync(stacks: "python");
        Assert.IsTrue(success, string.Join(" | ", errors));
        string python = readPython("app/routes/category.py");
        foreach (string text in new[] { "@event.listens_for(Session, \"do_orm_execute\")", "with_loader_criteria(Category, Category.is_deleted.is_(False), include_aliases=True)", "row.is_deleted = True",
            "row.deleted_on = datetime.now()", "@router.get(\"/api/categories/deleted\"", "@router.post(\"/api/categories/{id}/restore\"", "problem_type=\"/problems/not-deleted\"" })
            Assert.Contains(text, python);
        Assert.DoesNotContain("session.delete(row)", python);
        Assert.IsLessThan(python.IndexOf("/api/categories/{id}\"", StringComparison.Ordinal), python.IndexOf("/api/categories/deleted", StringComparison.Ordinal), "the fixed path comes before the route of one row");

        var (rustSuccess, rustErrors, readRust, _) = await GenerateAsync(stacks: "rust");
        Assert.IsTrue(rustSuccess, string.Join(" | ", rustErrors));
        string repo = readRust("src/repos/category.rs");
        foreach (string text in new[] { "const SOFT_DELETE: &str", "\"IsDeleted\" = 0", "pub async fn soft_delete(", "pub async fn restore(", "pub async fn deleted(" })
            Assert.Contains(text, repo);
        string rust = readRust("src/routes/category.rs");
        foreach (string text in new[] { "repo::soft_delete(&pool, id", ".route(\"/api/categories/deleted\", get(deleted_rows))", ".route(\"/api/categories/{id}/restore\", post(restore))", "\"/problems/not-deleted\"" })
            Assert.Contains(text, rust);
    }

    [TestMethod]
    public async Task The_Python_and_Rust_APIs_check_a_period_and_answer_current()
    {
        var (success, errors, readPython, _) = await GenerateAsync(stacks: "python");
        Assert.IsTrue(success, string.Join(" | ", errors));
        string python = readPython("app/routes/price.py");
        foreach (string text in new[] { "def check_effective(session: Session, row_id, valid_from, valid_to, groups: dict) -> None:", "check_effective(session, None, body.valid_from, body.valid_to, {\"item_id\": body.item_id})",
            "check_effective(session, id, body.changes().get(\"valid_from\", row.valid_from)", "@router.get(\"/api/prices/current\"", "problem_type=\"/problems/effective-overlap\"", "problem_type=\"/problems/effective-range\"" })
            Assert.Contains(text, python);
        Assert.IsLessThan(python.IndexOf("/api/prices/{id}\"", StringComparison.Ordinal), python.IndexOf("/api/prices/current", StringComparison.Ordinal), "the fixed path comes before the route of one row");
        Assert.DoesNotContain("check_effective", readPython("app/routes/customer.py"));

        var (rustSuccess, rustErrors, readRust, _) = await GenerateAsync(stacks: "rust");
        Assert.IsTrue(rustSuccess, string.Join(" | ", rustErrors));
        string rust = readRust("src/routes/price.rs");
        foreach (string text in new[] { "async fn check_effective(pool: &Pool, row_id: Option<i32>, row: &Price)", "check_effective(&pool, None, &row).await?;", "check_effective(&pool, Some(id), &row).await?;",
            ".route(\"/api/prices/current\", get(current))", "\"/problems/effective-overlap\"" })
            Assert.Contains(text, rust);
        Assert.DoesNotContain("check_effective", readRust("src/routes/customer.rs"));
    }

    [TestMethod]
    public async Task Every_front_end_has_a_show_deleted_switch_with_restore_and_a_valid_on_day()
    {
        var (success, errors, react, _) = await GenerateAsync(stacks: "react");
        Assert.IsTrue(success, string.Join(" | ", errors));
        string page = react("CustomerPage.tsx");
        foreach (string text in new[] { "Show deleted", "<RowShapeDialog", ".getDeleted()", ".restore(row.customerId!)" })
            Assert.Contains(text, page);
        Assert.Contains("Valid on...", react("PricePage.tsx"));
        Assert.Contains("export function RowShapeDialog", react("RowShapeDialog.tsx"));

        var (angularSuccess, angularErrors, angular, _) = await GenerateAsync(stacks: "angular");
        Assert.IsTrue(angularSuccess, string.Join(" | ", angularErrors));
        Assert.Contains("(click)=\"shapeView = 'deleted'\"", angular("customer.component.html"));
        Assert.Contains("<app-row-shape-dialog", angular("price.component.html"));
        Assert.Contains("selector: 'app-row-shape-dialog'", angular("row-shape-dialog.ts"));

        var (blazorSuccess, blazorErrors, blazor, _) = await GenerateAsync(stacks: "blazor");
        Assert.IsTrue(blazorSuccess, string.Join(" | ", blazorErrors));
        Assert.Contains("@onclick=\"ShowDeleted\"", blazor("CustomerPage.razor"));
        Assert.Contains("Client.GetCurrentAsync(day)", blazor("PricePage.razor"));
        Assert.Contains("@typeparam TRow", blazor("RowShapeDialog.razor"));

        var (odataSuccess, odataErrors, odata, _) = await GenerateAsync(new() { ["OData"] = "true", ["ODataTables"] = "Customer,Price", ["ODataScreens"] = "true", ["Screens"] = "Customer,Price" }, stacks: "api,react,blazor,uno");
        Assert.IsTrue(odataSuccess, string.Join(" | ", odataErrors));
        Assert.Contains("<RowShapeDialog", odata("CustomerODataPage.tsx"));
        Assert.Contains("import { customerApi }", odata("CustomerODataPage.tsx"));
        Assert.Contains("@inject CustomerClient ShapeClient", odata("CustomerODataPage.razor"));
        Assert.Contains("ViewModel.ShowDeleted", odata("CustomerODataListPage.xaml"));

        foreach (var stack in new[] { "winui3", "uno" })
        {
            var (desktopSuccess, desktopErrors, desktop, _) = await GenerateAsync(stacks: stack);
            Assert.IsTrue(desktopSuccess, string.Join(" | ", desktopErrors));
            Assert.Contains("ViewModel.ShowDeleted", desktop("CustomerListPage.xaml"));
            Assert.Contains("Click=\"OnRestoreClick\"", desktop("CustomerListPage.xaml"));
            Assert.Contains("ViewModel.ValidOnDay", desktop("PriceListPage.xaml"));
        }
    }

    [TestMethod]
    public async Task The_hooks_of_the_base_classes_run_without_sign_in_when_a_shape_needs_them()
    {
        var (success, errors, read, _) = await GenerateAsync();
        var (_, _, plain, _) = await GenerateAsync(new() { ["TreeTables"] = "", ["SoftDeleteTables"] = "", ["EffectiveDatedTables"] = "" });

        Assert.IsTrue(success, string.Join(" | ", errors));
        string crud = read("Apis/CrudApi.cs");
        Assert.Contains("if (await BeforeSaveAsync(context, newRow, true) is { } refusedSave)", crud);
        Assert.Contains("if (await BeforeDeleteAsync(context, id) is { } refusedDelete)", crud);
        Assert.Contains("protected virtual Task<IResult?> BeforeDeleteAsync", crud);
        Assert.DoesNotContain("BeforeSaveAsync", plain("Apis/CrudApi.cs"), "a project with no shape and no sign-in keeps the base classes as they were");
    }

    [TestMethod]
    public async Task The_context_hides_a_flagged_row_with_a_query_filter()
    {
        var (success, errors, read, _) = await GenerateAsync();

        Assert.IsTrue(success, string.Join(" | ", errors));
        string context = read("Data/ShopContext.cs");
        Assert.Contains("modelBuilder.Entity<Category>().HasQueryFilter(e => !e.IsDeleted);", context);
        Assert.Contains("modelBuilder.Entity<Customer>().HasQueryFilter(e => !e.IsDeleted);", context);
        Assert.DoesNotContain("HasQueryFilter(e => !e.IsDeleted);\n        modelBuilder.Entity<Price>", context);
    }

    [TestMethod]
    public async Task The_API_tests_make_their_own_rows_and_check_each_shape()
    {
        var (success, errors, read, _) = await GenerateAsync();

        Assert.IsTrue(success, string.Join(" | ", errors));
        string category = read("CategoryApiTests.cs");
        foreach (string name in new[] { "A_row_cannot_be_its_own_parent", "A_row_with_children_is_not_deleted_until_they_are_promoted", "The_tree_lists_a_parent_before_its_children", "A_level_past_the_depth_limit_is_refused",
            "A_deleted_row_is_hidden_from_every_read", "Restoring_a_row_that_is_not_there_answers_404" })
            Assert.Contains(name, category);
        string price = read("PriceApiTests.cs");
        foreach (string name in new[] { "A_period_that_runs_backwards_is_refused", "A_period_that_overlaps_another_row_of_the_group_is_refused", "The_current_rows_are_those_in_effect_on_the_day", "A_period_with_no_last_day_overlaps" })
            Assert.Contains(name, price);
        Assert.Contains("[\"validTo\"] = ApiFixture.SameDay()", price, "a new row sends its last day, or it would be open and overlap every later row");
        Assert.DoesNotContain("A_row_can_be_cloned", price, "a copy would have the same period");
        Assert.Contains("public static string NextDay()", read("ApiFixture.cs"));
        Assert.DoesNotContain("A_row_cannot_be_its_own_parent", read("CustomerApiTests.cs"));
    }

    [TestMethod]
    public async Task The_http_file_and_the_settings_are_documented()
    {
        foreach (string key in new[] { "TreeTables", "SoftDeleteTables", "EffectiveDatedTables" })
        {
            Assert.AreEqual("Table shapes", ProjectSettingGroups.TabOf(key));
            Assert.IsTrue(ProjectSettingsHints.All.ContainsKey(key), key);
        }
        var (success, errors, _, files) = await GenerateAsync();
        Assert.IsTrue(success, string.Join(" | ", errors));
        Assert.IsTrue(files.Any(f => f.EndsWith("Apis/RowShapes.cs")));
    }
}
