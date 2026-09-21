using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

[TestClass]
public class OutputFileNamingTests
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
        Assert.AreEqual(expected, OutputFileNaming.BuildFileName(Template(template), table));
    }

    [TestMethod]
    public void OutputName_replaces_the_whole_name_and_fills_in_the_table()
    {
        Assert.AreEqual("E_DonateLeaveApi.cs", OutputFileNaming.BuildFileName(Template("API_Crud", "{Table}Api.cs"), "E_DonateLeave"));
    }

    [TestMethod]
    public void OutputName_can_never_be_a_path()
    {
        Assert.AreEqual("x.cs", OutputFileNaming.BuildFileName(Template("API_Crud", "../../{Table}.cs"), "x"));
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
        Assert.AreEqual(7, groups["SP"]);
        Assert.AreEqual(1, groups["API"]);
        Assert.AreEqual(3, groups["CS"]);
        Assert.AreEqual(3, groups["TS"]);
        Assert.IsTrue(offered.All(t => !t.IsSuperseded));
    }
}

[TestClass]
public class DefaultAssetSeederTests
{
    // The CLI assembly carries the embedded copies of every shipped template; the seeder is handed it, as at run time.
    private static Assembly Shipped => typeof(CodeGenNew.Cli.Program).Assembly;

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

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
