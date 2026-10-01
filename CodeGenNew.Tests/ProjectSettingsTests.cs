using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

[TestClass]
public class ProjectSettingsTests
{
    [TestMethod]
    public void No_project_gives_null_everywhere_so_a_template_keeps_its_own_values()
    {
        var none = ProjectSettings.None;

        Assert.IsNull(none.ViewNamespace);
        Assert.IsNull(none.ContextName);
        Assert.IsNull(none.NoLookupParents);
        Assert.AreEqual(ProjectSettings.DefaultMinYear, none.MinYear);
        Assert.AreEqual(ProjectSettings.DefaultMaxYear, none.MaxYear);
    }

    [TestMethod]
    public void Only_the_project_name_is_needed_every_namespace_is_derived_from_it()
    {
        var project = ProjectSettings.Parse("ProjectName=InvoiceSystem");

        Assert.AreEqual("InvoiceSystem.App.Views", project.ViewNamespace);
        Assert.AreEqual("InvoiceSystem.App.ViewModels", project.ViewModelNamespace);
        Assert.AreEqual("InvoiceSystemContext", project.ContextName);
        Assert.AreEqual("InvoiceSystem.App.Data", project.ContextNamespace);
        Assert.AreEqual("InvoiceSystem.App.Entities", project.EntityNamespace);
        Assert.AreEqual("InvoiceSystem.App.Repositories", project.RepoNamespace);
    }

    [TestMethod]
    public void An_explicit_value_beats_the_derived_one_and_comments_and_blank_lines_are_skipped()
    {
        var project = ProjectSettings.Parse("# a comment\r\n\r\nProjectName = Acme\r\nViewNamespace = Acme.Desktop.Views\r\nMinYear=1990\r\nnot a setting\r\n");

        Assert.AreEqual("Acme.Desktop.Views", project.ViewNamespace);
        Assert.AreEqual("Acme.App.ViewModels", project.ViewModelNamespace);
        Assert.AreEqual(1990, project.MinYear);
    }

    [TestMethod]
    public void Once_a_project_is_chosen_an_unlisted_table_list_is_empty_not_the_templates_own()
    {
        var project = ProjectSettings.Parse("ProjectName=Acme\nNoLookupParents=Status, Category");

        CollectionAssert.AreEqual(new[] { "Status", "Category" }, project.NoLookupParents);
        Assert.HasCount(0, project.NoApiTables!);
    }

    [TestMethod]
    public void Command_line_overrides_win_over_the_file()
    {
        var project = ProjectSettings.Parse("ProjectName=Acme\nViewNamespace=Acme.Views")
            .WithOverrides([new("ViewNamespace", "Other.Views"), new("MaxYear", "2050")]);

        Assert.AreEqual("Other.Views", project.ViewNamespace);
        Assert.AreEqual(2050, project.MaxYear);
    }

    [TestMethod]
    public void Saved_text_reads_back_the_same()
    {
        var original = ProjectSettings.Parse("ProjectName=Acme\nContextNamespace=Acme.Db\nUsings=Acme.Common,Acme.Extras");

        var reread = ProjectSettings.Parse(original.ToFileText());

        Assert.AreEqual("Acme.Db", reread.ContextNamespace);
        CollectionAssert.AreEqual(new[] { "Acme.Common", "Acme.Extras" }, reread.Usings);
    }

    [TestMethod]
    public void A_project_file_that_omits_its_name_takes_it_from_the_file_name()
    {
        using var temp = new TempFolder();
        temp.File("Acme.config", "ViewNamespace=Acme.Views\n");

        var project = ProjectSettings.LoadNamed(temp.Path, "Acme");

        Assert.AreEqual("Acme", project.ProjectName);
        CollectionAssert.AreEqual(new[] { "Acme" }, ProjectSettings.ListProjects(temp.Path).ToArray());
    }

    [TestMethod]
    public void A_missing_project_file_is_reported()
    {
        using var temp = new TempFolder();

        Assert.ThrowsExactly<FileNotFoundException>(() => ProjectSettings.LoadNamed(temp.Path, "Nope"));
    }

    [TestMethod]
    [DataRow("WinUI3_MasterScreen_v1.tt", "namespace Acme.Ui.Views;", "AcmeDb")]
    [DataRow("CS_Repo_v1.tt", "namespace Acme.Repos;", "AcmeDb")]
    [DataRow("API_Crud_v1.tt", "namespace Acme.Web;", "AcmeDb")]
    public async Task A_project_replaces_the_namespaces_and_context_the_template_would_hard_code(string template, string expectedNamespace, string expectedContext)
    {
        var project = ProjectSettings.Parse(
            "ProjectName=Acme\nViewNamespace=Acme.Ui.Views\nRepoNamespace=Acme.Repos\nApiNamespace=Acme.Web\nContextName=AcmeDb");

        var model = template == "API_Crud_v1.tt" ? Sample.DonateLeave() : Sample.Holiday();
        var result = await TemplateRunner.RunAsync(Repo.Template(template), model, project);

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        Expect.Contains(result.GeneratedText!, expectedNamespace);
        Expect.Contains(result.GeneratedText!, expectedContext);
        Expect.DoesNotContain(result.GeneratedText!, "TimeEntryContext");
    }

    [TestMethod]
    public async Task With_no_project_a_template_generates_with_its_own_built_in_values()
    {
        var result = await TemplateRunner.RunAsync(Repo.Template("CS_Repo_v1.tt"), Sample.Holiday());

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        Expect.Contains(result.GeneratedText!, "TimeEntryContext");
    }

    [TestMethod]
    public async Task A_child_grid_row_is_clickable_and_opens_the_childs_own_dialog()
    {
        var result = await TemplateRunner.RunAsync(Repo.Template("WinUI3_DetailMasterScreen_v1.tt"), Sample.OrderWithLines());

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string text = result.GeneratedText!.Replace("\r\n", "\n");
        Expect.DoesNotContain(text, "IsEnabled=\"False\"");
        Expect.Contains(text, "IsItemClickEnabled=\"True\" ItemClick=\"OnOrderLineItemClick\"");
        Expect.Contains(text, "Entity: OrderLine entity");
        Expect.Contains(text, "new OrderLineDetailDialog(_context, entity)");
        Expect.Contains(text, "<x:Double x:Key=\"ContentDialogMaxWidth\">");
    }

    [TestMethod]
    public async Task A_table_listed_in_DetailMasterTables_opens_its_detail_master_dialog_from_the_list_and_from_a_parents_grid()
    {
        var project = ProjectSettings.Parse("ProjectName=Acme\nDetailMasterTables=Order,OrderLine");

        var list = await TemplateRunner.RunAsync(Repo.Template("WinUI3_MasterScreen_v1.tt"), Sample.OrderWithLines(), project);
        var master = await TemplateRunner.RunAsync(Repo.Template("WinUI3_DetailMasterScreen_v1.tt"), Sample.OrderWithLines(), project);

        Expect.Contains(list.GeneratedText!, "new OrderDetailMasterDialog(_context)");
        Expect.Contains(master.GeneratedText!, "new OrderLineDetailMasterDialog(_context, entity)");
    }
}
