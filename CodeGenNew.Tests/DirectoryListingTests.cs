using System.Xml.Linq;
using CodeGenNew.Cli;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> WinUI3_DirectoryListing and the kind of template it is: one that needs no table and no database (TemplateConfig.NoDatabase). </summary>
[TestClass]
public class DirectoryListingTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static async Task<Dictionary<string, string>> Files(ProjectSettings project)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("WinUI3_DirectoryListing_v1.tt"), project);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return GeneratedFiles.Split(result.GeneratedText!).ToDictionary(f => f.RelativePath, f => f.Content.Replace("\r\n", "\n"));
    }

    [TestMethod]
    public async Task It_writes_the_four_files_with_the_default_names_and_a_folder_under_the_projects_local_app_data()
    {
        var files = await Files(Project());

        CollectionAssert.AreEquivalent(new[] { "Views/DocumentListPage.xaml", "Views/DocumentListPage.xaml.cs", "ViewModels/DocumentFileRow.cs", "ViewModels/DocumentListViewModel.cs" }, files.Keys.ToArray());
        Expect.Contains(files["Views/DocumentListPage.xaml"], "x:Class=\"Acme.App.Views.DocumentListPage\"");
        Expect.Contains(files["Views/DocumentListPage.xaml"], "Text=\"Documents\"");
        Expect.Contains(files["ViewModels/DocumentListViewModel.cs"], "public const string FolderTemplate = \"%LocalAppData%\\\\Acme\\\\Document\";");
        Expect.Contains(files["ViewModels/DocumentListViewModel.cs"], "public const string Pattern = \"*.*\";");
        Expect.Contains(files["ViewModels/DocumentListViewModel.cs"], "namespace Acme.App.ViewModels;");
    }

    [TestMethod]
    public async Task The_project_names_the_listing_its_folder_pattern_and_namespaces()
    {
        var files = await Files(Project(("ListingName", "InvoiceAttachment"), ("ListingFolder", @"D:\Share\Attachments"), ("ListingPattern", "*.pdf"),
            ("ViewNamespace", "Acme.Desktop.Pages"), ("ViewModelNamespace", "Acme.Desktop.Models"), ("ViewsFolder", "Pages"), ("ViewModelsFolder", "Models")));

        CollectionAssert.AreEquivalent(new[] { "Pages/InvoiceAttachmentListPage.xaml", "Pages/InvoiceAttachmentListPage.xaml.cs", "Models/InvoiceAttachmentFileRow.cs", "Models/InvoiceAttachmentListViewModel.cs" }, files.Keys.ToArray());
        Expect.Contains(files["Pages/InvoiceAttachmentListPage.xaml"], "Text=\"Invoice Attachments\"");
        Expect.Contains(files["Pages/InvoiceAttachmentListPage.xaml"], "xmlns:vm=\"using:Acme.Desktop.Models\"");
        Expect.Contains(files["Models/InvoiceAttachmentListViewModel.cs"], "FolderTemplate = \"D:\\\\Share\\\\Attachments\";");
        Expect.Contains(files["Models/InvoiceAttachmentListViewModel.cs"], "Pattern = \"*.pdf\";");
        Expect.Contains(files["Pages/InvoiceAttachmentListPage.xaml.cs"], "public sealed partial class InvoiceAttachmentListPage : Page");
    }

    [TestMethod]
    public async Task The_xaml_is_well_formed_and_the_page_wires_every_handler_it_names()
    {
        var files = await Files(Project());

        var xaml = XDocument.Parse(files["Views/DocumentListPage.xaml"]);   // a "--" in a comment, an unclosed tag ...
        string code = files["Views/DocumentListPage.xaml.cs"];
        foreach (string handler in xaml.Descendants().SelectMany(e => e.Attributes()).Where(a => a.Name.LocalName is "Click" or "LostFocus").Select(a => a.Value).Distinct())
            Expect.Contains(code, $"void {handler}(");
    }

    [TestMethod]
    public async Task Delete_arms_on_the_first_click_and_a_name_that_is_taken_gets_a_number()
    {
        var files = await Files(Project());

        Expect.Contains(files["ViewModels/DocumentListViewModel.cs"], "if (!row.IsArmed)");
        Expect.Contains(files["ViewModels/DocumentListViewModel.cs"], "({copy}){Path.GetExtension(name)}");
        Expect.Contains(files["ViewModels/DocumentFileRow.cs"], "public void Arm() => DeleteText = \"Confirm Delete?\";");
    }

    [TestMethod]
    public void A_no_database_template_is_offered_on_no_table_and_the_cli_needs_no_connection()
    {
        var config = TemplateConfig.Load(Repo.Template("WinUI3_DirectoryListing_v1.tt") + ".config");
        Assert.IsTrue(config.NoDatabase);
        var info = TemplateCatalog.FindByName(Repo.TemplatesDirectory, "WinUI3_DirectoryListing_v1.tt")!;
        Assert.IsFalse(info.AppliesTo(tableHasPrimaryKey: true, isView: false));

        var options = ArgumentParser.Parse(["-T", "WinUI3_DirectoryListing_v1.tt"]);   // no -S, -d, -U
        Assert.AreEqual("WinUI3_DirectoryListing_v1.tt", options.Template);
        Assert.AreEqual("-S/--server is required.", ArgumentParser.MissingConnection(options));
        Assert.IsNull(ArgumentParser.MissingConnection(ArgumentParser.Parse(["-S", "s", "-d", "d", "-E", "-T", "x.tt"])));
        Assert.AreEqual("-U/--user is required unless -E/--trusted is used.", ArgumentParser.MissingConnection(ArgumentParser.Parse(["-S", "s", "-d", "d", "-T", "x.tt"])));
    }
}
