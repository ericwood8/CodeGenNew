using System.Data;
using CodeGenNew.Cli;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The project-settings follow-ups: --set and --projects-dir on the command line, the React / Angular folder names as project keys, NonNegativeColumns and the
/// project's currency in the WinUI3 list grids. </summary>
[TestClass]
public class ProjectFollowUpTests
{
    private static readonly string[] Connection = ["-S", "srv", "-d", "db", "-E", "-T", "CS_Entity_v1.tt"];

    [TestMethod]
    public void Set_overrides_any_project_setting_by_name_and_repeats()
    {
        var options = ArgumentParser.Parse([.. Connection, "--set", "currencycode=EUR", "--set", "NonNegativeColumns=CreditLimit,Item.Cost", "--set", "ModelsFolder="]);

        Assert.AreEqual("EUR", options.ProjectOverrides["CurrencyCode"]);
        Assert.AreEqual("CreditLimit,Item.Cost", options.ProjectOverrides["NonNegativeColumns"]);   // the value keeps its own commas and dots
        Assert.AreEqual("", options.ProjectOverrides["ModelsFolder"]);
    }

    [TestMethod]
    public void Set_refuses_an_unknown_key_and_a_value_without_an_equals_sign()
    {
        var unknown = Assert.ThrowsExactly<ArgumentParseException>(() => ArgumentParser.Parse([.. Connection, "--set", "Colour=Red"]));
        StringAssert.Contains(unknown.Message, "Unknown project setting 'Colour'");
        StringAssert.Contains(unknown.Message, "NonNegativeColumns");   // the known keys are listed

        var bare = Assert.ThrowsExactly<ArgumentParseException>(() => ArgumentParser.Parse([.. Connection, "--set", "CurrencyCode"]));
        StringAssert.Contains(bare.Message, "Key=Value");
    }

    [TestMethod]
    public void Projects_dir_names_the_folder_of_project_files()
    {
        Assert.AreEqual(@"C:\Shared\Projects", ArgumentParser.Parse([.. Connection, "--projects-dir", @"C:\Shared\Projects"]).ProjectsDirectory);
        Assert.IsNull(ArgumentParser.Parse(Connection).ProjectsDirectory);
    }

    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static TableModel Invoice() => Sample.Table("Invoice",
    [
        Sample.Column("InvoiceId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2),
        Sample.Column("CreditLimit", SqlDbType.Money, nullable: true, currency: true, ordinal: 3),
        Sample.Column("Balance", SqlDbType.Money, currency: true, ordinal: 4)
    ]);

    private static async Task<string> Render(string template, TableModel table, ProjectSettings project)
    {
        var result = await TemplateRunner.RunAsync(Repo.Template(template), table, project);
        Assert.IsTrue(result.Success, $"{template}: {string.Join(" | ", result.Errors)}");
        return result.GeneratedText!.Replace("\r\n", "\n");
    }

    [TestMethod]
    public async Task The_folder_keys_move_the_files_and_the_imports_between_them()
    {
        var project = Project(("ModelsFolder", "types"), ("ApiFolder", "clients"), ("PagesFolder", "screens"), ("ComponentsFolder", "widgets"), ("ServicesFolder", "data"));

        var model = GeneratedFiles.Split(await Render("TS_Model_v1.tt", Invoice(), project)).Single();
        Assert.AreEqual("types/invoice.ts", model.RelativePath);

        var api = GeneratedFiles.Split(await Render("TSX_Api_v1.tt", Invoice(), project)).Single();
        Assert.AreEqual("clients/invoiceApi.ts", api.RelativePath);
        Expect.Contains(api.Content, "from '../types/invoice';");

        var page = GeneratedFiles.Split(await Render("TSX_Page_v1.tt", Invoice(), project));
        Assert.IsTrue(page.Any(f => f.RelativePath == "screens/InvoicePage.tsx"), string.Join(", ", page.Select(f => f.RelativePath)));
        string tsx = page.Single(f => f.RelativePath == "screens/InvoicePage.tsx").Content;
        Expect.Contains(tsx, "from '../types/invoice';");
        Expect.Contains(tsx, "from '../clients/invoiceApi';");
        Expect.Contains(tsx, "from '../widgets/PaginationBar';");
        Expect.Contains(tsx, "from '../widgets/gridSort';");

        var service = GeneratedFiles.Split(await Render("TS_Service_v1.tt", Invoice(), project)).Single();
        Assert.AreEqual("data/invoice.service.ts", service.RelativePath);
        Expect.Contains(service.Content, "from '../types/invoice';");

        var component = GeneratedFiles.Split(await Render("TS_Component_v1.tt", Invoice(), project));
        Assert.IsTrue(component.All(f => f.RelativePath.StartsWith("widgets/invoice/")), string.Join(", ", component.Select(f => f.RelativePath)));
        Expect.Contains(component.Single(f => f.RelativePath.EndsWith(".component.ts")).Content, "from '../../data/invoice.service';");
    }

    [TestMethod]
    public async Task Without_folder_keys_the_defaults_are_the_samples_folders()
    {
        Assert.AreEqual("models/invoice.ts", GeneratedFiles.Split(await Render("TS_Model_v1.tt", Invoice(), Project())).Single().RelativePath);
        Assert.AreEqual("api/invoiceApi.ts", GeneratedFiles.Split(await Render("TSX_Api_v1.tt", Invoice(), Project())).Single().RelativePath);
        Assert.AreEqual("services/invoice.service.ts", GeneratedFiles.Split(await Render("TS_Service_v1.tt", Invoice(), Project())).Single().RelativePath);
    }

    [TestMethod]
    public void A_non_negative_column_is_named_by_itself_or_with_its_table()
    {
        var project = Project(("NonNegativeColumns", "Balance, Item.Cost"));

        Assert.IsTrue(project.IsNonNegative("Invoice", "Balance"));
        Assert.IsTrue(project.IsNonNegative("Customer", "balance"));
        Assert.IsTrue(project.IsNonNegative("Item", "Cost"));
        Assert.IsFalse(project.IsNonNegative("Invoice", "Cost"));
        Assert.IsFalse(Project().IsNonNegative("Invoice", "Balance"));
    }

    [TestMethod]
    public async Task A_non_negative_money_column_gets_a_minimum_of_zero_on_every_form()
    {
        var project = Project(("NonNegativeColumns", "Invoice.Balance"));

        var winui = GeneratedFiles.Split(await Render("WinUI3_DetailScreen_v1.tt", Invoice(), project));
        string xaml = winui.Single(f => f.RelativePath.EndsWith("DetailDialog.xaml")).Content;
        Assert.AreEqual(1, xaml.Split("Minimum=\"0\"").Length - 1, "only the listed column");
        Expect.Contains(xaml.Split("x:Name=\"BalanceBox\"")[1].Split("/>")[0], "Minimum=\"0\"");

        string tsx = await Render("TSX_Page_v1.tt", Invoice(), project);
        Assert.AreEqual(1, tsx.Split("min=\"0\"").Length - 1);

        string angular = string.Join("\n", GeneratedFiles.Split(await Render("TS_Component_v1.tt", Invoice(), project)).Select(f => f.Content));
        Assert.AreEqual(1, angular.Split("min=\"0\"").Length - 1);

        string none = string.Join("\n", GeneratedFiles.Split(await Render("TS_Component_v1.tt", Invoice(), Project())).Select(f => f.Content));
        Expect.DoesNotContain(none, "min=\"0\"");
    }

    [TestMethod]
    public async Task The_winui3_list_shows_money_in_the_projects_currency()
    {
        var files = GeneratedFiles.Split(await Render("WinUI3_MasterScreen_v1.tt", Invoice(), Project(("CurrencyCode", "EUR"))));
        string vm = files.Single(f => f.RelativePath.EndsWith("ListViewModel.cs")).Content;

        Expect.Contains(vm, "new Windows.Globalization.NumberFormatting.CurrencyFormatter(\"EUR\") { IsGrouped = true, FractionDigits = 2 }.FormatDouble((double)e.Balance)");
        Expect.Contains(vm, "e.CreditLimit is { } creditLimitMoney ? new Windows.Globalization.NumberFormatting.CurrencyFormatter(\"EUR\")");
        Expect.DoesNotContain(vm, "ToString(\"C2\")");
    }

    [TestMethod]
    [DataRow("TSX_Page_v1.tt")]
    [DataRow("TSX_DetailMasterPage_v1.tt")]
    public async Task A_react_foreign_key_select_starts_blank_and_a_cleared_optional_one_is_undefined_not_zero(string template)
    {
        var task = Sample.Table("Task",
        [
            Sample.Column("TaskId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2),
            Sample.Column("ProjectId", SqlDbType.Int, ordinal: 3),
            Sample.Column("OwnerId", SqlDbType.Int, nullable: true, ordinal: 4)
        ],
        [Sample.ForeignKey("ProjectId", "Project", "ProjectId", "Name"), Sample.ForeignKey("OwnerId", "Owner", "OwnerId", "Name")],
        childForeignKeys: [Sample.ChildForeignKey("TaskNote", "TaskId", "TaskId", childOwnPrimaryKey: ["TaskNoteId"])]);

        string tsx = GeneratedFiles.Split(await Render(template, task, Project())).Single(f => f.RelativePath.EndsWith("Page.tsx")).Content;

        // required: 0 is shown as blank (the row holds 0 until a parent is chosen), so the browser's required check refuses it instead of the first parent looking chosen
        Expect.Contains(tsx, "value={selectedRow.projectId || ''}");
        Expect.Contains(tsx, "projectId: Number(event.target.value)");
        // optional: choosing the blank clears the key (a 0 would name a parent that does not exist)
        Expect.Contains(tsx, "value={selectedRow.ownerId ?? ''}");
        Expect.Contains(tsx, "ownerId: event.target.value === '' ? undefined : Number(event.target.value)");
        Assert.AreEqual(2, tsx.Split("<option value=\"\"></option>").Length - 1, "a blank first entry in both");
    }
}
