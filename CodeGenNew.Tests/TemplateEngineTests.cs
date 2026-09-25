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
        Assert.IsTrue(config.TableOnly);
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
        Assert.IsFalse(config.TableOnly);
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
    public void RequiredPrimaryKeyShape_is_read_and_unknown_values_are_treated_as_unset()
    {
        using var temp = new TempFolder();

        Assert.AreEqual(PrimaryKeyRequirement.SingleInt, TemplateConfig.Load(temp.File("a.tt.config", "RequiredPrimaryKeyShape=SingleInt\n")).RequiredPrimaryKeyShape);
        Assert.AreEqual(PrimaryKeyRequirement.SingleIntOrGuid, TemplateConfig.Load(temp.File("b.tt.config", "RequiredPrimaryKeyShape=singleintorguid\n")).RequiredPrimaryKeyShape);
        Assert.IsNull(TemplateConfig.Load(temp.File("c.tt.config", "RequiredPrimaryKeyShape=NotARealShape\n")).RequiredPrimaryKeyShape);
        Assert.IsNull(TemplateConfig.Load(temp.File("d.tt.config", "TableOnly=true\n")).RequiredPrimaryKeyShape);
    }

    [TestMethod]
    public void PrimaryKeyShapeSatisfies_matches_each_tier()
    {
        var singleColumn = new TemplateConfig { RequiredPrimaryKeyShape = PrimaryKeyRequirement.SingleColumn };
        var singleIntOrGuid = new TemplateConfig { RequiredPrimaryKeyShape = PrimaryKeyRequirement.SingleIntOrGuid };
        var singleInt = new TemplateConfig { RequiredPrimaryKeyShape = PrimaryKeyRequirement.SingleInt };
        var unrestricted = new TemplateConfig();

        foreach (var shape in new[] { PrimaryKeyShape.SingleInt, PrimaryKeyShape.SingleUniqueIdentifier, PrimaryKeyShape.SingleOther })
            Assert.IsTrue(singleColumn.PrimaryKeyShapeSatisfies(shape), shape.ToString());
        Assert.IsFalse(singleColumn.PrimaryKeyShapeSatisfies(PrimaryKeyShape.Composite));
        Assert.IsFalse(singleColumn.PrimaryKeyShapeSatisfies(PrimaryKeyShape.None));

        Assert.IsTrue(singleIntOrGuid.PrimaryKeyShapeSatisfies(PrimaryKeyShape.SingleInt));
        Assert.IsTrue(singleIntOrGuid.PrimaryKeyShapeSatisfies(PrimaryKeyShape.SingleUniqueIdentifier));
        Assert.IsFalse(singleIntOrGuid.PrimaryKeyShapeSatisfies(PrimaryKeyShape.SingleOther));
        Assert.IsFalse(singleIntOrGuid.PrimaryKeyShapeSatisfies(PrimaryKeyShape.Composite));

        Assert.IsTrue(singleInt.PrimaryKeyShapeSatisfies(PrimaryKeyShape.SingleInt));
        Assert.IsFalse(singleInt.PrimaryKeyShapeSatisfies(PrimaryKeyShape.SingleUniqueIdentifier));
        Assert.IsFalse(singleInt.PrimaryKeyShapeSatisfies(PrimaryKeyShape.SingleOther));

        foreach (var shape in Enum.GetValues<PrimaryKeyShape>())
            Assert.IsTrue(unrestricted.PrimaryKeyShapeSatisfies(shape), shape.ToString());
    }

    [TestMethod]
    public void The_shipped_configs_restrict_the_key_shape_they_actually_need()
    {
        Assert.AreEqual(PrimaryKeyRequirement.SingleColumn, TemplateConfig.Load(Repo.Template("TS_Model_v1.tt.config")).RequiredPrimaryKeyShape);
        Assert.AreEqual(PrimaryKeyRequirement.SingleIntOrGuid, TemplateConfig.Load(Repo.Template("TS_Service_v1.tt.config")).RequiredPrimaryKeyShape);
        Assert.AreEqual(PrimaryKeyRequirement.SingleIntOrGuid, TemplateConfig.Load(Repo.Template("TS_Component_v1.tt.config")).RequiredPrimaryKeyShape);
        Assert.AreEqual(PrimaryKeyRequirement.SingleIntOrGuid, TemplateConfig.Load(Repo.Template("TS_DetailMasterComponent_v1.tt.config")).RequiredPrimaryKeyShape);
        Assert.AreEqual(PrimaryKeyRequirement.SingleInt, TemplateConfig.Load(Repo.Template("API_Crud_v1.tt.config")).RequiredPrimaryKeyShape);
        Assert.AreEqual(PrimaryKeyRequirement.SingleInt, TemplateConfig.Load(Repo.Template("WinUI3_MasterScreen_v1.tt.config")).RequiredPrimaryKeyShape);
        Assert.AreEqual(PrimaryKeyRequirement.SingleInt, TemplateConfig.Load(Repo.Template("WinUI3_DetailScreen_v1.tt.config")).RequiredPrimaryKeyShape);
        Assert.AreEqual(PrimaryKeyRequirement.SingleInt, TemplateConfig.Load(Repo.Template("WinUI3_DetailMasterScreen_v1.tt.config")).RequiredPrimaryKeyShape);
        // Junction/child-grid data access goes by the two FK columns or the child's own FK, never the table's
        // own primary key shape, so these are deliberately unrestricted.
        Assert.IsNull(TemplateConfig.Load(Repo.Template("SP_Junction_v1.tt.config")).RequiredPrimaryKeyShape);
        Assert.IsNull(TemplateConfig.Load(Repo.Template("WinUI3_JunctionEditor_v1.tt.config")).RequiredPrimaryKeyShape);
    }

    [TestMethod]
    public void RequiresNotNameActiveTable_is_read()
    {
        using var temp = new TempFolder();

        Assert.IsTrue(TemplateConfig.Load(temp.File("a.tt.config", "RequiresNotNameActiveTable=true\n")).RequiresNotNameActiveTable);
        Assert.IsFalse(TemplateConfig.Load(temp.File("b.tt.config", "TableOnly=true\n")).RequiresNotNameActiveTable);
    }

    [TestMethod]
    public void The_shipped_configs_that_assume_a_plain_getAll_style_backend_refuse_name_active_tables()
    {
        Assert.IsTrue(TemplateConfig.Load(Repo.Template("TS_Service_v1.tt.config")).RequiresNotNameActiveTable);
        Assert.IsTrue(TemplateConfig.Load(Repo.Template("TS_Component_v1.tt.config")).RequiresNotNameActiveTable);
        Assert.IsTrue(TemplateConfig.Load(Repo.Template("TS_DetailMasterComponent_v1.tt.config")).RequiresNotNameActiveTable);
        Assert.IsTrue(TemplateConfig.Load(Repo.Template("API_Crud_v1.tt.config")).RequiresNotNameActiveTable);
        Assert.IsTrue(TemplateConfig.Load(Repo.Template("WinUI3_MasterScreen_v1.tt.config")).RequiresNotNameActiveTable);
        Assert.IsTrue(TemplateConfig.Load(Repo.Template("WinUI3_DetailScreen_v1.tt.config")).RequiresNotNameActiveTable);
        Assert.IsTrue(TemplateConfig.Load(Repo.Template("WinUI3_DetailMasterScreen_v1.tt.config")).RequiresNotNameActiveTable);
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
        var config = new TemplateConfig { RequiredPrimaryKeyShape = PrimaryKeyRequirement.SingleInt };

        StringAssert.Contains(config.Refuse(Sample.NaturalKey())!, "SingleInt primary key");
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
    private static TemplateInfo Template(PrimaryKeyRequirement? shape) => new()
    {
        FilePath = "X.tt",
        Name = "X",
        Config = new TemplateConfig { RequiredPrimaryKeyShape = shape }
    };

    [TestMethod]
    public void A_template_that_needs_a_single_int_or_guid_key_does_not_apply_to_a_composite_or_natural_key_table()
    {
        var t = Template(PrimaryKeyRequirement.SingleIntOrGuid);

        Assert.IsTrue(t.AppliesTo(tableHasPrimaryKey: true, isView: false, primaryKeyShape: PrimaryKeyShape.SingleInt));
        Assert.IsTrue(t.AppliesTo(tableHasPrimaryKey: true, isView: false, primaryKeyShape: PrimaryKeyShape.SingleUniqueIdentifier));
        Assert.IsFalse(t.AppliesTo(tableHasPrimaryKey: true, isView: false, primaryKeyShape: PrimaryKeyShape.Composite));
        Assert.IsFalse(t.AppliesTo(tableHasPrimaryKey: true, isView: false, primaryKeyShape: PrimaryKeyShape.SingleOther));
    }

    [TestMethod]
    public void A_template_with_no_key_shape_restriction_applies_regardless_of_shape()
    {
        var t = Template(shape: null);

        foreach (var shape in Enum.GetValues<PrimaryKeyShape>())
            Assert.IsTrue(t.AppliesTo(tableHasPrimaryKey: true, isView: false, primaryKeyShape: shape), shape.ToString());
    }

    [TestMethod]
    public void A_template_that_requires_not_name_active_does_not_apply_to_a_name_active_table()
    {
        var t = new TemplateInfo { FilePath = "X.tt", Name = "X", Config = new TemplateConfig { RequiresNotNameActiveTable = true } };

        Assert.IsTrue(t.AppliesTo(tableHasPrimaryKey: true, isView: false, isNameActiveTable: false));
        Assert.IsFalse(t.AppliesTo(tableHasPrimaryKey: true, isView: false, isNameActiveTable: true));
    }

    [TestMethod]
    public void A_template_that_does_not_require_not_name_active_applies_either_way()
    {
        var t = new TemplateInfo { FilePath = "X.tt", Name = "X", Config = new TemplateConfig() };

        Assert.IsTrue(t.AppliesTo(tableHasPrimaryKey: true, isView: false, isNameActiveTable: false));
        Assert.IsTrue(t.AppliesTo(tableHasPrimaryKey: true, isView: false, isNameActiveTable: true));
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
        Assert.AreEqual(8, groups["SP"]);
        Assert.AreEqual(2, groups["API"]);
        Assert.AreEqual(3, groups["CS"]);
        Assert.AreEqual(5, groups["TS"]);
        Assert.AreEqual(4, groups["WinUI3"]);
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
