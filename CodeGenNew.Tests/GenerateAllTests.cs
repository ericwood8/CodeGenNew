using System.Data;
using System.Text.Json;
using System.Xml.Linq;
using CodeGenNew.Cli;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> Whole-project generation (ProjectPlan, ProjectGenerator, OutputWriter) and the essentials (the no-database file groups of each stack). </summary>
[TestClass]
public class GenerateAllTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    // ------------------------------------------------------------------ a small in-memory database

    private static TableModel Department() => Sample.Table("Department",
    [
        Sample.Column("DepartmentId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Title", SqlDbType.NVarChar, characters: 50, ordinal: 2)
    ],
    childForeignKeys: [Sample.ChildForeignKey("Employee", "DepartmentId", "DepartmentId", childOwnPrimaryKey: ["EmployeeId"])]);

    private static TableModel Employee() => Sample.Table("Employee",
    [
        Sample.Column("EmployeeId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2),
        Sample.Column("DepartmentId", SqlDbType.Int, ordinal: 3)
    ],
    [Sample.ForeignKey("DepartmentId", "Department", "DepartmentId", "Title")]);

    private static TableModel Keyless() => Sample.Table("Journal", [Sample.Column("Message", SqlDbType.NVarChar, characters: 50, ordinal: 1)]);

    private sealed class FakeProvider(params TableModel[] tables) : ISchemaProvider
    {
        public Task<List<TableSummary>> ListTablesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(tables.Select(t => new TableSummary { SchemaName = t.SchemaName, TableName = t.TableName, HasPrimaryKey = t.HasPrimaryKey }).ToList());
        public Task<List<ColumnSummary>> ListColumnSummariesAsync(string schemaName, string tableName, CancellationToken cancellationToken = default) => Task.FromResult(new List<ColumnSummary>());
        public Task<TableModel> BuildTableModelAsync(string schemaName, string tableName, bool includeRowData = false, bool includeReferencedDisplayColumns = false,
            CancellationToken cancellationToken = default) => Task.FromResult(tables.Single(t => t.TableName == tableName));
    }

    private static DatabaseModel Database(params TableModel[] tables) => new() { DatabaseName = "Acme", SchemaName = "dbo", Tables = tables.ToList() };

    // ------------------------------------------------------------------ the plan

    [TestMethod]
    public void The_plan_picks_templates_by_stack_and_tables_by_set()
    {
        var database = Database(Department(), Employee(), Keyless());
        var project = Project(("Screens", "Employee,Department"), ("Stacks", "Api,React"));
        var templates = TemplateCatalog.Discover(Repo.TemplatesDirectory);

        var steps = ProjectPlan.Build(templates, database, project, ["Api", "React"]).ToDictionary(s => s.Template.Name);

        CollectionAssert.AreEqual(new[] { "Department", "Employee" }, steps["CS_Entity"].TableNames.ToArray());   // a table with no key has no entity
        CollectionAssert.AreEqual(new[] { "Employee" }, steps["TSX_Page"].TableNames.ToArray());                  // a screen without child grids
        CollectionAssert.AreEqual(new[] { "Department" }, steps["TSX_DetailMasterPage"].TableNames.ToArray());   // a screen with child grids
        Assert.IsTrue(steps["CS_DbContext"].IsDatabaseLevel);
        Assert.IsTrue(steps.ContainsKey("TSX_Screens") && steps.ContainsKey("API_Crud"));
        Assert.IsFalse(steps.ContainsKey("WinUI3_MasterScreen"), "a stack that was not asked for");
        Assert.IsFalse(steps.ContainsKey("TS_Component"));
        Assert.IsFalse(steps.ContainsKey("SP_Insert"), "InPlan=false: only with PlanAlso");
        Assert.IsFalse(steps.ContainsKey("SP_EnumCasts"), "PostgreSQL only");
        Assert.IsFalse(steps.Values.Any(s => s.Template.Config.NoDatabase), "the essentials are not part of the table plan");
    }

    [TestMethod]
    public void PlanAlso_adds_an_opt_in_template_and_a_postgres_database_adds_its_own()
    {
        var templates = TemplateCatalog.Discover(Repo.TemplatesDirectory);
        var sqlServer = Database(Department(), Employee());
        var also = ProjectPlan.Build(templates, sqlServer, Project(("PlanAlso", "SP_Insert, SP_Lookup_v1.tt")), ["Api"]).Select(s => s.Template.Name).ToList();
        CollectionAssert.IsSubsetOf(new[] { "SP_Insert", "SP_Lookup" }, also);

        var postgres = new DatabaseModel { DatabaseName = "Acme", SchemaName = "public", Dialect = SqlDialect.PostgreSql, Tables = [Department()] };
        Assert.IsTrue(ProjectPlan.Build(templates, postgres, Project(), ["Api"]).Any(s => s.Template.Name == "SP_EnumCasts"));
    }

    // ------------------------------------------------------------------ the writer

    [TestMethod]
    public async Task A_file_is_created_then_unchanged_even_with_other_line_endings_then_updated()
    {
        using var temp = new TempFolder();
        var one = new List<(string, string)> { ("a/b.txt", "one\ntwo\n") };

        Assert.AreEqual(FileOutcomeKind.Created, (await OutputWriter.WriteAsync(temp.Path, one)).Single().Kind);
        File.WriteAllText(System.IO.Path.Combine(temp.Path, "a", "b.txt"), "one\r\ntwo\r\n");   // a CRLF checkout
        Assert.AreEqual(FileOutcomeKind.Unchanged, (await OutputWriter.WriteAsync(temp.Path, one)).Single().Kind);
        Assert.AreEqual("one\r\ntwo\r\n", File.ReadAllText(System.IO.Path.Combine(temp.Path, "a", "b.txt")), "an unchanged file is not rewritten");

        var changed = new List<(string, string)> { ("a/b.txt", "three\n") };
        Assert.AreEqual(FileOutcomeKind.WouldWrite, (await OutputWriter.WriteAsync(temp.Path, changed, dryRun: true)).Single().Kind);
        Assert.AreEqual(FileOutcomeKind.Skipped, (await OutputWriter.WriteAsync(temp.Path, changed, createOnly: true)).Single().Kind);
        Assert.AreEqual("one\r\ntwo\r\n", File.ReadAllText(System.IO.Path.Combine(temp.Path, "a", "b.txt")));
        Assert.AreEqual(FileOutcomeKind.Updated, (await OutputWriter.WriteAsync(temp.Path, changed)).Single().Kind);
        Assert.AreEqual("three\n", File.ReadAllText(System.IO.Path.Combine(temp.Path, "a", "b.txt")));
    }

    [TestMethod]
    public async Task A_path_that_climbs_out_of_the_folder_is_refused()
    {
        using var temp = new TempFolder();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () => await OutputWriter.WriteAsync(temp.Path, [("../x.txt", "x")]));
    }

    // ------------------------------------------------------------------ the whole run

    [TestMethod]
    public async Task A_run_writes_every_stack_file_once_reports_refusals_and_a_second_run_changes_nothing()
    {
        using var temp = new TempFolder();
        var provider = new FakeProvider(Department(), Employee());
        var options = new GenerateOptions
        {
            Project = Project(("Screens", "Employee,Department"), ("EntityNamespace", "Acme.Api.Entities"), ("RepoNamespace", "Acme.Api.Repositories"), ("ContextNamespace", "Acme.Api.Data"), ("ApiNamespace", "Acme.Api.Apis")),
            Stacks = ["Api", "React"], OutputDirectory = temp.Path, DatabaseName = "Acme", Schema = "dbo"
        };

        var first = await Repo.GenerateAsync(provider, options);

        Assert.IsTrue(first.Success, string.Join("\n", first.Errors));
        string Api(string relative) => System.IO.Path.Combine(temp.Path, "Acme.Api", relative);
        Assert.IsTrue(File.Exists(Api("Entities/Employee.cs")));
        Assert.IsTrue(File.Exists(Api("Repositories/DepartmentRepo.cs")));
        Assert.IsTrue(File.Exists(Api("Data/AcmeContext.cs")));
        Assert.IsTrue(File.Exists(System.IO.Path.Combine(temp.Path, "frontend", "src", "models", "employee.ts")));
        Assert.IsTrue(File.Exists(System.IO.Path.Combine(temp.Path, "frontend", "src", "pages", "DepartmentDetailMasterPage.tsx")));
        Assert.IsTrue(File.Exists(System.IO.Path.Combine(temp.Path, "sql", "Employee_Search.sql")));
        Assert.IsGreaterThan(20, first.Count(FileOutcomeKind.Created));
        Assert.AreEqual(0, first.Count(FileOutcomeKind.Updated));

        var second = await Repo.GenerateAsync(provider, options);
        Assert.AreEqual(0, second.Count(FileOutcomeKind.Created) + second.Count(FileOutcomeKind.Updated), "nothing changed, so nothing is written");
        Assert.IsGreaterThan(20, second.Count(FileOutcomeKind.Unchanged));
    }

    [TestMethod]
    public async Task A_dry_run_writes_nothing_and_a_template_filter_runs_only_that_template()
    {
        using var temp = new TempFolder();
        var provider = new FakeProvider(Department(), Employee());

        var dry = await Repo.GenerateAsync(provider, new GenerateOptions
        {
            Project = Project(), Stacks = ["Api"], OutputDirectory = temp.Path, DatabaseName = "Acme", Schema = "dbo", DryRun = true, OnlyTemplates = ["CS_Entity"]
        });

        Assert.AreEqual(2, dry.Count(FileOutcomeKind.WouldWrite));
        Assert.IsEmpty(Directory.GetFileSystemEntries(temp.Path));
        CollectionAssert.AreEquivalent(new[] { "CS_Entity" }, dry.Steps.Select(s => s.Template).Distinct().ToList());
    }

    [TestMethod]
    public async Task A_template_that_refuses_a_table_is_reported_not_a_failure()
    {
        using var temp = new TempFolder();
        // a name/active table has no plain CRUD API: API_Crud declines it
        var nameActive = Sample.Table("Tag",
        [
            Sample.Column("TagId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2),
            Sample.Column("IsActive", SqlDbType.Bit, ordinal: 3)
        ]);

        var report = await Repo.GenerateAsync(new FakeProvider(nameActive), new GenerateOptions
        {
            Project = Project(), Stacks = ["Api"], OutputDirectory = temp.Path, DatabaseName = "Acme", Schema = "dbo", OnlyTemplates = ["CS_Entity", "CS_Repo"]
        });

        Assert.IsTrue(report.Success);
        Assert.IsTrue(File.Exists(System.IO.Path.Combine(temp.Path, "Acme.Api", "Entities", "Tag.cs")));
    }

    // ------------------------------------------------------------------ the essentials

    [TestMethod]
    public void Each_stack_has_its_essentials_groups_and_a_stack_is_found_by_its_usual_names()
    {
        foreach (var (stack, _) in EssentialsCatalog.Stacks)
            Assert.IsGreaterThanOrEqualTo(2, EssentialsCatalog.Groups(Repo.TemplatesDirectory, stack).Count, stack);

        CollectionAssert.IsSubsetOf(new[] { "App", "MainWindow", "Project", "BaseClasses", "DirectoryListing" }, EssentialsCatalog.Groups(Repo.TemplatesDirectory, "WinUI3").Select(g => g.Name).ToList());
        CollectionAssert.IsSubsetOf(new[] { "Shell", "Styles", "Support", "Build" }, EssentialsCatalog.Groups(Repo.TemplatesDirectory, "React").Select(g => g.Name).ToList());
        CollectionAssert.IsSubsetOf(new[] { "Shell", "Config", "Styles" }, EssentialsCatalog.Groups(Repo.TemplatesDirectory, "Angular").Select(g => g.Name).ToList());
        CollectionAssert.IsSubsetOf(new[] { "Program", "BaseApi", "CrudApi", "BaseClasses" }, EssentialsCatalog.Groups(Repo.TemplatesDirectory, "Api").Select(g => g.Name).ToList());
        Assert.IsFalse(EssentialsCatalog.Groups(Repo.TemplatesDirectory, "WinUI3").Single(g => g.Name == "DirectoryListing").DefaultOn, "an optional group is not ticked");
        Assert.AreEqual("WinUI3", EssentialsCatalog.FindStack("winui"));
        Assert.AreEqual("React", EssentialsCatalog.FindStack("react"));
        Assert.IsNull(EssentialsCatalog.FindStack("vue"));
    }

    [TestMethod]
    public async Task The_winui3_essentials_write_the_app_the_main_window_and_the_project_files_that_parse()
    {
        using var temp = new TempFolder();
        var project = Project(("DatabaseProvider", "PostgreSql"), ("DatabaseUser", "dev"), ("DatabaseName", "acme_db"));

        var run = await EssentialsCatalog.GenerateAsync(EssentialsCatalog.Groups(Repo.TemplatesDirectory, "WinUI3").Where(g => g.DefaultOn), project, temp.Path);

        Assert.IsTrue(run.Success, string.Join("\n", run.Failures.SelectMany(f => f.Errors)));
        string App(string file) => System.IO.Path.Combine(temp.Path, "Acme.App", file);
        foreach (string xml in new[] { "App.xaml", "MainWindow.xaml", "Acme.App.csproj", "app.manifest" })
            XDocument.Load(App(xml));   // each is well-formed XML
        Assert.AreEqual("Host=localhost;Port=5432;Database=acme_db;Username=dev", JsonDocument.Parse(File.ReadAllText(App("appsettings.json"))).RootElement.GetProperty("ConnectionStrings").GetProperty("DbConnectionString").GetString());
        StringAssert.Contains(File.ReadAllText(App("Acme.App.csproj")), "Npgsql.EntityFrameworkCore.PostgreSQL");
        StringAssert.Contains(File.ReadAllText(App("MainWindow.xaml.cs")), "private readonly AcmeContext _context = new();");
        StringAssert.Contains(File.ReadAllText(App("MainWindow.xaml.cs")), "using Acme.App.Views;");
        StringAssert.Contains(File.ReadAllText(App("App.xaml")), "x:Class=\"Acme.App.App\"");
        StringAssert.Contains(File.ReadAllText(App("GlobalUsings.cs")), "global using Acme.App.Entities;");
        Assert.IsTrue(File.Exists(App("Repositories/GenericRepo.cs")));
        Assert.IsFalse(File.Exists(App("Views/DocumentListPage.xaml")), "the optional directory listing is not written unless asked for");
    }

    [TestMethod]
    public async Task The_web_essentials_use_the_projects_ports_and_the_angular_version()
    {
        using var temp = new TempFolder();
        var project = Project(("ApiPort", "5090"), ("DevPort", "5200"), ("AngularVersion", "22"));

        var react = await EssentialsCatalog.GenerateAsync(EssentialsCatalog.Groups(Repo.TemplatesDirectory, "React"), project, temp.Path);
        var angular = await EssentialsCatalog.GenerateAsync(EssentialsCatalog.Groups(Repo.TemplatesDirectory, "Angular"), project, temp.Path);

        Assert.IsTrue(react.Success && angular.Success);
        string vite = File.ReadAllText(System.IO.Path.Combine(temp.Path, "frontend", "vite.config.ts"));
        StringAssert.Contains(vite, "port: 5200,");
        StringAssert.Contains(vite, "'/api': 'http://localhost:5090'");
        JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(temp.Path, "frontend", "package.json")));
        StringAssert.Contains(File.ReadAllText(System.IO.Path.Combine(temp.Path, "frontend", "proxy.conf.json")), "http://localhost:5090");
        string appTs = File.ReadAllText(System.IO.Path.Combine(temp.Path, "frontend", "src", "app", "app.ts"));
        StringAssert.Contains(appTs, "ChangeDetectionStrategy.Eager");
        Assert.DoesNotContain("standalone: true", appTs);
        StringAssert.Contains(File.ReadAllText(System.IO.Path.Combine(temp.Path, "frontend", "src", "app", "app.html")), "@for (");
    }

    [TestMethod]
    public async Task Essentials_that_exist_are_left_alone_unless_replace_is_asked_for()
    {
        using var temp = new TempFolder();
        var groups = EssentialsCatalog.Groups(Repo.TemplatesDirectory, "React").Where(g => g.Name == "Styles").ToList();
        await EssentialsCatalog.GenerateAsync(groups, Project(), temp.Path);
        string css = System.IO.Path.Combine(temp.Path, "frontend", "src", "index.css");
        File.WriteAllText(css, "/* my own */");

        var kept = await EssentialsCatalog.GenerateAsync(groups, Project(), temp.Path);
        Assert.AreEqual(FileOutcomeKind.Skipped, kept.Outcomes.Single().Kind);
        Assert.AreEqual("/* my own */", File.ReadAllText(css));

        var replaced = await EssentialsCatalog.GenerateAsync(groups, Project(), temp.Path, replace: true);
        Assert.AreEqual(FileOutcomeKind.Updated, replaced.Outcomes.Single().Kind);
    }

    [TestMethod]
    public async Task The_api_essentials_name_the_cors_origins_of_the_stacks_the_project_builds()
    {
        using var temp = new TempFolder();
        await EssentialsCatalog.GenerateAsync(EssentialsCatalog.Groups(Repo.TemplatesDirectory, "Api").Where(g => g.Name == "Program"), Project(("Stacks", "Api,Angular,React")), temp.Path);

        string program = File.ReadAllText(System.IO.Path.Combine(temp.Path, "Acme.Api", "Program.cs"));

        StringAssert.Contains(program, "policy.WithOrigins(\"http://localhost:4200\", \"http://localhost:5173\")");
        StringAssert.Contains(program, "AddGeneratedDbContext");
    }

    // ------------------------------------------------------------------ the command line

    [TestMethod]
    public void The_generate_and_essentials_commands_parse_their_flags_without_a_template()
    {
        var generate = ArgumentParser.Parse(["generate", "-S", "srv", "-d", "db", "-E", "--project", "Acme", "--stack", "api,react", "--essentials", "--groups", "Shell,Styles", "--only", "SP_Search", "--dry-run", "-o", "out"]);

        Assert.AreEqual("generate", generate.Command);
        CollectionAssert.AreEqual(new[] { "api", "react" }, generate.Stacks);
        CollectionAssert.AreEqual(new[] { "Shell", "Styles" }, generate.Groups);
        CollectionAssert.AreEqual(new[] { "SP_Search" }, generate.Only);
        Assert.IsTrue(generate.Essentials && generate.DryRun);
        Assert.IsNull(ArgumentParser.MissingConnection(generate));

        var essentials = ArgumentParser.Parse(["essentials", "--stack", "winui3", "--project", "Acme", "--replace", "-o", "out"]);
        Assert.AreEqual("essentials", essentials.Command);
        Assert.IsTrue(essentials.Replace);
        Assert.IsTrue(ArgumentParser.Parse(["essentials", "--list"]).List);
    }

    [TestMethod]
    public async Task A_cached_template_gives_the_text_the_plain_runner_gives_for_each_table_and_reports_its_errors()
    {
        using var cache = new TemplateCache();
        foreach (var table in new[] { Department(), Employee() })
        {
            var plain = await TemplateRunner.RunAsync(Repo.Template("CS_Entity_v1.tt"), table, Project());
            var cached = await cache.RunAsync(Repo.Template("CS_Entity_v1.tt"), table, Project());
            Assert.IsTrue(plain.Success && cached.Success);
            Assert.AreEqual(plain.GeneratedText, cached.GeneratedText, table.TableName);
        }

        // a template that stops with Error(...) fails each time it is run, not just the first
        var keyless = await cache.RunAsync(Repo.Template("CS_Entity_v1.tt"), Keyless(), Project());
        Assert.IsFalse(keyless.Success);
        Assert.IsTrue((await cache.RunAsync(Repo.Template("CS_Entity_v1.tt"), Department(), Project())).Success, "a failed run does not poison the next one");
    }
}
