using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The slim lookup list (GET &lt;route&gt;/lookup answers { id, name, isActive }): the API base, the class API_Crud writes, and the Angular drop-downs that read it. </summary>
[TestClass]
public class LookupTests
{
    private static ProjectSettings Project() => ProjectSettings.FromValues([new("ProjectName", "Acme")]);

    private static async Task<string> Text(string template, TableModel table)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, Project());
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    [TestMethod]
    public async Task The_API_bases_answer_the_lookup_route_and_the_item_is_the_key_the_name_and_the_flag()
    {
        string cs = await CrudApiTests.Essential();

        Expect.Contains(cs, "public record LookupItem(int Id, string Name, bool? IsActive = null);");
        Expect.Contains(cs, "protected virtual bool CanLookup => false;");
        Expect.Contains(cs, "protected virtual IQueryable<LookupItem> LookupQuery(IQueryable<TEntity> rows) => throw new NotSupportedException();");
        Expect.Contains(cs, "app.MapGet(apiSubDir + \"/lookup\",");
        Expect.Contains(cs, "if (CanLookup)");
        Expect.Contains(cs, "LookupQuery(context.Set<TEntity>().AsNoTracking()).ToListAsync()");
        // a name and active table always has one, read with only the key, the name and the flag
        Expect.Contains(cs, ".Select(c => new LookupItem(EF.Property<int>(c, keyName), c.Name, c.IsActive))");
    }

    [TestMethod]
    public async Task A_table_with_a_display_column_reads_only_the_key_and_that_column()
    {
        string holiday = await Text("API_Crud_v1.tt", Sample.Holiday());

        Expect.Contains(holiday, "protected override bool CanLookup => true;");
        Expect.Contains(holiday, "rows.OrderBy(c => c.Name).Select(c => new LookupItem(c.HolidayId, c.Name, null));");
    }

    [TestMethod]
    public async Task A_table_with_no_display_column_has_no_lookup()
    {
        string cs = await Text("API_Crud_v1.tt", Sample.Table("Plain",
        [
            Sample.Column("PlainId", System.Data.SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Amount", System.Data.SqlDbType.Decimal, ordinal: 2)
        ]));

        Expect.DoesNotContain(cs, "CanLookup");
    }

    [TestMethod]
    public async Task A_name_and_active_class_leaves_the_lookup_to_its_base()
    {
        Expect.DoesNotContain(await Text("API_Crud_v1.tt", Sample.DepartmentTeam()), "CanLookup");
    }

    [TestMethod]
    public async Task The_service_base_has_getLookup_and_the_Lookup_type()
    {
        string service = (await CrudScreenTests.Essential())["crud.service.ts"];

        Expect.Contains(service, "export interface Lookup {");
        Expect.Contains(service, "getLookup(): Observable<Lookup[]> {");
        Expect.Contains(service, "`${this.apiUrl}/lookup`");
    }

    [TestMethod]
    public async Task A_drop_down_over_a_parent_reads_the_lookup_list_and_names_the_id_from_it()
    {
        var files = GeneratedFiles.Split(await Text("TS_Component_v1.tt", Sample.DonateLeave())).ToDictionary(f => Path.GetFileName(f.RelativePath), f => f.Content.ReplaceLineEndings("\n"));
        string ts = files["donateleave.component.ts"];
        string html = files["donateleave.component.html"];

        Expect.Contains(ts, "import { Lookup } from '../../services/crud.service';");
        Expect.Contains(ts, "employees: Lookup[] = [];");
        Expect.Contains(ts, "this.employeeService.getLookup().subscribe((data) => { this.employees = data; });");
        Expect.DoesNotContain(ts, "import { Employee }");   // the whole model is no longer needed for a drop-down
        Expect.Contains(ts, "this.employees.find((p) => p.id === id)?.name ?? ''");
        Expect.Contains(html, "[ngValue]=\"p.id\">{{p.name}}</option>");
    }

    [TestMethod]
    public async Task A_screen_over_its_own_table_reads_its_own_lookup()
    {
        var files = GeneratedFiles.Split(await Text("TS_Component_v1.tt", Sample.Table("Person",
        [
            Sample.Column("PersonId", System.Data.SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Name", System.Data.SqlDbType.NVarChar, characters: 50, ordinal: 2),
            Sample.Column("ManagerId", System.Data.SqlDbType.Int, nullable: true, ordinal: 3)
        ], [Sample.ForeignKey("ManagerId", "Person", "PersonId", "Name")]))).ToDictionary(f => Path.GetFileName(f.RelativePath), f => f.Content.ReplaceLineEndings("\n"));
        string ts = files["person.component.ts"];

        Expect.Contains(ts, "persons: Lookup[] = [];");
        Expect.Contains(ts, "this.service.getLookup().subscribe((data) => { this.persons = data; });");
    }
}
