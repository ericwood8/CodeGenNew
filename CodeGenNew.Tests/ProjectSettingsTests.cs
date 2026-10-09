using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

[TestClass]
public class ProjectSettingsTests
{
    [TestMethod]
    public void Every_setting_is_on_exactly_one_tab_of_the_settings_screen()
    {
        var listed = ProjectSettingGroups.All.SelectMany(g => g.Keys).ToList();
        CollectionAssert.AreEquivalent(ProjectSettings.Keys.Where(k => k != "ProjectName").ToList(), listed);
        Assert.AreEqual(listed.Count, listed.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.IsTrue(ProjectSettingGroups.All.All(g => g.Keys.Length <= ProjectSettingGroups.MaxKeysPerTab && g.Description.StartsWith("This tab has ")));
        Assert.AreEqual("Dashboard", ProjectSettingGroups.TabOf("Dashboard"));
        Assert.AreEqual(ProjectSettingGroups.General, ProjectSettingGroups.TabOf("NotAKnownKey"));
    }

    [TestMethod]
    public void Captions_put_spaces_between_words_and_every_true_false_flag_is_a_check_box()
    {
        Assert.AreEqual("Dashboard Strip", ProjectSettingsHints.Caption("DashboardStrip"));
        Assert.AreEqual("Api Docs", ProjectSettingsHints.Caption("ApiDocs"));
        foreach (string key in new[] { "ApiDocs", "ApiHttp", "ApiFakers", "ProjectDocs", "ApiValidation", "Dashboard", "DashboardStrip" })
        {
            Assert.Contains(key, ProjectSettingsHints.BooleanKeys, key);
            Assert.IsFalse(ProjectSettingsHints.All[key].StartsWith("true", StringComparison.OrdinalIgnoreCase), key + " hint should explain, not give the value");
        }
        Assert.IsTrue(ProjectSettingsHints.BooleanKeys.All(k => ProjectSettings.Keys.Contains(k)));
    }

    [TestMethod]
    public void Settings_with_a_fixed_list_of_values_offer_the_list_and_the_stacks_match_the_plan()
    {
        CollectionAssert.AreEqual(CodeGenNew.Generation.ProjectPlan.KnownStacks.ToList(), ProjectSettingChoices.Stacks.ToList());
        foreach (var (key, (kind, choices)) in ProjectSettingChoices.All)
        {
            Assert.IsTrue(ProjectSettings.Keys.Contains(key), key);
            Assert.DoesNotContain(key, ProjectSettingsHints.BooleanKeys, key);
            if (kind is SettingKind.Radio)
                Assert.IsLessThanOrEqualTo(3, choices.Length, key + " has more than three choices: use a drop-down");
            if (kind is SettingKind.Radio or SettingKind.Dropdown)
                Assert.AreEqual("", choices[0].Value, key + " starts with the not-set choice");
        }
        Assert.IsTrue(ProjectSettingChoices.All["DatabaseProvider"].Choices.Skip(1).All(c => ProjectSettings.FromValues([new("DatabaseProvider", c.Value)]).DatabaseDialect.ToString().Equals(c.Value, StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Whole_number_settings_have_a_range_that_holds_their_defaults()
    {
        foreach (var (key, (min, max)) in ProjectSettingChoices.Numbers)
        {
            Assert.IsTrue(ProjectSettings.Keys.Contains(key), key);
            Assert.IsFalse(ProjectSettingChoices.All.ContainsKey(key) || ProjectSettingsHints.BooleanKeys.Contains(key), key);
            Assert.IsTrue(min >= 1 && min < max, key);
        }
        var none = ProjectSettings.FromValues([]);
        foreach (int value in new[] { none.MinYear, none.MaxYear, none.EnumMaxRows, none.ApiPort, none.RustPort, none.DevPort("React"), none.DevPort("Angular") })
            Assert.IsTrue(value >= 1 && value <= 65535 || value <= 9999, value.ToString());
        Assert.IsTrue(none.MinYear >= ProjectSettingChoices.Numbers["MinYear"].Min && none.MaxYear <= ProjectSettingChoices.Numbers["MaxYear"].Max);
        Assert.IsLessThanOrEqualTo(ProjectSettingChoices.Numbers["EnumMaxRows"].Max, none.EnumMaxRows);
    }

    [TestMethod]
    public void No_project_gives_null_everywhere_so_a_template_keeps_its_own_values()
    {
        var none = ProjectSettings.None;

        Assert.IsNull(none.ViewNamespace);
        Assert.IsNull(none.ContextName);
        Assert.IsNull(none.NoLookup("Status", default));
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
    public void Without_a_project_the_enum_questions_have_no_answer_so_the_template_keeps_its_own_lists()
    {
        var lookup = new LookupShape(true, 3);

        Assert.IsNull(ProjectSettings.None.NoApi("Status", lookup));
        Assert.IsNull(ProjectSettings.None.NoLookup("Status", lookup));
        Assert.IsNull(ProjectSettings.None.IsEnumTable("Status", lookup));
    }

    [TestMethod]
    public void With_a_project_a_small_lookup_shaped_table_is_an_enum_and_a_large_or_odd_one_is_not()
    {
        var project = ProjectSettings.Parse("ProjectName=Acme\nEnumMaxRows=10");

        Assert.IsTrue(project.NoApi("Status", new LookupShape(true, 10)));
        Assert.IsTrue(project.NoRepository("Status", new LookupShape(true, 4)));
        Assert.IsFalse(project.NoApi("Status", new LookupShape(true, 11)), "more rows than EnumMaxRows");
        Assert.IsFalse(project.NoApi("Customer", new LookupShape(false, 3)), "not lookup-shaped");
        Assert.IsFalse(project.NoLookup("Customer", default), "an unknown shape is never an enum");
    }

    [TestMethod]
    public void A_name_ending_in_Type_or_Codes_makes_an_otherwise_ordinary_small_table_an_enum()
    {
        var project = ProjectSettings.Parse("ProjectName=Acme");
        var keyAndText = new LookupShape(false, 16, HasIntKeyAndText: true);

        Assert.IsTrue(project.NoApi("LeaveType", keyAndText), "Name + IsActive and flags would fail the strict test");
        Assert.IsTrue(project.NoApi("MeterTypeCodes", keyAndText));
        Assert.IsTrue(project.NoApi("meterTypeCode", keyAndText), "case-insensitive");
        Assert.IsFalse(project.NoApi("Department", keyAndText), "the name does not say lookup");
        Assert.IsFalse(project.NoApi("Code", keyAndText), "a suffix alone is not a name");
        Assert.IsFalse(project.NoApi("LeaveType", new LookupShape(false, 16)), "needs an integer key and a text column");
        Assert.IsFalse(project.NoApi("LeaveType", new LookupShape(false, 900, true)), "too many rows");
        Assert.IsTrue(ProjectSettings.Parse("ProjectName=Acme\nEnumNameSuffixes=Ref").NoApi("CountryRef", keyAndText), "the suffix list is configurable");
    }

    [TestMethod]
    public void EnumTables_replaces_the_derived_answer_and_a_specific_list_wins_for_its_own_question()
    {
        var project = ProjectSettings.Parse("ProjectName=Acme\nEnumTables=Status,Region\nNoLookupParents=Customer");
        var bigNonLookup = new LookupShape(false, 5000);

        Assert.IsTrue(project.NoApi("Region", bigNonLookup), "listed in EnumTables");
        Assert.IsFalse(project.NoApi("Colour", new LookupShape(true, 3)), "EnumTables is exact: the shape no longer decides");
        Assert.IsTrue(project.NoLookup("Customer", bigNonLookup), "NoLookupParents wins for this question");
        Assert.IsFalse(project.NoLookup("Status", new LookupShape(true, 3)), "...and then it is the whole answer");
        Assert.IsTrue(project.NoNavigation("Status", bigNonLookup), "other questions still follow EnumTables");
    }

    [TestMethod]
    public void The_lookup_shape_test_wants_a_single_int_key_no_foreign_keys_and_a_text_column()
    {
        ColumnModel Key() => Sample.Column("StatusId", System.Data.SqlDbType.Int, primaryKey: true, identity: true);
        ColumnModel Text() => Sample.Column("Name", System.Data.SqlDbType.NVarChar, characters: 50);

        Assert.IsTrue(LookupShape.Looks([Key(), Text()], foreignKeyCount: 0));
        Assert.IsFalse(LookupShape.Looks([Key(), Text()], foreignKeyCount: 1), "a foreign key makes it an ordinary table");
        Assert.IsFalse(LookupShape.Looks([Key(), Sample.Column("Code", System.Data.SqlDbType.Int)], 0), "no text column");
        Assert.IsFalse(LookupShape.Looks([Text()], 0), "no primary key");
        Assert.IsFalse(LookupShape.Looks(
            [Key(), Text(), Sample.Column("IsActive", System.Data.SqlDbType.Bit)], 0), "Name + IsActive is a user-managed list, not a fixed one");
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
        var result = await Repo.Cache.RunAsync(Repo.Template(template), model, project);

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        Expect.Contains(result.GeneratedText!, expectedNamespace);
        Expect.Contains(result.GeneratedText!, expectedContext);
        Expect.DoesNotContain(result.GeneratedText!, "MyAppContext");
    }

    [TestMethod]
    public async Task With_no_project_a_template_generates_with_its_own_built_in_values()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("CS_Repo_v1.tt"), Sample.Holiday());

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        Expect.Contains(result.GeneratedText!, "MyAppContext");
    }

    [TestMethod]
    public async Task A_child_grid_row_is_clickable_and_opens_the_childs_own_dialog()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("WinUI3_DetailMasterScreen_v1.tt"), Sample.OrderWithLines());

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

        var list = await Repo.Cache.RunAsync(Repo.Template("WinUI3_MasterScreen_v1.tt"), Sample.OrderWithLines(), project);
        var master = await Repo.Cache.RunAsync(Repo.Template("WinUI3_DetailMasterScreen_v1.tt"), Sample.OrderWithLines(), project);

        Expect.Contains(list.GeneratedText!, "new OrderDetailMasterDialog(_context)");
        Expect.Contains(master.GeneratedText!, "new OrderLineDetailMasterDialog(_context, entity)");
    }

    [TestMethod]
    public async Task Without_DetailMasterTables_a_table_with_child_tables_gets_a_list_page_that_opens_the_dialog_the_plan_writes()
    {
        var project = ProjectSettings.Parse("ProjectName=Acme");
        var order = Sample.OrderWithLines();

        var list = await Repo.Cache.RunAsync(Repo.Template("WinUI3_MasterScreen_v1.tt"), order, project);
        var master = await Repo.Cache.RunAsync(Repo.Template("WinUI3_DetailMasterScreen_v1.tt"), order, project);

        Assert.IsTrue(DatabaseModel.IsDetailMaster(order, project));
        Expect.Contains(list.GeneratedText!, "new OrderDetailMasterDialog(_context)");
        Expect.Contains(master.GeneratedText!, "new OrderLineDetailDialog(_context, entity)");
    }

    [TestMethod]
    public void A_table_known_by_name_follows_the_same_rule_as_one_known_by_model()
    {
        var unlisted = ProjectSettings.Parse("ProjectName=Acme");
        var listed = ProjectSettings.Parse("ProjectName=Acme\nDetailMasterTables=Order");

        Assert.IsTrue(DatabaseModel.IsDetailMaster("OrderLine", true, unlisted));
        Assert.IsFalse(DatabaseModel.IsDetailMaster("OrderLine", false, unlisted));
        Assert.IsTrue(DatabaseModel.IsDetailMaster("order", false, listed));
        Assert.IsFalse(DatabaseModel.IsDetailMaster("OrderLine", true, listed));
    }

    [TestMethod]
    [DataRow("WinUI3_MasterScreen_v1.tt")]
    [DataRow("WinUI3_DetailMasterScreen_v1.tt")]
    public async Task The_list_and_detail_master_templates_also_write_the_shared_PaginationBar(string template)
    {
        var project = ProjectSettings.Parse("ProjectName=Acme");

        var result = await Repo.Cache.RunAsync(Repo.Template(template), Sample.OrderWithLines(), project);

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        var files = GeneratedFiles.Split(result.GeneratedText!).ToDictionary(f => f.RelativePath.Replace('\\', '/'));
        Expect.Contains(files["Views/PaginationBar.xaml"].Content, "x:Class=\"Acme.App.Views.PaginationBar\"");
        Expect.Contains(files["Views/PaginationBar.xaml.cs"].Content, "namespace Acme.App.Views;");
        Expect.Contains(files["Views/PaginationBar.xaml.cs"].Content, "public sealed partial class PaginationBar : UserControl");
    }

    private static ProjectSettings With(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    [TestMethod]
    public void Hidden_parents_and_model_file_overrides_are_empty_for_a_project_that_lists_none_and_null_without_one()
    {
        Assert.IsNull(ProjectSettings.None.HiddenParents);
        Assert.IsNull(ProjectSettings.None.ModelFileOverrides);

        Assert.HasCount(0, With().HiddenParents!);
        Assert.HasCount(0, With().ModelFileOverrides!);
    }

    [TestMethod]
    public void Model_file_overrides_read_Table_equals_file_pairs_case_insensitively()
    {
        var overrides = With(("ModelFileOverrides", "DepartmentTeam=department, ProjectTask = project,broken,=x,y=")).ModelFileOverrides!;

        Assert.AreEqual("department", overrides["departmentteam"]);
        Assert.AreEqual("project", overrides["ProjectTask"]);
        Assert.HasCount(2, overrides);
        CollectionAssert.AreEqual(new[] { "SY_Display" }, With(("HiddenParents", "SY_Display")).HiddenParents);
    }

    [TestMethod]
    public void The_table_and_foreign_key_overloads_ask_the_same_question_as_the_shape_version()
    {
        var project = With();
        var holiday = Sample.Holiday();

        Assert.AreEqual(project.IsEnumTable(holiday.TableName, holiday.LookupShape), project.IsEnumTable(holiday));
        Assert.IsNull(ProjectSettings.None.IsEnumTable(holiday));
        Assert.AreEqual(project.IsEnumTable(holiday.ForeignKeys[0].ReferencedTable, holiday.ForeignKeys[0].ReferencedLookupShape), project.IsEnumTable(holiday.ForeignKeys[0]));
    }

    [TestMethod]
    public async Task A_project_can_name_the_base_entity_classes()
    {
        var withBase = await Repo.Cache.RunAsync(Repo.Template("CS_Entity_v1.tt"), Sample.Holiday(), With(("BaseEntity", "AuditedEntity")));
        var without = await Repo.Cache.RunAsync(Repo.Template("CS_Entity_v1.tt"), Sample.Holiday(), With());

        Assert.IsTrue(withBase.Success, string.Join(" | ", withBase.Errors));
        Expect.Contains(withBase.GeneratedText!, ": AuditedEntity");
        Expect.Contains(without.GeneratedText!, ": BaseEntity");
    }

    // ------------------------------------------------------------------ numeric input controls

    [TestMethod]
    [DataRow("Year", NumericKind.Year)]
    [DataRow("FiscalYear", NumericKind.Year)]
    [DataRow("Mth", NumericKind.Month)]
    [DataRow("Month_Number", NumericKind.Month)]
    [DataRow("Qtr", NumericKind.Quarter)]
    [DataRow("WeekNo", NumericKind.WeekNumber)]
    [DataRow("DiscountPercent", NumericKind.Percentage)]
    [DataRow("OrderQty", NumericKind.Count)]
    [DataRow("SortOrder", NumericKind.Sequence)]
    [DataRow("BirthYear", NumericKind.None)]   // a narrow list on purpose: a BirthYear limited to the year range would reject real data
    [DataRow("Description", NumericKind.None)]
    [DataRow("Count", NumericKind.Count)]
    public void An_integer_columns_name_says_what_kind_of_number_it_holds(string name, NumericKind expected) =>
        Assert.AreEqual(expected, NumericClassifier.Classify(name));

    [TestMethod]
    public void The_range_of_a_number_box_comes_from_the_kind_the_project_years_and_the_sql_type()
    {
        var project = With(("MinYear", "1990"), ("MaxYear", "2040"));
        NumericRange? Range(string name, System.Data.SqlDbType type, NumericKind kind) =>
            project.RangeFor(Sample.Column(name, type, numericKind: kind));

        Assert.AreEqual(new NumericRange(1990, 2040), Range("Year", System.Data.SqlDbType.Int, NumericKind.Year));
        Assert.AreEqual(new NumericRange(1, 12), Range("Month", System.Data.SqlDbType.Int, NumericKind.Month));
        Assert.AreEqual(new NumericRange(0, 100), Range("Pct", System.Data.SqlDbType.TinyInt, NumericKind.Percentage));
        Assert.AreEqual(new NumericRange(0, int.MaxValue), Range("Qty", System.Data.SqlDbType.Int, NumericKind.Count));
        Assert.AreEqual(new NumericRange(0, 255), Range("Flags", System.Data.SqlDbType.TinyInt, NumericKind.None));
        Assert.AreEqual(new NumericRange(1, 12), ProjectSettings.None.RangeFor(Sample.Column("Month", System.Data.SqlDbType.TinyInt, numericKind: NumericKind.Month)));
        Assert.IsNull(project.RangeFor(Sample.Column("Price", System.Data.SqlDbType.Decimal)), "not a whole number");
    }

    [TestMethod]
    public async Task A_whole_number_column_is_edited_in_a_number_box_limited_to_its_range()
    {
        var table = Sample.Table("Summary", [
            Sample.Column("SummaryId", System.Data.SqlDbType.Int, primaryKey: true, identity: true),
            Sample.Column("Name", System.Data.SqlDbType.NVarChar, characters: 50),
            Sample.Column("Year", System.Data.SqlDbType.Int, numericKind: NumericKind.Year),
            Sample.Column("MonthNumber", System.Data.SqlDbType.Int, nullable: true, numericKind: NumericKind.Month)]);
        var project = With(("MinYear", "1990"), ("MaxYear", "2040"));

        var result = await Repo.Cache.RunAsync(Repo.Template("WinUI3_DetailScreen_v1.tt"), table, project);

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        var files = GeneratedFiles.Split(result.GeneratedText!).ToDictionary(f => Path.GetFileName(f.RelativePath));
        string xaml = files["SummaryDetailDialog.xaml"].Content.Replace("\r\n", "\n");
        string viewModel = files["SummaryDetailViewModel.cs"].Content.Replace("\r\n", "\n");
        Expect.Contains(xaml, "Value=\"{x:Bind ViewModel.Year, Mode=TwoWay}\"");
        Expect.Contains(xaml, "Minimum=\"1990\" Maximum=\"2040\"");
        Expect.Contains(xaml, "Minimum=\"1\" Maximum=\"12\"");
        Expect.Contains(xaml, "Width=\"170\" HorizontalAlignment=\"Left\""); // Month Number: two digits plus the spin and clear buttons
        Expect.Contains(xaml, "Width=\"190\" HorizontalAlignment=\"Left\""); // Year: four digits
        Expect.Contains(viewModel, "private double _year = double.NaN;");
        Expect.Contains(viewModel, "Year = editing.Year;");
        Expect.Contains(viewModel, "MonthNumber = editing.MonthNumber ?? double.NaN;");
        Expect.Contains(viewModel, "ErrorMessage = \"Year is required.\"; return false;");
        Expect.Contains(viewModel, "entity.MonthNumber = null;");
        Expect.Contains(viewModel, "ErrorMessage = \"Year must be between 1990 and 2040.\"; return false; }");
    }

    private static TableModel SummaryTable() => Sample.Table("Summary", [
        Sample.Column("SummaryId", System.Data.SqlDbType.Int, primaryKey: true, identity: true),
        Sample.Column("Name", System.Data.SqlDbType.NVarChar, characters: 50),
        Sample.Column("Year", System.Data.SqlDbType.Int, numericKind: NumericKind.Year),
        Sample.Column("MonthNumber", System.Data.SqlDbType.Int, numericKind: NumericKind.Month)]);

    [TestMethod]
    [DataRow("TS_Component_v1.tt", "min=\"1\" max=\"12\"")]
    [DataRow("TSX_Page_v1.tt", "min=\"1\"")]
    public async Task The_typescript_screens_limit_a_whole_number_input_to_its_range(string template, string expected)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), SummaryTable(), With(("MinYear", "1990"), ("MaxYear", "2040")));

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        Expect.Contains(result.GeneratedText!, expected);
        Expect.Contains(result.GeneratedText!, "min=\"1990\"");
    }

    [TestMethod]
    public async Task The_validation_class_adds_a_Range_only_for_a_column_whose_name_says_what_it_holds()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("CS_Validation_v1.tt"), SummaryTable(), With(("MinYear", "1990"), ("MaxYear", "2040")));

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        Expect.Contains(result.GeneratedText!, "[Range(1990, 2040)]");
        Expect.Contains(result.GeneratedText!, "[Range(1, 12)]");
        Assert.AreEqual(2, result.GeneratedText!.Split("[Range(").Length - 1, "no Range for SummaryId or Name");
    }

    // ------------------------------------------------------------------ currency inputs

    [TestMethod]
    [DataRow("ItemPrice", true)]
    [DataRow("Amount", true)]
    [DataRow("Invoice_Amt", true)]
    [DataRow("TotalOfInvoices", true)]
    [DataRow("UnitCost", true)]
    [DataRow("Quantity", false)]
    [DataRow("TotalHours", false)]
    [DataRow("TotalQty", false)]
    [DataRow("Weight", false)]
    [DataRow("MSRP", true)]
    public void A_decimal_columns_name_says_whether_it_holds_money(string name, bool money) =>
        Assert.AreEqual(money, NumericClassifier.IsCurrencyName(name));

    [TestMethod]
    public void A_currency_column_shows_its_scale_and_money_types_show_two_places()
    {
        Assert.AreEqual(8, NumericClassifier.CurrencyDigits(Sample.Column("ItemPrice", System.Data.SqlDbType.Decimal, precision: 18, scale: 8)));
        Assert.AreEqual(2, NumericClassifier.CurrencyDigits(Sample.Column("Amount", System.Data.SqlDbType.Money)));
        Assert.AreEqual("EUR", With(("CurrencyCode", "eur")).CurrencyCode);
        Assert.AreEqual("USD", With().CurrencyCode);
    }

    private static TableModel LineTable() => Sample.Table("Line", [
        Sample.Column("LineId", System.Data.SqlDbType.Int, primaryKey: true, identity: true),
        Sample.Column("Name", System.Data.SqlDbType.NVarChar, characters: 50),
        Sample.Column("ItemPrice", System.Data.SqlDbType.Decimal, precision: 18, scale: 2, currency: true),
        Sample.Column("Discount", System.Data.SqlDbType.Decimal, nullable: true, precision: 18, scale: 4, currency: true),
        Sample.Column("Quantity", System.Data.SqlDbType.Decimal, precision: 18, scale: 8)]);

    [TestMethod]
    public async Task A_currency_column_is_a_currency_formatted_number_box_converted_to_a_decimal_on_save()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("WinUI3_DetailScreen_v1.tt"), LineTable(), With(("CurrencyCode", "EUR")));

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        var files = GeneratedFiles.Split(result.GeneratedText!).ToDictionary(f => Path.GetFileName(f.RelativePath));
        string xaml = files["LineDetailDialog.xaml"].Content.Replace("\r\n", "\n");
        string codeBehind = files["LineDetailDialog.xaml.cs"].Content.Replace("\r\n", "\n");
        string viewModel = files["LineDetailViewModel.cs"].Content.Replace("\r\n", "\n");
        Expect.Contains(xaml, "<NumberBox x:Name=\"ItemPriceBox\" Header=\"Item Price\" Value=\"{x:Bind ViewModel.ItemPrice, Mode=TwoWay}\"");
        Expect.Contains(xaml, "<NumberBox x:Name=\"QuantityBox\" Header=\"Quantity\"");   // a decimal quantity is not money: a plain number box, no currency formatter
        Expect.DoesNotContain(codeBehind, "QuantityBox.NumberFormatter = new CurrencyNumberFormatter");
        Expect.Contains(codeBehind, "ItemPriceBox.NumberFormatter = new CurrencyNumberFormatter(\"EUR\", 2);");
        Expect.Contains(codeBehind, "DiscountBox.NumberFormatter = new CurrencyNumberFormatter(\"EUR\", 4);");
        Expect.Contains(xaml, "Width=\"180\" HorizontalAlignment=\"Left\"");
        // the formatter that reads a plain number as well as a currency one is written next to the dialog
        string formatter = files["CurrencyNumberFormatter.cs"].Content;
        Expect.Contains(formatter, "public sealed partial class CurrencyNumberFormatter : INumberFormatter2, INumberParser");
        Expect.Contains(formatter, "public double? ParseDouble(string text) => _currency.ParseDouble(text) ?? _plain.ParseDouble(text);");
        Expect.Contains(viewModel, "private double _itemPrice = double.NaN;");
        Expect.Contains(viewModel, "ItemPrice = (double)editing.ItemPrice;");
        Expect.Contains(viewModel, "Discount = editing.Discount.HasValue ? (double)editing.Discount.Value : double.NaN;");
        Expect.Contains(viewModel, "entity.ItemPrice = Math.Round((decimal)ItemPrice, 2);");
        Expect.Contains(viewModel, "entity.Discount = null;");
    }

    [TestMethod]
    public async Task The_list_and_the_typescript_grids_format_a_currency_column_as_money()
    {
        var list = await Repo.Cache.RunAsync(Repo.Template("WinUI3_MasterScreen_v1.tt"), LineTable(), With(("CurrencyCode", "EUR")));
        var angular = await Repo.Cache.RunAsync(Repo.Template("TS_Component_v1.tt"), LineTable(), With(("CurrencyCode", "EUR")));
        var react = await Repo.Cache.RunAsync(Repo.Template("TSX_Page_v1.tt"), LineTable(), With(("CurrencyCode", "EUR")));

        Expect.Contains(list.GeneratedText!, "new Windows.Globalization.NumberFormatting.CurrencyFormatter(\"EUR\") { IsGrouped = true, FractionDigits = 2 }.FormatDouble((double)e.ItemPrice),");
        Expect.Contains(list.GeneratedText!, "e.Discount is { } discountMoney ? new Windows.Globalization.NumberFormatting.CurrencyFormatter(\"EUR\") { IsGrouped = true, FractionDigits = 4 }.FormatDouble((double)discountMoney) : \"\",");
        Expect.Contains(angular.GeneratedText!, "itemPrice | currency:'EUR':'symbol':'1.2-2'");
        Expect.Contains(react.GeneratedText!, "currency: 'EUR', minimumFractionDigits: 2, maximumFractionDigits: 2");
    }

    // ------------------------------------------------------------------ calendar fields

    [TestMethod]
    public async Task A_child_grid_shows_a_date_and_time_as_its_date_only()
    {
        var winui = await Repo.Cache.RunAsync(Repo.Template("WinUI3_DetailMasterScreen_v1.tt"), Sample.OrderWithLines(), With());
        var react = await Repo.Cache.RunAsync(Repo.Template("TSX_DetailMasterPage_v1.tt"), Sample.OrderWithLines(), With());
        var angular = await Repo.Cache.RunAsync(Repo.Template("TS_DetailMasterComponent_v1.tt"), Sample.OrderWithLines(), With());

        Expect.Contains(winui.GeneratedText!, "if (value is DateTime dateValue) return dateValue.ToString(\"MM/dd/yyyy\");");
        Expect.Contains(react.GeneratedText!, @"value.replace(/^(\d{4}-\d{2}-\d{2})T.*$/, '$1')");
        Expect.Contains(angular.GeneratedText!, "cell(value: unknown, key: string, names?: Record<string, string>): string {");
        Expect.Contains(angular.GeneratedText!, "{{cell(row[col], col, orderLineNames[col])}}");
    }

    [TestMethod]
    public async Task A_date_column_is_a_calendar_picker_and_a_nullable_one_gets_a_clear_button()
    {
        var table = Sample.Table("Summary", [
            Sample.Column("SummaryId", System.Data.SqlDbType.Int, primaryKey: true, identity: true),
            Sample.Column("Name", System.Data.SqlDbType.NVarChar, characters: 50),
            Sample.Column("StartDate", System.Data.SqlDbType.DateTime),
            Sample.Column("ClosedDate", System.Data.SqlDbType.DateTime, nullable: true)]);

        var result = await Repo.Cache.RunAsync(Repo.Template("WinUI3_DetailScreen_v1.tt"), table, With());

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        var files = GeneratedFiles.Split(result.GeneratedText!).ToDictionary(f => Path.GetFileName(f.RelativePath));
        string xaml = files["SummaryDetailDialog.xaml"].Content;
        string codeBehind = files["SummaryDetailDialog.xaml.cs"].Content;
        string viewModel = files["SummaryDetailViewModel.cs"].Content;
        Expect.Contains(xaml, "<CalendarDatePicker Header=\"Start Date\" Date=\"{x:Bind ViewModel.StartDate, Mode=TwoWay}\"");
        Expect.Contains(xaml, "Click=\"OnClearClosedDateClick\"");
        Expect.DoesNotContain(xaml, "Click=\"OnClearStartDateClick\"");   // a required date has no clear button
        Expect.Contains(codeBehind, "private void OnClearClosedDateClick(object sender, RoutedEventArgs e) => ViewModel.ClosedDate = null;");
        Expect.Contains(viewModel, "private DateTimeOffset? _startDate;");
        Expect.Contains(viewModel, "private TimeSpan _closedDateTime;");
        Expect.Contains(viewModel, "StartDate = new DateTimeOffset(editing.StartDate);");
        Expect.Contains(viewModel, "ClosedDate = editing.ClosedDate.HasValue ? new DateTimeOffset(editing.ClosedDate.Value) : null;");
        Expect.Contains(viewModel, "entity.ClosedDate = ClosedDate.Value.Date + _closedDateTime;");
    }

    [TestMethod]
    [DataRow("WinUI3_DetailScreen_v1.tt")]
    [DataRow("WinUI3_DetailMasterScreen_v1.tt")]
    public async Task Billing_and_shipping_fields_move_to_a_second_tab_and_the_main_tab_is_selected(string template)
    {
        var table = Sample.Table("Order", [
            Sample.Column("OrderId", System.Data.SqlDbType.Int, primaryKey: true, identity: true),
            Sample.Column("CustomerName", System.Data.SqlDbType.NVarChar, characters: 50),
            Sample.Column("BillingCity", System.Data.SqlDbType.NVarChar, characters: 50, nullable: true),
            Sample.Column("ShippingCity", System.Data.SqlDbType.NVarChar, characters: 50, nullable: true)],
            childForeignKeys: [Sample.ChildForeignKey("OrderLine", "OrderId", "OrderId", childOwnPrimaryKey: ["OrderLineId"])]);

        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, With());

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string xaml = GeneratedFiles.Split(result.GeneratedText!).Single(f => f.RelativePath.EndsWith("Order" + (template.Contains("Master") ? "DetailMasterDialog.xaml" : "DetailDialog.xaml"))).Content.Replace("\r\n", "\n");
        Expect.Contains(xaml, "SelectedIndex=\"0\"");
        Expect.Contains(xaml, "<TabViewItem Header=\"Main\"");
        Expect.Contains(xaml, "<TabViewItem Header=\"Billing &amp; Shipping\"");
        int main = xaml.IndexOf("Header=\"Main\"", StringComparison.Ordinal), second = xaml.IndexOf("Header=\"Billing &amp; Shipping\"", StringComparison.Ordinal);
        Assert.IsTrue(main < xaml.IndexOf("Header=\"Customer Name\"", StringComparison.Ordinal) && xaml.IndexOf("Header=\"Customer Name\"", StringComparison.Ordinal) < second);
        Assert.IsTrue(second < xaml.IndexOf("Header=\"Billing City\"", StringComparison.Ordinal) && second < xaml.IndexOf("Header=\"Shipping City\"", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("WinUI3_DetailScreen_v1.tt")]
    [DataRow("WinUI3_DetailMasterScreen_v1.tt")]
    public async Task Long_text_fields_move_to_a_Notes_tab_after_Billing_and_Shipping(string template)
    {
        var table = Sample.Table("Order", [
            Sample.Column("OrderId", System.Data.SqlDbType.Int, primaryKey: true, identity: true),
            Sample.Column("CustomerName", System.Data.SqlDbType.NVarChar, characters: 50),
            Sample.Column("BillingCity", System.Data.SqlDbType.NVarChar, characters: 50, nullable: true),
            Sample.Column("LineMemo", System.Data.SqlDbType.NVarChar, characters: 50, nullable: true),
            Sample.Column("Remarks", System.Data.SqlDbType.NVarChar, characters: 50, nullable: true),
            Sample.Column("PickNotes", System.Data.SqlDbType.NVarChar, characters: 50, nullable: true)],
            childForeignKeys: [Sample.ChildForeignKey("OrderLine", "OrderId", "OrderId", childOwnPrimaryKey: ["OrderLineId"])]);

        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, With());

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string xaml = GeneratedFiles.Split(result.GeneratedText!).Single(f => f.RelativePath.EndsWith("Order" + (template.Contains("Master") ? "DetailMasterDialog.xaml" : "DetailDialog.xaml"))).Content.Replace("\r\n", "\n");
        int At(string header) => xaml.IndexOf("Header=\"" + header + "\"", StringComparison.Ordinal);
        Assert.IsTrue(At("Main") < At("Customer Name") && At("Customer Name") < At("Billing &amp; Shipping"));
        Assert.IsTrue(At("Billing &amp; Shipping") < At("Billing City") && At("Billing City") < At("Notes"));
        Assert.IsTrue(At("Notes") < At("Line Memo") && At("Notes") < At("Remarks") && At("Notes") < At("Pick Notes"));
        Expect.Contains(xaml, "SelectedIndex=\"0\"");
    }

    [TestMethod]
    public async Task With_no_billing_fields_the_Notes_tab_is_the_second_page()
    {
        var table = Sample.Table("Order", [
            Sample.Column("OrderId", System.Data.SqlDbType.Int, primaryKey: true, identity: true),
            Sample.Column("CustomerName", System.Data.SqlDbType.NVarChar, characters: 50),
            Sample.Column("Notes", System.Data.SqlDbType.NVarChar, characters: 50, nullable: true)]);

        var result = await Repo.Cache.RunAsync(Repo.Template("WinUI3_DetailScreen_v1.tt"), table, With());

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        Expect.Contains(result.GeneratedText!, "<TabViewItem Header=\"Notes\"");
        Expect.DoesNotContain(result.GeneratedText!, "Billing &amp; Shipping");
    }

    [TestMethod]
    public async Task A_table_without_billing_shipping_or_note_fields_has_no_tabs()
    {
        var table = Sample.Table("Order", [
            Sample.Column("OrderId", System.Data.SqlDbType.Int, primaryKey: true, identity: true),
            Sample.Column("CustomerName", System.Data.SqlDbType.NVarChar, characters: 50)]);

        var result = await Repo.Cache.RunAsync(Repo.Template("WinUI3_DetailScreen_v1.tt"), table, With());

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        Expect.DoesNotContain(result.GeneratedText!, "<TabView");
    }

    [TestMethod]
    [DataRow("WinUI3_DetailScreen_v1.tt")]
    [DataRow("WinUI3_DetailMasterScreen_v1.tt")]
    public async Task A_decimal_that_is_not_money_is_a_number_box_limited_to_what_the_column_holds(string template)
    {
        var table = Sample.Table("Order", [
            Sample.Column("OrderId", System.Data.SqlDbType.Int, primaryKey: true, identity: true),
            Sample.Column("OnTimePercentage", System.Data.SqlDbType.Decimal, precision: 5, scale: 2, nullable: true),
            Sample.Column("Weight", System.Data.SqlDbType.Decimal, precision: 5, scale: 2),
            Sample.Column("Ratio", System.Data.SqlDbType.Float, nullable: true)],
            childForeignKeys: [Sample.ChildForeignKey("OrderLine", "OrderId", "OrderId", childOwnPrimaryKey: ["OrderLineId"])]);

        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, With());

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string text = result.GeneratedText!.Replace("\r\n", "\n");
        Expect.Contains(text, "Value=\"{x:Bind ViewModel.OnTimePercentage, Mode=TwoWay}\"");
        Expect.Contains(text, "Minimum=\"0\" Maximum=\"100\"");                       // a percentage is 0 to 100
        Expect.Contains(text, "Minimum=\"-999.99\" Maximum=\"999.99\"");              // decimal(5,2)
        Expect.DoesNotContain(text, "Text=\"{x:Bind ViewModel.OnTimePercentage");
        Expect.Contains(text, "OnTimePercentageBox.NumberFormatter = new Windows.Globalization.NumberFormatting.DecimalFormatter");
        Expect.Contains(text, "entity.OnTimePercentage = Math.Round((decimal)OnTimePercentage, 2);");
        Expect.Contains(text, "entity.Ratio = Ratio;");
        Expect.Contains(text, "OnTimePercentage = editing.OnTimePercentage.HasValue ? (double)editing.OnTimePercentage.Value : double.NaN;");
    }

    [TestMethod]
    public async Task The_error_bar_is_outside_the_scrolling_area_so_a_validation_message_is_always_visible()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("WinUI3_DetailScreen_v1.tt"), Sample.DonateLeave(), With());

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string text = result.GeneratedText!.Replace("\r\n", "\n");
        Assert.IsLessThan(text.IndexOf("<ScrollViewer", StringComparison.Ordinal), text.IndexOf("<InfoBar", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Callers_wait_for_a_closed_dialogs_own_load_before_reusing_the_DbContext()
    {
        var edit = await Repo.Cache.RunAsync(Repo.Template("WinUI3_DetailScreen_v1.tt"), Sample.DonateLeave(), With());
        var list = await Repo.Cache.RunAsync(Repo.Template("WinUI3_MasterScreen_v1.tt"), Sample.DonateLeave(), With());
        var master = await Repo.Cache.RunAsync(Repo.Template("WinUI3_DetailMasterScreen_v1.tt"), Sample.OrderWithLines(), With());

        Assert.IsTrue(edit.Success && list.Success && master.Success);
        Expect.Contains(edit.GeneratedText!, "Loaded += (_, _) => _loading = ViewModel.LoadLookupsAsync();");
        Expect.Contains(edit.GeneratedText!, "public async System.Threading.Tasks.Task<ContentDialogResult> ShowAndWaitAsync()");
        Expect.Contains(master.GeneratedText!, "await childDialog.ShowAndWaitAsync();");
        Expect.Contains(master.GeneratedText!, "await _drillDown.Task;");   // the parent's ShowAndWaitAsync outlasts the child dialog it opened
        Expect.Contains(master.GeneratedText!, "_drillDown.SetResult();");
        Expect.DoesNotContain(list.GeneratedText!, "await dialog.ShowAsync();");
        Expect.Contains(list.GeneratedText!, "await dialog.ShowAndWaitAsync();");
    }

    private static TableModel TabbedTable() => Sample.Table("Order", [
        Sample.Column("OrderId", System.Data.SqlDbType.Int, primaryKey: true, identity: true),
        Sample.Column("CustomerName", System.Data.SqlDbType.NVarChar, characters: 50),
        Sample.Column("OnTimePercentage", System.Data.SqlDbType.Decimal, precision: 5, scale: 2, nullable: true),
        Sample.Column("BillingCity", System.Data.SqlDbType.NVarChar, characters: 50, nullable: true),
        Sample.Column("LineMemo", System.Data.SqlDbType.NVarChar, characters: 50, nullable: true)],
        childForeignKeys: [Sample.ChildForeignKey("OrderLine", "OrderId", "OrderId", childOwnPrimaryKey: ["OrderLineId"])]);

    [TestMethod]
    public void FormPages_splits_main_billing_shipping_and_notes_and_leaves_a_plain_table_alone()
    {
        var pages = FormPages.For(TabbedTable().Columns.Where(c => !c.IsPrimaryKey).ToList());

        CollectionAssert.AreEqual(new[] { "Main", "Billing & Shipping", "Notes" }, pages.Select(p => p.Header).ToArray());
        CollectionAssert.AreEqual(new[] { "CustomerName", "OnTimePercentage" }, pages[0].Columns.Select(c => c.Name).ToArray());
        Assert.HasCount(1, FormPages.For([Sample.Column("Name", System.Data.SqlDbType.NVarChar, characters: 50)]));
        Assert.AreEqual("", FormPages.For([Sample.Column("Name", System.Data.SqlDbType.NVarChar, characters: 50)])[0].Header);
    }

    [TestMethod]
    [DataRow("TS_Component_v1.tt")]
    [DataRow("TS_DetailMasterComponent_v1.tt")]
    [DataRow("TSX_Page_v1.tt")]
    [DataRow("TSX_DetailMasterPage_v1.tt")]
    public async Task A_web_form_has_tabs_and_limits_a_percentage_to_0_100(string template)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), TabbedTable(), With());

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string text = result.GeneratedText!.Replace("\r\n", "\n");
        Expect.Contains(text, ">Main</button>");
        Expect.Contains(text, ">Billing &amp; Shipping</button>");
        Expect.Contains(text, ">Notes</button>");
        Assert.IsTrue(text.Contains("min=\"0\"") && text.Contains("max=\"100\""), "a percentage is limited to 0-100");
        Assert.IsTrue(text.Contains("activeTab = 0") || text.Contains("setTab(0)"), "a new form opens on the Main tab");
        if (template.StartsWith("TSX"))
            Expect.Contains(text, "onClick={revealInvalidTab}");
    }

    [TestMethod]
    [DataRow("TS_Component_v1.tt")]
    [DataRow("TSX_Page_v1.tt")]
    public async Task A_web_form_without_billing_shipping_or_note_fields_has_no_tabs(string template)
    {
        var table = Sample.Table("Order", [
            Sample.Column("OrderId", System.Data.SqlDbType.Int, primaryKey: true, identity: true),
            Sample.Column("CustomerName", System.Data.SqlDbType.NVarChar, characters: 50)]);

        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, With());

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        Expect.DoesNotContain(result.GeneratedText!, "role=\"tablist\"");
    }

    [TestMethod]
    public async Task A_master_dialogs_child_grid_has_row_level_Edit_and_Delete_in_every_stack()
    {
        var winui = await Repo.Cache.RunAsync(Repo.Template("WinUI3_DetailMasterScreen_v1.tt"), Sample.OrderWithLines(), With());
        var react = await Repo.Cache.RunAsync(Repo.Template("TSX_DetailMasterPage_v1.tt"), Sample.OrderWithLines(), With());
        var angular = await Repo.Cache.RunAsync(Repo.Template("TS_DetailMasterComponent_v1.tt"), Sample.OrderWithLines(), With());

        Assert.IsTrue(winui.Success && react.Success && angular.Success);
        Expect.Contains(winui.GeneratedText!, "Click=\"OnOrderLineEditClick\"");
        Expect.Contains(winui.GeneratedText!, "Click=\"OnOrderLineDeleteClick\"");
        Expect.Contains(winui.GeneratedText!, "<TextBlock Text=\"Actions\" FontWeight=\"SemiBold\" Width=\"120\" VerticalAlignment=\"Center\" />");
        Expect.Contains(react.GeneratedText!, "const editOrderLine = (row: any) => {");
        Expect.Contains(react.GeneratedText!, "const deleteOrderLine = async (row: any): Promise<void> => {");
        Expect.Contains(react.GeneratedText!, "onClick={() => editOrderLine(row)}");
        Expect.Contains(react.GeneratedText!, "?edit=${row['orderLineId']}&back=");
        Expect.Contains(angular.GeneratedText!, "editOrderLine(row: any): void {");
        Expect.Contains(angular.GeneratedText!, "deleteOrderLine(row: any): void {");
        Expect.Contains(angular.GeneratedText!, "(click)=\"editOrderLine(row)\"");
    }

    [TestMethod]
    public async Task A_master_detail_page_has_the_same_search_and_paging_as_a_plain_page_in_React_and_Angular()
    {
        var table = Sample.Table("Order", [
            Sample.Column("OrderId", System.Data.SqlDbType.Int, primaryKey: true, identity: true),
            Sample.Column("CustomerPO", System.Data.SqlDbType.NVarChar, characters: 50, nullable: true)],
            childForeignKeys: [Sample.ChildForeignKey("OrderLine", "OrderId", "OrderId", childOwnPrimaryKey: ["OrderLineId"])]);
        var react = await Repo.Cache.RunAsync(Repo.Template("TSX_DetailMasterPage_v1.tt"), table, With());
        var angular = await Repo.Cache.RunAsync(Repo.Template("TS_DetailMasterComponent_v1.tt"), table, With());

        Assert.IsTrue(react.Success && angular.Success);
        Expect.Contains(react.GeneratedText!, "placeholder=\"Search by Customer PO\"");
        Expect.Contains(react.GeneratedText!, "<PaginationBar");
        Expect.Contains(react.GeneratedText!, "orderApi.getPage(targetPage, sizeValue, filterValues, sortValue)");
        Expect.Contains(angular.GeneratedText!, "placeholder=\"Search by Customer PO\"");
        Expect.Contains(angular.GeneratedText!, "<mat-paginator");
        Expect.Contains(angular.GeneratedText!, "getPage(this.pageIndex + 1, this.pageSize");
    }

    [TestMethod]
    [DataRow("TS_Component_v1.tt")]
    [DataRow("TS_DetailMasterComponent_v1.tt")]
    [DataRow("TSX_Page_v1.tt")]
    [DataRow("TSX_DetailMasterPage_v1.tt")]
    public async Task A_nullable_date_has_a_Clear_button_in_the_web_forms_and_a_required_one_does_not(string template)
    {
        var table = Sample.Table("Order", [
            Sample.Column("OrderId", System.Data.SqlDbType.Int, primaryKey: true, identity: true),
            Sample.Column("OrderDate", System.Data.SqlDbType.DateTime),
            Sample.Column("DueDate", System.Data.SqlDbType.DateTime, nullable: true)],
            childForeignKeys: [Sample.ChildForeignKey("OrderLine", "OrderId", "OrderId", childOwnPrimaryKey: ["OrderLineId"])]);

        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, With());

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string text = result.GeneratedText!;
        Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(text, ">Clear</button>").Count(m => text.Substring(Math.Max(0, m.Index - 160), Math.Min(160, m.Index)).Contains("ueDate")), "one Clear button, on DueDate");
        Expect.DoesNotContain(text, "orderDate: undefined");
        Expect.DoesNotContain(text, "selectedRow.orderDate = undefined");
    }

    [TestMethod]
    [DataRow("TS_Component_v1.tt")]
    [DataRow("TS_DetailMasterComponent_v1.tt")]
    public async Task An_Angular_component_keeps_eager_change_detection(string template)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), TabbedTable(), With());

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        Expect.Contains(result.GeneratedText!, "import { ChangeDetectionStrategy, Component, ElementRef, HostListener, ViewChild } from '@angular/core';");
        Expect.Contains(result.GeneratedText!, "changeDetection: ChangeDetectionStrategy.Default,");
    }

    [TestMethod]
    [DataRow("TS_Component_v1.tt", "<dialog *ngIf=")]
    [DataRow("TS_DetailMasterComponent_v1.tt", "<dialog *ngIf=")]
    [DataRow("TSX_Page_v1.tt", "<dialog")]
    [DataRow("TSX_DetailMasterPage_v1.tt", "<dialog")]
    public async Task The_web_edit_form_is_a_modal_dialog_that_opens_over_the_page(string template, string opening)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), TabbedTable(), With());

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string text = result.GeneratedText!;
        Expect.Contains(text, opening);
        Expect.Contains(text, "</dialog>");
        Expect.Contains(text, template.StartsWith("TSX") ? "el.showModal()" : "showModal()");
        
    }

    private static TableModel CustomerWithPurchases() => Sample.Table("Customer", [
        Sample.Column("CustomerId", System.Data.SqlDbType.Int, primaryKey: true, identity: true),
        Sample.Column("CustomerName", System.Data.SqlDbType.NVarChar, characters: 50)],
        childForeignKeys: [Sample.ChildForeignKey("CustomerItem", "CustomerId", "CustomerId",
            childOwnPrimaryKey: ["CustomerItemId"],
            childOwnOtherForeignKeys: [Sample.ForeignKey("ItemId", "Item", "ItemId", "ItemNumber")])]);

    [TestMethod]
    public void ChildGridTitle_reads_the_projects_title_for_a_parent_and_child_pair()
    {
        var settings = With(("ChildGridTitles", "Customer.CustomerItem=Item Purchase History,Item.CustomerItem=Who Purchased?"));

        Assert.AreEqual("Item Purchase History", settings.ChildGridTitle("Customer", "CustomerItem", "Customer Item"));
        Assert.AreEqual("Who Purchased?", settings.ChildGridTitle("item", "customeritem", "Customer Item"));
        Assert.AreEqual("Sales Invoice", settings.ChildGridTitle("Customer", "SalesInvoice", "Sales Invoice"));
    }

    [TestMethod]
    [DataRow("WinUI3_DetailMasterScreen_v1.tt")]
    [DataRow("TSX_DetailMasterPage_v1.tt")]
    [DataRow("TS_DetailMasterComponent_v1.tt")]
    public async Task A_child_grid_uses_the_projects_title(string template)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), CustomerWithPurchases(), With(("ChildGridTitles", "Customer.CustomerItem=Item Purchase History")));

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        Expect.Contains(result.GeneratedText!, "Item Purchase History");
        Expect.DoesNotContain(result.GeneratedText!, ">Customer Item<");
    }

    [TestMethod]
    [DataRow("TSX_DetailMasterPage_v1.tt", "itemId: 'item'")]
    [DataRow("TS_DetailMasterComponent_v1.tt", "this.customerItemNames")]
    public async Task A_web_child_grid_shows_the_other_side_by_name_and_hides_ids(string template, string expected)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), CustomerWithPurchases(), With());

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string text = result.GeneratedText!;
        Assert.IsTrue(text.Contains("column: 'itemId'") || text.Contains("itemId: Object.fromEntries"), "the item id is resolved to the item's name");
        Assert.IsTrue(text.Contains("customerItemHidden") || text.Contains("customerItemHidden: string[]"), "the grid's own key columns are hidden");
        Assert.IsTrue(text.Contains("'customerId'") && text.Contains("'customerItemId'"), "own foreign key and own key are in the hidden list");
    }

    [TestMethod]
    public async Task A_child_grid_spaces_its_captions_and_shows_an_empty_cell_without_a_classic_binding()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("WinUI3_DetailMasterScreen_v1.tt"), Sample.OrderWithLines(), With());

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string text = result.GeneratedText!.Replace("\r\n", "\n");
        Expect.Contains(text, "label : SpacedHeader(name)).ToList();");
        Expect.Contains(text, "private static string SpacedHeader(string name)");
        Expect.Contains(text, "using System.Text.RegularExpressions;");
        Expect.Contains(text, "<DataTemplate x:DataType=\"x:String\">");
        Expect.Contains(text, "<TextBlock Text=\"{x:Bind}\" Width=\"120\" />");
    }

    // ------------------------------------------------------------------ grid rules

    [TestMethod]
    [DataRow("Item Number", "Item #")]
    [DataRow("Number", "#")]
    [DataRow("Month Num", "Month #")]
    [DataRow("Discount Percentage", "Discount %")]
    [DataRow("Tax Percent", "Tax %")]
    [DataRow("Numbers Of Things", "Numbers Of Things")]
    [DataRow("Item Description", "Item")]
    [DataRow("Customer Name", "Customer")]
    [DataRow("Item Short Descr", "Item")]
    [DataRow("Item Short Description", "Item")]
    [DataRow("Item Status", "Status")]
    [DataRow("Customer Status", "Status")]
    [DataRow("Alternate Name", "Alt. Name")]
    [DataRow("Name", "Name")]
    [DataRow("Description", "Description")]
    [DataRow("Status", "Status")]
    [DataRow("Status Description", "Status")]
    public void A_grid_caption_writes_number_as_a_hash_and_percent_as_a_percent_sign(string words, string expected) =>
        Assert.AreEqual(expected, GridCaption.From(words));

    [TestMethod]
    public void Long_text_means_max_text_a_long_length_or_a_note_name()
    {
        Assert.IsTrue(Sample.Column("Body", System.Data.SqlDbType.NVarChar, characters: 2000).IsLongTextColumn);
        Assert.IsTrue(Sample.Column("Notes", System.Data.SqlDbType.VarChar, characters: 50).IsLongTextColumn, "named like a note");
        Assert.IsTrue(Sample.Column("SummaryNote", System.Data.SqlDbType.VarChar, characters: 500).IsLongTextColumn);
        Assert.IsFalse(Sample.Column("ItemDescription", System.Data.SqlDbType.VarChar, characters: 255).IsLongTextColumn);
        Assert.IsFalse(Sample.Column("Name", System.Data.SqlDbType.VarChar, characters: 50).IsLongTextColumn);
        Assert.IsFalse(Sample.Column("Quantity", System.Data.SqlDbType.Int).IsLongTextColumn);
    }

    // A grid header, whichever stack wrote it: a plain <th>, a WinUI3 TextBlock or sort Button, a React SortHeader, an Angular sort button.
    private static bool HasHeader(string text, string caption)
    {
        const string Q = "\"";
        return text.Contains($"<th>{caption}</th>")
            || text.Contains($"Text={Q}{caption}{Q} FontWeight=")
            || text.Contains($"Content={Q}{caption}{Q} Tag=")
            || text.Contains($"label={Q}{caption}{Q} column=")
            || text.Contains($">{caption}{{{{ sortMark(");
    }

    private static TableModel GridTable() => Sample.Table("Item", [
        Sample.Column("ItemId", System.Data.SqlDbType.Int, primaryKey: true, identity: true),
        Sample.Column("Notes", System.Data.SqlDbType.VarChar, characters: 40, ordinal: 2),
        Sample.Column("ItemNumber", System.Data.SqlDbType.VarChar, characters: 30, ordinal: 3),
        Sample.Column("DiscountPercentage", System.Data.SqlDbType.Int, ordinal: 4)]);

    [TestMethod]
    [DataRow("WinUI3_MasterScreen_v1.tt")]
    [DataRow("TS_Component_v1.tt")]
    [DataRow("TSX_Page_v1.tt")]
    public async Task A_master_grid_leaves_out_long_text_and_writes_number_and_percent_as_symbols(string template)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), GridTable(), With());

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string text = result.GeneratedText!;
        Assert.IsTrue(HasHeader(text, "Item #"), "the Item Number column is captioned Item #");
        Expect.Contains(text, "Discount %");
        // Notes is a long text column: it never appears in a grid
        Expect.DoesNotContain(text, "<th>Notes</th>");
        Expect.DoesNotContain(text, "Text=\"Notes\" FontWeight=");
    }

    [TestMethod]
    [DataRow("WinUI3_MasterScreen_v1.tt")]
    [DataRow("TS_Component_v1.tt")]
    [DataRow("TSX_Page_v1.tt")]
    public async Task A_master_grid_shows_at_most_18_columns(string template)
    {
        var columns = new List<ColumnModel> { Sample.Column("ItemId", System.Data.SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1) };
        columns.AddRange(Enumerable.Range(1, 25).Select(i => Sample.Column("Quantity" + (char)('A' + i - 1), System.Data.SqlDbType.Int, ordinal: i + 1)));
        var result = await Repo.Cache.RunAsync(Repo.Template(template), Sample.Table("Item", columns), With());

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string text = result.GeneratedText!;
        Assert.IsTrue(HasHeader(text, "Quantity R"), "the 18th column is in the grid");
        Expect.DoesNotContain(text, "<th>Quantity S</th>");
        Expect.DoesNotContain(text, "Text=\"Quantity S\" FontWeight=");
    }

    [TestMethod]
    public void ForGrid_drops_long_text_even_with_few_columns_and_limits_the_rest_to_18()
    {
        var columns = new List<ColumnModel> { Sample.Column("Notes", System.Data.SqlDbType.VarChar, characters: 40) };
        columns.AddRange(Enumerable.Range(1, 30).Select(i => Sample.Column("C" + i, System.Data.SqlDbType.Int)));

        var grid = columns.ForGrid();

        Assert.HasCount(18, grid);
        Assert.IsFalse(grid.Any(c => c.Name == "Notes"));
        Assert.AreEqual("Notes", new[] { columns[0] }.ForGrid().Single().Name, "a table of only long text still gets a grid");
    }

    [TestMethod]
    public async Task A_child_grid_formats_money_orders_long_text_last_and_captions_from_the_childs_schema()
    {
        var child = new ChildForeignKeyModel
        {
            ConstraintName = "FK_Line_Order",
            ReferencingSchema = "dbo",
            ReferencingTable = "OrderLine",
            ReferencingColumns = ["OrderId"],
            ReferencedColumns = ["OrderId"],
            ReferencingPrimaryKeyColumns = ["OrderLineId"],
            ReferencingTableColumns =
            [
                Sample.Column("OrderLineId", System.Data.SqlDbType.Int, primaryKey: true, identity: true),
                Sample.Column("OrderId", System.Data.SqlDbType.Int),
                Sample.Column("Notes", System.Data.SqlDbType.VarChar, characters: 2000),
                Sample.Column("LinePrice", System.Data.SqlDbType.Decimal, precision: 18, scale: 2, currency: true)
            ]
        };
        var parent = Sample.Table("Order", [
            Sample.Column("OrderId", System.Data.SqlDbType.Int, primaryKey: true, identity: true),
            Sample.Column("OrderDate", System.Data.SqlDbType.Date)], childForeignKeys: [child]);
        var project = With(("CurrencyCode", "EUR"));

        var winui = await Repo.Cache.RunAsync(Repo.Template("WinUI3_DetailMasterScreen_v1.tt"), parent, project);
        var react = await Repo.Cache.RunAsync(Repo.Template("TSX_DetailMasterPage_v1.tt"), parent, project);
        var angular = await Repo.Cache.RunAsync(Repo.Template("TS_DetailMasterComponent_v1.tt"), parent, project);

        Assert.IsTrue(winui.Success, string.Join(" | ", winui.Errors));
        Expect.Contains(winui.GeneratedText!, "[\"LinePrice\"] = 2,");
        Expect.Contains(winui.GeneratedText!, "LongTextInOrderLineGrid = [ \"Notes\" ];");
        Expect.Contains(winui.GeneratedText!, ".Take(18).ToList();");
        Expect.Contains(winui.GeneratedText!, "return new Windows.Globalization.NumberFormatting.CurrencyFormatter(\"EUR\") { IsGrouped = true, FractionDigits = moneyDigits }.FormatDouble((double)moneyValue);");
        Expect.Contains(react.GeneratedText!, "const childCurrencyDigits: Record<string, number> = { linePrice: 2 };");
        Expect.Contains(react.GeneratedText!, "const childLongTextColumns: string[] = ['notes'];");
        Expect.Contains(react.GeneratedText!, "currency: 'EUR', minimumFractionDigits: d");
        Expect.Contains(react.GeneratedText!, "{caption(col)}");
        Expect.Contains(angular.GeneratedText!, "private readonly childCurrencyDigits: Record<string, number> = { linePrice: 2 };");
        Expect.Contains(angular.GeneratedText!, "{{caption(col)}}");
    }

    // ------------------------------------------------------------------ yes/no and date captions

    [TestMethod]
    public void A_grid_caption_marks_yes_no_columns_with_a_question_mark_and_drops_Date_from_date_columns()
    {
        ColumnModel Col(string name, System.Data.SqlDbType type) => Sample.Column(name, type);

        Assert.AreEqual("Closed?", GridCaption.For(Col("IsClosed", System.Data.SqlDbType.Bit), "Is Closed"));
        Assert.AreEqual("Is?", GridCaption.For(Col("Is", System.Data.SqlDbType.Bit), "Is"), "a lone Is stays");
        Assert.AreEqual("Closed?", GridCaption.For(Col("IsClosed", System.Data.SqlDbType.Bit), "Is Closed?"), "no second question mark");
        Assert.AreEqual("Added", GridCaption.For(Col("DateAdded", System.Data.SqlDbType.DateTime), "Date Added"));
        Assert.AreEqual("Invoice", GridCaption.For(Col("InvoiceDate", System.Data.SqlDbType.DateTime), "Invoice Date"));
        Assert.AreEqual("Date", GridCaption.For(Col("Date", System.Data.SqlDbType.Date), "Date"), "nothing would be left");
        Assert.AreEqual("Date Added", GridCaption.For(Col("DateAdded", System.Data.SqlDbType.VarChar), "Date Added"), "only a real date column");
    }

    [TestMethod]
    public void EnumTables_none_means_no_table_is_an_enum_whatever_its_shape()
    {
        var project = ProjectSettings.Parse("ProjectName=Acme\nEnumTables=none");

        Assert.HasCount(0, project.EnumTables!);
        Assert.IsFalse(project.IsEnumTable("ItemStatus", new LookupShape(true, 2, true)));
    }

    [TestMethod]
    [DataRow("WinUI3_MasterScreen_v1.tt")]
    [DataRow("TS_Component_v1.tt")]
    [DataRow("TSX_Page_v1.tt")]
    public async Task A_master_grid_captions_a_yes_no_column_with_a_question_mark_and_a_date_column_without_Date(string template)
    {
        var table = Sample.Table("Summary", [
            Sample.Column("SummaryId", System.Data.SqlDbType.Int, primaryKey: true, identity: true),
            Sample.Column("Name", System.Data.SqlDbType.NVarChar, characters: 50),
            Sample.Column("IsClosed", System.Data.SqlDbType.Bit),
            Sample.Column("DateAdded", System.Data.SqlDbType.DateTime)]);

        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, With());

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string text = result.GeneratedText!;
        Assert.IsTrue(HasHeader(text, "Closed?"), "yes/no caption");
        Assert.IsTrue(HasHeader(text, "Added"), "date caption without Date");
    }
}
