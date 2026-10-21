using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The controls and helpers every WinUI3 list page and dialog shares are written once by the Controls essentials group, not by the table templates. </summary>
[TestClass]
public class WinUI3ControlsTests
{
    private static readonly string[] TableTemplates = ["WinUI3_MasterScreen_v1.tt", "WinUI3_DetailScreen_v1.tt", "WinUI3_DetailMasterScreen_v1.tt"];

    /// <summary> The files of the Controls group for project Acme, by relative path (forward slashes). </summary>
    public static async Task<Dictionary<string, string>> Files(params (string Key, string Value)[] values)
    {
        var project = ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));
        var result = await Repo.Cache.RunAsync(Repo.Template("WinUI3_EssentialControls_v1.tt"), project);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return GeneratedFiles.Split(result.GeneratedText!).ToDictionary(f => f.RelativePath.Replace('\\', '/'), f => f.Content.Replace("\r\n", "\n"));
    }

    [TestMethod]
    public async Task The_group_writes_the_pager_the_sort_store_and_the_currency_formatter()
    {
        var files = await Files();

        CollectionAssert.AreEquivalent(
            new[] { "ViewModels/ViewModelBase.cs", "ViewModels/GridSortStore.cs", "Views/BusyOverlay.xaml", "Views/BusyOverlay.xaml.cs", "Views/CurrencyNumberFormatter.cs", "Views/PaginationBar.xaml", "Views/PaginationBar.xaml.cs" },
            files.Keys.ToArray());
        Expect.Contains(files["ViewModels/ViewModelBase.cs"], "public abstract partial class ViewModelBase : ObservableObject");
        Expect.Contains(files["ViewModels/ViewModelBase.cs"], "protected async Task RunBusyAsync(Func<Task> work)");
        Expect.Contains(files["Views/BusyOverlay.xaml"], "Visibility=\"{x:Bind IsActive, Mode=OneWay}\"");
        Expect.Contains(files["Views/PaginationBar.xaml"], "x:Class=\"Acme.App.Views.PaginationBar\"");
        Expect.Contains(files["Views/PaginationBar.xaml.cs"], "namespace Acme.App.Views;");
        Expect.Contains(files["Views/PaginationBar.xaml.cs"], "public sealed partial class PaginationBar : UserControl");
        Expect.Contains(files["ViewModels/GridSortStore.cs"], "namespace Acme.App.ViewModels;");
        Expect.Contains(files["Views/CurrencyNumberFormatter.cs"], "namespace Acme.App.Views;");
        Expect.Contains(files["Views/CurrencyNumberFormatter.cs"], "public double? ParseDouble(string text) => _currency.ParseDouble(text) ?? _plain.ParseDouble(text);");
    }

    [TestMethod]
    public async Task The_folders_and_namespaces_follow_the_project()
    {
        var files = await Files(("ViewNamespace", "Shop.Screens"), ("ViewModelNamespace", "Shop.Models"), ("ViewsFolder", "Pages"), ("ViewModelsFolder", "Logic"));

        Expect.Contains(files["Pages/PaginationBar.xaml"], "x:Class=\"Shop.Screens.PaginationBar\"");
        Expect.Contains(files["Logic/GridSortStore.cs"], "namespace Shop.Models;");
        Expect.Contains(files["Pages/CurrencyNumberFormatter.cs"], "namespace Shop.Screens;");
    }

    [TestMethod]
    [DataRow("WinUI3_MasterScreen_v1.tt")]
    [DataRow("WinUI3_DetailScreen_v1.tt")]
    [DataRow("WinUI3_DetailMasterScreen_v1.tt")]
    public void The_view_models_derive_from_the_base_and_do_not_repeat_its_members(string template)
    {
        string text = File.ReadAllText(Repo.Template(template));

        Expect.Contains(text, "ViewModel : ViewModelBase");
        Expect.DoesNotContain(text, "private string? _errorMessage;");
        Expect.DoesNotContain(text, "public bool HasError =>");
    }

    [TestMethod]
    [DataRow("WinUI3_MasterScreen_v1.tt")]
    [DataRow("WinUI3_DetailScreen_v1.tt")]
    [DataRow("WinUI3_DetailMasterScreen_v1.tt")]
    public void The_table_templates_no_longer_write_them(string template)
    {
        string text = File.ReadAllText(Repo.Template(template));

        foreach (string file in new[] { "PaginationBar.xaml", "GridSortStore.cs", "CurrencyNumberFormatter.cs" })
            Expect.DoesNotContain(text, "@@@FILE <#= viewsFolder #>/" + file);
        Expect.DoesNotContain(text, "@@@FILE <#= viewModelsFolder #>/GridSortStore.cs");
        Assert.HasCount(3, TableTemplates);
    }

    [TestMethod]
    public async Task The_uno_group_writes_the_same_three_controls()
    {
        var project = ProjectSettings.FromValues([new("ProjectName", "Acme")]);
        var result = await Repo.Cache.RunAsync(Repo.Template("UNO_EssentialControls_v1.tt"), project);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        var files = GeneratedFiles.Split(result.GeneratedText!).ToDictionary(f => f.RelativePath.Replace('\\', '/'), f => f.Content.ReplaceLineEndings("\n"));

        CollectionAssert.AreEquivalent(
            new[] { "ViewModels/GridSortStore.cs", "Views/CurrencyNumberFormatter.cs", "Views/PaginationBar.xaml", "Views/PaginationBar.xaml.cs" },
            files.Keys.ToArray());
        Expect.Contains(files["Views/PaginationBar.xaml"], "x:Class=\"Acme.App.Views.PaginationBar\"");
        Expect.Contains(files["ViewModels/GridSortStore.cs"], "namespace Acme.App.ViewModels;");
    }

    [TestMethod]
    [DataRow("UNO_MasterScreen_v1.tt")]
    [DataRow("UNO_DetailScreen_v1.tt")]
    [DataRow("UNO_DetailMasterScreen_v1.tt")]
    public void The_uno_table_templates_no_longer_write_them(string template)
    {
        string text = File.ReadAllText(Repo.Template(template));

        foreach (string file in new[] { "PaginationBar.xaml", "CurrencyNumberFormatter.cs" })
            Expect.DoesNotContain(text, "@@@FILE <#= viewsFolder #>/" + file);
        Expect.DoesNotContain(text, "@@@FILE <#= viewModelsFolder #>/GridSortStore.cs");
    }
}
