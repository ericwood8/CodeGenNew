using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.SchemaIntrospection;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests;

/// <summary> The pieces of generated front-end code that several stacks write from one place in Core: the desktop form's load, save and XAML, the list page's boxes and cells, the child grids, the .NET API client
/// and model, the junction sides and the screen menu. The templates of every stack that uses one are compared with their earlier output elsewhere; these pin each piece on its own. </summary>
[TestClass]
public class SharedFrontEndCodeTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Shop")).Append(new("DatabaseProvider", "Sqlite")).Append(new("EnumTables", "none")));

    private static async Task<T> WithTablesAsync<T>(Func<Func<string, Task<TableModel>>, Task<T>> body)
    {
        string path = Path.Combine(Path.GetTempPath(), "codegen_shared_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString))
            {
                connection.Open();
                foreach (string statement in new[]
                {
                    "CREATE TABLE Region (RegionId INTEGER PRIMARY KEY, Name VARCHAR(40) NOT NULL)",
                    "CREATE TABLE Customer (CustomerId INTEGER PRIMARY KEY, AccountNumber VARCHAR(20) NOT NULL, Email VARCHAR(80) NULL, RegionId INTEGER NOT NULL REFERENCES Region (RegionId), CreditLimit DECIMAL(10,2) NULL, IsTaxable BIT NOT NULL DEFAULT 0, AddedOn DATETIME NOT NULL)",
                    "CREATE TABLE Person (PersonId INTEGER PRIMARY KEY, Name VARCHAR(40) NOT NULL)",
                    "CREATE TABLE Tag (TagId INTEGER PRIMARY KEY, Name VARCHAR(40) NOT NULL)",
                    "CREATE TABLE PersonTag (PersonId INTEGER NOT NULL REFERENCES Person (PersonId), TagId INTEGER NOT NULL REFERENCES Tag (TagId), PRIMARY KEY (PersonId, TagId))"
                })
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = statement;
                    command.ExecuteNonQuery();
                }
            }
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = path };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            return await body(name => provider.BuildTableModelAsync("main", name, includeReferencedDisplayColumns: true));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task A_junction_table_says_its_anchor_and_target_sides_once()
    {
        await WithTablesAsync(async table =>
        {
            var junction = (await table("PersonTag")).Junction!;
            Assert.AreEqual("Person", junction.AnchorRole);
            Assert.AreEqual("Tag", junction.TargetRole);
            Assert.AreEqual("PersonId", junction.AnchorColumn.Name);
            Assert.AreEqual("int", junction.AnchorCs);
            Assert.AreEqual("int", junction.TargetCs);
            Assert.AreEqual("number", junction.TargetTs);
            CollectionAssert.AreEqual(new[] { "Name" }, junction.DisplayColumns.ToArray());
            Assert.IsNull((await table("Person")).Junction);
            return 0;
        });
    }

    [TestMethod]
    public async Task The_form_code_loads_checks_and_copies_each_field_the_same_way_for_every_desktop_dialog()
    {
        await WithTablesAsync(async table =>
        {
            var customer = await table("Customer");
            var project = Project(("NotFutureDateColumns", "AddedOn"));
            var form = FormModel.For(customer, project);
            ColumnModel Column(string name) => customer.Columns.First(c => c.Name == name);

            Assert.AreEqual("string?", DesktopFormCode.FieldType(form, Column("AccountNumber")));
            Assert.AreEqual("double", DesktopFormCode.FieldType(form, Column("CreditLimit")));
            Assert.AreEqual("bool?", DesktopFormCode.FieldType(form, Column("IsTaxable")));
            Assert.AreEqual("DateTimeOffset?", DesktopFormCode.FieldType(form, Column("AddedOn")));
            Assert.AreEqual("int?", DesktopFormCode.FieldType(form, Column("RegionId")));

            Assert.AreEqual("            AccountNumber = editing.AccountNumber.ToString();\n", DesktopFormCode.LoadLines(form, Column("AccountNumber")));
            Assert.AreEqual("", DesktopFormCode.LoadLines(form, Column("RegionId")), "a foreign key is set after its drop-down is filled");
            StringAssert.Contains(DesktopFormCode.LoadLines(form, Column("AddedOn")), "_addedOnTime = editing.AddedOn.TimeOfDay;");

            string required = DesktopFormCode.SaveLines(form, project, Column("AccountNumber"));
            StringAssert.Contains(required, "if (string.IsNullOrWhiteSpace(AccountNumber)) { ErrorMessage = \"Account Number is required.\"; return false; }");
            StringAssert.Contains(required, "        entity.AccountNumber = AccountNumber;\n");
            StringAssert.Contains(DesktopFormCode.SaveLines(form, project, Column("RegionId")), "entity.RegionId = RegionId.Value;");
            string money = DesktopFormCode.SaveLines(form, project, Column("CreditLimit"));
            StringAssert.Contains(money, "            entity.CreditLimit = null;\n");
            StringAssert.Contains(money, "must be between");
            string date = DesktopFormCode.SaveLines(form, project, Column("AddedOn"));
            StringAssert.Contains(date, "must not be later than today.");
            StringAssert.Contains(date, "else { entity.AddedOn = AddedOn.Value.Date + _addedOnTime; }");
            Assert.IsNotNull(DesktopFormCode.ShapeCheck(project, customer, Column("Email"), "Email", "Email", optional: true));
            Assert.IsNull(DesktopFormCode.ShapeCheck(project, customer, Column("AccountNumber"), "AccountNumber", "Account Number", optional: false));
            return 0;
        });
    }

    [TestMethod]
    public async Task A_guarded_column_gets_its_allowed_flag_and_the_load_that_reads_the_guards()
    {
        await WithTablesAsync(async table =>
        {
            var customer = await table("Customer");
            var form = FormModel.For(customer, Project(("ApiAuth", "true"), ("GuardedColumns", "Customer.CreditLimit; by Employee.IsManager")));
            string properties = DesktopFormCode.GuardProperties(form);
            StringAssert.Contains(properties, "private bool _creditLimitAllowed;");
            StringAssert.Contains(properties, "public Visibility CreditLimitNoteVisibility => CreditLimitAllowed ? Visibility.Collapsed : Visibility.Visible;");
            string load = DesktopFormCode.GuardLoadLines(form, "Customer");
            StringAssert.Contains(load, "var guards = await _api.Customer.GetGuardsAsync();");
            StringAssert.Contains(load, "CreditLimitAllowed = guards.GetValueOrDefault(\"creditLimit\");");
            string xaml = DesktopFormXaml.Field(form, Project(("ApiAuth", "true"), ("GuardedColumns", "Customer.CreditLimit; by Employee.IsManager")), customer, customer.Columns.First(c => c.Name == "CreditLimit"), textKey: false, withGuards: true);
            StringAssert.Contains(xaml, "<ContentControl HorizontalContentAlignment=\"Stretch\" IsEnabled=\"{x:Bind ViewModel.CreditLimitAllowed, Mode=OneWay}\">");

            var plain = FormModel.For(customer, Project());
            Assert.AreEqual("", DesktopFormCode.GuardProperties(plain));
            Assert.AreEqual("", DesktopFormCode.GuardLoadLines(plain, "Customer"));
            return 0;
        });
    }

    [TestMethod]
    public async Task The_form_xaml_binds_each_kind_of_field_to_the_view_model_property_of_its_name()
    {
        await WithTablesAsync(async table =>
        {
            var customer = await table("Customer");
            var project = Project();
            var form = FormModel.For(customer, project);
            string Xaml(string name) => DesktopFormXaml.Field(form, project, customer, customer.Columns.First(c => c.Name == name), textKey: false);

            StringAssert.Contains(Xaml("AccountNumber"), "<TextBox Header=\"Account Number\"");
            StringAssert.Contains(Xaml("AccountNumber"), "Text=\"{x:Bind ViewModel.AccountNumber, Mode=TwoWay}\" MaxLength=\"20\"");
            StringAssert.Contains(Xaml("IsTaxable"), "<CheckBox Content=\"Is Taxable?\" IsChecked=\"{x:Bind ViewModel.IsTaxable, Mode=TwoWay}\" />");
            StringAssert.Contains(Xaml("AddedOn"), "<CalendarDatePicker Header=\"Added On\"");
            StringAssert.Contains(Xaml("RegionId"), "ItemsSource=\"{x:Bind ViewModel.RegionOptions}\"");
            Assert.IsTrue(Xaml("AccountNumber").StartsWith("            <TextBox"));
            return 0;
        });
    }

    [TestMethod]
    public async Task The_list_page_boxes_and_cells_come_from_the_form()
    {
        await WithTablesAsync(async table =>
        {
            var customer = await table("Customer");
            var project = Project();
            var form = FormModel.For(customer, project);
            var boxes = DesktopGridCode.SearchBoxes(form);
            Assert.IsTrue(boxes.Any(b => b is { Field: "accountNumberFilter", Prop: "AccountNumberFilter", ParamName: "accountNumber" }));

            ColumnModel Column(string name) => customer.Columns.First(c => c.Name == name);
            Assert.AreEqual("                e.AccountNumber,\n", DesktopGridCode.CellLines(form, project, Column("AccountNumber")));
            Assert.AreEqual("                e.IsTaxable ? \"Yes\" : \"No\",\n", DesktopGridCode.CellLines(form, project, Column("IsTaxable")));
            Assert.AreEqual("                e.AddedOn.ToString(\"MM/dd/yyyy\"),\n", DesktopGridCode.CellLines(form, project, Column("AddedOn")));
            StringAssert.Contains(DesktopGridCode.CellLines(form, project, Column("RegionId")), "regionNames.TryGetValue(e.RegionId, out var regionIdName)");
            Assert.AreEqual(1, form.ParentsOf(form.GridColumns).Count);
            return 0;
        });
    }

    [TestMethod]
    public async Task A_master_detail_dialog_describes_each_child_grid_once()
    {
        await WithTablesAsync(async table =>
        {
            var region = await table("Region");
            var child = DesktopChild.For(region, Project(), needsOwnKey: true).Single();
            Assert.AreEqual("Customer", child.Table);
            Assert.AreEqual("Customer", child.Type);
            Assert.AreEqual("CustomerId", child.KeyProp);
            Assert.AreEqual("customer", child.Var);
            Assert.AreEqual("RegionId", child.FkColumn);
            Assert.AreEqual("RegionId", child.ParentKeyColumn);
            Assert.AreEqual("int", child.ParentKeyCs);
            CollectionAssert.Contains(child.HideColumns.ToList(), "CustomerId");
            Assert.AreEqual(0, DesktopChild.For(await table("Tag"), Project(), needsOwnKey: false).Count(c => c.Table != "PersonTag"));
            return 0;
        });
    }

    [TestMethod]
    public async Task The_dotnet_client_and_model_are_the_same_text_for_blazor_and_uno_but_for_the_namespaces()
    {
        await WithTablesAsync(async table =>
        {
            var customer = await table("Customer");
            var project = Project();
            string blazor = ApiClientCode.Client(customer, project, "Shop.Blazor.Models", "Shop.Blazor.Services");
            string uno = ApiClientCode.Client(customer, project, "Shop.App.Models", "Shop.App.Services");
            Assert.AreEqual(blazor.Replace("Shop.Blazor", "Shop.App"), uno);
            StringAssert.Contains(uno, "public class CustomerClient(HttpClient http)");
            StringAssert.Contains(uno, "public record CustomerLookup(int Id, string Name, bool? IsActive = null);");
            Assert.IsFalse(uno.Contains("GetGuardsAsync"));
            StringAssert.Contains(ApiClientCode.Client(customer, Project(("ApiAuth", "true"), ("GuardedColumns", "Customer.CreditLimit; by Employee.IsManager")), "M", "S"), "GetGuardsAsync");

            string model = ApiClientCode.Model(customer, project, "Shop.App.Models", tinyIntAsShort: false);
            StringAssert.Contains(model, "namespace Shop.App.Models;");
            StringAssert.Contains(model, "public string AccountNumber { get; set; } = \"\";");
            StringAssert.Contains(model, "public decimal? CreditLimit { get; set; }");
            return 0;
        });
    }

    [TestMethod]
    public async Task The_screen_menu_lists_the_dashboard_the_screens_and_the_odata_pages_with_their_roles()
    {
        await WithTablesAsync(async table =>
        {
            var customer = await table("Customer");
            var region = await table("Region");
            var plain = ScreenMenu.For(Project(("Dashboard", "false")), [region, customer], []);
            CollectionAssert.AreEqual(new[] { "Region", "Customer" }, plain.Select(e => e.Label).ToArray());
            Assert.IsTrue(plain.All(e => e.Kind == ScreenMenuKind.Screen && e.Roles.Count == 0));

            var project = Project(("Dashboard", "true"), ("Auth", "true"), ("ApiAuth", "true"), ("TableReadRoles", "Customer:Administrator"));
            var full = ScreenMenu.For(project, [region, customer], [customer]);
            CollectionAssert.AreEqual(new[] { "Dashboard", "Region", "Customer", "Customer (OData)" }, full.Select(e => e.Label).ToArray());
            CollectionAssert.AreEqual(new[] { ScreenMenuKind.Dashboard, ScreenMenuKind.Screen, ScreenMenuKind.Screen, ScreenMenuKind.OData }, full.Select(e => e.Kind).ToArray());
            CollectionAssert.AreEqual(new[] { "Administrator" }, full[2].Roles.ToArray());
            Assert.AreEqual(0, full[1].Roles.Count);

            var replaced = ScreenMenu.For(Project(("Dashboard", "false"), ("OData", "true"), ("ODataScreens", "Replace")), [customer], [customer]);
            Assert.AreEqual(1, replaced.Count, "Replace: the OData page takes the REST page's place and has no entry of its own");
            return 0;
        });
    }
}
