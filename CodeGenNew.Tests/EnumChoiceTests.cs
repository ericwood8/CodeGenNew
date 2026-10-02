using System.Data;
using CodeGenNew.Core;
using CodeGenNew.SchemaIntrospection;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> A column whose values the database lists (a MySQL enum) is a drop-down on every screen instead of a free text box. </summary>
[TestClass]
public class EnumChoiceTests
{
    [TestMethod]
    public void An_enum_column_type_gives_its_values()
    {
        CollectionAssert.AreEqual(new[] { "Open", "Closed", "On hold" }, MySqlSchemaProvider.ParseEnumValues("enum('Open','Closed','On hold')")!);
    }

    [TestMethod]
    public void A_quote_inside_a_value_is_doubled_and_a_comma_stays_inside_the_value()
    {
        CollectionAssert.AreEqual(new[] { "it's", "a,b" }, MySqlSchemaProvider.ParseEnumValues("enum('it''s','a,b')")!);
    }

    [TestMethod]
    [DataRow("varchar(50)")]
    [DataRow("set('a','b')")]
    [DataRow("int")]
    public void Only_an_enum_gives_choices(string columnType)
    {
        Assert.IsNull(MySqlSchemaProvider.ParseEnumValues(columnType));
    }

    private static TableModel Ticket(bool nullable) => Sample.Table("Ticket",
    [
        Sample.Column("TicketId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Title", SqlDbType.NVarChar, characters: 100, ordinal: 2),
        Sample.Column("Status", SqlDbType.NVarChar, characters: 20, nullable: nullable, ordinal: 3, choices: ["Open", "Won't fix", "A & B"])
    ],
    // the master-detail templates need a child table
    childForeignKeys: [Sample.ChildForeignKey("TicketNote", "TicketId", "TicketId", childOwnPrimaryKey: ["TicketNoteId"])]);

    private static async Task<string> Render(string template, TableModel table)
    {
        var result = await TemplateRunner.RunAsync(Repo.Template(template), table);
        Assert.IsTrue(result.Success, $"{template}: {string.Join(" | ", result.Errors)}");
        return result.GeneratedText!.Replace("\r\n", "\n");
    }

    [TestMethod]
    [DataRow("TSX_Page_v1.tt")]
    [DataRow("TSX_DetailMasterPage_v1.tt")]
    public async Task React_shows_the_values_in_a_select(string template)
    {
        string tsx = await Render(template, Ticket(nullable: false));

        Expect.Contains(tsx, "<option value=\"Open\">Open</option>");
        Expect.Contains(tsx, "<option value=\"Won't fix\">Won't fix</option>");
        Expect.Contains(tsx, "<option value=\"A &amp; B\">A &amp; B</option>");
        Expect.Contains(tsx, "value={selectedRow.status ?? ''}");
        Expect.DoesNotContain(tsx, "aria-label=\"Status\"\n              value={selectedRow.status}");
    }

    [TestMethod]
    public async Task React_lets_a_nullable_enum_be_cleared()
    {
        string tsx = await Render("TSX_Page_v1.tt", Ticket(nullable: true));

        Expect.Contains(tsx, "status: event.target.value || undefined");
    }

    [TestMethod]
    [DataRow("TS_Component_v1.tt")]
    [DataRow("TS_DetailMasterComponent_v1.tt")]
    public async Task Angular_shows_the_values_in_a_select(string template)
    {
        string files = await Render(template, Ticket(nullable: false));

        Expect.Contains(files, "[(ngModel)]=\"selectedRow.status\" required>");
        Expect.Contains(files, "<option value=\"Open\">Open</option>");
        Expect.Contains(files, "<option value=\"A &amp; B\">A &amp; B</option>");
    }

    [TestMethod]
    [DataRow("WinUI3_DetailScreen_v1.tt")]
    [DataRow("WinUI3_DetailMasterScreen_v1.tt")]
    public async Task WinUI3_shows_the_values_in_a_combo_box(string template)
    {
        var files = GeneratedFiles.Split(await Render(template, Ticket(nullable: false)));
        string xaml = files.Single(f => f.RelativePath.EndsWith("DetailDialog.xaml") || f.RelativePath.EndsWith("DetailMasterDialog.xaml")).Content;
        string viewModel = files.Single(f => f.RelativePath.EndsWith("ViewModel.cs")).Content;

        Expect.Contains(xaml, "<ComboBox Header=\"Status\" ItemsSource=\"{x:Bind ViewModel.StatusChoices}\"");
        Expect.Contains(xaml, "SelectedItem=\"{x:Bind ViewModel.Status, Mode=TwoWay}\"");
        Expect.Contains(viewModel, "public string[] StatusChoices { get; } = [\"Open\", \"Won't fix\", \"A & B\"];");
    }

    [TestMethod]
    public async Task A_column_without_choices_stays_a_text_box()
    {
        string tsx = await Render("TSX_Page_v1.tt", Sample.Holiday());

        Expect.DoesNotContain(tsx, "<option value=\"Open\"");
        Expect.Contains(tsx, "type=\"text\"");
    }
}
