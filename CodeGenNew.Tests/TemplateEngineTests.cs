using System.Reflection;
using System.Text;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

[TestClass]
public class TemplateInfoBuildFileNameTests
{
    private static TemplateInfo Template(string name, string? outputName = null) => new()
    {
        FilePath = name + ".tt",
        Name = name,
        SubmenuGroup = name.Contains('_') ? name[..name.IndexOf('_')] : null,
        Config = new TemplateConfig { OutputName = outputName }
    };

    [TestMethod]
    [DataRow("SP_Update", "ProductionUnitMaster", "ProductionUnitMaster_Update.sql")]
    [DataRow("CS_Widget", "Holiday", "Holiday_Widget.cs")]
    [DataRow("TS_Thing", "Holiday", "Holiday_Thing.ts")]
    [DataRow("API_Thing", "Holiday", "Holiday_Thing.cs")]
    [DataRow("Other_Thing", "Holiday", "Holiday_Thing.txt")]
    public void The_extension_follows_the_submenu_group(string template, string table, string expected)
    {
        Assert.AreEqual(expected, Template(template).BuildFileName(table));
    }

    [TestMethod]
    public void OutputName_replaces_the_whole_name_and_fills_in_the_table()
    {
        Assert.AreEqual("E_DonateLeaveApi.cs", Template("API_Crud", "{Table}Api.cs").BuildFileName("E_DonateLeave"));
    }

    [TestMethod]
    public void OutputName_can_never_be_a_path()
    {
        Assert.AreEqual("x.cs", Template("API_Crud", "../../{Table}.cs").BuildFileName("x"));
    }
}

[TestClass]
public class TemplateConfigTests
{
    [TestMethod]
    public void A_missing_config_file_is_the_conservative_default()
    {
        var config = TemplateConfig.Load(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N") + ".config"));

        Assert.IsTrue(config.RequiresPrimaryKey);
        Assert.AreEqual(TemplateScope.Table, config.Scope);
        Assert.IsFalse(config.NeedsRowData);
        Assert.IsFalse(config.NeedsReferencedDisplayColumns);
        Assert.IsNull(config.OutputName);
    }

    [TestMethod]
    public void Every_key_is_read_and_comments_and_blank_lines_are_ignored()
    {
        using var temp = new TempFolder();
        string path = temp.File("x.tt.config", "# a comment\n\nRequiresPrimaryKey=false\nTableOnly = false\nNeedsRowData=true\nNeedsReferencedDisplayColumns=true\nOutputName={Table}Api.cs\nUnknownKey=whatever\n");

        var config = TemplateConfig.Load(path);

        Assert.IsFalse(config.RequiresPrimaryKey);
        CollectionAssert.AreEqual(new[] { "UnknownKey" }, config.UnknownKeys.ToArray());
        Assert.IsTrue(config.NeedsRowData);
        Assert.IsTrue(config.NeedsReferencedDisplayColumns);
        Assert.AreEqual("{Table}Api.cs", config.OutputName);
    }

    [TestMethod]
    public void The_shipped_configs_say_what_their_templates_need()
    {
        Assert.IsTrue(TemplateConfig.Load(Repo.Template("SP_Load_v1.tt.config")).NeedsRowData);
        Assert.IsTrue(TemplateConfig.Load(Repo.Template("CS_Enum_v1.tt.config")).NeedsRowData);
        Assert.IsTrue(TemplateConfig.Load(Repo.Template("SP_Lookup_v1.tt.config")).NeedsReferencedDisplayColumns);
        Assert.IsTrue(TemplateConfig.Load(Repo.Template("TS_Component_v1.tt.config")).NeedsReferencedDisplayColumns);
        Assert.AreEqual("{Table}Api.cs", TemplateConfig.Load(Repo.Template("API_Crud_v1.tt.config")).OutputName);
        Assert.AreEqual("{Table}Repo.cs", TemplateConfig.Load(Repo.Template("CS_Repo_v1.tt.config")).OutputName);
    }

    [TestMethod]
    public void RequiresSingleKey_is_read_and_is_off_unless_set()
    {
        using var temp = new TempFolder();

        Assert.IsTrue(TemplateConfig.Load(temp.File("a.tt.config", "RequiresSingleKey=true\n")).RequiresSingleKey);
        Assert.IsFalse(TemplateConfig.Load(temp.File("b.tt.config", "RequiresSingleKey=false\n")).RequiresSingleKey);
        Assert.IsFalse(TemplateConfig.Load(temp.File("c.tt.config", "TableOnly=true\n")).RequiresSingleKey);
    }

    [TestMethod]
    public void PrimaryKeyShapeSatisfies_accepts_an_int_guid_or_text_key_only_when_a_single_key_is_required()
    {
        var single = new TemplateConfig { RequiresSingleKey = true };
        var unrestricted = new TemplateConfig();

        foreach (var shape in new[] { PrimaryKeyShape.SingleInt, PrimaryKeyShape.SingleUniqueIdentifier, PrimaryKeyShape.SingleText })
            Assert.IsTrue(single.PrimaryKeyShapeSatisfies(shape), shape.ToString());
        foreach (var shape in new[] { PrimaryKeyShape.SingleOther, PrimaryKeyShape.Composite, PrimaryKeyShape.None })
            Assert.IsFalse(single.PrimaryKeyShapeSatisfies(shape), shape.ToString());

        foreach (var shape in Enum.GetValues<PrimaryKeyShape>())
            Assert.IsTrue(unrestricted.PrimaryKeyShapeSatisfies(shape), shape.ToString());
    }

    [TestMethod]
    public void The_shipped_configs_that_read_the_key_form_from_KeyType_require_a_single_key()
    {
        foreach (string name in new[] { "TS_Model", "TS_Service", "TS_Component", "TS_DetailMasterComponent", "API_Crud", "WinUI3_MasterScreen", "WinUI3_DetailScreen", "WinUI3_DetailMasterScreen" })
            Assert.IsTrue(TemplateConfig.Load(Repo.Template(name + "_v1.tt.config")).RequiresSingleKey, name);
        // Junction/child-grid data access goes by the two FK columns or the child's own FK, never the table's
        // own primary key shape, so these are deliberately unrestricted.
        Assert.IsFalse(TemplateConfig.Load(Repo.Template("SP_Junction_v1.tt.config")).RequiresSingleKey);
        Assert.IsFalse(TemplateConfig.Load(Repo.Template("WinUI3_JunctionEditor_v1.tt.config")).RequiresSingleKey);
    }

    [TestMethod]
    public void RequiresNotNameActiveTable_is_read()
    {
        using var temp = new TempFolder();

        Assert.IsTrue(TemplateConfig.Load(temp.File("a.tt.config", "RequiresNotNameActiveTable=true\n")).RequiresNotNameActiveTable);
        Assert.IsFalse(TemplateConfig.Load(temp.File("b.tt.config", "TableOnly=true\n")).RequiresNotNameActiveTable);
    }

    [TestMethod]
    public void The_shipped_configs_refuse_a_name_active_table_only_where_the_screen_cannot_handle_it()
    {
        // the ASP.NET API and every UI stack's screens handle a name/active table (NameActiveCrudApi, NameActiveCrudScreen, the list through GetAll)
        Assert.IsFalse(TemplateConfig.Load(Repo.Template("TS_Service_v1.tt.config")).RequiresNotNameActiveTable);
        Assert.IsFalse(TemplateConfig.Load(Repo.Template("TS_Component_v1.tt.config")).RequiresNotNameActiveTable);
        Assert.IsFalse(TemplateConfig.Load(Repo.Template("TS_DetailMasterComponent_v1.tt.config")).RequiresNotNameActiveTable);
        Assert.IsFalse(TemplateConfig.Load(Repo.Template("API_Crud_v1.tt.config")).RequiresNotNameActiveTable);
        foreach (string stem in new[] { "WinUI3_MasterScreen", "WinUI3_DetailScreen", "WinUI3_DetailMasterScreen", "UNO_MasterScreen", "UNO_DetailScreen", "UNO_DetailMasterScreen",
            "UNO_Client", "UNO_Model", "BLZ_Client", "BLZ_Model", "BLZ_Page", "BLZ_Test" })
            Assert.IsFalse(TemplateConfig.Load(Repo.Template(stem + "_v1.tt.config")).RequiresNotNameActiveTable, stem);
        // a single row of a name/active table, and the OData pages, stay with the plain tables
        foreach (string stem in new[] { "WinUI3_SingleRowScreen", "UNO_SingleRowScreen", "UNO_ODataPage", "BLZ_ODataPage" })
            Assert.IsTrue(TemplateConfig.Load(Repo.Template(stem + "_v1.tt.config")).RequiresNotNameActiveTable, stem);
        // TS_Model has no API dependency at all (it's just the interface shape); junction/child-grid
        // templates go through their own dedicated endpoints, not a plain getAll() -- neither is restricted.
        Assert.IsFalse(TemplateConfig.Load(Repo.Template("TS_Model_v1.tt.config")).RequiresNotNameActiveTable);
        Assert.IsFalse(TemplateConfig.Load(Repo.Template("TS_JunctionComponent_v1.tt.config")).RequiresNotNameActiveTable);
        Assert.IsFalse(TemplateConfig.Load(Repo.Template("WinUI3_JunctionEditor_v1.tt.config")).RequiresNotNameActiveTable);
    }
}

[TestClass]
public class TemplateConfigRefuseTests
{
    // Refuse(TableModel) is what the CLI's own pre-check calls (Program.cs used to run these five checks by
    // hand, one per restriction); AppliesTo/PrimaryKeyShapeSatisfies above already cover the underlying
    // rules, so these just prove Refuse explains each one in a way a person reads sensibly.

    [TestMethod]
    public void Refuse_explains_a_missing_primary_key()
    {
        var config = new TemplateConfig { RequiresPrimaryKey = true };
        var table = Sample.Table("Thing", [Sample.Column("Label", System.Data.SqlDbType.NVarChar)]); // no PK

        StringAssert.Contains(config.Refuse(table)!, "requires a primary key");
    }

    [TestMethod]
    public void A_template_for_one_database_refuses_a_table_of_another()
    {
        var config = new TemplateConfig { Dialects = ["SqlServer"] };
        var columns = new List<ColumnModel> { Sample.Column("ThingId", System.Data.SqlDbType.Int, primaryKey: true) };
        var sqlServerTable = Sample.Table("Thing", columns);
        var postgresTable = new TableModel
        {
            SchemaName = "public", TableName = "Thing", QuotedName = "\"public\".\"Thing\"", Dialect = SqlDialect.PostgreSql,
            Columns = columns, PrimaryKeyColumns = columns, ForeignKeys = [], ChildForeignKeys = []
        };

        Assert.IsNull(config.Refuse(sqlServerTable));
        StringAssert.Contains(config.Refuse(postgresTable)!, "is not written for");
    }

    [TestMethod]
    public void The_earlier_keys_are_still_read_and_reported_as_legacy()
    {
        using var temp = new TempFolder();
        var config = TemplateConfig.Load(temp.File("a.tt.config", "RequiresPrimaryKey=false\nTableOnly=true\nDatabaseOnly=true\nSqlServerOnly=true\nRequiresSingleKey=true\n"));

        Assert.AreEqual(TemplateScope.Database, config.Scope);
        Assert.IsFalse(config.RequiresPrimaryKey);
        Assert.IsTrue(config.RequiresSingleKey);
        CollectionAssert.AreEqual(new[] { "SqlServer" }, config.Dialects.ToArray(), "SqlServerOnly is Dialects=SqlServer");
        CollectionAssert.AreEquivalent(new[] { "RequiresPrimaryKey", "TableOnly", "DatabaseOnly", "SqlServerOnly", "RequiresSingleKey" }, config.LegacyKeys.ToArray());
        Assert.IsEmpty(config.UnknownKeys);
    }

    [TestMethod]
    public void Scope_and_Requires_are_read()
    {
        using var temp = new TempFolder();
        var none = TemplateConfig.Load(temp.File("a.tt.config", "Scope=None\n"));
        var database = TemplateConfig.Load(temp.File("b.tt.config", "Scope=Database\n"));
        var all = TemplateConfig.Load(temp.File("c.tt.config", "Requires=PrimaryKey,SingleKey,NotNameActive,JunctionTable,ChildTables,AuditTable\n"));
        var nothing = TemplateConfig.Load(temp.File("d.tt.config", "Requires=\n"));

        Assert.IsTrue(none.NoDatabase);
        Assert.IsFalse(none.DatabaseOnly);
        Assert.IsTrue(database.DatabaseOnly);
        Assert.IsTrue(all.RequiresPrimaryKey && all.RequiresSingleKey && all.RequiresNotNameActiveTable && all.RequiresJunctionTable && all.RequiresChildTables && all.RequiresAuditTable);
        Assert.IsFalse(nothing.RequiresPrimaryKey || nothing.RequiresSingleKey || nothing.RequiresNotNameActiveTable);
        Assert.IsEmpty(all.UnknownKeys);
    }

    [TestMethod]
    public void A_mistyped_key_or_value_is_reported_not_silently_ignored()
    {
        using var temp = new TempFolder();
        var config = TemplateConfig.Load(temp.File("a.tt.config", "Requries=PrimaryKey\nRequires=PrimaryKey,SingleKye\nScope=Tabel\nRequiredPrimaryKeyShape=SingleInt\nOutputFolder.React=src\n"));

        CollectionAssert.AreEquivalent(new[] { "Requries", "Requires=SingleKye", "Scope=Tabel", "RequiredPrimaryKeyShape" }, config.UnknownKeys.ToArray());
    }

    [TestMethod]
    public void No_shipped_config_has_an_unknown_or_earlier_format_key()
    {
        // The earlier keys still work for a template someone wrote, but the shipped ones are in the current format, and a typo in one of them would drop a restriction without a word.
        foreach (string path in Directory.GetFiles(Repo.TemplatesDirectory, "*.tt.config"))
        {
            var config = TemplateConfig.Load(path);
            Assert.IsEmpty(config.UnknownKeys, Path.GetFileName(path) + " has an unknown key: " + string.Join(", ", config.UnknownKeys));
            Assert.IsEmpty(config.LegacyKeys, Path.GetFileName(path) + " uses an earlier key: " + string.Join(", ", config.LegacyKeys));
        }
    }

    [TestMethod]
    public void Refuse_explains_a_non_junction_table()
    {
        var config = new TemplateConfig { RequiresJunctionTable = true };

        StringAssert.Contains(config.Refuse(Sample.DonateLeave())!, "junction/bridge table");
    }

    [TestMethod]
    public void Refuse_explains_a_table_with_no_children()
    {
        var config = new TemplateConfig { RequiresChildTables = true };

        StringAssert.Contains(config.Refuse(Sample.DonateLeave())!, "foreign key pointing back at it");
    }

    [TestMethod]
    public void Refuse_explains_a_wrong_primary_key_shape()
    {
        var config = new TemplateConfig { RequiresSingleKey = true };

        StringAssert.Contains(config.Refuse(Sample.CompositeKey())!, "Composite");
        Assert.IsNull(config.Refuse(Sample.NaturalKey()), "a text key is one of the keys KeyType describes");
    }

    [TestMethod]
    public void Refuse_explains_a_name_active_table()
    {
        var config = new TemplateConfig { RequiresNotNameActiveTable = true };

        StringAssert.Contains(config.Refuse(Sample.DepartmentTeam())!, "NameActiveRepo");
    }

    [TestMethod]
    public void Refuse_is_null_when_every_restriction_is_satisfied()
    {
        Assert.IsNull(new TemplateConfig().Refuse(Sample.DonateLeave()));
    }
}

[TestClass]
public class TemplateInfoAppliesToTests
{
    private static TemplateInfo Template(bool requiresSingleKey) => new()
    {
        FilePath = "X.tt",
        Name = "X",
        Config = new TemplateConfig { RequiresSingleKey = requiresSingleKey }
    };

    [TestMethod]
    public void A_template_that_needs_a_single_key_does_not_apply_to_a_composite_or_other_key_table()
    {
        var t = Template(requiresSingleKey: true);

        Assert.IsTrue(t.AppliesTo(tableHasPrimaryKey: true, primaryKeyShape: PrimaryKeyShape.SingleInt));
        Assert.IsTrue(t.AppliesTo(tableHasPrimaryKey: true, primaryKeyShape: PrimaryKeyShape.SingleUniqueIdentifier));
        Assert.IsTrue(t.AppliesTo(tableHasPrimaryKey: true, primaryKeyShape: PrimaryKeyShape.SingleText));
        Assert.IsFalse(t.AppliesTo(tableHasPrimaryKey: true, primaryKeyShape: PrimaryKeyShape.Composite));
        Assert.IsFalse(t.AppliesTo(tableHasPrimaryKey: true, primaryKeyShape: PrimaryKeyShape.SingleOther));
    }

    [TestMethod]
    public void A_template_with_no_key_shape_restriction_applies_regardless_of_shape()
    {
        var t = Template(requiresSingleKey: false);

        foreach (var shape in Enum.GetValues<PrimaryKeyShape>())
            Assert.IsTrue(t.AppliesTo(tableHasPrimaryKey: true, primaryKeyShape: shape), shape.ToString());
    }

    [TestMethod]
    public void A_template_that_requires_not_name_active_does_not_apply_to_a_name_active_table()
    {
        var t = new TemplateInfo { FilePath = "X.tt", Name = "X", Config = new TemplateConfig { RequiresNotNameActiveTable = true } };

        Assert.IsTrue(t.AppliesTo(tableHasPrimaryKey: true, isNameActiveTable: false));
        Assert.IsFalse(t.AppliesTo(tableHasPrimaryKey: true, isNameActiveTable: true));
    }

    [TestMethod]
    public void A_template_that_does_not_require_not_name_active_applies_either_way()
    {
        var t = new TemplateInfo { FilePath = "X.tt", Name = "X", Config = new TemplateConfig() };

        Assert.IsTrue(t.AppliesTo(tableHasPrimaryKey: true, isNameActiveTable: false));
        Assert.IsTrue(t.AppliesTo(tableHasPrimaryKey: true, isNameActiveTable: true));
    }
}

[TestClass]
public class TemplateCatalogTests
{
    [TestMethod]
    [DataRow("SP_Save_v2", "SP_Save", 2, true)]
    [DataRow("SP_Save", "SP_Save", 0, false)]
    [DataRow("SP_Save_v10", "SP_Save", 10, true)]
    [DataRow("TS_Model_v1", "TS_Model", 1, true)]
    [DataRow("SP_Version_Notes", "SP_Version_Notes", 0, false)]
    public void ParseName_splits_the_version_suffix(string stem, string baseName, int version, bool hasSuffix)
    {
        var parsed = TemplateCatalog.ParseName(stem);

        Assert.AreEqual(baseName, parsed.BaseName);
        Assert.AreEqual(version, parsed.Version);
        Assert.AreEqual(hasSuffix, parsed.HasVersionSuffix);
    }

    private static TempFolder FolderWithVersions()
    {
        var temp = new TempFolder();
        foreach (string name in new[] { "SP_A.tt", "SP_A_v1.tt", "SP_A_v2.tt", "SP_B_v1.tt", "Flat.tt" })
            temp.File(name, "");
        return temp;
    }

    [TestMethod]
    public void Only_the_newest_version_of_each_template_is_offered()
    {
        using var temp = FolderWithVersions();

        var offered = TemplateCatalog.Discover(temp.Path).Select(t => t.FileStem).OrderBy(s => s).ToList();

        CollectionAssert.AreEqual(new[] { "Flat", "SP_A_v2", "SP_B_v1" }, offered);
    }

    [TestMethod]
    public void The_management_list_still_shows_old_versions_marked_as_superseded()
    {
        using var temp = FolderWithVersions();

        var all = TemplateCatalog.DiscoverAll(temp.Path);

        Assert.HasCount(5, all);
        Assert.AreEqual(2, all.Single(t => t.FileStem == "SP_A_v1").SupersededByVersion);
        Assert.IsFalse(all.Single(t => t.FileStem == "SP_A_v2").IsSuperseded);
    }

    [TestMethod]
    public void A_name_without_a_version_finds_the_latest_and_a_versioned_name_pins_that_file()
    {
        using var temp = FolderWithVersions();

        Assert.AreEqual("SP_A_v2", TemplateCatalog.FindByName(temp.Path, "SP_A.tt")!.FileStem);
        Assert.AreEqual("SP_A_v1", TemplateCatalog.FindByName(temp.Path, "SP_A_v1.tt")!.FileStem);
        Assert.IsNull(TemplateCatalog.FindByName(temp.Path, "SP_Nothing.tt"));
    }

    [TestMethod]
    public void The_group_before_the_first_underscore_is_the_submenu()
    {
        using var temp = FolderWithVersions();

        var all = TemplateCatalog.Discover(temp.Path);

        Assert.AreEqual("SP", all.Single(t => t.FileStem == "SP_B_v1").SubmenuGroup);
        Assert.IsNull(all.Single(t => t.FileStem == "Flat").SubmenuGroup);
    }

    [TestMethod]
    public void The_shipped_templates_are_all_offered_with_their_groups()
    {
        var offered = TemplateCatalog.Discover(Repo.TemplatesDirectory);

        var groups = offered.GroupBy(t => t.SubmenuGroup).ToDictionary(g => g.Key!, g => g.Count());
        Assert.AreEqual(20, groups["SP"]);
        Assert.AreEqual(20, groups["API"]);
        Assert.AreEqual(27, groups["CS"]);
        Assert.AreEqual(23, groups["TS"]);
        Assert.AreEqual(18, groups["WinUI3"]);
        Assert.AreEqual(2, groups["FS"]);
        Assert.AreEqual(5, groups["MD"]);
        Assert.AreEqual(22, groups["TSX"]);
        Assert.AreEqual(1, groups["PROTO"]);
        Assert.AreEqual(23, groups["RS"]);
        Assert.AreEqual(21, groups["BLZ"]);
        Assert.AreEqual(15, groups["PY"]);
        Assert.IsTrue(offered.All(t => !t.IsSuperseded));
    }
}

[TestClass]
public class DefaultAssetSeederTests
{
    // The CLI assembly carries the embedded copies of every shipped template; the seeder is handed it, as at run time.
    private static Assembly Shipped => typeof(CodeGenNew.Cli.Program).Assembly;

    private static string Hash(string text) => Encoding.UTF8.GetBytes(text).Sha256Hex();

    private static IReadOnlyList<SeedNotice> Seed(TempFolder temp) =>
        DefaultAssetSeeder.EnsureDefaultAssets(
            Path.Combine(temp.Path, "Templates"), Path.Combine(temp.Path, "SpecialLogicColumns.config"), Path.Combine(temp.Path, "Output"), Shipped);

    [TestMethod]
    public void Every_template_and_config_in_the_repository_is_seeded_byte_for_byte()
    {
        // Catches a new template that was added to Templates\ but not to the seeder's list (it would never reach a user).
        using var temp = new TempFolder();

        Seed(temp);

        var shipped = Directory.GetFiles(Repo.TemplatesDirectory, "*.tt*").Select(Path.GetFileName).OrderBy(n => n).ToList();
        Assert.IsGreaterThanOrEqualTo(28, shipped.Count, "expected at least 14 templates and 14 configs");
        foreach (string? name in shipped)
        {
            string seeded = Path.Combine(temp.Path, "Templates", name!);
            Assert.IsTrue(File.Exists(seeded), $"{name} is in Templates\\ but was not seeded (add it to DefaultAssetSeeder's list)");
            CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(Repo.TemplatesDirectory, name!)), File.ReadAllBytes(seeded), $"{name} differs from the repository copy");
        }
        Assert.IsTrue(File.Exists(Path.Combine(temp.Path, "SpecialLogicColumns.config")));
        Assert.IsTrue(Directory.Exists(Path.Combine(temp.Path, "Output")));
    }

    [TestMethod]
    public void A_second_run_changes_nothing()
    {
        using var temp = new TempFolder();
        Seed(temp);

        var notices = Seed(temp);

        Assert.IsEmpty(notices);
    }

    [TestMethod]
    public void A_deleted_file_is_created_again()
    {
        using var temp = new TempFolder();
        Seed(temp);
        File.Delete(Path.Combine(temp.Path, "Templates", "SP_Save_v1.tt.config"));

        var notices = Seed(temp);

        Assert.AreEqual(SeedOutcome.Created, notices.Single(n => n.FileName == "SP_Save_v1.tt.config").Outcome);
    }

    [TestMethod]
    public void An_untouched_older_copy_is_refreshed_in_place()
    {
        using var temp = new TempFolder();
        Seed(temp);
        string path = Path.Combine(temp.Path, "Templates", "SP_Save_v1.tt.config");
        // pretend the seeder itself wrote an older version: the file and the seeder's record agree
        File.WriteAllText(path, "older shipped text");
        string statePath = Path.Combine(temp.Path, "Templates", "SeededAssets.config");
        var state = File.ReadAllLines(statePath).Select(l => l.StartsWith("SP_Save_v1.tt.config|") ? "SP_Save_v1.tt.config|" + Hash("older shipped text") : l);
        File.WriteAllLines(statePath, state);

        var notices = Seed(temp);

        Assert.AreEqual(SeedOutcome.Refreshed, notices.Single(n => n.FileName == "SP_Save_v1.tt.config").Outcome);
        CollectionAssert.AreEqual(File.ReadAllBytes(Repo.Template("SP_Save_v1.tt.config")), File.ReadAllBytes(path));
    }

    [TestMethod]
    public void A_customized_file_is_never_overwritten_and_the_shipped_copy_goes_beside_it()
    {
        using var temp = new TempFolder();
        Seed(temp);
        string path = Path.Combine(temp.Path, "Templates", "SP_Save_v1.tt");
        File.WriteAllText(path, "my own version");

        var notices = Seed(temp);

        Assert.AreEqual(SeedOutcome.KeptCustomized, notices.Single(n => n.FileName == "SP_Save_v1.tt").Outcome);
        Assert.AreEqual("my own version", File.ReadAllText(path));
        CollectionAssert.AreEqual(File.ReadAllBytes(Repo.Template("SP_Save_v1.tt")), File.ReadAllBytes(path + ".new"));
    }
}
