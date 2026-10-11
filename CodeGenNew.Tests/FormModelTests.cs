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
        CollectionAssert.AreEqual(new[] { "Region", "Style", "Account" }, form.Parents.Select(p => p.Table).ToArray(), "one parent per table, in column order");

        var plain = FormModel.For(table, Project(("NoLookupParents", "Region")));
        Assert.IsNull(plain.LookupOf(table.Columns.First(c => c.Name == "RegionId")), "NoLookupParents leaves a plain box");
        CollectionAssert.DoesNotContain(plain.Parents.Select(p => p.Table).ToList(), "Region");
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

    [TestMethod]
    public void A_parent_carries_what_every_stack_names_its_drop_down_from()
    {
        var table = Account();
        var form = FormModel.For(table, Project(("HiddenParents", "Style")));
        var region = form.Parents.First(p => p.Table == "Region");
        Assert.AreEqual("Region", region.BaseName);
        Assert.AreEqual("RegionId", region.Column.Name);
        Assert.AreEqual("RegionId", region.KeyName);
        Assert.AreEqual("Name", region.DisplayName);
        Assert.AreEqual("regions", region.List);
        Assert.AreEqual("regionName", region.NameMethod);
        Assert.AreEqual("number", region.KeyTs);
        Assert.AreEqual("int", region.KeyCs);
        Assert.AreEqual("Lookup", region.LookupType);
        Assert.IsTrue(region.UsesLookupEndpoint);
        Assert.AreEqual("id", region.KeyJson);
        Assert.AreEqual("name", region.DisplayJson);
        Assert.IsFalse(region.IsSelf);
        Assert.IsTrue(form.Parents.First(p => p.Table == "Account").IsSelf, "the manager of an account is an account");
        Assert.AreSame(region, form.ParentOf(table.Columns.First(c => c.Name == "RegionId")));
        Assert.IsNull(form.ParentOf(table.Columns.First(c => c.Name == "Nickname")));
    }

    [TestMethod]
    public void A_parent_with_a_guid_key_reads_its_rows_not_the_lookup_list()
    {
        var table = Sample.Table("Pet",
        [
            Sample.Column("PetId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Text("Name", 2),
            Sample.Column("OwnerId", SqlDbType.UniqueIdentifier, ordinal: 3)
        ],
        [Sample.ForeignKey("OwnerId", "Owner", "OwnerId", "Name")]);
        var owner = FormModel.For(table, Project()).Parents.Single();
        Assert.IsFalse(owner.UsesLookupEndpoint);
        Assert.AreEqual("ownerId", owner.KeyJson);
        Assert.AreEqual("name", owner.DisplayJson);
    }

    [TestMethod]
    public void The_names_of_a_table_on_every_screen_come_from_the_model()
    {
        var table = Sample.Table("E_TimeSheet", [Sample.Column("TimeSheetId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Text("StartDate", 2)]);
        var form = FormModel.For(table, Project());
        Assert.AreEqual("TimeSheet", form.BaseName);
        Assert.AreEqual("Time Sheet", form.Title);
        Assert.AreEqual("time sheet", form.Noun);
        Assert.AreEqual("timeSheet", form.Variable);
        Assert.AreEqual("timesheet", form.Stem);
        Assert.AreEqual("timeSheetStartDate", form.FieldId(table.Columns[1]), "the id of a field, on every web stack");
        Assert.AreEqual("TimeSheetService", AngularNames.ServiceClass("E_TimeSheet"));
        Assert.AreEqual("timesheet.service", AngularNames.ServiceFile("E_TimeSheet"));
        Assert.AreEqual("timeSheetService", AngularNames.ServiceField("E_TimeSheet"));
        Assert.AreEqual("timeSheetApi", ReactNames.ApiVar("E_TimeSheet"));
        Assert.AreEqual("timesheetApi", ReactNames.ApiFile("E_TimeSheet"));
        Assert.AreEqual("setCustomers", ReactNames.SetList("customers"));
    }

    [TestMethod]
    public void Every_stack_gives_a_column_the_same_kind_of_editor()
    {
        var table = Sample.Table("Job",
        [
            Sample.Column("JobId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Text("Title", 2),
            Sample.Column("RegionId", SqlDbType.Int, ordinal: 3),
            Sample.Column("Status", SqlDbType.NVarChar, characters: 10, choices: ["Open", "Closed"], ordinal: 4),
            Sample.Column("IsOpen", SqlDbType.Bit, ordinal: 5),
            Sample.Column("Due", SqlDbType.DateTime, ordinal: 6),
            Sample.Column("Budget", SqlDbType.Decimal, precision: 18, scale: 2, currency: true, ordinal: 7),
            Sample.Column("MonthNumber", SqlDbType.Int, numericKind: NumericKind.Month, ordinal: 8),
            Sample.Column("Notes", SqlDbType.NVarChar, characters: 4000, ordinal: 9),
            Sample.Column("CreateDate", SqlDbType.DateTime, createDateColumn: true, ordinal: 10)
        ],
        [Sample.ForeignKey("RegionId", "Region", "RegionId", "Name")]);
        var form = FormModel.For(table, Project());
        FieldKind Kind(string name) => form.KindOf(table.Columns.First(c => c.Name == name));
        Assert.AreEqual(FieldKind.Text, Kind("Title"));
        Assert.AreEqual(FieldKind.Lookup, Kind("RegionId"));
        Assert.AreEqual(FieldKind.Choices, Kind("Status"));
        Assert.AreEqual(FieldKind.Boolean, Kind("IsOpen"));
        Assert.AreEqual(FieldKind.Date, Kind("Due"));
        Assert.AreEqual(FieldKind.Money, Kind("Budget"));
        Assert.AreEqual(FieldKind.Number, Kind("MonthNumber"));
        Assert.AreEqual(FieldKind.Multiline, Kind("Notes"));
        Assert.AreEqual(FieldKind.SystemStamp, Kind("CreateDate"));

        var country = Sample.Table("Country", [Sample.Column("Alpha3Code", SqlDbType.Char, primaryKey: true, characters: 3, ordinal: 1), Text("Name", 2)]);
        Assert.AreEqual(FieldKind.TypedKey, FormModel.For(country, Project()).KindOf(country.Columns[0]));
    }

    [TestMethod]
    public void A_view_model_holds_each_column_one_way_and_a_grid_shows_it_one_way()
    {
        var table = Sample.Table("Job",
        [
            Sample.Column("JobId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Text("Title", 2),
            Sample.Column("RegionId", SqlDbType.Int, ordinal: 3),
            Sample.Column("IsOpen", SqlDbType.Bit, ordinal: 4),
            Sample.Column("Due", SqlDbType.DateTime, ordinal: 5),
            Sample.Column("Budget", SqlDbType.Decimal, precision: 18, scale: 2, currency: true, ordinal: 6),
            Sample.Column("Weight", SqlDbType.Decimal, precision: 9, scale: 3, ordinal: 7),
            Sample.Column("MonthNumber", SqlDbType.Int, numericKind: NumericKind.Month, ordinal: 8),
            Sample.Column("Ref", SqlDbType.UniqueIdentifier, ordinal: 9)
        ],
        [Sample.ForeignKey("RegionId", "Region", "RegionId", "Name")]);
        var form = FormModel.For(table, Project());
        ColumnModel Col(string name) => table.Columns.First(c => c.Name == name);

        Assert.AreEqual(ValueKind.Text, form.ValueOf(Col("Title")));
        Assert.AreEqual(ValueKind.Lookup, form.ValueOf(Col("RegionId")));
        Assert.AreEqual(ValueKind.Boolean, form.ValueOf(Col("IsOpen")));
        Assert.AreEqual(ValueKind.Date, form.ValueOf(Col("Due")));
        Assert.AreEqual(ValueKind.Money, form.ValueOf(Col("Budget")));
        Assert.AreEqual(ValueKind.Decimal, form.ValueOf(Col("Weight")));
        Assert.AreEqual(ValueKind.WholeNumber, form.ValueOf(Col("MonthNumber")));
        Assert.AreEqual(ValueKind.Guid, form.ValueOf(Col("Ref")));

        Assert.AreEqual(CellKind.Lookup, form.CellOf(Col("RegionId")));
        Assert.AreEqual(CellKind.Boolean, form.CellOf(Col("IsOpen")));
        Assert.AreEqual(CellKind.Date, form.CellOf(Col("Due")));
        Assert.AreEqual(CellKind.Money, form.CellOf(Col("Budget")));
        Assert.AreEqual(CellKind.Decimal, form.CellOf(Col("Weight")));
        Assert.AreEqual(CellKind.Plain, form.CellOf(Col("MonthNumber")));
        Assert.AreEqual(CellKind.Plain, form.CellOf(Col("Title")));
        Assert.AreEqual(CellKind.Lookup, GridCells.KindOf(Col("Title"), lookedUp: true), "a child grid names a foreign key the page does not edit");
    }

    [TestMethod]
    public async Task The_winui3_list_shows_a_decimal_that_is_not_money_with_two_decimals_like_the_web()
    {
        var table = Sample.Table("Job",
        [
            Sample.Column("JobId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Text("Title", 2),
            Sample.Column("Weight", SqlDbType.Decimal, precision: 9, scale: 3, ordinal: 3),
            Sample.Column("Spare", SqlDbType.Decimal, precision: 9, scale: 3, nullable: true, ordinal: 4)
        ]);
        string list = await Rendered("WinUI3_MasterScreen_v1.tt", table, Project());
        StringAssert.Contains(list, "e.Weight.ToString(\"N2\"),");
        StringAssert.Contains(list, "e.Spare?.ToString(\"N2\") ?? \"\",");
    }

    [TestMethod]
    public void A_number_box_carries_the_same_limits_on_every_web_stack()
    {
        var month = NumberBox.For("Job", Sample.Column("MonthNumber", SqlDbType.Int, numericKind: NumericKind.Month), Project());
        Assert.IsNull(month.Step);
        Assert.AreEqual("1", month.Min);
        Assert.AreEqual("12", month.Max);
        Assert.IsNotNull(month.Characters, "a month is a short box");
        StringAssert.Contains(month.Html, " min=\"1\" max=\"12\" style=\"width: ");
        StringAssert.Contains(month.Jsx, " min=\"1\" max=\"12\" style={{ width: '");

        var rate = NumberBox.For("Job", Sample.Column("Rate", SqlDbType.Decimal, precision: 9, scale: 3), Project());
        Assert.AreEqual("0.001", rate.Step);
        Assert.AreEqual("-999999.999", rate.Min);
        Assert.AreEqual("999999.999", rate.Max);

        var money = MoneyBox.For("Job", Sample.Column("Budget", SqlDbType.Decimal, precision: 18, scale: 2, currency: true), Project());
        Assert.AreEqual(2, money.Digits);
        Assert.AreEqual("USD", money.Currency);
        Assert.IsNull(money.Min);
    }

    [TestMethod]
    public void A_new_row_starts_with_a_value_for_every_required_field_the_same_on_both_typescript_stacks()
    {
        var table = Account();
        var form = FormModel.For(table, Project());
        CollectionAssert.AreEqual(new[]
        {
            "nickname: ''", "name: ''", "passwordHash: ''", "regionId: 0", "styleId: 0", "balance: 0", "createDate: new Date().toISOString().substring(0, 10)"
        }, form.NewRowTypeScript.ToArray());

        var country = Sample.Table("Country", [Sample.Column("Alpha3Code", SqlDbType.Char, primaryKey: true, characters: 3, ordinal: 1), Text("Name", 2)]);
        Assert.AreEqual("alpha3Code: ''", FormModel.For(country, Project()).NewRowTypeScript[0], "a typed key starts empty");
    }

    [TestMethod]
    public void The_search_boxes_are_one_per_column_or_one_for_the_search_protocol()
    {
        var table = Account();
        var plain = FormModel.For(table, Project());
        CollectionAssert.AreEqual(table.SearchableColumns.Select(c => c.Name).ToArray(), plain.WebSearchFields.Select(f => f.Name).ToArray());
        var box = FormModel.For(table, Project(("ListProtocol", "Search"))).WebSearchFields.Single();
        Assert.AreEqual("search", box.ParamName);
        Assert.IsNull(box.Column);
    }

    [TestMethod]
    public void A_table_has_a_find_by_name_route_only_with_a_required_Name_a_non_text_key_and_no_active_flag()
    {
        TableModel Make(string key, bool nameNullable, bool active)
        {
            var columns = new List<ColumnModel>
            {
                key == "text" ? Sample.Column("Code", SqlDbType.Char, primaryKey: true, characters: 3, ordinal: 1) : Sample.Column("Id", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
                Text("Name", 2, nullable: nameNullable)
            };
            if (active) columns.Add(Sample.Column("IsActive", SqlDbType.Bit, ordinal: 3));
            return Sample.Table("Thing", columns);
        }
        Assert.IsTrue(Make("int", false, false).CanFindByName);
        Assert.IsFalse(Make("text", false, false).CanFindByName, "GET route/{id} and GET route/{name} would be one route");
        Assert.IsFalse(Make("int", true, false).CanFindByName);
        Assert.IsFalse(Make("int", false, true).CanFindByName, "a name / active table has its own API");
    }

    [TestMethod]
    public void The_child_grids_carry_the_json_names_and_the_lookup_route_the_web_clients_read()
    {
        var items = Sample.ForeignKey("ItemId", "Item", "ItemId", "ItemNumber");
        var table = Sample.Table("Account", [Sample.Column("AccountId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Text("Name", 2)],
            childForeignKeys: [ChildLink("Transfer", "FromAccountId", items), ChildLink("Transfer", "ToAccountId", items)]);
        var grid = ChildGrid.For(table, Project())[0];
        Assert.AreEqual("transferFromAccount", grid.Variable);
        Assert.AreEqual("transfers", grid.ApiRoute);
        Assert.AreEqual("transfer", grid.PageRoute);
        Assert.AreEqual("transferId", grid.KeyJson);
        Assert.AreEqual("fromAccountId", grid.ForeignKeyJson);
        var item = grid.Lookups.Single();
        Assert.AreEqual("itemId", item.ColumnJson);
        Assert.AreEqual("items/lookup", item.WebRoute);
        Assert.AreEqual("id", item.WebKey);
        Assert.AreEqual("name", item.WebDisplay);
        Assert.IsNotNull(grid.EditLookupOf(grid.Link.ReferencingTableColumns.First(c => c.Name == "ItemId")));
        Assert.IsNull(grid.EditLookupOf(grid.Link.ReferencingTableColumns.First(c => c.Name == "Memo")));
    }

    [TestMethod]
    public async Task React_Angular_and_Blazor_fill_a_drop_down_from_the_parents_lookup_list()
    {
        var table = Account();
        var project = Project(("HiddenParents", "Style"), ("AngularVersion", "22"));

        string react = await Rendered("TSX_Page_v1.tt", table, project);
        StringAssert.Contains(react, "regionApi.getLookup().then(setRegions)");
        StringAssert.Contains(react, "useState<Lookup[]>([])");
        StringAssert.Contains(react, "accountApi.getLookup().then(setAccountsLookup)", "a drop-down over the table itself (a manager) is its own list, not the page's rows");
        StringAssert.Contains(react, "<option key={p.id} value={p.id}>{p.name}</option>");

        StringAssert.Contains(await Rendered("TS_Component_v1.tt", table, project), "getLookup().subscribe((data) => { this.regions = data; })");

        string page = await Rendered("BLZ_Page_v1.tt", table, project);
        StringAssert.Contains(page, "private List<RegionLookup> lookupRegion = [];");
        StringAssert.Contains(page, "await lookupRegionClient.GetLookupAsync();");
        StringAssert.Contains(page, "<option value=\"@parent.Id\">@parent.Name</option>");

        var region = Sample.Table("Region", [Sample.Column("RegionId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Text("Name", 2)]);
        string client = await Rendered("BLZ_Client_v1.tt", region, project);
        StringAssert.Contains(client, "public record RegionLookup(int Id, string Name, bool? IsActive = null);");
        StringAssert.Contains(client, "public async Task<List<RegionLookup>> GetLookupAsync()");
        StringAssert.Contains(await Rendered("TSX_Api_v1.tt", region, project), "getLookup: () => request<{ id: number; name: string; isActive?: boolean | null }[]>(`${apiUrl}/lookup`),");
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

        // the id of each input is accountNickname on every web stack (FormModel.FieldId); a WinUI3 field is a two-way binding of the view model
        const string webId = "id=\"account([A-Z][A-Za-z0-9]+)\"";
        CollectionAssert.AreEqual(expected, Fields(await Rendered("TSX_Page_v1.tt", table, project), webId), "React");
        CollectionAssert.AreEqual(expected, Fields(await Rendered("TS_Component_v1.tt", table, project), webId), "Angular");
        CollectionAssert.AreEqual(expected, Fields(await Rendered("BLZ_Page_v1.tt", table, project), webId), "Blazor");
        CollectionAssert.AreEqual(expected, Fields(await Rendered("WinUI3_DetailScreen_v1.tt", table, project), @"ViewModel\.(\w+), Mode=TwoWay"), "WinUI3");
    }
}
