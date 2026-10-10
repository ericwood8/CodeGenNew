using System.Data;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The key of a table (int, guid, text): one Core rule for the shape, and one description of how the key travels (C#, TypeScript, Python, route). </summary>
[TestClass]
public class KeyTypeTests
{
    [TestMethod]
    public void Every_provider_classifies_a_key_with_the_same_rule()
    {
        Assert.AreEqual(PrimaryKeyShape.None, PrimaryKeyShapes.Classify(0, null));
        Assert.AreEqual(PrimaryKeyShape.Composite, PrimaryKeyShapes.Classify(2, "int"));
        foreach (string whole in new[] { "int", "bigint", "smallint", "tinyint" })
            Assert.AreEqual(PrimaryKeyShape.SingleInt, PrimaryKeyShapes.Classify(1, whole), whole);
        Assert.AreEqual(PrimaryKeyShape.SingleUniqueIdentifier, PrimaryKeyShapes.Classify(1, "uniqueidentifier"));
        foreach (string text in new[] { "char", "varchar", "nchar", "nvarchar" })
            Assert.AreEqual(PrimaryKeyShape.SingleText, PrimaryKeyShapes.Classify(1, text), text);
        foreach (string other in new[] { "datetime2", "decimal", "date" })
            Assert.AreEqual(PrimaryKeyShape.SingleOther, PrimaryKeyShapes.Classify(1, other), other);
    }

    [TestMethod]
    public void A_table_and_its_summary_agree_on_the_shape_of_a_text_key()
    {
        var table = Sample.NaturalKey();
        Assert.AreEqual(PrimaryKeyShape.SingleText, table.PrimaryKeyShape);
        Assert.AreEqual(PrimaryKeyShapes.Classify(1, "char"), table.PrimaryKeyShape);
    }

    [TestMethod]
    public void Each_kind_of_key_describes_how_it_travels()
    {
        var text = Sample.NaturalKey().PrimaryKeyType!;
        Assert.AreEqual(KeyKind.Text, text.Kind);
        Assert.AreEqual("string", text.CSharp);
        Assert.AreEqual("string", text.TypeScript);
        Assert.AreEqual("str", text.Python);
        Assert.AreEqual("{id}", text.RouteSegment());
        Assert.AreEqual("''", text.TypeScriptEmpty);
        Assert.IsTrue(text.ChosenByPerson);
        Assert.IsTrue(text.IsNotInt);
        Assert.AreEqual("GenericRepo<Country, string>", text.RepoBase("Country"));

        var guid = Sample.AccountRef().PrimaryKeyType!;
        Assert.AreEqual(KeyKind.Guid, guid.Kind);
        Assert.AreEqual("Guid", guid.CSharp);
        Assert.AreEqual("string", guid.TypeScript);
        Assert.AreEqual("UUID", guid.Python);
        Assert.AreEqual("{id:guid}", guid.RouteSegment());
        Assert.AreEqual("GenericRepo<AccountRef, Guid>", guid.RepoBase("AccountRef"));

        var number = Sample.Holiday().PrimaryKeyType!;
        Assert.AreEqual(KeyKind.Int, number.Kind);
        Assert.AreEqual("int", number.CSharp);
        Assert.AreEqual("number", number.TypeScript);
        Assert.AreEqual("{id:int}", number.RouteSegment());
        Assert.AreEqual("0", number.TypeScriptEmpty);
        Assert.AreEqual("GenericRepo<Holiday>", number.RepoBase("Holiday"));   // the original form, so existing repositories are unchanged
    }

    [TestMethod]
    public void A_bigint_key_is_a_long_with_a_long_route()
    {
        var table = Sample.Table("Ledger", [Sample.Column("LedgerId", SqlDbType.BigInt, primaryKey: true, ordinal: 1)]);
        var key = table.PrimaryKeyType!;
        Assert.AreEqual("long", key.CSharp);
        Assert.AreEqual("{id:long}", key.RouteSegment());
        Assert.AreEqual("GenericRepo<Ledger, long>", key.RepoBase("Ledger"));
    }

    [TestMethod]
    public void A_table_without_one_supported_key_has_no_key_type()
    {
        Assert.IsNull(Sample.CompositeKey().PrimaryKeyType);
        var dated = Sample.Table("Day", [Sample.Column("When", SqlDbType.Date, primaryKey: true, ordinal: 1)]);
        Assert.AreEqual(PrimaryKeyShape.SingleOther, dated.PrimaryKeyShape);
        Assert.IsNull(dated.PrimaryKeyType);
    }

    private static async Task<string> Render(string template, TableModel table)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, ProjectSettings.FromValues([new("ProjectName", "Acme")]));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    [TestMethod]
    public async Task The_repository_base_follows_the_key_and_an_int_key_keeps_the_original_form()
    {
        Expect.Contains(await Render("CS_Repo_v1.tt", Sample.Holiday()), ": GenericRepo<Holiday>");
        Expect.Contains(await Render("CS_Repo_v1.tt", Sample.NaturalKey()), "public partial class CountryRepo : GenericRepo<Country, string>");
        Expect.Contains(await Render("CS_Repo_v1.tt", Sample.AccountRef()), "public partial class AccountRefRepo : GenericRepo<AccountRef, Guid>");
    }

    [TestMethod]
    public async Task The_generic_repository_is_written_over_the_key_type_with_an_int_shortcut()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("CS_EssentialBase_v1.tt"), ProjectSettings.FromValues([new("ProjectName", "Acme")]));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        var files = GeneratedFiles.Split(result.GeneratedText!).ToDictionary(f => Path.GetFileName(f.RelativePath), f => f.Content.ReplaceLineEndings("\n"));

        string contract = files["IGenericRepo.cs"];
        Expect.Contains(contract, "public interface IGenericRepo<T, TKey> where T : BaseEntity where TKey : notnull");
        Expect.Contains(contract, "public Task<int> DeleteAsync(string deleteFromTable, TKey deleteId);");
        Expect.Contains(contract, "public interface IGenericRepo<T> : IGenericRepo<T, int> where T : BaseEntity");

        string repo = files["GenericRepo.cs"];
        Expect.Contains(repo, "public class GenericRepo<T, TKey> : IGenericRepo<T, TKey>, IDisposable where T : BaseEntity where TKey : notnull");
        Expect.Contains(repo, "public class GenericRepo<T> : GenericRepo<T, int>, IGenericRepo<T> where T : BaseEntity");
        Expect.Contains(repo, "Expression.Call(typeof(EF), nameof(EF.Property), [typeof(TKey)], row, Expression.Constant(keyName));");
        Expect.Contains(repo, "GetAllOrderBy<TOrder>");   // the order type may not be called TKey now
    }

    private static TableModel CountryWithName() => Sample.Table("Country",
    [
        Sample.Column("Alpha3Code", SqlDbType.Char, primaryKey: true, characters: 3, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 60, ordinal: 2)
    ]);

    [TestMethod]
    public async Task The_API_of_a_table_follows_its_key()
    {
        string text = await Render("API_Crud_v1.tt", CountryWithName());
        Expect.Contains(text, "public class CountryApi : CrudApi<Country, CountryRepo, string>");
        Expect.Contains(text, "protected override Expression<Func<Country, string>> Key => c => c.Alpha3Code;");
        Expect.Contains(text, "protected override IQueryable<LookupItem<string>> LookupQuery(IQueryable<Country> rows) =>");
        Expect.Contains(text, "new LookupItem<string>(c.Alpha3Code, c.Name, null)");
        Expect.DoesNotContain(text, "CanFindByName");   // GET <route>/{id} and GET <route>/{name} would be the same route

        string guid = await Render("API_Crud_v1.tt", Sample.AccountRef());
        Expect.Contains(guid, ": CrudApi<AccountRef, AccountRefRepo, Guid>");
        Expect.Contains(guid, "Expression<Func<AccountRef, Guid>> Key");

        string number = await Render("API_Crud_v1.tt", Sample.Holiday());
        Expect.Contains(number, "public class HolidayApi : CrudApi<Holiday, HolidayRepo>");   // the int form is what it always was
        Expect.Contains(number, "Expression<Func<Holiday, int>> Key => c => c.HolidayId;");
        Expect.Contains(number, "new LookupItem(c.HolidayId, c.Name, null)");
        Expect.Contains(number, "protected override bool CanFindByName => true;");
    }

    [TestMethod]
    public async Task The_API_base_is_generic_over_the_key_with_the_int_form_kept_and_a_text_key_is_chosen_by_the_person()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("API_EssentialCrudApi_v1.tt"), ProjectSettings.FromValues([new("ProjectName", "Acme")]));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string cs = result.GeneratedText!.ReplaceLineEndings("\n");

        Expect.Contains(cs, "public abstract class CrudApi<TEntity, TRepo, TKey> : BaseApi<TEntity>");
        Expect.Contains(cs, "where TRepo : GenericRepo<TEntity, TKey>");
        Expect.Contains(cs, "public abstract class CrudApi<TEntity, TRepo> : CrudApi<TEntity, TRepo, int>");
        Expect.Contains(cs, "typeof(TKey) == typeof(int) ? \":int\" : typeof(TKey) == typeof(long) ? \":long\" : typeof(TKey) == typeof(Guid) ? \":guid\" : \"\"");
        Expect.Contains(cs, "if (!SameKey(KeyOf(updatedRow), id))");
        Expect.Contains(cs, "title: \"Key is required\"");
        Expect.Contains(cs, "statusCode: 409, title: \"Duplicate key\"");
    }

    [TestMethod]
    public async Task The_SQL_Server_update_quotes_a_text_or_guid_key_and_leaves_an_int_key_alone()
    {
        // the statement is built as text, so a text key pasted in unquoted is "Invalid column name 'CAN'" (found by running it) and a quote in it would end the literal
        string text = await Render("SP_Update_v1.tt", CountryWithName());
        Expect.Contains(text, "' WHERE ([Alpha3Code] = ''' + REPLACE(RTRIM(LTRIM(@pAlpha3Code)), '''', '''''') + ''')';");

        string guid = await Render("SP_Update_v1.tt", Sample.AccountRef());
        Expect.Contains(guid, "' WHERE ([AccountRefID] = ''' + REPLACE(RTRIM(LTRIM(");

        string number = await Render("SP_Update_v1.tt", Sample.Holiday());
        Expect.Contains(number, "' WHERE ([HolidayId] = ' + @pHolidayId + ')';");
    }

    [TestMethod]
    public void The_ASP_NET_API_serves_guid_and_text_key_tables_and_the_other_stacks_do_not_yet()
    {
        var project = ProjectSettings.FromValues([new("ProjectName", "Acme")]);
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [Sample.NaturalKey(), Sample.AccountRef(), Sample.Holiday()] };

        CollectionAssert.AreEquivalent(new[] { "AccountRef", "Country", "Holiday" }, database.AspNetApiTables(project).Select(t => t.TableName).ToArray());
        CollectionAssert.AreEquivalent(new[] { "Holiday" }, database.ApiTables(project).Select(t => t.TableName).ToArray());
        Assert.IsTrue(Sample.NaturalKey().HasKeyedCrudApi(project));
        Assert.IsFalse(Sample.NaturalKey().HasCrudApi(project));
        Assert.IsFalse(Sample.Holiday().HasKeyedCrudApi(project));
    }

    private static async Task<Dictionary<string, string>> RenderFiles(string template, TableModel table) =>
        GeneratedFiles.Split(await Render(template, table)).ToDictionary(f => Path.GetFileName(f.RelativePath), f => f.Content.ReplaceLineEndings("\n"));

    private static TableModel Observance() => Sample.Table("Observance",
    [
        Sample.Column("ObservanceId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("CountryCode", SqlDbType.Char, characters: 3, ordinal: 2),
        Sample.Column("Title", SqlDbType.NVarChar, characters: 80, ordinal: 3)
    ],
    [Sample.ForeignKey("CountryCode", "Country", "Alpha3Code", "Name")]);

    [TestMethod]
    public void Only_the_stacks_that_read_the_key_type_list_the_screens_of_a_text_key_table()
    {
        var project = ProjectSettings.FromValues([new("ProjectName", "Acme")]);
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [Sample.NaturalKey(), Sample.AccountRef(), Sample.Holiday()] };

        CollectionAssert.AreEquivalent(new[] { "AccountRef", "Country", "Holiday" }, database.AspNetSearchApiTables(project).Select(t => t.TableName).ToArray());
        CollectionAssert.AreEquivalent(new[] { "AccountRef", "Country", "Holiday" }, database.ScreenTables(project, "Angular").Select(t => t.TableName).ToArray());
        CollectionAssert.AreEquivalent(new[] { "Holiday" }, database.ScreenTables(project).Select(t => t.TableName).ToArray());
        CollectionAssert.AreEquivalent(new[] { "AccountRef", "Country", "Holiday" }, database.ScreenTables(project, "React").Select(t => t.TableName).ToArray());
        CollectionAssert.AreEquivalent(new[] { "AccountRef", "Country", "Holiday" }, database.ScreenTables(project, "Angular", "React").Select(t => t.TableName).ToArray());
        CollectionAssert.AreEquivalent(new[] { "AccountRef", "Country", "Holiday" }, database.ScreenTables(project, "Blazor").Select(t => t.TableName).ToArray());
        CollectionAssert.AreEquivalent(new[] { "Holiday" }, database.ScreenTables(project, "Blazor", "Python").Select(t => t.TableName).ToArray());
    }

    [TestMethod]
    public async Task The_Angular_service_and_screen_of_a_text_key_table_follow_the_key()
    {
        string service = (await RenderFiles("TS_Service_v1.tt", CountryWithName()))["country.service.ts"];
        Expect.Contains(service, "export class CountryService extends CrudService<Country, string> {");   // no findByName: GET <route>/{name} would be GET <route>/{id}

        var files = await RenderFiles("TS_Component_v1.tt", CountryWithName());
        string ts = files["country.component.ts"];
        Expect.Contains(ts, "extends PagedCrudScreen<Country, string>");
        Expect.Contains(ts, "protected readonly idKey = 'alpha3Code';");
        Expect.Contains(ts, "protected override readonly emptyKey: string = '';");
        Expect.Contains(ts, "protected override readonly keyChosen = true;");
        Expect.Contains(ts, "return { alpha3Code: '', name: '' };");

        string html = files["country.component.html"];
        Expect.Contains(html, "<h2>{{adding ? 'Add' : 'Edit'}} Country</h2>");
        Expect.Contains(html, "[(ngModel)]=\"selectedRow.alpha3Code\" maxlength=\"3\" [readonly]=\"!adding\"");   // typed on a new row, read-only afterwards
        Expect.Contains(html, "{{ country.alpha3Code }}");   // the key is a column of the grid
        Assert.IsLessThan(html.IndexOf("selectedRow.name", StringComparison.Ordinal), html.IndexOf("selectedRow.alpha3Code", StringComparison.Ordinal));   // the key is the first field
    }

    [TestMethod]
    public async Task The_Angular_screen_of_an_int_key_table_is_what_it_was()
    {
        var files = await RenderFiles("TS_Component_v1.tt", Sample.Holiday());
        Expect.DoesNotContain(files["holiday.component.ts"], "keyChosen");
        Expect.DoesNotContain(files["holiday.component.ts"], "emptyKey");
        Expect.Contains(files["holiday.component.html"], "<h2>{{selectedRow.holidayId ? 'Edit' : 'Add'}} Holiday</h2>");
        Expect.DoesNotContain(files["holiday.component.html"], "[readonly]");
    }

    [TestMethod]
    public async Task A_drop_down_over_a_text_key_parent_reads_the_lookup_with_text_ids()
    {
        var files = await RenderFiles("TS_Component_v1.tt", Observance());
        string ts = files["observance.component.ts"];

        Expect.Contains(ts, "import { Lookup } from '../../services/crud.service';");
        Expect.Contains(ts, "countries: Lookup<string>[] = [];");
        Expect.Contains(ts, "countryName(id?: string): string {");
        Expect.Contains(ts, "this.countryService.getLookup().subscribe((data) => { this.countries = data; });");
        Expect.Contains(files["observance.component.html"], "[ngValue]=\"p.id\">{{p.name}}</option>");
    }

    [TestMethod]
    public async Task The_Angular_master_screen_of_a_text_key_table_shows_its_child_grids_once_the_row_is_saved()
    {
        var table = Sample.Table("Country",
        [
            Sample.Column("Alpha3Code", SqlDbType.Char, primaryKey: true, characters: 3, ordinal: 1),
            Sample.Column("Name", SqlDbType.NVarChar, characters: 60, ordinal: 2)
        ],
        childForeignKeys: [Sample.ChildForeignKey("Observance", "CountryCode", "ObservanceId")]);

        var files = await RenderFiles("TS_DetailMasterComponent_v1.tt", table);
        string ts = files["country-detail-master.component.ts"];
        string html = files["country-detail-master.component.html"];

        Expect.Contains(ts, "extends PagedCrudScreen<Country, string>");
        Expect.Contains(ts, "protected override readonly keyChosen = true;");
        Expect.Contains(ts, "private loadObservance(parentId: string): void {");
        Expect.Contains(html, "*ngIf=\"!adding; else saveObservanceFirst\"");   // a typed key is set on a new row too, so the key cannot say the row is saved
        Expect.Contains(html, "<h2>{{adding ? 'Add' : 'Edit'}} Country</h2>");
    }

    [TestMethod]
    public async Task The_service_base_encodes_the_key_and_the_screen_base_knows_when_a_row_is_new()
    {
        var files = (await CrudScreenTests.Essential());
        string service = files["crud.service.ts"];
        Expect.Contains(service, "getById(id: K): Observable<T> {\n        return this.http.get<T>(`${this.apiUrl}/${encodeURIComponent(id)}`);");
        Expect.Contains(service, "clone(id: K): Observable<T> {\n        return this.http.post<T>(`${this.apiUrl}/${encodeURIComponent(id)}/clone`, null);");

        string screen = files["crud-screen.ts"];
        Expect.Contains(screen, "protected readonly keyChosen: boolean = false;");
        Expect.Contains(screen, "const isNew = this.adding || !this.idOf(row);");
        Expect.Contains(screen, "if (isNew && !this.keyChosen) {");
        Expect.Contains(files["crud-screen.spec.ts"], "creates a new row with the text key the person typed, not an update to it");
    }

    // ------------------------------------------------------------------ request files, API tests and API documents

    private static ProjectSettings ProjectWith(params (string Key, string Value)[] more) =>
        ProjectSettings.FromValues([new("ProjectName", "Acme"), .. more.Select(m => new KeyValuePair<string, string>(m.Key, m.Value))]);

    private static async Task<string> RenderDatabase(string template, ProjectSettings project, params TableModel[] tables)
    {
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [.. tables] };
        var result = await Repo.Cache.RunAsync(Repo.Template(template), database, project);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    [TestMethod]
    public async Task A_request_file_uses_the_key_of_the_table_for_its_variable_route_and_body()
    {
        string text = await Render("API_Http_v1.tt", CountryWithName());
        Expect.Contains(text, "@countryId = Sam\n");   // the key the add request creates, typed by the person
        Expect.Contains(text, "GET {{host}}/api/countries/{{countryId}}");
        Expect.Contains(text, "POST {{host}}/api/countries\nContent-Type: application/json\n\n{\n  \"alpha3Code\": \"Sam\",");
        Expect.Contains(text, "PUT {{host}}/api/countries/{{countryId}}\nContent-Type: application/json\n\n{\n  \"alpha3Code\": \"{{countryId}}\",");
        Expect.Contains(text, "/api/countries/search?");   // a text key table has the search endpoint now

        Expect.Contains(await Render("API_Http_v1.tt", Sample.AccountRef()), "@accountRefId = 00000000-0000-0000-0000-000000000001\n");

        string number = await Render("API_Http_v1.tt", Sample.Holiday());
        Expect.Contains(number, "@id = 1\n");
        Expect.Contains(number, "GET {{host}}/api/holidays/{{id}}");
        Expect.Contains(number, "\"holidayId\": {{id}},");
    }

    [TestMethod]
    public async Task An_API_test_creates_the_row_with_a_key_it_makes_and_finds_it_by_that_key()
    {
        string text = await Render("API_Test_v1.tt", CountryWithName());
        Expect.Contains(text, "[\"alpha3Code\"] = ApiFixture.NewCode(3),");
        Expect.Contains(text, "string id = row[\"alpha3Code\"]!.GetValue<string>();");
        Expect.Contains(text, "client.GetAsync($\"{Route}/{Uri.EscapeDataString(id)}\")");
        Expect.Contains(text, "ApiFixture.Client.GetAsync($\"{Route}/{Uri.EscapeDataString(new string('~', 3))}\")");

        string guid = await Render("API_Test_v1.tt", Sample.AccountRef());
        Expect.Contains(guid, "[\"accountRefID\"] = Guid.NewGuid(),");
        Expect.Contains(guid, "string id = row[\"accountRefID\"]!.GetValue<string>();");
        Expect.Contains(guid, "{Route}/{Guid.NewGuid()}");

        string number = await Render("API_Test_v1.tt", Sample.Holiday());
        Expect.Contains(number, "long id = row[\"holidayId\"]!.GetValue<long>();");
        Expect.Contains(number, "client.GetAsync($\"{Route}/{id}\")");
        Expect.Contains(number, "{Route}/{int.MaxValue}");
    }

    [TestMethod]
    public async Task An_API_test_points_a_text_foreign_key_at_the_first_row_of_its_text_key_parent()
    {
        string text = await Render("API_Test_v1.tt", Observance());
        Expect.Contains(text, "[\"countryCode\"] = await ApiFixture.ExistingCodeAsync(\"/api/countries\", \"alpha3Code\", \"Sam\"),");
        Expect.DoesNotContain(text, "ApiFixture.Unique(\"Sample country code\"");
    }

    [TestMethod]
    public async Task The_fixture_can_make_a_text_key_and_read_a_text_foreign_key()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("API_EssentialTests_v1.tt"), ProjectWith(("ApiTests", "true")));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string text = result.GeneratedText!.ReplaceLineEndings("\n");
        Expect.Contains(text, "public static string NewCode(int length)");
        Expect.Contains(text, "public static async Task<string> ExistingCodeAsync(string route, string keyProperty, string fallback)");
    }

    [TestMethod]
    public async Task The_collections_the_CSV_endpoints_and_the_OpenAPI_document_list_the_tables_of_every_key()
    {
        var tables = new[] { CountryWithName(), Sample.AccountRef(), Sample.Holiday() };

        string postman = await RenderDatabase("MD_Postman_v1.tt", ProjectWith(), tables);
        Expect.Contains(postman, "{ \"key\": \"id\", \"value\": \"1\" },");
        Expect.Contains(postman, "{ \"key\": \"accountRefId\", \"value\": \"00000000-0000-0000-0000-000000000001\" },");
        Expect.Contains(postman, "{ \"key\": \"countryId\", \"value\": \"Sam\" }\n");
        Expect.Contains(postman, "{{countryId}}");

        string bruno = await RenderDatabase("MD_Bruno_v1.tt", ProjectWith(), tables);
        Expect.Contains(bruno, "  id: 1\n  accountRefId: 00000000-0000-0000-0000-000000000001\n  countryId: Sam\n");
        Expect.Contains(bruno, "@@@FILE Country/");
        Expect.Contains(bruno, "url: {{baseUrl}}/api/countries/{{countryId}}");

        string csv = await RenderDatabase("API_Csv_v1.tt", ProjectWith(), tables);
        Expect.Contains(csv, "MapTable<Country,");
        Expect.Contains(csv, "MapTable<AccountRef,");
        Expect.Contains(csv, "MapTable<Holiday,");

        string yaml = await RenderDatabase("API_OpenApi_v1.tt", ProjectWith(), tables);
        var document = new Microsoft.OpenApi.Readers.OpenApiStringReader().Read(yaml.Replace("@@@FILE openapi.yaml@@@\n", ""), out var diagnostic);
        Assert.IsEmpty(diagnostic.Errors);
        var country = document.Paths["/api/countries/{id}"].Operations[Microsoft.OpenApi.Models.OperationType.Get].Parameters[0].Schema;
        Assert.AreEqual("string", country.Type);
        Assert.IsNull(country.Format);
        var guid = document.Paths["/api/accountrefs/{id}"].Operations[Microsoft.OpenApi.Models.OperationType.Get].Parameters[0].Schema;
        Assert.AreEqual("uuid", guid.Format);
        var number = document.Paths["/api/holidays/{id}"].Operations[Microsoft.OpenApi.Models.OperationType.Get].Parameters[0].Schema;
        Assert.AreEqual("integer", number.Type);
        Assert.IsTrue(document.Paths.ContainsKey("/api/countries/search"));
    }

    [TestMethod]
    public async Task A_project_that_generates_Rust_documents_the_tables_of_every_key_too()
    {
        string yaml = await RenderDatabase("API_OpenApi_v1.tt", ProjectWith(("Stacks", "Rust")), CountryWithName(), Sample.Holiday());
        Expect.Contains(yaml, "/api/holidays:");
        Expect.Contains(yaml, "/api/countries:");
    }

    // ------------------------------------------------------------------ Python and Rust

    [TestMethod]
    public async Task The_Python_routes_of_a_text_key_table_take_a_string_and_refuse_a_blank_or_taken_key()
    {
        string py = BlazorFiles(await Render("PY_Routes_v1.tt", CountryWithName()))["country.py"];
        Expect.Contains(py, "def get_by_id(id: str, session: Session = Depends(get_session)):");
        Expect.Contains(py, "def update(id: str, body: CountrySchema, session: Session = Depends(get_session)):");
        Expect.Contains(py, "from urllib.parse import quote");
        Expect.Contains(py, "    if not body.alpha3_code.strip():\n        raise ApiError(400, \"Key is required\")\n    if session.get(Country, body.alpha3_code) is not None:\n        raise ApiError(409, \"Duplicate key\")");
        Expect.Contains(py, "quote(row.alpha3_code, safe='')");
        Expect.DoesNotContain(py, "uuid4");

        string schema = BlazorFiles(await Render("PY_Schema_v1.tt", CountryWithName()))["country.py"];
        Expect.Contains(schema, "alpha3_code: str = Field(alias=\"alpha3Code\")");   // no default: the person types it

        string number = BlazorFiles(await Render("PY_Routes_v1.tt", Sample.Holiday()))["holiday.py"];
        Expect.Contains(number, "def get_by_id(id: int,");
        Expect.DoesNotContain(number, "Duplicate key");
        Expect.DoesNotContain(number, "quote");
    }

    [TestMethod]
    public async Task The_Python_routes_of_a_guid_key_table_give_the_empty_guid_a_new_value()
    {
        string py = BlazorFiles(await Render("PY_Routes_v1.tt", Sample.AccountRef()))["account_ref.py"];
        Expect.Contains(py, "from uuid import UUID, uuid4");
        Expect.Contains(py, "def get_by_id(id: UUID,");
        Expect.Contains(py, "    if row.account_ref_id == UUID(int=0):\n        row.account_ref_id = uuid4()");
    }

    [TestMethod]
    public async Task The_Python_package_lists_cover_the_tables_of_every_key()
    {
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [CountryWithName(), Sample.AccountRef(), Sample.Holiday()] };
        string text = await RenderDatabase("PY_Mod_v1.tt", ProjectWith(), [.. database.Tables]);
        Expect.Contains(text, "from app.routes import country");
        Expect.Contains(text, "from app.routes import account_ref");
        Expect.Contains(text, "from app.routes import holiday");

        var templates = TemplateCatalog.Discover(Repo.TemplatesDirectory);
        var postgres = new DatabaseModel { DatabaseName = "Acme", SchemaName = "public", Dialect = SqlDialect.PostgreSql, Tables = database.Tables };   // the Rust stack is not written for SQL Server
        var steps = ProjectPlan.Build(templates, postgres, ProjectWith(), ["Python", "Rust"]).ToDictionary(s => s.Template.Name);
        foreach (string name in new[] { "PY_Model", "PY_Routes", "PY_Schema", "RS_Repo", "RS_Routes", "RS_Struct" })
            CollectionAssert.AreEquivalent(new[] { "AccountRef", "Country", "Holiday" }, steps[name].TableNames.ToArray(), name);
    }

    [TestMethod]
    public async Task The_Rust_repository_and_routes_of_a_text_key_table_pass_the_key_by_reference()
    {
        var repo = BlazorFiles(await Render("RS_Repo_v1.tt", CountryWithName()))["country.rs"];
        Expect.Contains(repo, "pub async fn get_by_id(pool: &Pool, id: &str) -> Result<Option<Country>, sqlx::Error>");
        Expect.Contains(repo, "pub async fn update(pool: &Pool, id: &str, row: &Country)");
        Expect.Contains(repo, "pub async fn delete(pool: &Pool, id: &str)");

        var routes = BlazorFiles(await Render("RS_Routes_v1.tt", CountryWithName()))["country.rs"];
        Expect.Contains(routes, "Path(id): Path<String>");
        Expect.Contains(routes, "repo::get_by_id(&pool, &id)");
        Expect.Contains(routes, "repo::update(&pool, &id, &row)");
        Expect.Contains(routes, "repo::delete(&pool, &id)");
        Expect.Contains(routes, "return Err(ApiError::BadRequest(\"Key is required\".into()));");
        Expect.Contains(routes, "return Err(ApiError::Conflict(\"Duplicate key\".into()));");
        Expect.Contains(routes, "path_segment(&row.alpha3_code)");

        var number = BlazorFiles(await Render("RS_Routes_v1.tt", Sample.Holiday()))["holiday.rs"];
        Expect.Contains(number, "repo::get_by_id(&pool, id)");
        Expect.DoesNotContain(number, "Conflict");
        Expect.DoesNotContain(number, "path_segment");
    }

    [TestMethod]
    public async Task The_Rust_routes_of_a_guid_key_table_give_the_empty_guid_a_new_value()
    {
        var routes = BlazorFiles(await Render("RS_Routes_v1.tt", Sample.AccountRef()))["account_ref.rs"];
        Expect.Contains(routes, "ApiJson(mut row): ApiJson<AccountRef>");
        Expect.Contains(routes, "if row.account_ref_id.is_nil() {\n        row.account_ref_id = uuid::Uuid::new_v4();");
        Expect.Contains(routes, "Path(id): Path<Uuid>");
    }

    [TestMethod]
    public async Task The_Rust_support_files_hold_what_a_text_or_guid_key_needs()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("RS_EssentialMain_v1.tt"), ProjectWith());
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string main = result.GeneratedText!.ReplaceLineEndings("\n");
        Expect.Contains(main, "Conflict(String),");
        Expect.Contains(main, "ApiError::Conflict(message) => (StatusCode::CONFLICT, message).into_response(),");
        Expect.Contains(main, "pub fn path_segment(value: &str) -> String {");

        var cargo = await Repo.Cache.RunAsync(Repo.Template("RS_EssentialCargo_v1.tt"), ProjectWith());
        Assert.IsTrue(cargo.Success, string.Join(" | ", cargo.Errors));
        Expect.Contains(cargo.GeneratedText!, "uuid = { version = \"1\", features = [\"serde\", \"v4\"] }");
    }

    [TestMethod]
    public void A_plan_runs_the_request_and_test_templates_for_the_tables_of_every_key()
    {
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [CountryWithName(), Sample.AccountRef(), Sample.Holiday(), Sample.CompositeKey()] };
        var templates = TemplateCatalog.Discover(Repo.TemplatesDirectory);
        var project = ProjectWith(("ApiHttp", "true"), ("ApiTests", "true"));

        var steps = ProjectPlan.Build(templates, database, project, ["Api"]).ToDictionary(s => s.Template.Name);
        foreach (string name in new[] { "API_Http", "API_Test" })
            CollectionAssert.AreEquivalent(new[] { "AccountRef", "Country", "Holiday" }, steps[name].TableNames.ToArray(), name);
        CollectionAssert.AreEquivalent(new[] { "AccountRef", "Country", "Holiday" }, steps["API_Search"].TableNames.ToArray());
    }

    [TestMethod]
    public void The_text_requirement_accepts_a_text_key_and_the_older_tiers_do_not()
    {
        var all = new TemplateConfig { RequiredPrimaryKeyShape = PrimaryKeyRequirement.SingleIntGuidOrText };
        Assert.IsTrue(all.PrimaryKeyShapeSatisfies(PrimaryKeyShape.SingleText));
        Assert.IsTrue(all.PrimaryKeyShapeSatisfies(PrimaryKeyShape.SingleInt));
        Assert.IsTrue(all.PrimaryKeyShapeSatisfies(PrimaryKeyShape.SingleUniqueIdentifier));
        Assert.IsFalse(all.PrimaryKeyShapeSatisfies(PrimaryKeyShape.SingleOther));
        Assert.IsFalse(all.PrimaryKeyShapeSatisfies(PrimaryKeyShape.Composite));

        Assert.IsFalse(new TemplateConfig { RequiredPrimaryKeyShape = PrimaryKeyRequirement.SingleIntOrGuid }.PrimaryKeyShapeSatisfies(PrimaryKeyShape.SingleText));
        Assert.IsFalse(new TemplateConfig { RequiredPrimaryKeyShape = PrimaryKeyRequirement.SingleInt }.PrimaryKeyShapeSatisfies(PrimaryKeyShape.SingleText));
        Assert.IsTrue(new TemplateConfig { RequiredPrimaryKeyShape = PrimaryKeyRequirement.SingleColumn }.PrimaryKeyShapeSatisfies(PrimaryKeyShape.SingleText));
    }

    // ------------------------------------------------------------------ React

    private static TableModel CountryWithChild() => Sample.Table("Country",
    [
        Sample.Column("Alpha3Code", SqlDbType.Char, primaryKey: true, characters: 3, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 60, ordinal: 2)
    ],
    childForeignKeys: [Sample.ChildForeignKey("Observance", "CountryCode", "Alpha3Code", childOwnPrimaryKey: ["ObservanceId"])]);

    // a child whose own key is text: its key is encoded into the link to its screen and into the delete route
    private static TableModel CountryWithTextKeyChild() => Sample.Table("Country",
    [
        Sample.Column("Alpha3Code", SqlDbType.Char, primaryKey: true, characters: 3, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 60, ordinal: 2)
    ],
    childForeignKeys:
    [
        new ChildForeignKeyModel
        {
            ConstraintName = "FK_CountryCode_Region",
            ReferencingSchema = "dbo",
            ReferencingTable = "Region",
            ReferencingColumns = ["CountryCode"],
            ReferencedColumns = ["Alpha3Code"],
            ReferencingPrimaryKeyColumns = ["RegionCode"],
            ReferencingTableColumns =
            [
                Sample.Column("RegionCode", SqlDbType.Char, primaryKey: true, characters: 5, ordinal: 1),
                Sample.Column("CountryCode", SqlDbType.Char, characters: 3, ordinal: 2)
            ]
        }
    ]);

    [TestMethod]
    public async Task The_React_api_module_of_a_text_key_table_encodes_the_key_and_has_no_find_by_name()
    {
        string api = GeneratedFiles.Split(await Render("TSX_Api_v1.tt", CountryWithName())).Single().Content.ReplaceLineEndings("\n");
        Expect.Contains(api, "getById: (id: string) => request<Country>(`${apiUrl}/${encodeURIComponent(id)}`),");
        Expect.Contains(api, "update: (id: string, country: Country) => request<Country>(`${apiUrl}/${encodeURIComponent(id)}`, { method: 'PUT'");
        Expect.Contains(api, "delete: (id: string) => request<void>(`${apiUrl}/${encodeURIComponent(id)}`, { method: 'DELETE' }),");
        Expect.DoesNotContain(api, "findByName");   // GET <route>/{id} and GET <route>/{name} would be the same route

        string guid = GeneratedFiles.Split(await Render("TSX_Api_v1.tt", Sample.AccountRef())).Single().Content.ReplaceLineEndings("\n");
        Expect.Contains(guid, "getById: (id: string) => request<AccountRef>(`${apiUrl}/${id}`),");

        string number = GeneratedFiles.Split(await Render("TSX_Api_v1.tt", Sample.Holiday())).Single().Content.ReplaceLineEndings("\n");
        Expect.Contains(number, "getById: (id: number) => request<Holiday>(`${apiUrl}/${id}`),");
        Expect.DoesNotContain(number, "encodeURIComponent");
    }

    [TestMethod]
    public async Task The_React_page_of_a_text_key_table_types_the_key_on_a_new_row_and_locks_it_afterwards()
    {
        var files = GeneratedFiles.Split(await Render("TSX_Page_v1.tt", CountryWithName())).ToDictionary(f => Path.GetFileName(f.RelativePath), f => f.Content.ReplaceLineEndings("\n"));
        string tsx = files["CountryPage.tsx"];

        Expect.Contains(tsx, "const [adding, setAdding] = useState(false);");
        Expect.Contains(tsx, "      if (adding) {\n        await countryApi.create(country);\n      } else {\n        await countryApi.update(country.alpha3Code!, country);\n      }");   // the typed key says nothing about whether the row is new
        Expect.Contains(tsx, "setAdding(true);");
        Expect.Contains(tsx, "setSelectedRow({ alpha3Code: '', name: '' });");
        Expect.Contains(tsx, "setAdding(false);");
        Expect.Contains(tsx, "<h2>{adding ? 'Add' : 'Edit'} Country</h2>");
        Expect.Contains(tsx, "readOnly={!adding}");
        Expect.Contains(tsx, "<td>{country.alpha3Code}</td>");   // the key is a column of the grid
        Expect.Contains(tsx, "countryApi.getById(openId)");   // a text key is not a number
        Expect.Contains(tsx, "err.status === 409) { setError('A country with this alpha3 code already exists!'); }");   // the API refuses a key that is taken
        string spec = files["CountryPage.test.tsx"];
        Expect.Contains(spec, "import { fireEvent, render, screen } from '@testing-library/react';");
        Expect.Contains(spec, "expect(screen.getByLabelText('Alpha3 Code')).not.toHaveAttribute('readonly');");
        Expect.Contains(spec, "expect(screen.getByLabelText('Alpha3 Code')).toHaveAttribute('readonly');");
        Assert.IsLessThan(tsx.IndexOf("selectedRow.name", StringComparison.Ordinal), tsx.IndexOf("value={selectedRow.alpha3Code ?? ''}", StringComparison.Ordinal));   // the key is the first field
    }

    [TestMethod]
    public async Task The_React_page_of_an_int_key_table_is_what_it_was()
    {
        string tsx = GeneratedFiles.Split(await Render("TSX_Page_v1.tt", Sample.Holiday())).First().Content.ReplaceLineEndings("\n");
        Expect.DoesNotContain(tsx, "adding");
        Expect.DoesNotContain(tsx, "readOnly");
        Expect.DoesNotContain(tsx, "409");
        Expect.Contains(tsx, "<h2>{selectedRow.holidayId ? 'Edit' : 'Add'} Holiday</h2>");
        Expect.Contains(tsx, "      if (holiday.holidayId) {\n        await holidayApi.update(holiday.holidayId, holiday);\n      } else {\n        holiday.holidayId = 0;");
        Expect.Contains(tsx, "holidayApi.getById(Number(openId))");
    }

    [TestMethod]
    public async Task A_React_drop_down_over_a_text_key_parent_compares_codes_to_codes()
    {
        string tsx = GeneratedFiles.Split(await Render("TSX_Page_v1.tt", Observance())).First().Content.ReplaceLineEndings("\n");
        Expect.Contains(tsx, "const countryName = (id?: string): string => {");
        Expect.Contains(tsx, "onChange={(event) => setSelectedRow({ ...selectedRow, countryCode: event.target.value })}");   // not Number(...)
        Expect.Contains(tsx, "value={selectedRow.countryCode ?? ''}");   // a blank first choice, not 0
    }

    [TestMethod]
    public async Task The_React_master_page_of_a_text_key_table_shows_its_child_grids_once_the_row_is_saved()
    {
        var files = GeneratedFiles.Split(await Render("TSX_DetailMasterPage_v1.tt", CountryWithChild())).ToDictionary(f => Path.GetFileName(f.RelativePath), f => f.Content.ReplaceLineEndings("\n"));
        string tsx = files["CountryDetailMasterPage.tsx"];

        Expect.Contains(tsx, "const [adding, setAdding] = useState(false);");
        Expect.Contains(tsx, "{!adding ? (");   // a typed key is set on a new row too, so the key cannot say the row is saved
        Expect.Contains(tsx, "<h2>{adding ? 'Add' : 'Edit'} Country</h2>");
        Expect.Contains(tsx, "const loadObservance = (parentId: string) => {");
        Expect.Contains(tsx, "?edit=${row['observanceId']}&back=${encodeURIComponent(`/country?edit=${encodeURIComponent(selectedRow?.alpha3Code ?? '')}`)}");   // the text key of the parent is encoded into the link back
        Expect.Contains(tsx, "readOnly={!adding}");
    }

    [TestMethod]
    public async Task The_React_master_page_encodes_the_text_key_of_a_child_in_its_link_and_delete_route()
    {
        string tsx = GeneratedFiles.Split(await Render("TSX_DetailMasterPage_v1.tt", CountryWithTextKeyChild())).First().Content.ReplaceLineEndings("\n");
        Expect.Contains(tsx, "?edit=${encodeURIComponent(row['regionCode'])}&back=");
        Expect.Contains(tsx, "/api/regions/${encodeURIComponent(row['regionCode'])}`, { method: 'DELETE' }");

        string intChild = GeneratedFiles.Split(await Render("TSX_DetailMasterPage_v1.tt", CountryWithChild())).First().Content.ReplaceLineEndings("\n");
        Expect.Contains(intChild, "?edit=${row['observanceId']}&back=");
        Expect.Contains(intChild, "/api/observances/${row['observanceId']}`, { method: 'DELETE' }");
    }

    [TestMethod]
    public async Task The_React_master_page_of_an_int_key_table_links_its_children_as_it_did()
    {
        string tsx = GeneratedFiles.Split(await Render("TSX_DetailMasterPage_v1.tt", Sample.OrderWithLines())).First().Content.ReplaceLineEndings("\n");
        Expect.DoesNotContain(tsx, "adding");
        Expect.Contains(tsx, "&back=${encodeURIComponent(`/order?edit=${selectedRow?.orderId}`)}");
    }

    [TestMethod]
    public async Task The_React_screens_list_the_tables_of_every_key()
    {
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [CountryWithName(), Sample.AccountRef(), Sample.Holiday()] };
        string tsx = await RenderDatabase("TSX_Screens_v1.tt", ProjectWith(), [.. database.Tables]);
        foreach (string route in new[] { "country", "account-ref", "holiday" })
            Expect.Contains(tsx, $"path: '{route}'");
    }

    // ------------------------------------------------------------------ Blazor

    private static Dictionary<string, string> BlazorFiles(string text) =>
        GeneratedFiles.Split(text).ToDictionary(f => Path.GetFileName(f.RelativePath), f => f.Content.ReplaceLineEndings("\n"));

    [TestMethod]
    public async Task The_Blazor_client_of_a_text_key_table_takes_a_string_and_escapes_it_into_the_route()
    {
        string text = BlazorFiles(await Render("BLZ_Client_v1.tt", CountryWithName()))["CountryClient.cs"];
        Expect.Contains(text, "public async Task<Country> GetAsync(string id) => await (await http.GetAsync($\"{Url}/{Uri.EscapeDataString(id)}\")).ReadAsync<Country>();");
        Expect.Contains(text, "UpdateAsync(string id, Country row)");
        Expect.Contains(text, "DeleteAsync(string id)");

        string guid = BlazorFiles(await Render("BLZ_Client_v1.tt", Sample.AccountRef()))["AccountRefClient.cs"];
        Expect.Contains(guid, "GetAsync(Guid id) => await (await http.GetAsync($\"{Url}/{id}\"))");

        string number = BlazorFiles(await Render("BLZ_Client_v1.tt", Sample.Holiday()))["HolidayClient.cs"];
        Expect.Contains(number, "GetAsync(int id) => await (await http.GetAsync($\"{Url}/{id}\"))");
        Expect.DoesNotContain(number, "EscapeDataString(id)");
    }

    [TestMethod]
    public async Task The_Blazor_page_of_a_text_key_table_types_the_key_on_a_new_row_and_locks_it_afterwards()
    {
        string razor = BlazorFiles(await Render("BLZ_Page_v1.tt", CountryWithName()))["CountryPage.razor"];
        Expect.Contains(razor, "<InputText id=\"country-alpha3code\" @bind-Value=\"row.Alpha3Code\" maxlength=\"3\" readonly=\"@(!isNew)\" required />");
        Expect.Contains(razor, "<td>@item.Alpha3Code</td>");   // the key is a column of the grid
        Assert.IsLessThan(razor.IndexOf("row.Name", StringComparison.Ordinal), razor.IndexOf("row.Alpha3Code", StringComparison.Ordinal));   // the key is the first field
        Expect.Contains(razor, "catch (ApiException ex) when (ex.Status == 409) { message = \"A country with this alpha3 code already exists!\"; }");
        Expect.Contains(razor, "await Client.UpdateAsync(row.Alpha3Code, row);");

        string number = BlazorFiles(await Render("BLZ_Page_v1.tt", Sample.Holiday()))["HolidayPage.razor"];
        Expect.DoesNotContain(number, "readonly=");
        Expect.DoesNotContain(number, "409");
        Expect.DoesNotContain(number, "row.HolidayId\"");
    }

    [TestMethod]
    public async Task A_Blazor_drop_down_over_a_text_key_parent_starts_blank_and_compares_codes()
    {
        string razor = BlazorFiles(await Render("BLZ_Page_v1.tt", Observance()))["ObservancePage.razor"];
        Expect.Contains(razor, "<option value=\"\">Select</option>");   // not default(string), which would render the word Select as the value
        Expect.Contains(razor, "private string NameCountry(string? id)");
        Expect.Contains(razor, "@bind-Value=\"row.CountryCode\"");
    }

    [TestMethod]
    public async Task The_Blazor_model_and_registration_cover_the_tables_of_every_key()
    {
        Expect.Contains(BlazorFiles(await Render("BLZ_Model_v1.tt", CountryWithName()))["Country.cs"], "public string Alpha3Code { get; set; } = \"\";");

        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [CountryWithName(), Sample.AccountRef(), Sample.Holiday()] };
        string text = await RenderDatabase("BLZ_Screens_v1.tt", ProjectWith(), [.. database.Tables]);
        foreach (string name in new[] { "Country", "AccountRef", "Holiday" })
        {
            Expect.Contains(text, $"services.AddScoped<{name}Client>();");
        }
        Expect.Contains(text, "href=\"country\"");

        var templates = TemplateCatalog.Discover(Repo.TemplatesDirectory);
        var steps = ProjectPlan.Build(templates, database, ProjectWith(), ["Blazor"]).ToDictionary(s => s.Template.Name);
        foreach (string name in new[] { "BLZ_Client", "BLZ_Model", "BLZ_Page" })
            CollectionAssert.AreEquivalent(new[] { "AccountRef", "Country", "Holiday" }, steps[name].TableNames.ToArray(), name);
    }

    // ------------------------------------------------------------------ WinUI3

    [TestMethod]
    public async Task The_WinUI3_list_page_of_a_text_key_table_carries_the_key_as_text_and_shows_it_as_a_column()
    {
        var files = BlazorFiles(await Render("WinUI3_MasterScreen_v1.tt", CountryWithName()));
        string code = files["CountryListPage.xaml.cs"];
        Expect.Contains(code, "string id = (string)((Button)sender).Tag;");
        Expect.DoesNotContain(code, "(int)((Button)sender).Tag");
        Expect.Contains(code, "FindAsync(id)");

        string vm = files["CountryListViewModel.cs"];
        Expect.Contains(vm, "public string Id { get; init; } = \"\";");
        Expect.Contains(vm, "public async Task DeleteAsync(string id)");
        Expect.Contains(vm, "Id = e.Alpha3Code");
        Expect.Contains(vm, "e.Alpha3Code,");   // the key is a column of the grid
        string xaml = files["CountryListPage.xaml"];
        Assert.IsGreaterThanOrEqualTo(0, xaml.IndexOf("Content=\"Alpha3 Code\"", StringComparison.Ordinal));
        Assert.IsLessThan(xaml.IndexOf("Content=\"Name\"", StringComparison.Ordinal), xaml.IndexOf("Content=\"Alpha3 Code\"", StringComparison.Ordinal));   // the key is the first column
    }

    [TestMethod]
    public async Task The_WinUI3_list_page_of_an_int_key_table_is_what_it_was()
    {
        var files = BlazorFiles(await Render("WinUI3_MasterScreen_v1.tt", Sample.Holiday()));
        Expect.Contains(files["HolidayListPage.xaml.cs"], "int id = (int)((Button)sender).Tag;");
        Expect.Contains(files["HolidayListViewModel.cs"], "public int Id { get; init; }\n");
        Expect.DoesNotContain(files["HolidayListViewModel.cs"], "\n                e.HolidayId,\n");   // not a cell
    }

    [TestMethod]
    public async Task The_WinUI3_dialog_of_a_text_key_table_types_the_key_on_a_new_row_and_locks_it_afterwards()
    {
        var files = BlazorFiles(await Render("WinUI3_DetailScreen_v1.tt", CountryWithName()));
        string xaml = files["CountryDetailDialog.xaml"];
        Expect.Contains(xaml, "Text=\"{x:Bind ViewModel.Alpha3Code, Mode=TwoWay}\" MaxLength=\"3\" IsReadOnly=\"{x:Bind ViewModel.IsExisting}\"");
        Assert.IsLessThan(xaml.IndexOf("ViewModel.Name,", StringComparison.Ordinal), xaml.IndexOf("ViewModel.Alpha3Code,", StringComparison.Ordinal));   // the key is the first field

        string vm = files["CountryDetailViewModel.cs"];
        Expect.Contains(vm, "public bool IsExisting => _editing is not null;");
        Expect.Contains(vm, "if (_editing is null && await _repo.ExistsAsync(entity.Alpha3Code))");   // a key that is taken is a message, not an exception
        Expect.Contains(vm, "A country with this alpha3 code already exists.");

        var number = BlazorFiles(await Render("WinUI3_DetailScreen_v1.tt", Sample.Holiday()));
        Expect.DoesNotContain(number["HolidayDetailDialog.xaml"], "IsReadOnly");
        Expect.DoesNotContain(number["HolidayDetailViewModel.cs"], "IsExisting");
        Expect.DoesNotContain(number["HolidayDetailViewModel.cs"], "ExistsAsync");
    }

    [TestMethod]
    public async Task The_WinUI3_master_dialog_of_a_text_key_table_filters_its_child_grids_by_the_text_key()
    {
        var files = BlazorFiles(await Render("WinUI3_DetailMasterScreen_v1.tt", CountryWithChild()));
        string vm = files["CountryDetailMasterViewModel.cs"];
        Expect.Contains(vm, "EF.Property<string>(c, \"CountryCode\") == _editing!.Alpha3Code");
        Expect.Contains(vm, "_context.Set<Observance>().AsNoTracking()");   // tracked children would be deleted with their parent
        Expect.Contains(vm, "public bool IsExisting => _editing is not null;");
        Expect.Contains(files["CountryDetailMasterDialog.xaml"], "IsReadOnly=\"{x:Bind ViewModel.IsExisting}\"");
    }

    // ------------------------------------------------------------------ foreign keys to a text key

    [TestMethod]
    public void A_foreign_key_has_the_key_type_of_its_referencing_column()
    {
        var observance = Observance();
        Assert.AreEqual(KeyKind.Text, observance.ForeignKeyType(observance.ForeignKeys[0])!.Kind);
        Assert.AreEqual("string", observance.ForeignKeyType(observance.ForeignKeys[0])!.TypeScript);
        Assert.AreEqual("string", observance.ForeignKeyType(observance.ForeignKeys[0])!.CSharp);
        Assert.IsNull(observance.ForeignKeyType(new ForeignKeyModel { ConstraintName = "x", ReferencingColumns = ["A", "B"], ReferencedSchema = "dbo", ReferencedTable = "T", ReferencedColumns = ["A", "B"] }));   // a composite foreign key has no key type
    }

    [TestMethod]
    public async Task The_WinUI3_list_names_the_country_of_a_row_by_its_text_key()
    {
        string vm = BlazorFiles(await Render("WinUI3_MasterScreen_v1.tt", Observance()))["ObservanceListViewModel.cs"];
        Expect.Contains(vm, "countryNames.TryGetValue(e.CountryCode, out var countryCodeName) ? countryCodeName : e.CountryCode.ToString()");
        Expect.Contains(vm, "ToDictionaryAsync(r => r.Alpha3Code");
    }

    [TestMethod]
    public async Task The_WinUI3_dialog_offers_the_parents_of_a_text_foreign_key_as_a_drop_down_of_codes()
    {
        var files = BlazorFiles(await Render("WinUI3_DetailScreen_v1.tt", Observance()));
        string vm = files["ObservanceDetailViewModel.cs"];
        Expect.Contains(vm, "public class ObservanceDetailLookupOptionString\n{\n    public string Id { get; set; } = \"\";");
        Expect.Contains(vm, "public ObservableCollection<ObservanceDetailLookupOptionString> CountryOptions { get; } = [];");
        Expect.Contains(vm, "private string? _countryCode;");
        Expect.Contains(vm, "entity.CountryCode = CountryCode;");
        Expect.DoesNotContain(vm, "CountryCode.Value");
        Expect.Contains(files["ObservanceDetailDialog.xaml"], "<ComboBox Header=\"Country Code\" ItemsSource=\"{x:Bind ViewModel.CountryOptions}\"");

        // an int foreign key is what it was
        var number = BlazorFiles(await Render("WinUI3_DetailScreen_v1.tt", Sample.Holiday()));
        Expect.DoesNotContain(number["HolidayDetailViewModel.cs"], "LookupOptionString");
    }

    [TestMethod]
    public async Task A_WinUI3_child_grid_resolves_a_text_foreign_key_of_a_child_row_by_name()
    {
        var childOther = Sample.ForeignKey("CountryCode", "Country", "Alpha3Code", "Name");
        var table = Sample.Table("Region",
        [
            Sample.Column("RegionId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Name", SqlDbType.NVarChar, characters: 40, ordinal: 2)
        ],
        childForeignKeys:
        [
            new ChildForeignKeyModel
            {
                ConstraintName = "FK_RegionId_Office",
                ReferencingSchema = "dbo",
                ReferencingTable = "Office",
                ReferencingColumns = ["RegionId"],
                ReferencedColumns = ["RegionId"],
                ReferencingPrimaryKeyColumns = ["OfficeId"],
                ReferencingTableForeignKeys = [childOther],
                ReferencingTableColumns =
                [
                    Sample.Column("OfficeId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
                    Sample.Column("RegionId", SqlDbType.Int, ordinal: 2),
                    Sample.Column("CountryCode", SqlDbType.Char, characters: 3, ordinal: 3)
                ]
            }
        ]);
        string vm = BlazorFiles(await Render("WinUI3_DetailMasterScreen_v1.tt", table))["RegionDetailMasterViewModel.cs"];
        Expect.Contains(vm, "raw is string rawIdCountryCode");   // the id is a string, so a dictionary of names keyed by string finds it
        Expect.DoesNotContain(vm, "raw is int rawId");
    }

    [TestMethod]
    public void A_React_plan_runs_the_api_and_page_templates_for_the_tables_of_every_key()
    {
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [CountryWithName(), Sample.AccountRef(), Sample.Holiday(), Sample.CompositeKey()] };
        var templates = TemplateCatalog.Discover(Repo.TemplatesDirectory);

        var steps = ProjectPlan.Build(templates, database, ProjectWith(), ["React"]).ToDictionary(s => s.Template.Name);
        CollectionAssert.AreEquivalent(new[] { "AccountRef", "Country", "Holiday" }, steps["TSX_Page"].TableNames.ToArray());
        CollectionAssert.AreEquivalent(new[] { "AccountRef", "Country", "Holiday" }, steps["TSX_Api"].TableNames.Where(n => n != "CompositeKey").ToArray());
    }
}
