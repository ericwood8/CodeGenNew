using System.Data;
using System.Text.Json;
using CodeGenNew.Cli;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The follow-ups of whole-project generation: the table filter, stale files, the diff, the partner check, the build and test steps and the extra essentials groups. </summary>
[TestClass]
public class GenerateAllFollowUpTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static TableModel Department() => Sample.Table("Department",
    [
        Sample.Column("DepartmentId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Title", SqlDbType.NVarChar, characters: 50, ordinal: 2)
    ]);

    private static TableModel Employee() => Sample.Table("Employee",
    [
        Sample.Column("EmployeeId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2)
    ]);

    private sealed class FakeProvider(params TableModel[] tables) : ISchemaProvider
    {
        public Task<List<TableSummary>> ListTablesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(tables.Select(t => new TableSummary { SchemaName = t.SchemaName, TableName = t.TableName, HasPrimaryKey = t.HasPrimaryKey }).ToList());
        public Task<List<ColumnSummary>> ListColumnSummariesAsync(string schemaName, string tableName, CancellationToken cancellationToken = default) => Task.FromResult(new List<ColumnSummary>());
        public Task<TableModel> BuildTableModelAsync(string schemaName, string tableName, bool includeRowData = false, bool includeReferencedDisplayColumns = false,
            CancellationToken cancellationToken = default) => Task.FromResult(tables.Single(t => t.TableName == tableName));
    }

    private static GenerateOptions Options(string folder, ProjectSettings? project = null, Action<GenerateOptionsBuilder>? change = null)
    {
        var builder = new GenerateOptionsBuilder { Project = project ?? Project(("EntityNamespace", "Acme.Api.Entities"), ("RepoNamespace", "Acme.Api.Repositories"), ("ContextNamespace", "Acme.Api.Data"), ("ApiNamespace", "Acme.Api.Apis")), Folder = folder };
        change?.Invoke(builder);
        return new GenerateOptions
        {
            Project = builder.Project, Stacks = ["Api"], OutputDirectory = folder, DatabaseName = "Acme", Schema = "dbo", DryRun = builder.DryRun, DeleteStale = builder.DeleteStale,
            OnlyTemplates = builder.Only, Tables = builder.Tables, WithDiff = builder.WithDiff
        };
    }

    private sealed class GenerateOptionsBuilder
    {
        public required ProjectSettings Project { get; set; }
        public required string Folder { get; init; }
        public bool DryRun { get; set; }
        public bool DeleteStale { get; set; }
        public bool WithDiff { get; set; }
        public IReadOnlyList<string>? Only { get; set; }
        public IReadOnlyList<string>? Tables { get; set; }
    }

    private static string Api(TempFolder temp, string relative) => System.IO.Path.Combine(temp.Path, "Acme.Api", relative);

    // ------------------------------------------------------------------ the diff

    [TestMethod]
    public void A_diff_shows_the_changed_lines_with_context_and_ignores_line_endings()
    {
        Assert.IsNull(TextDiff.Unified("a\r\nb\r\n", "a\nb\n"));

        string diff = TextDiff.Unified("one\ntwo\nthree\nfour\nfive\nsix\nseven\neight\nnine\nten\n", "one\ntwo\nthree\nfour\nFIVE\nsix\nseven\neight\nnine\nten\n", "file.txt")!;

        StringAssert.Contains(diff, "--- file.txt");
        StringAssert.Contains(diff, "- five\n+ FIVE\n");
        StringAssert.Contains(diff, "  four\n");
        Assert.IsFalse(diff.Contains("  ten"), "a line far from the change is not shown");
    }

    [TestMethod]
    public async Task A_skipped_or_replaced_file_carries_its_diff_when_asked_for()
    {
        using var temp = new TempFolder();
        File.WriteAllText(System.IO.Path.Combine(temp.Path, "a.txt"), "old\nsame\n");

        var skipped = (await OutputWriter.WriteAsync(temp.Path, [("a.txt", "new\nsame\n")], createOnly: true, withDiff: true)).Single();
        var without = (await OutputWriter.WriteAsync(temp.Path, [("a.txt", "new\nsame\n")], createOnly: true)).Single();

        Assert.AreEqual(FileOutcomeKind.Skipped, skipped.Kind);
        StringAssert.Contains(skipped.Diff!, "- old\n+ new\n");
        Assert.IsNull(without.Diff);
    }

    // ------------------------------------------------------------------ the table filter

    [TestMethod]
    public async Task The_table_filter_regenerates_only_that_tables_files_and_leaves_the_whole_database_files_out()
    {
        using var temp = new TempFolder();
        var provider = new FakeProvider(Department(), Employee());

        var report = await Repo.GenerateAsync(provider, Options(temp.Path, change: o => o.Tables = ["employee", "Nowhere"]));

        Assert.IsTrue(report.Success, string.Join("\n", report.Errors));
        Assert.IsTrue(File.Exists(Api(temp, "Entities/Employee.cs")));
        Assert.IsFalse(File.Exists(Api(temp, "Entities/Department.cs")));
        Assert.IsFalse(File.Exists(Api(temp, "Data/AcmeContext.cs")), "the context covers every table, so a one-table run does not write it");
        Assert.IsTrue(report.Warnings.Any(w => w.Contains("Nowhere")));
    }

    // ------------------------------------------------------------------ stale files

    [TestMethod]
    public async Task A_file_the_plan_no_longer_produces_is_reported_stale_and_only_an_unedited_one_is_deleted()
    {
        using var temp = new TempFolder();
        var first = await Repo.GenerateAsync(new FakeProvider(Department(), Employee()), Options(temp.Path));
        Assert.IsTrue(first.ManifestWritten);
        Assert.AreEqual(0, first.Stale.Count);
        Assert.IsTrue(File.Exists(GenerationManifest.PathFor(temp.Path)));

        // the Employee table is dropped; someone edited its repository by hand
        File.AppendAllText(Api(temp, "Repositories/EmployeeRepo.cs"), "// my change\n");
        var second = await Repo.GenerateAsync(new FakeProvider(Department()), Options(temp.Path));

        var stale = second.Stale.ToDictionary(s => s.Path);
        Assert.IsTrue(stale.ContainsKey("Acme.Api/Entities/Employee.cs") && stale.ContainsKey("Acme.Api/Repositories/EmployeeRepo.cs"));
        Assert.IsFalse(stale["Acme.Api/Entities/Employee.cs"].Edited);
        Assert.IsTrue(stale["Acme.Api/Repositories/EmployeeRepo.cs"].Edited);
        Assert.IsTrue(File.Exists(Api(temp, "Entities/Employee.cs")), "reporting does not delete");

        var third = await Repo.GenerateAsync(new FakeProvider(Department()), Options(temp.Path, change: o => o.DeleteStale = true));

        Assert.IsFalse(File.Exists(Api(temp, "Entities/Employee.cs")), "an unedited stale file is deleted");
        Assert.IsTrue(File.Exists(Api(temp, "Repositories/EmployeeRepo.cs")), "an edited one never is");
        Assert.IsTrue(third.Stale.Single(s => s.Path.EndsWith("Entities/Employee.cs")).Deleted);
        Assert.IsTrue(third.Stale.Single(s => s.Path.EndsWith("EmployeeRepo.cs")).Edited);

        var fourth = await Repo.GenerateAsync(new FakeProvider(Department()), Options(temp.Path));
        CollectionAssert.AreEqual(new[] { "Acme.Api/Repositories/EmployeeRepo.cs" }, fourth.Stale.Select(s => s.Path).Where(p => p.Contains("Employee")).ToList(), "the edited file stays on the list");
    }

    [TestMethod]
    public async Task A_dry_run_and_a_partial_run_report_no_stale_files_and_do_not_lose_the_list()
    {
        using var temp = new TempFolder();
        await Repo.GenerateAsync(new FakeProvider(Department(), Employee()), Options(temp.Path));
        string manifest = File.ReadAllText(GenerationManifest.PathFor(temp.Path));

        var partial = await Repo.GenerateAsync(new FakeProvider(Department()), Options(temp.Path, change: o => o.Only = ["CS_Entity"]));
        Assert.AreEqual(0, partial.Stale.Count, "a run of part of the plan cannot tell what is stale");
        StringAssert.Contains(File.ReadAllText(GenerationManifest.PathFor(temp.Path)), "Employee.cs");   // the earlier entries are kept

        File.WriteAllText(GenerationManifest.PathFor(temp.Path), manifest);
        var dry = await Repo.GenerateAsync(new FakeProvider(Department()), Options(temp.Path, change: o => { o.DryRun = true; o.DeleteStale = true; }));
        Assert.IsTrue(dry.Stale.Count > 0 && dry.Stale.All(s => !s.Deleted));
        Assert.IsTrue(File.Exists(Api(temp, "Entities/Employee.cs")));
        Assert.AreEqual(manifest, File.ReadAllText(GenerationManifest.PathFor(temp.Path)), "a dry run writes no manifest");
    }

    [TestMethod]
    public void The_manifest_hash_ignores_line_endings()
    {
        Assert.AreEqual(GenerationManifest.Hash("a\nb\n"), GenerationManifest.Hash("a\r\nb\r\n"));
        Assert.AreNotEqual(GenerationManifest.Hash("a\n"), GenerationManifest.Hash("b\n"));
    }

    // ------------------------------------------------------------------ the partner check

    [TestMethod]
    public async Task An_essentials_group_warns_about_a_partner_file_that_is_not_there_yet()
    {
        using var temp = new TempFolder();
        var main = EssentialsCatalog.Groups(Repo.TemplatesDirectory, "WinUI3").Where(g => g.Name == "MainWindow").ToList();

        var first = await EssentialsCatalog.GenerateAsync(main, Project(), temp.Path);

        Assert.IsTrue(first.Warnings.Any(w => w.Contains("MainWindow.Screens.cs")));
        Assert.IsTrue(first.Warnings.Any(w => w.Contains("Data/AcmeContext.cs")), "the context's name comes from the project");

        string app = System.IO.Path.Combine(temp.Path, "Acme.App");
        File.WriteAllText(System.IO.Path.Combine(app, "MainWindow.Screens.cs"), "// generated");
        Directory.CreateDirectory(System.IO.Path.Combine(app, "Data"));
        File.WriteAllText(System.IO.Path.Combine(app, "Data", "AcmeContext.cs"), "// generated");
        Assert.AreEqual(0, (await EssentialsCatalog.GenerateAsync(main, Project(), temp.Path)).Warnings.Count);
    }

    // ------------------------------------------------------------------ build and test

    [TestMethod]
    public void A_stack_has_default_build_and_test_commands_and_a_project_can_replace_or_switch_them_off()
    {
        Assert.AreEqual("npm run build", ProjectBuilder.CommandFor(Project(), "React", "build"));
        Assert.AreEqual("npm test", ProjectBuilder.CommandFor(Project(), "React", "test"));
        Assert.AreEqual("npm test -- --watch=false", ProjectBuilder.CommandFor(Project(), "Angular", "test"));
        Assert.AreEqual("dotnet build -v q", ProjectBuilder.CommandFor(Project(), "Api", "build"));
        Assert.IsNull(ProjectBuilder.CommandFor(Project(), "Api", "test"), "no tests by default");
        Assert.AreEqual("dotnet test", ProjectBuilder.CommandFor(Project(("TestApi", "dotnet test")), "Api", "test"));
        Assert.IsNull(ProjectBuilder.CommandFor(Project(("BuildReact", "none")), "React", "build"));
    }

    [TestMethod]
    public async Task The_build_step_runs_the_command_in_the_stacks_folder_and_reports_success_or_failure()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(System.IO.Path.Combine(temp.Path, "Acme.Api"));

        var ok = await ProjectBuilder.RunAsync(Project(("BuildApi", "echo Build succeeded.")), ["Api"], temp.Path, build: true, test: false);
        var failing = await ProjectBuilder.RunAsync(Project(("BuildApi", "echo Program.cs(3,1): error CS1002: ; expected & exit 1")), ["Api"], temp.Path, build: true, test: false);
        var missing = await ProjectBuilder.RunAsync(Project(), ["Angular"], temp.Path, build: true, test: false);

        Assert.IsTrue(ok.Single().Success);
        StringAssert.Contains(ok.Single().Summary, "Build succeeded.");
        Assert.IsFalse(failing.Single().Success);
        StringAssert.Contains(failing.Single().Summary, "error CS1002");
        Assert.IsFalse(missing.Single().Success, "a stack whose folder was not generated cannot be built");
        StringAssert.Contains(missing.Single().Summary, "does not exist");
    }

    [TestMethod]
    public void A_build_summary_keeps_the_errors_and_totals_not_the_whole_output()
    {
        string output = "restoring...\nx.cs(1,1): error CS0103: no such name\n lots of noise\n Test Files  6 passed (6)\n      Tests  9 passed (9)\n";

        string summary = ProjectBuilder.Summarise(output, 1);

        StringAssert.Contains(summary, "error CS0103");
        StringAssert.Contains(summary, "Tests  9 passed");
        Assert.IsFalse(summary.Contains("lots of noise"));
    }

    // ------------------------------------------------------------------ more essentials groups

    [TestMethod]
    public async Task The_extra_groups_write_the_angular_build_files_the_api_launch_settings_and_the_ignore_and_editor_files()
    {
        using var temp = new TempFolder();
        var project = Project(("AngularVersion", "21"), ("ApiPort", "5099"), ("Screens", "Employee"));
        var wanted = new[] { ("Angular", "Build"), ("Api", "Launch"), ("Api", "EditorConfig"), ("React", "Git"), ("Angular", "Git") };
        var groups = wanted.Select(w => EssentialsCatalog.Groups(Repo.TemplatesDirectory, w.Item1).Single(g => g.Name == w.Item2)).ToList();

        var run = await EssentialsCatalog.GenerateAsync(groups, project, temp.Path);

        Assert.IsTrue(run.Success, string.Join("\n", run.Failures.SelectMany(f => f.Errors)));
        var package = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(temp.Path, "frontend", "package.json"))).RootElement;
        Assert.AreEqual("^21.0.0", package.GetProperty("dependencies").GetProperty("@angular/core").GetString());
        Assert.AreEqual("~5.9.2", package.GetProperty("devDependencies").GetProperty("typescript").GetString());
        JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(temp.Path, "frontend", "angular.json")));
        StringAssert.Contains(File.ReadAllText(System.IO.Path.Combine(temp.Path, "frontend", "angular.json")), "azure-blue.css");
        var launch = JsonDocument.Parse(File.ReadAllText(Api(temp, "Properties/launchSettings.json"))).RootElement;
        Assert.AreEqual("http://localhost:5099", launch.GetProperty("profiles").GetProperty("http").GetProperty("applicationUrl").GetString());
        StringAssert.Contains(File.ReadAllText(Api(temp, "Acme.Api.http")), "GET {{host}}/api/employees/search");
        StringAssert.Contains(File.ReadAllText(Api(temp, ".editorconfig")), "dotnet_diagnostic.IDE0028.severity = warning");
        StringAssert.Contains(File.ReadAllText(Api(temp, ".gitignore")), "bin/");
        StringAssert.Contains(File.ReadAllText(System.IO.Path.Combine(temp.Path, "frontend", ".gitignore")), "node_modules/");   // React and Angular would share this folder: the first group written is kept
    }

    // ------------------------------------------------------------------ the command line

    [TestMethod]
    public void The_follow_up_flags_parse()
    {
        var options = ArgumentParser.Parse(["generate", "-S", "s", "-d", "d", "-E", "--table", "Customer,Item", "--delete-stale", "--build", "--test", "--diff", "--project", "Acme", "--stack", "api"]);

        Assert.AreEqual("Customer,Item", options.Table);
        Assert.IsTrue(options.DeleteStale && options.Build && options.Test && options.Diff);
    }
}
