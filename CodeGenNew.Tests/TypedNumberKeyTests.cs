using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> A whole-number key the database does not hand out (no identity, no default) is typed by the person on a new row, like a text key, and the entity says EF Core must send it. </summary>
[TestClass]
public class TypedNumberKeyTests
{
    private static TableModel Ticket(bool identity = false, string? defaultSql = null) => Sample.Table("Ticket",
    [
        Sample.Column("TicketNo", SqlDbType.Int, primaryKey: true, identity: identity, defaultSql: defaultSql, ordinal: 1),
        Sample.Column("Subject", SqlDbType.NVarChar, characters: 100, ordinal: 2),
        Sample.Column("Priority", SqlDbType.Int, ordinal: 3)
    ]);

    private static ProjectSettings Project(params KeyValuePair<string, string>[] more) =>
        ProjectSettings.FromValues([new("ProjectName", "Acme"), .. more]);

    [TestMethod]
    public void A_key_nobody_generates_is_typed_and_comes_first_on_the_form()
    {
        var form = FormModel.For(Ticket(), Project());

        Assert.IsTrue(form.TypedKey);
        Assert.IsFalse(form.TextKey);
        Assert.AreEqual("TicketNo", form.Columns[0].Name);
        Assert.AreEqual(FieldKind.TypedNumberKey, form.KindOf(form.Columns[0]));
        Assert.AreEqual("ticketNo: 0", form.NewRowTypeScript[0]);
    }

    [TestMethod]
    public void An_identity_a_default_or_a_key_sequence_hands_the_key_out_so_it_is_not_typed()
    {
        Assert.IsFalse(FormModel.For(Ticket(identity: true), Project()).TypedKey);
        Assert.IsFalse(FormModel.For(Ticket(defaultSql: "(NEXT VALUE FOR dbo.TicketSeq)"), Project()).TypedKey);
        Assert.IsFalse(FormModel.For(Ticket(), Project(new KeyValuePair<string, string>("KeySequenceTables", "Ticket"))).TypedKey);
        Assert.IsTrue(FormModel.For(Ticket(), Project()).TypedKey);
    }

    [TestMethod]
    public void A_text_key_is_still_typed_and_still_text()
    {
        var country = Sample.Table("Country", [Sample.Column("Alpha3Code", SqlDbType.Char, primaryKey: true, characters: 3, ordinal: 1), Sample.Column("Name", SqlDbType.NVarChar, characters: 40, ordinal: 2)]);
        var form = FormModel.For(country, Project());

        Assert.IsTrue(form.TypedKey);
        Assert.IsTrue(form.TextKey);
        Assert.AreEqual(FieldKind.TypedKey, form.KindOf(form.Columns[0]));
    }

    [TestMethod]
    public void The_entity_tells_EF_Core_not_to_generate_a_typed_whole_number_key()
    {
        string typed = EntityCode.Of(Ticket(), Project()).Body;
        string identity = EntityCode.Of(Ticket(identity: true), Project()).Body;

        StringAssert.Contains(typed, "[DatabaseGenerated(DatabaseGeneratedOption.None)]");
        Assert.DoesNotContain("DatabaseGenerated", identity);
        Assert.IsTrue(EntityCode.Of(Ticket(), Project()).UsesSchema);
    }

    [TestMethod]
    public void The_desktop_form_gives_the_key_a_number_box_that_is_off_once_the_row_exists()
    {
        var table = Ticket();
        var form = FormModel.For(table, Project());

        string xaml = DesktopFormXaml.Field(form, Project(), table, form.Columns[0], form.TextKey);

        StringAssert.Contains(xaml, "<NumberBox Header=\"Ticket No\"");
        StringAssert.Contains(xaml, "IsEnabled=\"{x:Bind ViewModel.IsNewRow}\"");
    }
}
