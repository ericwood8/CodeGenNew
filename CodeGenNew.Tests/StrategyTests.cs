using System.Data;
using CodeGenNew.Cli;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The technology choices of the Generate All dialog and <c>codegen generate</c>: the rule table, the project it writes and the tables each shape is given. </summary>
[TestClass]
public class StrategyTests
{
    private static readonly SqlDialect[] Databases = Enum.GetValues<SqlDialect>();

    private static bool ShouldBeAccepted(SqlDialect database, StrategyAccess access, StrategyStyle style, StrategyLanguage language) => access switch
    {
        StrategyAccess.Routines => language == StrategyLanguage.CSharp && database != SqlDialect.Sqlite,
        StrategyAccess.Ef => language == StrategyLanguage.CSharp,
        _ => language switch
        {
            StrategyLanguage.Rust => database != SqlDialect.SqlServer && style == StrategyStyle.Rest,
            StrategyLanguage.Python => style == StrategyStyle.Rest,
            _ => false
        }
    };

    [TestMethod]
    public void Every_combination_is_refused_with_a_reason_or_turned_into_settings_the_generator_accepts()
    {
        int accepted = 0, refused = 0;
        foreach (var database in Databases)
            foreach (var ui in Enum.GetValues<StrategyUi>().Where(u => u != StrategyUi.WinUI3))
                foreach (var access in Enum.GetValues<StrategyAccess>())
                    foreach (var style in Enum.GetValues<StrategyStyle>())
                        foreach (var language in Enum.GetValues<StrategyLanguage>())
                        {
                            var choice = new StrategyChoice(database, ui, access, style, language);
                            string label = $"{database} {ui} {access} {style} {language}";
                            string? refusal = StrategyRules.Refusal(choice);
                            if (!ShouldBeAccepted(database, access, style, language))
                            {
                                Assert.IsNotNull(refusal, $"{label} should be refused");
                                Assert.AreEqual(refusal, Assert.ThrowsExactly<InvalidOperationException>(() => StrategyRules.ToSettings(choice, "Acme", "acme")).Message);
                                refused++;
                                continue;
                            }

                            Assert.IsNull(refusal, $"{label} should be accepted");
                            var settings = StrategyRules.ToSettings(choice, "Acme", "acme");
                            foreach (string key in settings.Values.Keys)
                                CollectionAssert.Contains(ProjectSettings.Keys.ToList(), key, $"{label}: {key} is not a project setting");
                            foreach (string stack in settings.Stacks)
                                Assert.IsNotNull(EssentialsCatalog.FindStack(stack), $"{label}: stack {stack}");
                            Assert.AreEqual(style == StrategyStyle.OData, settings.OData, label);
                            if (access != StrategyAccess.OwnSql)
                                Assert.AreEqual(access == StrategyAccess.Ef ? AccessMode.Ef : AccessMode.Routines, settings.AccessModeFor(database), label);
                            accepted++;
                        }
        Assert.IsGreaterThan(0, accepted);
        Assert.IsGreaterThan(0, refused);
    }

    [TestMethod]
    public void WinUI3_takes_no_api_choice_and_is_complete_with_the_ui_alone()
    {
        foreach (var database in Databases)
        {
            var alone = new StrategyChoice(database, StrategyUi.WinUI3);
            Assert.IsNull(StrategyRules.Refusal(alone));
            Assert.AreEqual(0, StrategyRules.Missing(alone).Count);
            var settings = StrategyRules.ToSettings(alone, "Acme", "acme");
            CollectionAssert.AreEqual(new[] { "WinUI3" }, settings.Stacks);
            Assert.IsFalse(settings.Values.ContainsKey("AccessMode") || settings.Values.ContainsKey("OData"));

            Assert.AreEqual(StrategyRules.WinUi3NeedsNoApi, StrategyRules.Refusal(alone with { Access = StrategyAccess.Ef }));
            Assert.AreEqual(StrategyRules.WinUi3NeedsNoApi, StrategyRules.Refusal(alone with { Style = StrategyStyle.Rest }));
            Assert.AreEqual(StrategyRules.WinUi3NeedsNoApi, StrategyRules.Refusal(alone with { Language = StrategyLanguage.CSharp }));
        }

        var picked = new StrategyChoice(SqlDialect.SqlServer, StrategyUi.React, StrategyAccess.Ef, StrategyStyle.OData, StrategyLanguage.CSharp);
        var narrowed = StrategyRules.Narrow(picked with { Ui = StrategyUi.WinUI3 });
        Assert.IsTrue(narrowed.Access is null && narrowed.Style is null && narrowed.Language is null, "picking WinUI3 clears the API choices");
    }

    [TestMethod]
    public void The_rules_name_why_a_pair_does_not_go_together()
    {
        var rust = new StrategyChoice(SqlDialect.PostgreSql, StrategyUi.React, StrategyAccess.OwnSql, StrategyStyle.OData, StrategyLanguage.Rust);
        Assert.AreEqual(StrategyRules.ODataIsCSharpOnly, StrategyRules.Refusal(rust));
        Assert.AreEqual(StrategyRules.SqliteHasNoRoutines, StrategyRules.Refusal(new StrategyChoice(SqlDialect.Sqlite, StrategyUi.React, StrategyAccess.Routines)));
        Assert.AreEqual(StrategyRules.RustCannotReadSqlServer, StrategyRules.Refusal(new StrategyChoice(SqlDialect.SqlServer, Language: StrategyLanguage.Rust)));
        Assert.AreEqual(StrategyRules.RustAndPythonWriteOwnSql, StrategyRules.Refusal(new StrategyChoice(SqlDialect.MySql, StrategyUi.Uno, StrategyAccess.Ef, Language: StrategyLanguage.Python)));
        Assert.AreEqual(StrategyRules.CSharpUsesRoutinesOrEf, StrategyRules.Refusal(new StrategyChoice(SqlDialect.MySql, StrategyUi.Uno, StrategyAccess.OwnSql, Language: StrategyLanguage.CSharp)));
        Assert.IsNull(StrategyRules.Refusal(new StrategyChoice(SqlDialect.Sqlite)), "nothing picked yet is not a conflict");
    }

    [TestMethod]
    public void Each_list_is_narrowed_by_the_picks_before_it_and_never_ends_with_nothing_to_pick()
    {
        foreach (var database in Databases)
        {
            var start = new StrategyChoice(database, StrategyUi.Blazor);
            var access = StrategyRules.AccessOptions(start).Where(o => o.IsOffered).ToList();
            Assert.AreEqual(database != SqlDialect.Sqlite, access.Any(o => o.Value == StrategyAccess.Routines), $"{database}: routines");
            Assert.IsTrue(StrategyRules.AccessOptions(start).Single(o => o.Value == StrategyAccess.Routines).WhyNot is null == (database != SqlDialect.Sqlite));
            foreach (var chosenAccess in access)
            {
                var withAccess = start with { Access = chosenAccess.Value };
                var styles = StrategyRules.StyleOptions(withAccess).Where(o => o.IsOffered).ToList();
                Assert.IsGreaterThan(0, styles.Count, $"{database} {chosenAccess.Value}: styles");
                foreach (var style in styles)
                {
                    var languages = StrategyRules.LanguageOptions(withAccess with { Style = style.Value }).Where(o => o.IsOffered).ToList();
                    Assert.IsGreaterThan(0, languages.Count, $"{database} {chosenAccess.Value} {style.Value}: languages");
                    foreach (var language in languages)
                        Assert.IsNull(StrategyRules.Refusal(withAccess with { Style = style.Value, Language = language.Value }));
                }
            }
        }

        var ownSql = new StrategyChoice(SqlDialect.SqlServer, StrategyUi.React, StrategyAccess.OwnSql);
        CollectionAssert.AreEqual(new[] { StrategyStyle.Rest }, StrategyRules.StyleOptions(ownSql).Where(o => o.IsOffered).Select(o => o.Value).ToArray());
        CollectionAssert.AreEqual(new[] { StrategyLanguage.Python }, StrategyRules.LanguageOptions(ownSql with { Style = StrategyStyle.Rest }).Where(o => o.IsOffered).Select(o => o.Value).ToArray(),
            "Rust cannot read SQL Server");
        Assert.AreEqual(StrategyRules.ODataIsCSharpOnly, StrategyRules.StyleOptions(ownSql).Single(o => o.Value == StrategyStyle.OData).WhyNot);
    }

    [TestMethod]
    public void A_later_pick_that_an_earlier_change_rules_out_is_cleared_and_the_others_stay()
    {
        var all = new StrategyChoice(SqlDialect.PostgreSql, StrategyUi.Angular, StrategyAccess.Ef, StrategyStyle.OData, StrategyLanguage.CSharp);
        Assert.AreEqual(all, StrategyRules.Narrow(all));

        var ownSql = StrategyRules.Narrow(all with { Access = StrategyAccess.OwnSql });
        Assert.IsTrue(ownSql.Style is null && ownSql.Language is null, "OData and C# do not go with own SQL");
        Assert.AreEqual(StrategyUi.Angular, ownSql.Ui);

        var restOnly = StrategyRules.Narrow(all with { Style = StrategyStyle.Rest, Language = StrategyLanguage.Rust });
        Assert.IsNull(restOnly.Language, "Rust goes with own SQL, not EF");
        Assert.AreEqual(StrategyStyle.Rest, restOnly.Style);
    }

    [TestMethod]
    public void Missing_names_the_choices_still_to_make_in_the_dialogs_order()
    {
        CollectionAssert.AreEqual(new[] { "UI technology" }, StrategyRules.Missing(new StrategyChoice(SqlDialect.MySql)).ToArray());
        CollectionAssert.AreEqual(new[] { "API access mode", "API style", "API language" }, StrategyRules.Missing(new StrategyChoice(SqlDialect.MySql, StrategyUi.Uno)).ToArray());
        CollectionAssert.AreEqual(new[] { "API style" }, StrategyRules.Missing(new StrategyChoice(SqlDialect.MySql, StrategyUi.Uno, StrategyAccess.Ef, Language: StrategyLanguage.CSharp)).ToArray());
    }

    [TestMethod]
    public void The_settings_written_equal_a_project_file_made_by_hand_for_the_same_choices()
    {
        var choice = new StrategyChoice(SqlDialect.PostgreSql, StrategyUi.React, StrategyAccess.Routines, StrategyStyle.OData, StrategyLanguage.CSharp);
        var written = StrategyRules.ToSettings(choice, "Acme", "acme", "db.example.test", "app");

        var byHand = ProjectSettings.Parse("""
            # made by hand for the same choices
            ProjectName=Acme
            DatabaseProvider=PostgreSql
            DatabaseName=acme
            DatabaseServer=db.example.test
            DatabaseUser=app
            Stacks=Api,React
            AccessMode=Routines
            OData=true
            """);

        CollectionAssert.AreEquivalent(byHand.Values.ToDictionary(), written.Values.ToDictionary());
        CollectionAssert.AreEquivalent(byHand.Values.ToDictionary(), ProjectSettings.Parse(written.ToFileText()).Values.ToDictionary(), "the file text reads back as the same settings");
        Assert.AreEqual(byHand.DatabaseDialect, written.DatabaseDialect);
        CollectionAssert.AreEqual(byHand.Stacks, written.Stacks);
        Assert.AreEqual(0, written.Screens.Length, "the screens' table lists stay empty so the plan's defaults cover every table");
        Assert.AreEqual(0, written.DetailMasterTables?.Length ?? 0);
    }

    [TestMethod]
    public void Rust_and_Python_stacks_and_a_sqlite_file_get_their_own_settings()
    {
        var rust = StrategyRules.ToSettings(new StrategyChoice(SqlDialect.Sqlite, StrategyUi.Angular, StrategyAccess.OwnSql, StrategyStyle.Rest, StrategyLanguage.Rust), "Acme", "C:/data/acme.db", "ignored", "ignored");
        CollectionAssert.AreEqual(new[] { "Angular", "Rust" }, rust.Stacks);
        Assert.IsFalse(rust.Values.ContainsKey("AccessMode"));
        Assert.IsFalse(rust.Values.ContainsKey("DatabaseServer") || rust.Values.ContainsKey("DatabaseUser"), "a file database has neither");
        Assert.AreEqual("Sqlite", rust.Values["DatabaseProvider"]);

        var python = StrategyRules.ToSettings(new StrategyChoice(SqlDialect.SqlServer, StrategyUi.Uno, StrategyAccess.OwnSql, StrategyStyle.Rest, StrategyLanguage.Python), "Acme", "Acme");
        CollectionAssert.AreEqual(new[] { "Uno", "Python" }.Order().ToArray(), python.Stacks.Order().ToArray());
        Assert.IsFalse(python.Values.ContainsKey("DatabaseUser"), "a blank login (Windows authentication) is not written");
    }

    [TestMethod]
    public void The_text_of_a_choice_is_read_the_way_it_is_typed_on_the_command_line()
    {
        Assert.IsTrue(StrategyRules.TryParseUi("winui3", out var ui) && ui == StrategyUi.WinUI3);
        Assert.IsFalse(StrategyRules.TryParseUi("7", out _));
        Assert.IsTrue(StrategyRules.TryParseAccess("Own-SQL", out var access) && access == StrategyAccess.OwnSql);
        Assert.IsTrue(StrategyRules.TryParseAccess("routines", out access) && access == StrategyAccess.Routines);
        Assert.IsFalse(StrategyRules.TryParseAccess("grpc", out _));
        Assert.IsTrue(StrategyRules.TryParseStyle("OData", out var style) && style == StrategyStyle.OData);
        Assert.IsFalse(StrategyRules.TryParseStyle("graphql", out _));
        Assert.IsTrue(StrategyRules.TryParseLanguage("c#", out var language) && language == StrategyLanguage.CSharp);
        Assert.IsFalse(StrategyRules.TryParseLanguage("go", out _));

        var options = ArgumentParser.Parse(["generate", "-S", "s", "-d", "d", "-E", "--ui", "react", "--access-mode", "ef", "--api-style", "rest", "--api-language", "csharp"]);
        Assert.IsTrue(options.HasStrategy);
        Assert.AreEqual(StrategyUi.React, options.Ui);
        Assert.AreEqual(StrategyAccess.Ef, options.Access);
        Assert.AreEqual(StrategyStyle.Rest, options.ApiStyle);
        Assert.AreEqual(StrategyLanguage.CSharp, options.ApiLanguage);
        Assert.IsFalse(ArgumentParser.Parse(["generate", "-S", "s", "-d", "d", "-E"]).HasStrategy);
        Assert.ThrowsExactly<ArgumentParseException>(() => ArgumentParser.Parse(["generate", "--ui", "gtk"]));
    }

    [TestMethod]
    public async Task Rust_with_OData_is_refused_before_any_file_is_written_and_a_good_choice_does_not_get_that_far_without_all_of_it()
    {
        using var temp = new TempFolder();
        string projects = Path.Combine(temp.Path, "Projects");
        string output = Path.Combine(temp.Path, "Output");
        string[] common = ["generate", "-S", "no-such-server", "-d", "Acme", "-E", "--projects-dir", projects, "-o", output, "--ui", "react"];

        var refused = ArgumentParser.Parse([.. common, "--access-mode", "ownsql", "--api-style", "odata", "--api-language", "rust", "--provider", "PostgreSql"]);
        Assert.AreNotEqual(0, await GenerateCommand.RunAsync(refused, temp.Path, new AppSettings(), Repo.TemplatesDirectory, "SpecialLogicColumns.config", output));

        var incomplete = ArgumentParser.Parse([.. common, "--access-mode", "ef"]);
        Assert.AreNotEqual(0, await GenerateCommand.RunAsync(incomplete, temp.Path, new AppSettings(), Repo.TemplatesDirectory, "SpecialLogicColumns.config", output));

        var both = ArgumentParser.Parse([.. common, "--stack", "api", "--access-mode", "ef", "--api-style", "rest", "--api-language", "csharp"]);
        Assert.AreNotEqual(0, await GenerateCommand.RunAsync(both, temp.Path, new AppSettings(), Repo.TemplatesDirectory, "SpecialLogicColumns.config", output));

        Assert.IsFalse(Directory.Exists(projects), "no project file was written");
        Assert.IsFalse(Directory.Exists(output), "no output was written");
    }

    // ------------------------------------------------------------------ the project file

    [TestMethod]
    public void A_project_is_saved_once_and_a_taken_name_is_never_overwritten()
    {
        using var temp = new TempFolder();
        var first = StrategyRules.ToSettings(new StrategyChoice(SqlDialect.SqlServer, StrategyUi.WinUI3), "Acme", "Acme");

        Assert.AreEqual(StrategySaveResult.Saved, StrategyProjectFile.Save(temp.Path, "Acme", first, write: false));
        Assert.IsFalse(File.Exists(ProjectSettings.FilePathFor(temp.Path, "Acme")), "a dry run writes nothing");

        Assert.AreEqual(StrategySaveResult.Saved, StrategyProjectFile.Save(temp.Path, "Acme", first));
        Assert.AreEqual(StrategySaveResult.Unchanged, StrategyProjectFile.Save(temp.Path, "Acme", first));

        File.AppendAllText(ProjectSettings.FilePathFor(temp.Path, "Acme"), "Screens=Customer" + Environment.NewLine);
        string edited = File.ReadAllText(ProjectSettings.FilePathFor(temp.Path, "Acme"));
        Assert.AreEqual(StrategySaveResult.Unchanged, StrategyProjectFile.Save(temp.Path, "Acme", first), "hand additions do not make the choices differ");

        var other = StrategyRules.ToSettings(new StrategyChoice(SqlDialect.SqlServer, StrategyUi.React, StrategyAccess.Ef, StrategyStyle.Rest, StrategyLanguage.CSharp), "Acme", "Acme");
        Assert.AreEqual(StrategySaveResult.NameTaken, StrategyProjectFile.Save(temp.Path, "Acme", other));
        Assert.AreEqual(edited, File.ReadAllText(ProjectSettings.FilePathFor(temp.Path, "Acme")), "the existing project is untouched");

        var laid = StrategyProjectFile.ChoicesOver(ProjectSettings.LoadNamed(temp.Path, "Acme"), other);
        CollectionAssert.AreEqual(new[] { "Api", "React" }, laid.Stacks);
        CollectionAssert.AreEqual(new[] { "Customer" }, laid.Screens, "the project keeps what the choices do not say");
    }

    [TestMethod]
    public void A_project_name_comes_from_the_database_name_or_a_sqlite_file()
    {
        Assert.AreEqual("InvoiceSystem", StrategyProjectFile.NameFor("InvoiceSystem"));
        Assert.AreEqual("invoicesystem", StrategyProjectFile.NameFor("invoicesystem"));
        Assert.AreEqual("shop", StrategyProjectFile.NameFor(@"C:\data\shop.db"));
        Assert.AreEqual("shop", StrategyProjectFile.NameFor("/data/shop.sqlite"));
        Assert.AreEqual("P2024data", StrategyProjectFile.NameFor("2024-data"));
        Assert.AreEqual(StrategyProjectFile.DefaultName, StrategyProjectFile.NameFor("..."));
    }

    // ------------------------------------------------------------------ the tables

    private static TableModel Customer() => Sample.Table("Customer",
    [
        Sample.Column("CustomerId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2),
        Sample.Column("CreditLimit", SqlDbType.Int, ordinal: 3)
    ]);

    private static TableModel Junction() => Sample.Table("CustomerTag",
    [
        Sample.Column("CustomerId", SqlDbType.Int, primaryKey: true, ordinal: 1),
        Sample.Column("TagId", SqlDbType.Int, primaryKey: true, ordinal: 2),
        Sample.Column("CreateDate", SqlDbType.DateTime, createDateColumn: true, ordinal: 3),
        Sample.Column("CreateUser", SqlDbType.VarChar, characters: 30, createUserColumn: true, ordinal: 4)
    ],
    [Sample.ForeignKey("CustomerId", "Customer", "CustomerId", "Name"), Sample.ForeignKey("TagId", "Tag", "TagId", "Name")]);

    private static TableModel CompositeKey() => Sample.Table("OrderLine",
    [
        Sample.Column("OrderId", SqlDbType.Int, primaryKey: true, ordinal: 1),
        Sample.Column("LineNumber", SqlDbType.Int, primaryKey: true, ordinal: 2),
        Sample.Column("Quantity", SqlDbType.Int, ordinal: 3)
    ]);

    private static TableModel Status() => Sample.Table("Status",
    [
        Sample.Column("StatusId", SqlDbType.Int, primaryKey: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 30, ordinal: 2)
    ]);

    private static TableModel NoKey() => Sample.Table("Journal", [Sample.Column("Message", SqlDbType.NVarChar, characters: 50, ordinal: 1)]);

    private static DatabaseModel Database(params TableModel[] tables) => new() { DatabaseName = "Acme", SchemaName = "dbo", Tables = tables.ToList() };

    [TestMethod]
    public void Each_table_shape_is_given_the_templates_of_its_shape_and_none_of_another()
    {
        var database = Database(Customer(), Sample.DepartmentTeam(), Junction(), CompositeKey(), Status(), NoKey());
        var project = ProjectSettings.FromValues([new("ProjectName", "Acme"), new("EnumTables", "Status"), new("Stacks", "Api,React")]);
        var steps = ProjectPlan.Build(TemplateCatalog.Discover(Repo.TemplatesDirectory), database, project, ["Api", "React"]);

        HashSet<string> Given(string table) => steps.Where(s => !s.IsDatabaseLevel && s.TableNames.Contains(table)).Select(s => s.Template.Name).ToHashSet();
        var ordinary = Given("Customer");
        var nameActive = Given("DepartmentTeam");
        var junction = Given("CustomerTag");
        var composite = Given("OrderLine");
        var enumeration = Given("Status");

        CollectionAssert.IsSubsetOf(new[] { "API_Crud", "CS_Repo", "CS_Entity", "TSX_Page" }, ordinary.ToList());
        Assert.IsFalse(ordinary.Contains("API_Junction") || ordinary.Contains("CS_Enum"));

        CollectionAssert.IsSubsetOf(new[] { "API_Crud", "CS_Repo", "CS_Entity" }, nameActive.ToList());
        Assert.IsFalse(nameActive.Contains("TSX_Page") || nameActive.Contains("API_Junction") || nameActive.Contains("CS_Enum"));

        CollectionAssert.IsSubsetOf(new[] { "API_Junction", "CS_Entity" }, junction.ToList());
        Assert.IsFalse(junction.Contains("API_Crud") || junction.Contains("CS_Repo") || junction.Contains("TSX_Page") || junction.Contains("CS_Enum"));

        Assert.IsTrue(composite.Contains("CS_Entity"));
        Assert.IsFalse(composite.Contains("API_Junction") || composite.Contains("API_Crud") || composite.Contains("TSX_Page") || composite.Contains("CS_Enum"));

        Assert.IsTrue(enumeration.Contains("CS_Enum"));
        Assert.IsFalse(enumeration.Contains("API_Crud") || enumeration.Contains("TSX_Page") || enumeration.Contains("API_Junction"));

        Assert.AreEqual(0, Given("Journal").Count, "a table with no key fits no template");
    }

    [TestMethod]
    public void A_table_no_template_fits_and_a_child_without_a_screen_are_listed_not_failed()
    {
        var department = Sample.Table("Department",
        [
            Sample.Column("DepartmentId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Title", SqlDbType.NVarChar, characters: 50, ordinal: 2)
        ],
        childForeignKeys: [Sample.ChildForeignKey("Employee", "DepartmentId", "DepartmentId", childOwnPrimaryKey: ["EmployeeId"])]);
        var database = Database(department, NoKey());
        var project = ProjectSettings.FromValues([new("ProjectName", "Acme")]);
        var steps = ProjectPlan.Build(TemplateCatalog.Discover(Repo.TemplatesDirectory), database, project, ["Api", "React"]);

        var notes = TableShapeNotes.Of(steps, database, project, ["Api", "React"]);

        CollectionAssert.Contains(notes, "Journal: no template of the chosen technologies fits its shape (no primary key), so it is skipped.");
        CollectionAssert.Contains(notes, "Employee: a child of Department, no screen.");
        Assert.AreEqual(2, notes.Count);

        Assert.AreEqual(1, TableShapeNotes.Of(steps, database, project, ["Api"]).Count, "without a UI stack there are no screens to link");
    }

    [TestMethod]
    public void A_table_the_chosen_api_has_no_template_for_says_which_shape_it_is()
    {
        var database = Database(Customer(), Status(), CompositeKey());
        var project = ProjectSettings.FromValues([new("ProjectName", "Acme"), new("EnumTables", "Status")]);
        var steps = ProjectPlan.Build(TemplateCatalog.Discover(Repo.TemplatesDirectory), database, project, ["Python"]);

        var notes = TableShapeNotes.Of(steps, database, project, ["Python"]);

        CollectionAssert.Contains(notes, "Status: no template of the chosen technologies fits its shape (enum table), so it is skipped.");
        CollectionAssert.Contains(notes, "OrderLine: no template of the chosen technologies fits its shape (composite primary key), so it is skipped.");
        Assert.AreEqual(2, notes.Count, "Customer has Python templates");
    }
}
