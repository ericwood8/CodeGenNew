using System.Data;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> DesktopTests=true: the WinUI3 and Uno apps ship a test project that compiles their own view models, and a test class per dialog whose cases come from one plan in Core. </summary>
[TestClass]
public class DesktopGeneratedTestsTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")).Append(new("NotFutureDateColumns", "BornOn")).Append(new("DatePairs", "StartDate>EndDate")));

    private static TableModel Person() => Sample.Table("Person",
    [
        Sample.Column("PersonId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("FullName", SqlDbType.NVarChar, characters: 40, ordinal: 2),
        Sample.Column("Email", SqlDbType.NVarChar, characters: 80, ordinal: 3),
        Sample.Column("BornOn", SqlDbType.Date, ordinal: 4),
        Sample.Column("StartDate", SqlDbType.Date, ordinal: 5),
        Sample.Column("EndDate", SqlDbType.Date, nullable: true, ordinal: 6),
        Sample.Column("Hours", SqlDbType.Int, ordinal: 7)
    ]);

    private static async Task<Dictionary<string, string>> Run(string template, TableModel? table, ProjectSettings project)
    {
        var result = table is null ? await Repo.Cache.RunAsync(Repo.Template(template), project) : await Repo.Cache.RunAsync(Repo.Template(template), table, project);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return GeneratedFiles.Split(result.GeneratedText!.ReplaceLineEndings("\n")).ToDictionary(f => Path.GetFileName(f.RelativePath), f => f.Content.ReplaceLineEndings("\n"));
    }

    [TestMethod]
    public void The_plan_asks_for_the_first_required_sentence_and_one_refusal_per_rule_of_the_form()
    {
        var plan = DesktopTestPlan.For(Person(), Project(), DesktopTestStack.Uno)!;

        Assert.AreEqual("Full Name is required.", plan.RequiredSentence);
        Assert.AreEqual("PersonDetailViewModel", plan.ViewModel);
        CollectionAssert.AreEqual(new[]
        {
            "Refuses_a_Email_that_is_not_an_email_address", "Refuses_a_Born_On_later_than_today", "Refuses_a_End_Date_before_the_Start_Date", "Refuses_a_Hours_that_is_not_whole"
        }, plan.Refusals.Select(r => r.Name).ToArray(), string.Join(", ", plan.Refusals.Select(r => r.Name)));
        Assert.AreEqual("Email is not an email address.", plan.Refusals[0].Sentence);
        Assert.AreEqual("Born On must not be later than today.", plan.Refusals[1].Sentence);
        Expect.Contains(plan.RowJson, "\"personId\": 1");
        Expect.Contains(plan.RowJson, "\"fullName\":");
    }

    [TestMethod]
    public void A_table_with_child_tables_is_tested_through_its_master_detail_view_model()
    {
        var child = new ChildForeignKeyModel
        {
            ConstraintName = "FK_Badge_Person",
            ReferencingSchema = "dbo",
            ReferencingTable = "Badge",
            ReferencingColumns = ["PersonId"],
            ReferencedColumns = ["PersonId"],
            ReferencingPrimaryKeyColumns = ["BadgeId"],
            ReferencingTableColumns = [Sample.Column("BadgeId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Sample.Column("PersonId", SqlDbType.Int, ordinal: 2)]
        };
        var master = Sample.Table("Person", Person().Columns, childForeignKeys: [child]);

        var plan = DesktopTestPlan.For(master, Project(), DesktopTestStack.WinUI3)!;

        Assert.AreEqual("PersonDetailMasterViewModel", plan.ViewModel);
        Assert.AreEqual("LoadAsync", plan.LoadMethod);
    }

    [TestMethod]
    public async Task Both_stacks_write_the_same_cases_over_their_own_data()
    {
        var project = Project(("DesktopTests", "true"));
        string winui3 = (await Run("WinUI3_Test_v1.tt", Person(), project))["PersonDetailViewModelTests.cs"];
        string uno = (await Run("UNO_Test_v1.tt", Person(), project))["PersonDetailViewModelTests.cs"];

        foreach (string text in new[] { winui3, uno })
        {
            Expect.Contains(text, "Assert.AreEqual(\"Full Name is required.\", model.ErrorMessage);");
            Expect.Contains(text, "model.EndDate = model.StartDate!.Value.AddDays(-1);");
            Expect.Contains(text, "model.BornOn = DateTimeOffset.Now.AddYears(5);");
            Expect.Contains(text, "Assert.IsNull(TestJson.FirstDifference(");
        }
        Expect.Contains(winui3, "var db = await TestDb.CreateAsync();");
        Expect.Contains(winui3, "new PersonDetailViewModel(db.Context, edit ? Row() : null)");
        Expect.DoesNotContain(winui3, "FakeApi");
        Expect.Contains(uno, "new PersonDetailViewModel(api.Clients, edit ? Row() : null)");
        Expect.Contains(uno, "Assert.AreEqual(\"/api/persons/1\", write.Path);");
        Expect.Contains(uno, "Assert.AreEqual(\"Some of the values are not acceptable!\", model.ErrorMessage);");
        Expect.DoesNotContain(winui3, "BadRequest");
    }

    [TestMethod]
    public void A_row_version_column_starts_with_the_value_the_test_database_gives_it()
    {
        var table = Sample.Table("Stock",
        [
            Sample.Column("StockId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Title", SqlDbType.NVarChar, characters: 40, ordinal: 2),
            Sample.Column("Quantity", SqlDbType.Int, ordinal: 3),
            Sample.Column("RowVersion", SqlDbType.Timestamp, ordinal: 4)
        ]);

        Expect.Contains(DesktopTestPlan.For(table, Project(), DesktopTestStack.WinUI3)!.RowJson, $"\"rowVersion\": \"{DesktopTestCode.RowVersionBase64}\"");
    }

    [TestMethod]
    public async Task Nothing_is_written_unless_the_project_asks_for_the_tests()
    {
        Assert.AreEqual("", (await Run("UNO_Test_v1.tt", Person(), Project())).Count == 0 ? "" : "files");
        Assert.AreEqual("", (await Run("WinUI3_EssentialTests_v1.tt", null, Project())).Count == 0 ? "" : "files");
    }

    [TestMethod]
    public async Task The_test_project_compiles_the_apps_own_files_and_the_two_platform_stand_ins()
    {
        var project = Project(("DesktopTests", "true"), ("DatabaseProvider", "SqlServer"));
        var winui3 = await Run("WinUI3_EssentialTests_v1.tt", null, project);
        var uno = await Run("UNO_EssentialTests_v1.tt", null, project);

        string winui3Project = winui3["Acme.WinUI3.Tests.csproj"];
        Expect.Contains(winui3Project, "<Compile Include=\"../Acme.App/ViewModels/**/*.cs\" LinkBase=\"App/ViewModels\" />");
        Expect.Contains(winui3Project, "<Compile Include=\"../Acme.App/Repositories/**/*.cs\" LinkBase=\"App/Repositories\" />");
        Expect.Contains(winui3Project, "Microsoft.EntityFrameworkCore.SqlServer");
        Expect.Contains(winui3Project, "Microsoft.EntityFrameworkCore.Sqlite");
        Expect.DoesNotContain(winui3Project, "WindowsAppSDK");
        Expect.Contains(winui3["TestDb.cs"], "var context = new AcmeContext(new DbContextOptionsBuilder<AcmeContext>().UseSqlite(connection).Options);");
        Expect.Contains(winui3["TestDb.cs"], "BLOB NOT NULL DEFAULT x'0000000000000001'");
        string unoProject = uno["Acme.Uno.Tests.csproj"];
        Expect.Contains(unoProject, "<Compile Include=\"../Acme.Uno/Services/**/*.cs\" LinkBase=\"App/Services\" />");
        Expect.DoesNotContain(unoProject, "Uno.Sdk");
        Assert.AreEqual(winui3["PlatformShims.cs"], uno["PlatformShims.cs"]);
        Expect.Contains(uno["FakeApi.cs"], "public FakeApi() => Clients = new ApiClients(new HttpClient(_handler)");
    }

    [TestMethod]
    public void DesktopTests_adds_the_two_test_templates_two_test_folders_and_two_test_commands()
    {
        var project = Project(("DesktopTests", "true"), ("OutputWinUI3Tests", "checks"));

        CollectionAssert.IsSubsetOf(new[] { "WinUI3_Test", "UNO_Test" }, project.ImpliedPlanTemplates.ToArray());
        Assert.AreEqual("checks", project.OutputFolderOf("winui3tests"));
        Assert.AreEqual("Acme.Uno.Tests", project.OutputFolderOf("unotests"));
        Expect.Contains(ProjectBuilder.CommandFor(project, "WinUI3", "test")!, "checks");
        Expect.Contains(ProjectBuilder.CommandFor(project, "Uno", "test")!, "Acme.Uno.Tests");
        Assert.IsNull(ProjectBuilder.CommandFor(Project(), "Uno", "test"));
    }
}
