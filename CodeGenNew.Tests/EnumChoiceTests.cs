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

    // ------------------------------------------------------------------ PostgreSQL: native enum types and CHECK lists

    [TestMethod]
    public void A_native_enum_is_a_string_as_long_as_its_longest_label_and_called_varchar_in_the_sql()
    {
        var (type, length, _, _, declaration) = PostgresSchemaProvider.MapType("ticket_status", null, null, null, ["Open", "In progress", "Closed"]);

        Assert.AreEqual("varchar", type);
        Assert.AreEqual(11, length);
        Assert.AreEqual("varchar(11)", declaration);
        Assert.AreEqual("sql_variant", PostgresSchemaProvider.MapType("ticket_status", null, null, null).SqlTypeName);   // without its labels it stays unsupported
    }

    [TestMethod]
    [DataRow("CHECK (((priority)::text = ANY ((ARRAY['Low'::character varying, 'Medium'::character varying, 'High'::character varying])::text[])))", "Low|Medium|High")]
    [DataRow("CHECK ((priority = ANY (ARRAY['Low'::text, 'High'::text])))", "Low|High")]
    [DataRow("CHECK (((kind)::text = ANY ((ARRAY['it''s'::character varying, 'a, b'::character varying, 'x]y'::character varying])::text[])))", "it's|a, b|x]y")]
    public void A_check_that_lists_values_gives_them(string definition, string expected)
    {
        CollectionAssert.AreEqual(expected.Split('|'), PostgresSchemaProvider.ParseCheckValues(definition)!);
    }

    [TestMethod]
    [DataRow("CHECK ((points > 0))")]
    [DataRow("CHECK ((points >= 0) AND (points <= 10))")]
    [DataRow("CHECK (((status)::text = ANY ((ARRAY['A'::character varying, 'B'::character varying])::text[])) OR (status IS NULL))")]
    [DataRow("CHECK (((status)::text <> ALL ((ARRAY['A'::character varying])::text[])))")]
    [DataRow("CHECK ((length((code)::text) = ANY (ARRAY[3, 5])))")]
    [DataRow("CHECK (((a)::text = ANY ((ARRAY[(b)::text, 'X'::text])::text[])))")]
    public void A_check_that_is_not_a_plain_list_gives_nothing(string definition)
    {
        Assert.IsNull(PostgresSchemaProvider.ParseCheckValues(definition));
    }

    [TestMethod]
    public async Task The_postgres_search_compares_an_enum_columns_text()
    {
        var ticket = Sample.Table("Ticket",
        [
            Sample.Column("TicketId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Title", SqlDbType.NVarChar, characters: 50, ordinal: 2),
            Sample.Column("Status", SqlDbType.NVarChar, characters: 11, ordinal: 3, choices: ["Open", "Closed"], enumType: "public.ticket_status")
        ]);
        var pg = new TableModel
        {
            SchemaName = "public", TableName = ticket.TableName, QuotedName = ticket.QuotedName, Dialect = SqlDialect.PostgreSql,
            Columns = ticket.Columns, PrimaryKeyColumns = ticket.PrimaryKeyColumns, ForeignKeys = ticket.ForeignKeys, ChildForeignKeys = ticket.ChildForeignKeys,
            DisplayColumns = ticket.DisplayColumns, LookupShape = ticket.LookupShape
        };

        string sql = await Render("SP_Search_v1.tt", pg);

        Expect.Contains(sql, "\"Status\"::text ILIKE '%' || btrim(");   // an enum has no ILIKE of its own
        Expect.Contains(sql, "THEN t.\"Status\"::text END ASC");          // and sorts by its text
        Expect.DoesNotContain(sql, "\"Title\"::text");
    }

    [TestMethod]
    public void A_unique_enum_column_has_no_suggested_value_so_the_table_gets_no_clone_button()
    {
        var table = Sample.Table("Ticket",
        [
            Sample.Column("TicketId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Status", SqlDbType.NVarChar, characters: 11, inUniqueIndex: true, ordinal: 2, choices: ["Open"], enumType: "public.ticket_status")
        ]);

        Assert.IsFalse(CloneShape.CanClone(table, ProjectSettings.FromValues([new KeyValuePair<string, string>("ProjectName", "Acme")])));
    }

    [TestMethod]
    public async Task The_cast_script_creates_one_guarded_assignment_cast_per_enum_type()
    {
        TableModel Table(string name, params string[] types) => Sample.Table(name,
            [Sample.Column(name + "Id", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
             ..types.Select((t, i) => Sample.Column("Kind" + i, SqlDbType.NVarChar, characters: 10, ordinal: i + 2, choices: ["A"], enumType: t))]);
        var db = new DatabaseModel
        {
            DatabaseName = "Acme", SchemaName = "public", Dialect = SqlDialect.PostgreSql,
            Tables = [Table("Ticket", "public.ticket_status", "tools.\"odd\""), Table("Task", "public.ticket_status")]
        };

        var result = await TemplateRunner.RunAsync(Repo.Template("SP_EnumCasts_v1.tt"), db);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        var file = GeneratedFiles.Split(result.GeneratedText!).Single();

        Assert.AreEqual("EnumCasts.sql", file.RelativePath);
        string sql = file.Content.Replace("\r\n", "\n");
        Expect.Contains(sql, "CREATE CAST (text AS \"public\".\"ticket_status\") WITH INOUT AS ASSIGNMENT;");
        Expect.Contains(sql, "CREATE CAST (character varying AS \"public\".\"ticket_status\") WITH INOUT AS ASSIGNMENT;");   // the entity says varchar(n)
        Expect.Contains(sql, "CREATE CAST (text AS \"tools\".\"\"\"odd\"\"\") WITH INOUT AS ASSIGNMENT;");   // a quote in a name is doubled
        Expect.Contains(sql, "IF NOT EXISTS (SELECT 1 FROM pg_cast WHERE castsource = 'text'::regtype");
        Assert.AreEqual(1, sql.Split("CREATE CAST (text AS \"public\".\"ticket_status\")").Length - 1, "one cast per type, however many columns use it");
    }

    [TestMethod]
    public async Task The_cast_script_is_for_postgres_and_says_so_when_there_is_nothing_to_cast()
    {
        var other = await TemplateRunner.RunAsync(Repo.Template("SP_EnumCasts_v1.tt"), new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [Sample.Holiday()] });
        Assert.IsFalse(other.Success);
        StringAssert.Contains(string.Join(" | ", other.Errors), "PostgreSQL");

        var none = await TemplateRunner.RunAsync(Repo.Template("SP_EnumCasts_v1.tt"),
            new DatabaseModel { DatabaseName = "Acme", SchemaName = "public", Dialect = SqlDialect.PostgreSql, Tables = [Sample.Holiday()] });
        Assert.IsTrue(none.Success, string.Join(" | ", none.Errors));
        Expect.Contains(none.GeneratedText!, "nothing to cast");
    }
}
