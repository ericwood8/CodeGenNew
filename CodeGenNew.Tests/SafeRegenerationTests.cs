using System.Data;
using CodeGenNew.Cli;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> A run must not silently replace a file someone edited since the last run, or one no earlier run recorded. </summary>
[TestClass]
public class SafeRegenerationTests
{
    private const string Entity = "Acme.Api/Entities/Department.cs";

    private static TableModel Department(int titleLength = 50) => Sample.Table("Department",
    [
        Sample.Column("DepartmentId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Title", SqlDbType.NVarChar, characters: titleLength, ordinal: 2)
    ]);

    private sealed class FakeProvider(TableModel table) : ISchemaProvider
    {
        public Task<List<TableSummary>> ListTablesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<TableSummary> { new() { SchemaName = table.SchemaName, TableName = table.TableName, HasPrimaryKey = table.HasPrimaryKey } });
        public Task<List<ColumnSummary>> ListColumnSummariesAsync(string schemaName, string tableName, CancellationToken cancellationToken = default) => Task.FromResult(new List<ColumnSummary>());
        public Task<TableModel> BuildTableModelAsync(string schemaName, string tableName, bool includeRowData = false, bool includeReferencedDisplayColumns = false,
            CancellationToken cancellationToken = default) => Task.FromResult(table);
    }

    private static Task<GenerateReport> RunAsync(string folder, int titleLength = 50, bool dryRun = false, bool force = false, bool writeBeside = false) =>
        Repo.GenerateAsync(new FakeProvider(Department(titleLength)), new GenerateOptions
        {
            Project = ProjectSettings.FromValues(new KeyValuePair<string, string>[] { new("ProjectName", "Acme"), new("EntityNamespace", "Acme.Api.Entities") }),
            Stacks = ["Api"], OutputDirectory = folder, DatabaseName = "Acme", Schema = "dbo", OnlyTemplates = ["CS_Entity"],
            DryRun = dryRun, Force = force, WriteBeside = writeBeside
        });

    private static string EntityPath(TempFolder temp) => System.IO.Path.Combine(temp.Path, Entity);

    private static FileOutcome EntityOutcome(GenerateReport report) => report.AllFiles.Single(f => f.FullPath.EndsWith("Department.cs", StringComparison.OrdinalIgnoreCase));

    [TestMethod]
    public async Task A_file_nobody_edited_is_replaced_when_the_template_changes()
    {
        using var temp = new TempFolder();
        await RunAsync(temp.Path, titleLength: 50);
        string before = File.ReadAllText(EntityPath(temp));

        var second = await RunAsync(temp.Path, titleLength: 80);

        Assert.AreEqual(FileOutcomeKind.Updated, EntityOutcome(second).Kind);
        Assert.AreNotEqual(before, File.ReadAllText(EntityPath(temp)));
    }

    [TestMethod]
    public async Task An_edited_file_is_kept_and_reported_and_the_manifest_still_remembers_the_generated_text()
    {
        using var temp = new TempFolder();
        await RunAsync(temp.Path, titleLength: 50);
        string manifestBefore = File.ReadAllText(GenerationManifest.PathFor(temp.Path));
        File.AppendAllText(EntityPath(temp), "// my change\n");

        var second = await RunAsync(temp.Path, titleLength: 80);

        Assert.AreEqual(FileOutcomeKind.Edited, EntityOutcome(second).Kind);
        StringAssert.EndsWith(File.ReadAllText(EntityPath(temp)), "// my change\n");
        Assert.IsEmpty(second.Stale, "a kept file is not stale");
        Assert.AreEqual(manifestBefore, File.ReadAllText(GenerationManifest.PathFor(temp.Path)), "the hash of the last generated text stays, so the next run still sees the edit");

        var third = await RunAsync(temp.Path, titleLength: 80);
        Assert.AreEqual(FileOutcomeKind.Edited, EntityOutcome(third).Kind);
    }

    [TestMethod]
    public async Task An_edited_file_that_already_holds_the_new_text_is_unchanged_and_recorded_again()
    {
        using var temp = new TempFolder();
        await RunAsync(temp.Path, titleLength: 50);
        File.AppendAllText(EntityPath(temp), "// my change\n");
        await RunAsync(temp.Path, titleLength: 80, force: true);
        string forced = File.ReadAllText(EntityPath(temp));
        File.WriteAllText(EntityPath(temp), forced.Replace("\n", "\r\n"));

        var again = await RunAsync(temp.Path, titleLength: 80);

        Assert.AreEqual(FileOutcomeKind.Unchanged, EntityOutcome(again).Kind, "a CRLF copy of the generated text is not an edit");
    }

    [TestMethod]
    public async Task A_file_no_earlier_run_recorded_is_kept_as_unknown()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(EntityPath(temp))!);
        File.WriteAllText(EntityPath(temp), "// written by hand\n");

        var report = await RunAsync(temp.Path);

        Assert.AreEqual(FileOutcomeKind.Unknown, EntityOutcome(report).Kind);
        Assert.AreEqual("// written by hand\n", File.ReadAllText(EntityPath(temp)));
        Assert.IsFalse(File.Exists(GenerationManifest.PathFor(temp.Path)) && File.ReadAllText(GenerationManifest.PathFor(temp.Path)).Contains("Department.cs"), "an unrecorded file stays unrecorded");
    }

    [TestMethod]
    public async Task Force_overwrites_an_edited_and_an_unrecorded_file_and_records_the_new_text()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(EntityPath(temp))!);
        File.WriteAllText(EntityPath(temp), "// written by hand\n");

        var report = await RunAsync(temp.Path, force: true);

        Assert.AreEqual(FileOutcomeKind.Updated, EntityOutcome(report).Kind);
        StringAssert.Contains(File.ReadAllText(GenerationManifest.PathFor(temp.Path)), "Department.cs");
        Assert.AreEqual(FileOutcomeKind.Unchanged, EntityOutcome(await RunAsync(temp.Path)).Kind);
    }

    [TestMethod]
    public async Task Write_beside_keeps_the_edit_and_writes_the_new_text_next_to_it()
    {
        using var temp = new TempFolder();
        await RunAsync(temp.Path, titleLength: 50);
        File.AppendAllText(EntityPath(temp), "// my change\n");

        var report = await RunAsync(temp.Path, titleLength: 80, writeBeside: true);

        Assert.AreEqual(FileOutcomeKind.Edited, EntityOutcome(report).Kind);
        StringAssert.EndsWith(File.ReadAllText(EntityPath(temp)), "// my change\n");
        string beside = File.ReadAllText(EntityPath(temp) + OutputWriter.BesideSuffix);
        Assert.AreEqual(EntityOutcome(report).Content, beside);
        Assert.DoesNotContain("my change", beside);
    }

    [TestMethod]
    public async Task A_dry_run_reports_the_kept_file_and_writes_nothing()
    {
        using var temp = new TempFolder();
        await RunAsync(temp.Path, titleLength: 50);
        File.AppendAllText(EntityPath(temp), "// my change\n");

        var report = await RunAsync(temp.Path, titleLength: 80, dryRun: true, writeBeside: true);

        Assert.AreEqual(FileOutcomeKind.Edited, EntityOutcome(report).Kind);
        Assert.IsFalse(File.Exists(EntityPath(temp) + OutputWriter.BesideSuffix));
    }

    [TestMethod]
    public void The_command_line_knows_force_and_write_beside()
    {
        var options = ArgumentParser.Parse(["generate", "-S", "s", "-d", "d", "-E", "--force", "--write-beside"]);

        Assert.IsTrue(options.Force && options.WriteBeside);
        Assert.IsFalse(ArgumentParser.Parse(["generate", "-S", "s", "-d", "d", "-E"]).Force);
    }
}
