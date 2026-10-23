using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> A table that has just been made and holds no rows: <c>dbo.EmptyTest</c> of the InvoiceSystem sample on SQL Server (Docs/Verification/TestDatabases/InvoiceSystemEmptyTest.sql, kept empty on purpose).
/// The schema is read from the live database and every stack is generated for it; the table must come out of the run still empty. Set CODEGENNEW_SQLSERVER_HOST and CODEGENNEW_SQLSERVER_DATABASE
/// (Windows authentication unless CODEGENNEW_SQLSERVER_USER is set); without them, or without the table, the tests report themselves as inconclusive. </summary>
[TestClass]
public class EmptyTableTests
{
    private const string Table = "EmptyTest";

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;

    private static ConnectionRequest Request()
    {
        string? host = Env("CODEGENNEW_SQLSERVER_HOST"), database = Env("CODEGENNEW_SQLSERVER_DATABASE");
        if (host is null || database is null)
            Assert.Inconclusive("Set CODEGENNEW_SQLSERVER_HOST and CODEGENNEW_SQLSERVER_DATABASE (a database that has dbo.EmptyTest) to run the empty table tests.");
        string? user = Env("CODEGENNEW_SQLSERVER_USER");
        return new ConnectionRequest
        {
            Provider = DatabaseProvider.SqlServer, ServerName = host!, DatabaseName = database!,
            AuthMode = user is null ? AuthMode.WindowsAuth : AuthMode.SqlLogin, UserName = user, Password = Env("CODEGENNEW_SQLSERVER_PASSWORD")
        };
    }

    private static async Task<long> RowCountAsync(ConnectionRequest request)
    {
        await using var connection = request.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(N'dbo.EmptyTest', N'U') IS NULL THEN -1 ELSE (SELECT COUNT_BIG(*) FROM dbo.EmptyTest) END";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<long> RequireEmptyTableAsync(ConnectionRequest request)
    {
        long rows = await RowCountAsync(request);
        if (rows < 0)
            Assert.Inconclusive("The database has no dbo.EmptyTest; run Docs/Verification/TestDatabases/InvoiceSystemEmptyTest.sql.");
        Assert.AreEqual(0, rows, "dbo.EmptyTest must stay empty: no seed script and no test may write to it");
        return rows;
    }

    [TestMethod]
    public async Task The_empty_table_is_read_with_its_columns_its_key_and_no_rows()
    {
        var request = Request();
        await RequireEmptyTableAsync(request);

        var model = await new SqlServerSchemaProvider(request, Path.Combine(AppContext.BaseDirectory, "SpecialLogicColumns.config")).BuildTableModelAsync("dbo", Table);

        CollectionAssert.AreEqual(new[] { "EmptyTestId", "Name", "Code", "Amount", "StartDate", "IsTaxable", "Note" }, model.Columns.Select(c => c.Name).ToArray());
        Assert.AreEqual(1, model.PrimaryKeyColumns.Count);
        Assert.AreEqual("EmptyTestId", model.PrimaryKeyColumns[0].Name);
        Assert.IsTrue(model.PrimaryKeyColumns[0].IsIdentity);
        Assert.IsTrue(model.Columns.Single(c => c.Name == "Note").IsNullable);
        Assert.IsFalse(model.Columns.Single(c => c.Name == "Name").IsNullable);
        Assert.AreEqual(0, model.ForeignKeys.Count);
        Assert.AreEqual(0, model.ChildForeignKeys.Count);
        Assert.IsFalse(model.IsJunctionTable);
        Assert.IsFalse(model.IsNameActiveTable, "a Name column without an IsActive column is an ordinary table");
    }

    [TestMethod]
    public async Task Every_stack_generates_for_the_empty_table_and_the_table_stays_empty()
    {
        var request = Request();
        await RequireEmptyTableAsync(request);
        using var temp = new TempFolder();
        var project = ProjectSettings.FromValues(new Dictionary<string, string>
        {
            ["ProjectName"] = "Empty", ["DatabaseProvider"] = "SqlServer", ["DatabaseName"] = request.DatabaseName, ["EnumTables"] = "none", ["OutputAngular"] = "frontend-ng",
            ["Dtos"] = "true", ["ApiTests"] = "true", ["ApiValidation"] = "true", ["ApiCsv"] = "true", ["OData"] = "true", ["EfConfigurations"] = "true"
        });
        string[] stacks = ["Api", "WinUI3", "React", "Angular", "Blazor", "Uno", "Python"];
        var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));

        var report = await Repo.GenerateAsync(provider, new GenerateOptions
        {
            Project = project, Stacks = stacks, OutputDirectory = temp.Path, DatabaseName = request.DatabaseName, Schema = "dbo", Tables = [Table], Force = true
        });

        Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
        var files = Directory.GetFiles(temp.Path, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(temp.Path, f).Replace('\\', '/')).ToList();
        foreach (string folder in new[] { "Empty.Api", "Empty.Api.Tests", "Empty.App", "frontend", "frontend-ng", "Empty.Blazor", "Empty.Uno", "Empty.Python" })
            Assert.IsTrue(files.Any(f => f.StartsWith(folder + "/") && Path.GetFileName(f).Replace("_", "").Contains(Table, StringComparison.OrdinalIgnoreCase)), $"{folder} has a file for {Table}: " + string.Join(", ", files.Select(f => f.Split('/')[0]).Distinct()));

        string Read(string relative) => File.ReadAllText(Path.Combine(temp.Path, relative)).Replace("\r\n", "\n");
        string? Find(string folder, string suffix) => files.FirstOrDefault(f => f.StartsWith(folder + "/") && f.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

        string entity = Read(Find("Empty.Api", "/EmptyTest.cs") ?? throw new AssertFailedException("no entity class"));
        Expect.Contains(entity, "public required int EmptyTestId { get; set; }");
        Expect.Contains(entity, "public required decimal Amount { get; set; }");
        Expect.Contains(entity, "public string? Note { get; set; }");

        string tests = Read(Find("Empty.Api.Tests", "EmptyTestApiTests.cs") ?? throw new AssertFailedException("no API test class"));
        Expect.Contains(tests, "A_row_can_be_added_read_changed_and_removed");
        Expect.Contains(tests, "A_row_that_is_not_there_answers_404");

        Assert.AreEqual(0, await RowCountAsync(request), "generating wrote a row into the empty table");
    }

    [TestMethod]
    public async Task A_dry_run_for_the_empty_table_writes_no_file()
    {
        var request = Request();
        await RequireEmptyTableAsync(request);
        using var temp = new TempFolder();
        var project = ProjectSettings.FromValues(new Dictionary<string, string>
        {
            ["ProjectName"] = "Empty", ["DatabaseProvider"] = "SqlServer", ["DatabaseName"] = request.DatabaseName, ["EnumTables"] = "none"
        });
        var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));

        var report = await Repo.GenerateAsync(provider, new GenerateOptions
        {
            Project = project, Stacks = ["Api", "React"], OutputDirectory = temp.Path, DatabaseName = request.DatabaseName, Schema = "dbo", Tables = [Table], DryRun = true
        });

        Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
        Assert.IsTrue(report.Count(FileOutcomeKind.WouldWrite) > 0, "the plan has files for the table");
        Assert.AreEqual(0, Directory.GetFiles(temp.Path, "*", SearchOption.AllDirectories).Length);
    }

    // The templates that decline an ordinary table with no relations and no opt-in settings, each with a plain reason (not a junction table, no detail rows, no OData, a setting that names no table ...).
    private static readonly string[] Declined =
    [
        "API_Junction_v1", "API_JunctionTest_v1", "API_OData_v1", "BLZ_Junction_v1", "BLZ_ODataClient_v1", "CS_SeedData_v1", "PY_Gates_v1", "PY_Junction_v1", "RS_Gates_v1", "RS_Junction_v1",
        "SP_AuditTable_v1", "SP_BulkUpdate_v1", "SP_EnumCasts_v1", "SP_Junction_v1", "SP_KeySequence_v1", "SP_ReplicationTriggers_v1", "SP_TemporalTable_v1", "TS_DetailMasterComponent_v1",
        "TS_JunctionComponent_v1", "TS_ODataService_v1", "TSX_DetailMasterPage_v1", "TSX_JunctionPage_v1", "TSX_ODataApi_v1", "UNO_DetailMasterScreen_v1", "UNO_JunctionEditor_v1",
        "UNO_ODataClient_v1", "WinUI3_DetailMasterScreen_v1", "WinUI3_JunctionEditor_v1"
    ];

    [TestMethod]
    public async Task Every_shipped_template_either_writes_for_the_empty_table_or_declines_it_with_a_reason()
    {
        var request = Request();
        await RequireEmptyTableAsync(request);
        var model = await new SqlServerSchemaProvider(request, Path.Combine(AppContext.BaseDirectory, "SpecialLogicColumns.config")).BuildTableModelAsync("dbo", Table);
        var project = ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Empty", ["DatabaseProvider"] = "SqlServer", ["DatabaseName"] = request.DatabaseName, ["EnumTables"] = "none", ["Dtos"] = "true" });
        var declined = new List<string>();
        var broken = new List<string>();
        // a template that reads the table's rows is run by the generator, which reads them (empty here); this model has none read, so it is left to the command-line check of the notes
        foreach (var template in TemplateCatalog.Discover(Repo.TemplatesDirectory).Where(t => !t.Config.NeedsRowData))
        {
            var result = template.Config.DatabaseOnly
                ? await Repo.Cache.RunAsync(template.FilePath, new DatabaseModel { DatabaseName = request.DatabaseName, SchemaName = "dbo", Tables = [model] }, project)
                : await Repo.Cache.RunAsync(template.FilePath, model, project);
            string errors = string.Join(" | ", result.Errors);
            if (result.Success)
                continue;
            declined.Add(template.FileStem);
            string first = result.Errors.FirstOrDefault() ?? "";   // the reason the template gives; whatever follows it comes from running it on a model the generator would not have offered it
            if (first.Contains("error CS") || new[] { "NullReference", "IndexOutOfRange", "ArgumentOutOfRange", "KeyNotFound", "DivideByZero", "InvalidCast", "Sequence contains", "Error running transform" }.Any(first.Contains))
                broken.Add(template.FileStem + ": " + errors[..Math.Min(300, errors.Length)]);
        }

        Assert.AreEqual(0, broken.Count, string.Join(Environment.NewLine, broken));
        CollectionAssert.AreEquivalent(Declined, declined, "the templates that decline the empty table changed: " + string.Join(", ", declined.Except(Declined).Concat(Declined.Except(declined).Select(d => "no longer " + d))));
    }
}
