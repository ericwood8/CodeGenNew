using System.Data;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The model every front end reads for a form and for the child grids under it (FormModel, ChildGrid), and a test that the React, Angular, Blazor and WinUI3 screens of one table edit the same fields. </summary>
[TestClass]
public class FormModelTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static ColumnModel Text(string name, int ordinal, bool nullable = false) => Sample.Column(name, SqlDbType.NVarChar, characters: 40, nullable: nullable, ordinal: ordinal);

    /// <summary> Account: a key, a Name that is not first, a password hash, a binary column, a computed one, a parent that is looked up, a styling parent the project hides, and a parent with no display column. </summary>
    private static TableModel Account() => Sample.Table("Account",
    [
        Sample.Column("AccountId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Text("Nickname", 2),
        Text("Name", 3),
        Text("PasswordHash", 4),
        Sample.Column("Photo", SqlDbType.VarBinary, nullable: true, ordinal: 5),
        Sample.Column("FullLabel", SqlDbType.NVarChar, characters: 80, computed: true, ordinal: 6),
        Sample.Column("RegionId", SqlDbType.Int, ordinal: 7),
        Sample.Column("StyleId", SqlDbType.Int, ordinal: 8),
        Sample.Column("ParentAccountId", SqlDbType.Int, nullable: true, ordinal: 9),
        Sample.Column("Balance", SqlDbType.Decimal, precision: 18, scale: 2, ordinal: 10, currency: true),
        Sample.Column("CreateDate", SqlDbType.DateTime, createDateColumn: true, ordinal: 11)
    ],
    [
        Sample.ForeignKey("RegionId", "Region", "RegionId", "Name"),
        Sample.ForeignKey("StyleId", "Style", "StyleId", "Name"),
        Sample.ForeignKey("ParentAccountId", "Account", "AccountId", "Name")
    ]);

    [TestMethod]
    public void The_form_has_the_columns_a_person_fills_in_with_Name_first()
    {
        var form = FormModel.For(Account(), Project(("HiddenParents", "Style")));
        CollectionAssert.AreEqual(new[] { "Name", "Nickname", "RegionId", "ParentAccountId", "Balance", "CreateDate" }, form.Columns.Select(c => c.Name).ToArray(),
            "no key, no password hash, no binary, no computed column, no foreign key to a hidden parent");
    }

    [TestMethod]
    public void A_foreign_key_is_a_drop_down_unless_the_parent_has_no_display_column_or_the_project_says_no_and_a_tree_looks_itself_up()
    {
        var table = Account();
        var form = FormModel.For(table, Project());
        Assert.IsNotNull(form.LookupOf(table.Columns.First(c => c.Name == "RegionId")));
        Assert.IsNotNull(form.LookupOf(table.Columns.First(c => c.Name == "ParentAccountId")), "a foreign key to the table itself is a drop-down too");
        Assert.IsTrue(form.IsSelfReference(form.LookupOf(table.Columns.First(c => c.Name == "ParentAccountId"))!));
        Assert.IsNull(form.LookupOf(table.Columns.First(c => c.Name == "Nickname")), "not a foreign key");
        CollectionAssert.AreEqual(new[] { "Region", "Style", "Account" }, form.Parents.Select(p => p.ReferencedTable).ToArray(), "one parent per table, in column order");

        var plain = FormModel.For(table, Project(("NoLookupParents", "Region")));
        Assert.IsNull(plain.LookupOf(table.Columns.First(c => c.Name == "RegionId")), "NoLookupParents leaves a plain box");
        CollectionAssert.DoesNotContain(plain.Parents.Select(p => p.ReferencedTable).ToList(), "Region");
    }

    [TestMethod]
    public void A_typed_key_is_the_first_field_and_money_is_a_money_input_only_when_nothing_else_edits_it()
    {
        var country = Sample.Table("Country",
        [
            Sample.Column("Alpha3Code", SqlDbType.Char, primaryKey: true, characters: 3, ordinal: 1),
            Text("Name", 2),
            Sample.Column("Fee", SqlDbType.Decimal, precision: 18, scale: 2, ordinal: 3, currency: true)
        ]);
        var form = FormModel.For(country, Project());
        Assert.IsTrue(form.TextKey);
        Assert.AreEqual("Alpha3Code", form.Columns[0].Name);
        Assert.IsTrue(form.IsMoneyInput(country.Columns.First(c => c.Name == "Fee")));
        Assert.IsFalse(form.IsMoneyInput(country.Columns.First(c => c.Name == "Name")));

        var account = Account();
        var accountForm = FormModel.For(account, Project());
        Assert.IsTrue(accountForm.IsMoneyInput(account.Columns.First(c => c.Name == "Balance")));
        Assert.IsTrue(account.Columns.First(c => c.Name == "Balance").IsMoneyAmount);
        Assert.IsFalse(Sample.Column("Quantity", SqlDbType.Int, currency: true).IsMoneyAmount, "a whole number the name calls money is not a money amount");
    }

    private static ChildForeignKeyModel ChildLink(string childTable, string childColumn, params ForeignKeyModel[] otherKeys) =>
        new()
        {
            ConstraintName = $"FK_{childColumn}_{childTable}",
            ReferencingSchema = "dbo",
            ReferencingTable = childTable,
            ReferencingColumns = [childColumn],
            ReferencedColumns = ["AccountId"],
            ReferencingPrimaryKeyColumns = [childTable + "Id"],
            ReferencingTableColumns =
            [
                Sample.Column(childTable + "Id", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
                Sample.Column(childColumn, SqlDbType.Int, ordinal: 2),
                Sample.Column("ItemId", SqlDbType.Int, ordinal: 3),
                Sample.Column("RegionId", SqlDbType.Int, ordinal: 4),
                Text("Memo", 5)
            ],
            ReferencingTableForeignKeys = [Sample.ForeignKey(childColumn, "Account", "AccountId", "Name"), .. otherKeys]
        };

    [TestMethod]
    public void A_child_grid_names_the_roles_when_a_child_references_the_parent_twice_and_resolves_the_other_foreign_keys()
    {
        var items = Sample.ForeignKey("ItemId", "Item", "ItemId", "ItemNumber");
        var region = Sample.ForeignKey("RegionId", "Region", "RegionId", "Name");
        var table = Sample.Table("Account", [Sample.Column("AccountId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Text("Name", 2)],
            childForeignKeys: [ChildLink("Transfer", "FromAccountId", items, region), ChildLink("Transfer", "ToAccountId", items, region), ChildLink("Note", "AccountId", items)]);

        var grids = ChildGrid.For(table, Project(("NoLookupParents", "Region")));

        CollectionAssert.AreEqual(new[] { "TransferFromAccount", "TransferToAccount", "Note" }, grids.Select(g => g.Name).ToArray(), "one grid per foreign key, told apart by the role");
        Assert.AreEqual("Transfer (From Account)", grids[0].Title);
        Assert.AreEqual("Note", grids[2].Title);
        Assert.AreEqual("TransferId", grids[0].KeyName);

        CollectionAssert.AreEqual(new[] { "ItemId" }, grids[0].Lookups.Select(l => l.ColumnName).ToArray(), "Region is not looked up, so its id is not named; the foreign key back to the account is the grid's own");
        Assert.IsTrue(grids[0].Lookups[0].UsesLookupEndpoint, "a whole-number key is named by the parent's lookup list");
        CollectionAssert.AreEqual(new[] { "FromAccountId", "TransferId", "RegionId" }, grids[0].HiddenColumns.ToArray(), "this row's key, the child's own key, and the ids that are not named");
    }

    [TestMethod]
    public void A_child_grid_follows_the_projects_title_the_api_exclusions_and_the_editable_list()
    {
        var table = Sample.Table("Account", [Sample.Column("AccountId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Text("Name", 2)],
            childForeignKeys: [ChildLink("Note", "AccountId"), ChildLink("Audit", "AccountId")]);

        var grids = ChildGrid.For(table, Project(("ChildGridTitles", "Account.Note=Remarks"), ("NoApiTables", "Audit"), ("EditableChildren", "Account.Note")));

        Assert.AreEqual(1, grids.Count, "a child table the project gives no API has no grid");
        Assert.AreEqual("Remarks", grids[0].Title);
        Assert.IsTrue(grids[0].IsEditable);
    }

    [TestMethod]
    public void The_captions_the_stacks_share_come_from_one_place()
    {
        Assert.AreEqual("Customer", Labels.Column("CustomerId"));
        Assert.AreEqual("Role", Labels.Column("SY_RoleId"));
        Assert.AreEqual("Is Taxable?", Labels.Field(Sample.Column("IsTaxable", SqlDbType.Bit)));
        Assert.AreEqual("Credit Limit", Labels.Field(Sample.Column("CreditLimit", SqlDbType.Decimal)));
        Assert.AreEqual("Enter credit limit", Labels.Placeholder(Sample.Column("CreditLimit", SqlDbType.Decimal)));
        Assert.AreEqual("sales-invoice", ScreenNames.Route("SalesInvoice"));
        Assert.AreEqual("salesinvoices", ScreenNames.ApiRoute("SalesInvoice"));
        Assert.AreEqual("/api/salesinvoices", BlazorNames.ApiRoute("SalesInvoice"), "the Blazor clients and the React and Angular ones build the same route");
    }

    // ------------------------------------------------------------------ the four stacks edit the same fields

    private static async Task<string> Rendered(string template, TableModel table, ProjectSettings project)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, project);
        Assert.IsTrue(result.Success, $"{template}: {string.Join(" | ", result.Errors)}");
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    [TestMethod]
    public async Task React_Angular_Blazor_and_WinUI3_edit_the_same_fields_of_a_table()
    {
        // StyleId is a hidden parent and PasswordHash, Photo and FullLabel are never edited; the system writes CreateDate (shown as text, not a field)
        var table = Account();
        var project = Project(("HiddenParents", "Style"), ("AngularVersion", "22"));
        var expected = new[] { "balance", "name", "nickname", "parentaccountid", "regionid" };

        static string[] Fields(string text, string pattern) => System.Text.RegularExpressions.Regex.Matches(text, pattern)
            .Select(m => m.Groups[1].Value.ToLowerInvariant().Replace("-", "")).Where(f => f != "createdate" && f != "form").Distinct().Order().ToArray();

        // the id of each input: account-nickname (React, Blazor), accountNickname (Angular); a WinUI3 field is a two-way binding of the view model
        CollectionAssert.AreEqual(expected, Fields(await Rendered("TSX_Page_v1.tt", table, project), "id=\"account-([A-Za-z0-9]+)\""), "React");
        CollectionAssert.AreEqual(expected, Fields(await Rendered("TS_Component_v1.tt", table, project), "id=\"account([A-Z][A-Za-z0-9]+)\""), "Angular");
        CollectionAssert.AreEqual(expected, Fields(await Rendered("BLZ_Page_v1.tt", table, project), "id=\"account-([A-Za-z0-9]+)\""), "Blazor");
        CollectionAssert.AreEqual(expected, Fields(await Rendered("WinUI3_DetailScreen_v1.tt", table, project), @"ViewModel\.(\w+), Mode=TwoWay"), "WinUI3");
    }
}
