using System.Data;
using CodeGenNew.Core;
using CodeGenNew.Core.Api;
using CodeGenNew.Core.Party;
using CodeGenNew.Core.Python;
using CodeGenNew.Core.Rust;

namespace CodeGenNew.Tests;

/// <summary> PartyTables: a party table and the table of its identifiers. </summary>
[TestClass]
public class PartyTableTests
{
    private static TableModel Party() => Sample.Table("Party",
    [
        Sample.Column("PartyId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("LegalName", SqlDbType.VarChar, characters: 100, ordinal: 2),
    ], []);

    private static TableModel Identifier(bool nullableValue = false, bool secondLink = false, string scheme = "Scheme") => Sample.Table("PartyIdentifier",
    [
        Sample.Column("PartyIdentifierId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("PartyId", SqlDbType.Int, ordinal: 2),
        Sample.Column("IssuerPartyId", SqlDbType.Int, nullable: true, ordinal: 3),
        Sample.Column(scheme, SqlDbType.VarChar, characters: 20, ordinal: 4),
        Sample.Column("Value", SqlDbType.VarChar, characters: 50, nullable: nullableValue, ordinal: 5),
        Sample.Column("Sequence", SqlDbType.Int, ordinal: 6),
    ], secondLink ? [Sample.ForeignKey("PartyId", "Party", "PartyId"), Sample.ForeignKey("IssuerPartyId", "Party", "PartyId")] : [Sample.ForeignKey("PartyId", "Party", "PartyId")]);

    private static DatabaseModel Database(TableModel? identifier = null) => new() { DatabaseName = "Shop", SchemaName = "dbo", Tables = [Party(), identifier ?? Identifier()] };

    private static ProjectSettings Project(string setting = "Party:PartyIdentifier") =>
        ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["PartyTables"] = setting, ["AccessMode"] = "Ef" });

    [TestMethod]
    public void The_setting_names_the_two_tables_and_finds_the_link_and_the_default_columns()
    {
        var database = Database();
        var shape = PartyPlan.PartyOf(database.Tables[0], Project(), database)!;
        Assert.AreEqual("PartyIdentifier", shape.Identifiers.TableName);
        Assert.AreEqual("PartyId", shape.Link.Name);
        Assert.AreEqual("Scheme", shape.Scheme.Name);
        Assert.AreEqual("Value", shape.Value.Name);
        Assert.IsNotNull(PartyPlan.IdentifiersOf(database.Tables[1], Project(), database));
        Assert.IsNull(PartyPlan.IdentifiersOf(database.Tables[0], Project(), database));
        Assert.HasCount(0, RowShapePlan.Problems(database, Project()));
        Assert.IsTrue(RowShapePlan.Has(database.Tables[0], Project()) && RowShapePlan.Has(database.Tables[1], Project()));
        Assert.IsTrue(RowShapePlan.Any(Project()));
    }

    [TestMethod]
    public void The_scheme_and_value_columns_can_be_named()
    {
        var database = Database(Identifier(scheme: "Kind"));
        Assert.AreEqual("Kind", PartyPlan.PartyOf(database.Tables[0], Project("Party:PartyIdentifier:Kind:Value"), database)!.Scheme.Name);
        Assert.IsTrue(RowShapePlan.Problems(database, Project()).Any(p => p.Contains("needs Scheme and Value")));
    }

    [TestMethod]
    [DataRow("Party", "needs the table of its identifiers")]
    [DataRow("Missing:PartyIdentifier", "is not a table of the database")]
    [DataRow("Party:Missing", "Missing is not a table of the database")]
    [DataRow("Party:PartyIdentifier:Sequence:Value", "text columns that cannot be NULL")]
    [DataRow("Party:PartyIdentifier:Value:Value", "cannot be the same column")]
    [DataRow("Party:Party", "exactly one single-column foreign key")]
    public void A_setting_that_cannot_work_is_reported(string setting, string expected)
    {
        var problems = RowShapePlan.Problems(Database(), Project(setting));
        Assert.IsTrue(problems.Any(p => p.Contains(expected)), string.Join(" | ", problems));
    }

    [TestMethod]
    public void A_value_that_allows_null_and_a_second_link_to_the_party_are_reported()
    {
        Assert.IsTrue(RowShapePlan.Problems(Database(Identifier(nullableValue: true)), Project()).Any(p => p.Contains("cannot be NULL")));
        Assert.IsTrue(RowShapePlan.Problems(Database(Identifier(secondLink: true)), Project()).Any(p => p.Contains("it has 2")));
    }

    [TestMethod]
    public void The_api_refuses_a_repeated_identifier_and_finds_a_party_by_one()
    {
        string code = RowShapeWriter.File(Database(), Project(), "ShopContext", []);
        Expect.Contains(code, "CheckIdentifierAsync(ShopContext context, PartyIdentifier row)");
        Expect.Contains(code, "x.PartyIdentifierId != row.PartyIdentifierId && x.Scheme.ToLower() == scheme && x.Value.ToLower() == value");
        Expect.Contains(code, "RowShapeProblems.IdentifierTaken(");
        Expect.Contains(code, PartyWords.IdentifierTakenType);
        Expect.Contains(code, "if (await CheckIdentifierAsync(context, row) is { } identifierProblem)");
        Expect.Contains(code, "/by-identifier\", ([FromServices] ShopContext context, [FromQuery] string? value, [FromQuery] string? scheme) => ByIdentifierAsync(context, value, scheme))");
        Expect.Contains(code, "context.Set<Party>().AsNoTracking().Where(p => held.Any(i => i.PartyId == p.PartyId))");
        Expect.Contains(code, "PartyShape.Map(app);");
        Expect.Contains(code, "PartyIdentifierShape.Map(app);");
        Assert.IsTrue(RowShapeWriter.HasSaveCheck(Identifier(), Project()));
        Assert.IsFalse(RowShapeWriter.HasSaveCheck(Party(), Project()));
    }
    [TestMethod]
    public void The_generated_tests_play_the_rule_and_the_search()
    {
        var database = Database();
        var project = Project();
        PartyPlan.Remember(database, project);
        string tests = RowShapeTests.Methods(ApiTestPlan.For(database.Tables[1], project)!, database.Tables[1], project);
        Expect.Contains(tests, "A_scheme_and_value_pair_another_row_holds_is_refused");
        Expect.Contains(tests, PartyWords.IdentifierTakenType);
        Expect.Contains(tests, "The_party_is_found_by_its_identifier");
        Expect.Contains(tests, "/by-identifier?value=");
    }

    [TestMethod]
    public void The_python_and_rust_apis_carry_the_same_rule_and_route()
    {
        string python = PythonPartyWriter.Helper(new("PartyIdentifier", "party_identifier_id", "scheme", "value"))
            + PythonPartyWriter.Route(new("Party", "party_id", "PartyIdentifier", "party_identifier", "party_id", "scheme", "value"), "/api/parties", "PartySchema", "", "");
        Expect.Contains(python, "func.lower(PartyIdentifier.scheme) == scheme.lower()");
        Expect.Contains(python, PartyWords.IdentifierTakenType);
        Expect.Contains(python, "@router.get(\"/api/parties/by-identifier\", response_model=list[PartySchema])");
        string rust = RustPartyWriter.CheckCode(new("PartyIdentifier", "\"PartyIdentifier\"", "\"PartyIdentifierId\"", "i32", "scheme", "\"Scheme\"", "value", "\"Value\""))
            + RustPartyWriter.FindCode(new("Party", "\"Party\"", "\"PartyId\"", "\"PartyId\"", "\"PartyIdentifier\"", "\"PartyId\"", "\"Scheme\"", "\"Value\""))
            + RustPartyWriter.RouteLine("/api/parties", "");
        Expect.Contains(rust, "async fn check_identifier(pool: &Pool, row_id: Option<i32>, row: &mut PartyIdentifier)");
        Expect.Contains(rust, PartyWords.IdentifierTakenType);
        Expect.Contains(rust, ".route(\"/api/parties/by-identifier\", get(by_identifier))");
    }

    [TestMethod]
    public void The_python_and_rust_tests_are_written_for_a_party_table_they_can_fill()
    {
        var database = Database();
        var project = ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["PartyTables"] = "Party:PartyIdentifier", ["PythonTests"] = "true", ["RustTests"] = "true" });
        var names = project.ImpliedPlanTemplates.ToList();
        CollectionAssert.Contains(names, "PY_TestParties");
        CollectionAssert.Contains(names, "RS_TestParties");
        CollectionAssert.DoesNotContain(Project().ImpliedPlanTemplates.ToList(), "RS_TestParties");

        var plan = PartyTestPlan.For(database, project)!;
        Assert.AreEqual("{ \"partyId\": 0, \"legalName\": " + database.Tables[0].Columns[1].ValidExample(project, "Party") + " }", plan.PartyBody);
        Expect.Contains(plan.IdentifierBody, "\"partyId\": 0, \"scheme\": \"\", \"value\": \"\"");

        string python = PythonPartyTests.File(database, project);
        Expect.Contains(python, "def test_a_scheme_and_value_pair_another_row_holds_is_refused(rows):");
        Expect.Contains(python, "def test_a_party_is_found_by_its_identifier(rows):");
        Expect.Contains(python, "TAKEN = \"" + PartyWords.IdentifierTakenType + "\"");
        string rust = RustPartyTests.File(database, project);
        Expect.Contains(rust, "async fn a_scheme_and_value_pair_another_row_holds_is_refused()");
        Expect.Contains(rust, "by-identifier?value=%20{}%20&scheme=vat");
        Assert.DoesNotContain("§", python + rust);

        var shortValue = Sample.Table("PartyIdentifier",
        [
            Sample.Column("PartyIdentifierId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("PartyId", SqlDbType.Int, ordinal: 2),
            Sample.Column("Scheme", SqlDbType.VarChar, characters: 20, ordinal: 3),
            Sample.Column("Value", SqlDbType.VarChar, characters: 5, ordinal: 4),
        ], [Sample.ForeignKey("PartyId", "Party", "PartyId")]);
        Assert.IsNull(PartyTestPlan.For(Database(shortValue), project), "a value too short for the test values");
        Assert.IsFalse(RustPartyTests.Applies(Database(shortValue), project));
    }

    [TestMethod]
    public void The_web_lists_of_a_party_table_get_a_box_that_finds_by_identifier()
    {
        Assert.IsTrue(PartyScreens.HasBox(Party(), Project()));
        Assert.IsFalse(PartyScreens.HasBox(Identifier(), Project()));
        Assert.AreEqual("  byIdentifier: (value: string) => request<Party[]>(`${apiUrl}/by-identifier?value=${encodeURIComponent(value)}`),", PartyScreens.ReactApiLine("Party"));
        string paged = string.Join("\n", PartyScreens.ReactHandlers("partyApi", "setParties", paged: true));
        Expect.Contains(paged, "partyApi.byIdentifier(value).then((rows) => {\n      setParties(rows);\n      setPage(1);\n      setTotalPages(1);");
        Assert.DoesNotContain("setPage", string.Join("\n", PartyScreens.ReactHandlers("partyApi", "setParties", paged: false)));
        Expect.Contains(string.Join("\n", PartyScreens.AngularServiceLines("Party")), "byIdentifier(value: string): Observable<Party[]>");
        Expect.Contains(string.Join("\n", PartyScreens.AngularBox()), "[(ngModel)]=\"identifierText\" (keyup.enter)=\"findByIdentifier()\"");
        Expect.Contains(string.Join("\n", PartyScreens.AngularHandlers(paged: true)), "this.rows = rows; this.pageIndex = 0; this.totalCount = rows.length;");
        Assert.DoesNotContain("pageIndex", string.Join("\n", PartyScreens.AngularHandlers(paged: false)));
        Expect.Contains(string.Join("\n", PartyScreens.CsClientLines("Party")), "public async Task<List<Party>> ByIdentifierAsync(string value)");
        Expect.Contains(string.Join("\n", PartyScreens.BlazorBox()), "@onclick=\"FindByIdentifierAsync\">Find</button>");
        Expect.Contains(string.Join("\n", PartyScreens.BlazorHandlers("Client", paged: true)), "items = await Client.ByIdentifierAsync(identifierText.Trim());");
        string box = string.Join("\n", PartyScreens.ReactBox());
        Expect.Contains(box, "aria-label=\"Find by identifier\"");
        Expect.Contains(box, "<button type=\"button\" onClick={showAllParties}>Show all</button>");
        string reactTest = string.Join("\n", PartyScreens.ReactBoxTests("partyApi", "PartyPage", "getPage"));
        Expect.Contains(reactTest, "vi.spyOn(partyApi, 'byIdentifier').mockResolvedValue([]);");
        Expect.Contains(reactTest, "expect(found).toHaveBeenCalledWith('AB12');");
        Expect.Contains(reactTest, "const list = vi.spyOn(partyApi, 'getPage');");
        string angularTest = PartyScreens.AngularBoxTest();
        Expect.Contains(angularTest, "component.findByIdentifier();");
        Expect.Contains(angularTest, "endsWith('/by-identifier?value=AB12')");
        string blazorTest = PartyScreens.BlazorBoxTest();
        Expect.Contains(blazorTest, "Api.Answer(\"/by-identifier?value=AB12\", \"[]\");");
        Expect.Contains(blazorTest, "Input(\"  AB12  \")");
    }

    [TestMethod]
    public void The_desktop_lists_of_a_party_table_get_the_box_and_the_identifier_dialog_says_why_a_save_is_refused()
    {
        var project = Project();
        PartyPlan.Remember(Database(), project);
        Expect.Contains(PartyScreens.DesktopBox(), "AutomationProperties.Name=\"Find by identifier\" Text=\"{x:Bind ViewModel.IdentifierText, Mode=TwoWay}\"");
        Expect.Contains(PartyScreens.DesktopBox(), "<Button Content=\"Show all\"");
        Expect.Contains(PartyScreens.DesktopHandlers(), "OnFindByIdentifierClick(object sender, RoutedEventArgs e) => await ViewModel.FindByIdentifierAsync();");
        Expect.Contains(PartyScreens.DesktopMembers(), "public bool FindingIdentifier => _identifierSought is not null;");
        Expect.Contains(PartyScreens.UnoRows("Party"), "private async Task<List<Party>> PartiesFoundAsync() => await Client.ByIdentifierAsync(_identifierSought!);");
        string rows = PartyScreens.WinUI3Rows(Party(), project, "_context");
        Expect.Contains(rows, "var held = _context.Set<PartyIdentifier>().AsNoTracking().Where(i => i.Value.ToLower() == wanted);");
        Expect.Contains(rows, "Where(p => held.Any(i => i.PartyId == p.PartyId)).OrderBy(p => p.PartyId).Take(50)");
        Assert.AreEqual("", PartyScreens.WinUI3Rows(Identifier(), project, "_context"));

        string check = PartyScreens.CheckLines(project, Identifier(), "_context");
        Expect.Contains(check, "entity.Scheme = (entity.Scheme ?? \"\").Trim();");
        Expect.Contains(check, "AnyAsync(x => x.PartyIdentifierId != entity.PartyIdentifierId && x.Scheme.ToLower() == heldScheme && x.Value.ToLower() == heldValue))");
        Expect.Contains(check, "ErrorMessage = \"" + PartyWords.IdentifierTaken + "\";");
        string race = PartyScreens.RaceLines(project, Identifier(), "_context");
        Expect.Contains(race, "_context.Entry(entity).State = Microsoft.EntityFrameworkCore.EntityState.Detached;");
        Expect.Contains(race, "AnyAsync(x => x.PartyIdentifierId != entity.PartyIdentifierId && x.Scheme.ToLower() == heldScheme && x.Value.ToLower() == heldValue)");
        Expect.Contains(race, "ErrorMessage = \"" + PartyWords.IdentifierTaken + "\";");
        Assert.AreEqual("", PartyScreens.RaceLines(project, Party(), "_context"));
        Assert.AreEqual("", PartyScreens.CheckLines(project, Party(), "_context"));

        Assert.IsTrue(PartyScreens.SaysTaken(Identifier(), project));
        Assert.IsFalse(PartyScreens.SaysTaken(Party(), project));
        Expect.Contains(string.Join("\n", PartyScreens.ReactProblemDetail()), "const problemDetail = (error: unknown): string | undefined => {");
    }

    [TestMethod]
    public void An_identifier_table_without_a_unique_index_on_the_pair_is_named()
    {
        string missing = PartyPlan.MissingIndexes(Database(), Project()).Single();
        Expect.Contains(missing, "PartyIdentifier holds the identifiers of Party without a unique index on Scheme and Value");
        var indexed = Sample.Table("PartyIdentifier", Identifier().Columns.ToList(), [Sample.ForeignKey("PartyId", "Party", "PartyId")], indexes: [new IndexModel("UX_Pair", true, ["Value", "Scheme"])]);
        Assert.HasCount(0, PartyPlan.MissingIndexes(Database(indexed), Project()));
    }

    [TestMethod]
    public async Task A_save_the_unique_index_on_the_pair_refuses_answers_as_the_rule_does()
    {
        TableModel Indexed(params IndexModel[] indexes) => Sample.Table("PartyIdentifier", Identifier().Columns.ToList(), [Sample.ForeignKey("PartyId", "Party", "PartyId")], indexes: indexes.ToList());
        var pair = new IndexModel("UX_Pair", true, ["Scheme", "Value"]);
        var key = new IndexModel("PK_PartyIdentifier", true, ["PartyIdentifierId"]);

        var project = Project();
        Assert.HasCount(0, PartyPlan.PairIndexes(project));
        PartyPlan.Remember(Database(), project);
        Assert.HasCount(0, PartyPlan.PairIndexes(project));
        PartyPlan.Remember(Database(Indexed(key, new IndexModel("UX_Issuer", true, ["IssuedBy"]), pair)), project);
        var index = PartyPlan.PairIndexes(project).Single();
        Assert.AreEqual("PartyIdentifier", index.Table);
        Assert.AreEqual("UX_Pair", index.Name);
        Assert.AreEqual("UX_Pair'", PartyPlan.RefusalMark(index, SqlDialect.SqlServer));
        Assert.AreEqual("UX_Pair'", PartyPlan.RefusalMark(index, SqlDialect.MySql));
        Assert.AreEqual("UX_Pair\"", PartyPlan.RefusalMark(index, SqlDialect.PostgreSql));
        Assert.AreEqual("PartyIdentifier.Scheme, PartyIdentifier.Value'", PartyPlan.RefusalMark(index, SqlDialect.Sqlite));

        string program = (await Repo.Cache.RunAsync(Repo.Template("API_EssentialProgram_v1.tt"), project)).GeneratedText!;
        Expect.Contains(program, "Constraint.Unique when RepeatsIdentifier(error) => Problem(StatusCodes.Status409Conflict, \"" + PartyWords.IdentifierTakenType + "\", \"Identifier taken\", \"" + PartyWords.IdentifierTaken + "\"),");
        Expect.Contains(program, "private static readonly string[] IdentifierIndexes = [\"UX_Pair'\"];");
        Expect.Contains(program, "error.InnerException?.Message is { } message && IdentifierIndexes.Any(mark => message.Contains(mark, StringComparison.OrdinalIgnoreCase));");
        string plain = (await Repo.Cache.RunAsync(Repo.Template("API_EssentialProgram_v1.tt"), Project())).GeneratedText!;
        Assert.DoesNotContain("RepeatsIdentifier", plain);

        var names = new PythonPartyWriter.IdentifierNames("PartyIdentifier", "party_identifier_id", "scheme", "value");
        Expect.Contains(PythonPartyWriter.Helper(names), "def identifier_taken(session: Session, scheme: str, value: str, key) -> bool:");
        Assert.AreEqual("    pair = (row.scheme, row.value)", PythonPartyWriter.PairLine(names));
        Expect.Contains(PythonPartyWriter.RefusedLines("None"), "        if identifier_taken(session, *pair, None):");
        Expect.Contains(PythonPartyWriter.RefusedLines("id"), "            raise identifier_refusal()");

        string rust = RustPartyWriter.CheckCode(new("PartyIdentifier", "\"PartyIdentifier\"", "\"PartyIdentifierId\"", "i32", "scheme", "\"Scheme\"", "value", "\"Value\""));
        Expect.Contains(rust, "async fn identifier_refused(pool: &Pool, row_id: Option<i32>, row: &mut PartyIdentifier, error: sqlx::Error) -> ApiError {");
        Expect.Contains(rust, "database.kind() == sqlx::error::ErrorKind::UniqueViolation");
        Expect.Contains(RustPartyWriter.InsertLines, "Err(error) => return Err(identifier_refused(&pool, None, &mut row, error).await),");
        Expect.Contains(RustPartyWriter.UpdateLines("id"), "Err(error) => Err(identifier_refused(&pool, Some(id), &mut row, error).await),");
    }

    [TestMethod]
    public void Identifiers_saved_as_rows_of_the_party_form_go_through_the_rule()
    {
        var project = ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["PartyTables"] = "Party:PartyIdentifier", ["EditableChildren"] = "Party.PartyIdentifier", ["AccessMode"] = "Ef" });
        Assert.IsTrue(PartyPlan.SavedWith(Party(), "PartyIdentifier", project));
        Assert.IsFalse(PartyPlan.SavedWith(Identifier(), "PartyIdentifier", project));
        Assert.IsFalse(PartyPlan.SavedWith(Party(), "PartyNote", project));

        string code = RowShapeWriter.File(Database(), project, "ShopContext", []);
        Expect.Contains(code, "public static async Task<IResult?> CheckIdentifierRowsAsync(DbContext context, int partyId, List<PartyIdentifier> rows)");
        Expect.Contains(code, "if (!seen.Add((scheme, value)) || await context.Set<PartyIdentifier>().AsNoTracking().AnyAsync(x => x.PartyId != partyId && x.Scheme.ToLower() == scheme && x.Value.ToLower() == value))");
        Assert.DoesNotContain("CheckIdentifierRowsAsync", RowShapeWriter.File(Database(), Project(), "ShopContext", []));

        Assert.IsTrue(PartyScreens.SaysTaken(Party(), project));
        Assert.IsFalse(PartyScreens.SaysTaken(Party(), Project()));

        Assert.AreEqual("", PartyScreens.EditRowsCheckLines(project, Party(), "PartyIdentifier", "identifiersEdit", "_context"));
        PartyPlan.Remember(Database(), project);
        string lines = PartyScreens.EditRowsCheckLines(project, Party(), "PartyIdentifier", "identifiersEdit", "_context");
        Expect.Contains(lines, "foreach (var line in identifiersEdit)");
        Expect.Contains(PartyScreens.EditRowsCheckLines(project, Party(), "PartyIdentifier", "identifiersEdit", "_context", "Refused"), "foreach (var lineRefused in identifiersEdit)");
        Expect.Contains(lines, "_context.Set<PartyIdentifier>().AsNoTracking().AnyAsync(x => x.PartyId != entity.PartyId && x.Scheme.ToLower() == lineScheme && x.Value.ToLower() == lineValue)");
        Expect.Contains(lines, "ErrorMessage = \"" + PartyWords.IdentifierTaken + "\";");
        Assert.AreEqual("", PartyScreens.EditRowsCheckLines(project, Party(), "PartyNote", "notesEdit", "_context"));
    }

    [TestMethod]
    public async Task The_odata_routes_run_the_rule_and_the_odata_modules_carry_the_search()
    {
        var project = ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["PartyTables"] = "Party:PartyIdentifier", ["OData"] = "true", ["AccessMode"] = "Ef" });
        string odata = (await Repo.Cache.RunAsync(Repo.Template("API_OData_v1.tt"), Database(), project)).GeneratedText!;
        Expect.Contains(odata, "public sealed class PartyIdentifierODataRules : ODataRules<PartyIdentifier,");
        Expect.Contains(odata, "PartyIdentifierShape.CheckSaveAsync(context, row, isNew);");
        Assert.DoesNotContain("PartyShape.CheckIdentifierAsync", odata);

        Assert.AreEqual("  byIdentifier: (value: string) => request<Party[]>(`/api/parties/by-identifier?value=${encodeURIComponent(value)}`),", PartyScreens.ReactODataApiLine("Party", "/api/parties"));
        Expect.Contains(string.Join("\n", PartyScreens.AngularODataServiceLines("Party")), "return this.http.get<Party[]>(`${this.restUrl}/by-identifier?value=${encodeURIComponent(value)}`);");
        Expect.Contains(string.Join("\n", PartyScreens.CsODataClientLines("Party", "api/parties")),
            "public async Task<List<Party>> ByIdentifierAsync(string value) => await (await http.GetAsync($\"api/parties/by-identifier?value={Uri.EscapeDataString(value)}\")).ReadAsync<List<Party>>();");
    }

    private static TableModel Child(string name, params (string Name, SqlDbType Type, bool Nullable)[] columns) => Sample.Table(name,
        [Sample.Column(name + "Id", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Sample.Column("PartyId", SqlDbType.Int, ordinal: 2),
            .. columns.Select((c, i) => Sample.Column(c.Name, c.Type, characters: c.Type == SqlDbType.VarChar ? 20 : 0, nullable: c.Nullable, ordinal: i + 3))],
        [Sample.ForeignKey("PartyId", "Party", "PartyId")]);

    [TestMethod]
    public void The_children_of_a_party_are_named_in_its_entry_and_found_from_the_columns_of_the_tables()
    {
        var project = Project("Party:PartyIdentifier:names=PartyName:contacts=ContactMethod:taxes=TaxRegistration:roles=PartyRole");
        var entry = PartyPlan.Entries(project).Single();
        Assert.AreEqual("PartyIdentifier", entry.Identifiers);
        Assert.AreEqual("Scheme", entry.Scheme);
        CollectionAssert.AreEqual(new[] { ("names", "PartyName"), ("contacts", "ContactMethod"), ("taxes", "TaxRegistration"), ("roles", "PartyRole") }, entry.Children.ToArray());
        CollectionAssert.AreEqual(new[] { "PartyName", "ContactMethod" }, project.DefaultRowTables);
        CollectionAssert.AreEqual(new[] { "TaxRegistration" }, project.EffectiveDatedTables);

        var name = Child("PartyName", ("NameType", SqlDbType.VarChar, false), ("IsPrimary", SqlDbType.Bit, false));
        var contact = Child("ContactMethod", ("MethodType", SqlDbType.VarChar, false), ("IsPreferred", SqlDbType.Bit, false));
        var tax = Child("TaxRegistration", ("Country", SqlDbType.VarChar, false), ("ValidFrom", SqlDbType.Date, false), ("ValidTo", SqlDbType.Date, true));
        Assert.AreEqual("IsPrimary", DefaultRowPlan.For(name, project)!.Flag.Name);
        CollectionAssert.AreEqual(new[] { "PartyId", "NameType" }, DefaultRowPlan.For(name, project)!.Groups.Select(c => c.Name).ToArray());
        Assert.AreEqual("IsPreferred", DefaultRowPlan.For(contact, project)!.Flag.Name);
        var period = EffectiveDatePlan.For(tax, project)!;
        Assert.AreEqual("ValidFrom", period.From.Name);
        CollectionAssert.AreEqual(new[] { "PartyId", "Country" }, period.Groups.Select(c => c.Name).ToArray());

        var role = Sample.Table("PartyRole", [Sample.Column("PartyId", SqlDbType.Int, primaryKey: true, ordinal: 1), Sample.Column("RoleId", SqlDbType.Int, primaryKey: true, ordinal: 2)],
            [Sample.ForeignKey("PartyId", "Party", "PartyId"), Sample.ForeignKey("RoleId", "Role", "RoleId")]);
        var database = new DatabaseModel { DatabaseName = "Shop", SchemaName = "dbo", Tables = [Party(), Identifier(), name, contact, tax, role] };
        CollectionAssert.AreEqual(Array.Empty<string>(), PartyPlan.Problems(database, project));

        var missing = Project("Party:PartyIdentifier:names=Nowhere:cards=PartyName:roles=PartyName");
        var problems = PartyPlan.Problems(database, missing);
        Assert.IsTrue(problems.Any(p => p.Contains("Nowhere is not a table")), string.Join("|", problems));
        Assert.IsTrue(problems.Any(p => p.Contains("cards=PartyName is not a kind of child table")), string.Join("|", problems));
        Assert.IsTrue(problems.Any(p => p.Contains("roles=PartyName: PartyName is not a junction table")), string.Join("|", problems));
    }

    [TestMethod]
    public void A_scheme_and_a_value_under_one_parent_are_suggested_as_party_identifiers()
    {
        var plain = ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["AccessMode"] = "Ef" });
        var found = DatabaseShapeDetector.Of(Database(), plain).Single(d => d.Suggestion.Shape == "Party identifiers");
        Assert.AreEqual("PartyIdentifier", found.Table);
        Assert.AreEqual("Party:PartyIdentifier", found.Suggestion.Item);
        Assert.IsTrue(found.Suggestion.Fits, found.Suggestion.Note);
        Assert.IsFalse(DatabaseShapeDetector.Of(Database(), Project()).Any(d => d.Suggestion.Shape == "Party identifiers"), "a listed table is not suggested again");
        Assert.IsFalse(DatabaseShapeDetector.Of(Database(Identifier(secondLink: true)), plain).Single(d => d.Suggestion.Shape == "Party identifiers").Suggestion.Fits, "two links to the party only look like it");
    }
}
