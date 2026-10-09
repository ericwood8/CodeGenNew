using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> Tables with a NOT NULL Name and a NOT NULL IsActive column: the NameActive essentials group, NameActiveCrudApi and the short class API_Crud writes on it, the registration,
/// and the Angular service and screens. </summary>
[TestClass]
public class NameActiveTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static async Task<Dictionary<string, string>> Run(string template, TableModel? table = null, params (string Key, string Value)[] values)
    {
        var result = table is null
            ? await Repo.Cache.RunAsync(Repo.Template(template), Project(values))
            : await Repo.Cache.RunAsync(Repo.Template(template), table, Project(values));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return GeneratedFiles.Split(result.GeneratedText!).ToDictionary(f => Path.GetFileName(f.RelativePath), f => f.Content.ReplaceLineEndings("\n"));
    }

    private static async Task<string> RenderApi(TableModel table)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("API_Crud_v1.tt"), table, Project());
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    [TestMethod]
    public async Task The_NameActive_group_writes_the_entity_base_the_repository_interface_and_the_repository()
    {
        var files = await Run("CS_EssentialNameActive_v1.tt");

        CollectionAssert.AreEquivalent(new[] { "BaseNameActiveEntity.cs", "INameActiveRepo.cs", "NameActiveRepo.cs" }, files.Keys.ToArray());
        Expect.Contains(files["BaseNameActiveEntity.cs"], "public class BaseNameActiveEntity : BaseEntity");
        Expect.Contains(files["BaseNameActiveEntity.cs"], "public required string Name { get; set; }");
        Expect.Contains(files["BaseNameActiveEntity.cs"], "public required bool IsActive { get; set; } = true;");

        string repo = files["NameActiveRepo.cs"];
        Expect.Contains(repo, "public class NameActiveRepo<T> : INameActiveRepo<T>, IDisposable where T : BaseNameActiveEntity");
        Expect.Contains(repo, "protected readonly AcmeContext _context;");
        Expect.Contains(repo, "newRow.Name = newRow.Name.Trim();");
        Expect.Contains(repo, "AnyAsync(d => d.Name == name && d.IsActive && EF.Property<int>(d, keyName) != id)");   // the row being renamed never counts against itself
        Expect.Contains(repo, "GetAllActive()");
        Expect.Contains(repo, "if (rowToDelete is null)");   // delete answers 0, -1, 1 like GenericRepo
        Expect.Contains(repo, "return 1;");
    }

    [TestMethod]
    public async Task The_entity_base_follows_the_projects_class_names()
    {
        var files = await Run("CS_EssentialNameActive_v1.tt", null, ("BaseEntity", "RowBase"), ("BaseNameActiveEntity", "NamedRow"));

        Assert.IsTrue(files.ContainsKey("NamedRow.cs"));
        Expect.Contains(files["NamedRow.cs"], "public class NamedRow : RowBase");
        Expect.Contains(files["NameActiveRepo.cs"], "where T : NamedRow");
    }

    [TestMethod]
    public async Task The_group_is_a_Api_and_WinUI3_essentials_group_named_NameActive()
    {
        foreach (string stack in new[] { "Api", "WinUI3" })
            CollectionAssert.Contains(EssentialsCatalog.Groups(Repo.TemplatesDirectory, stack).Select(g => g.Name).ToList(), "NameActive", stack);
    }

    [TestMethod]
    public async Task NameActiveCrudApi_serves_the_list_the_active_list_find_by_name_and_answers_a_duplicate_name_as_a_409_problem()
    {
        string cs = await CrudApiTests.Essential();

        Expect.Contains(cs, "public abstract class NameActiveCrudApi<TEntity, TRepo> : BaseApi<TEntity>");
        Expect.Contains(cs, "where TEntity : BaseNameActiveEntity");
        Expect.Contains(cs, "where TRepo : NameActiveRepo<TEntity>");
        Expect.Contains(cs, "app.MapGet(apiSubDir + \"/active\",");
        Expect.Contains(cs, "app.MapGet(apiSubDir + \"/{name}\",");
        Expect.Contains(cs, "Results.Problem(statusCode: 409, title: \"Duplicate name\"");
        Expect.Contains(cs, "if (string.IsNullOrWhiteSpace(newRow.Name))");
        Expect.Contains(cs, "if (!await NewRepo(context).AddAsync(newRow))");
        Expect.Contains(cs, "return saved is null ? DuplicateName(updatedRow.Name) : Results.Ok(saved);");
        Expect.Contains(cs, "Results.Problem(statusCode: 400, title: \"In use\"");

        string validated = await CrudApiTests.Essential(("ApiValidation", "true"));
        Assert.IsGreaterThan(cs.Split("ValidationFilter<TEntity>").Length, validated.Split("ValidationFilter<TEntity>").Length);

        string renamed = await CrudApiTests.Essential(("BaseNameActiveEntity", "NamedRow"));
        Expect.Contains(renamed, "where TEntity : NamedRow");
    }

    [TestMethod]
    public async Task A_name_active_table_gets_a_short_class_on_NameActiveCrudApi()
    {
        string cs = await RenderApi(Sample.DepartmentTeam());

        Expect.Contains(cs, "public class DepartmentTeamApi : NameActiveCrudApi<DepartmentTeam, DepartmentTeamRepo>");
        Expect.Contains(cs, "protected override DepartmentTeamRepo NewRepo(AcmeContext context) => new(context);");
        Expect.Contains(cs, "protected override Expression<Func<DepartmentTeam, int>> Key => c => c.DepartmentTeamId;");
        // the base answers find by name; a name/active table has no clone and no newest-first date column
        Expect.DoesNotContain(cs, "CanFindByName");
        Expect.DoesNotContain(cs, "CanClone");
        Expect.DoesNotContain(cs, "NewestFirst");
        Assert.IsLessThan(25, cs.Split('\n').Length, "a short class");
    }

    [TestMethod]
    public void The_ASP_NET_API_serves_a_name_active_table_but_the_other_stacks_do_not()
    {
        var project = Project();
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [Sample.DonateLeave(), Sample.DepartmentTeam()] };

        CollectionAssert.AreEqual(new[] { "E_DonateLeave" }, database.ApiTables(project).Select(t => t.TableName).ToArray());
        CollectionAssert.AreEquivalent(new[] { "E_DonateLeave", "DepartmentTeam" }, database.AspNetApiTables(project).Select(t => t.TableName).ToArray());
        Assert.IsTrue(Sample.DepartmentTeam().HasNameActiveApi(project));
        Assert.IsFalse(Sample.DepartmentTeam().HasCrudApi(project));
        Assert.IsFalse(Sample.DonateLeave().HasNameActiveApi(project));
        Assert.IsFalse(Sample.DepartmentTeam().HasSearchApi(project));
        // an enum table is not served
        Assert.IsFalse(Sample.DepartmentTeam().HasNameActiveApi(Project(("NoRepositoryTables", "DepartmentTeam"))));
    }

    [TestMethod]
    public async Task The_plan_runs_API_Crud_for_a_name_active_table_and_the_others_do_not()
    {
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [Sample.DonateLeave(), Sample.DepartmentTeam()] };
        var templates = TemplateCatalog.Discover(Repo.TemplatesDirectory);
        var steps = ProjectPlan.Build(templates, database, Project(), ["Api"]).ToDictionary(s => s.Template.Name);

        CollectionAssert.AreEquivalent(new[] { "E_DonateLeave", "DepartmentTeam" }, steps["API_Crud"].TableNames.ToArray());
        CollectionAssert.AreEqual(new[] { "E_DonateLeave" }, steps["API_Search"].TableNames.ToArray());   // no search endpoint for a name/active table
        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task The_registration_lists_the_name_active_api_and_gives_it_no_search_api()
    {
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [Sample.DonateLeave(), Sample.DepartmentTeam()] };
        var result = await Repo.Cache.RunAsync(Repo.Template("API_Registration_v1.tt"), database, Project());
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string cs = result.GeneratedText!;

        Expect.Contains(cs, "new DepartmentTeamApi().Register(app);");
        Expect.DoesNotContain(cs, "DepartmentTeamSearchApi");
        Expect.Contains(cs, "new E_DonateLeaveSearchApi<E_DonateLeave>().Register(app);");
    }

    [TestMethod]
    public async Task The_Angular_service_of_a_name_active_table_adds_the_active_list_and_has_no_page_call()
    {
        string service = (await Run("TS_Service_v1.tt", Sample.DepartmentTeam())).Single().Value;

        Expect.Contains(service, "import { NameActiveCrudService } from './crud.service';");
        Expect.Contains(service, "export class DepartmentTeamService extends NameActiveCrudService<DepartmentTeam> {");
        Expect.Contains(service, "super(http, 'api/departmentteams');");
        Expect.DoesNotContain(service, "getPage");
        Expect.DoesNotContain(service, "PagedResult");

        string crud = (await CrudScreenTests.Essential())["crud.service.ts"];
        Expect.Contains(crud, "export abstract class NameActiveCrudService<T, K extends number | string = number> extends NamedCrudService<T, K> {");
        Expect.Contains(crud, "`${this.apiUrl}/active`");
    }

    [TestMethod]
    public async Task The_Angular_screen_of_a_name_active_table_narrows_the_list_by_name_and_by_active_and_does_not_page()
    {
        var files = await Run("TS_Component_v1.tt", Sample.DepartmentTeam());
        string ts = files["departmentteam.component.ts"];
        string html = files["departmentteam.component.html"];

        Expect.Contains(ts, "import { NameActiveCrudScreen } from '../crud-screen';");
        Expect.Contains(ts, "export class DepartmentTeamComponent extends NameActiveCrudScreen<DepartmentTeam> {");
        Expect.Contains(ts, "protected readonly service = inject(DepartmentTeamService);");
        Expect.DoesNotContain(ts, "MatPaginatorModule");
        Expect.DoesNotContain(ts, "sortableColumns");
        Expect.DoesNotContain(ts, "gridKey");
        Expect.Contains(html, "[(ngModel)]=\"searchText\"");
        Expect.Contains(html, "[(ngModel)]=\"activeOnly\"");
        Expect.Contains(html, "let departmentTeam of visibleRows");
        Expect.Contains(html, "name=\"departmentTeamIsActive\"");   // the IsActive check box of the form
        Expect.DoesNotContain(html, "mat-paginator");
        Expect.DoesNotContain(html, "openSortMenu");
        Expect.DoesNotContain(html, "Clone");
        Expect.Contains(files["departmentteam.component.spec.ts"], "component.visibleRows.length");

        string crud = (await CrudScreenTests.Essential())["crud-screen.ts"];
        Expect.Contains(crud, "export abstract class NameActiveCrudScreen<T extends { name: string; isActive: boolean }, K extends number | string = number> extends CrudScreen<T, K> {");
        Expect.Contains(crud, "get visibleRows(): T[] {");
        Expect.Contains(crud, "(!this.activeOnly || row.isActive)");
    }

    [TestMethod]
    public async Task The_detail_master_screen_of_a_name_active_parent_is_on_the_same_base_and_keeps_its_child_grids()
    {
        var files = await Run("TS_DetailMasterComponent_v1.tt", Sample.TimeSheetWithEmployeeAndDetail());
        Assert.IsNotEmpty(files);   // the plain parent is unchanged: still on PagedCrudScreen
        Expect.Contains(files.Single(f => f.Key.EndsWith(".component.ts")).Value, "extends PagedCrudScreen<TimeSheet>");

        var nameActive = await Run("TS_DetailMasterComponent_v1.tt", Sample.NameActiveTableWithChildren());
        string ts = nameActive.Single(f => f.Key.EndsWith(".component.ts")).Value;
        string html = nameActive.Single(f => f.Key.EndsWith(".component.html")).Value;
        Expect.Contains(ts, "extends NameActiveCrudScreen<Team>");
        Expect.DoesNotContain(ts, "MatPaginatorModule");
        Expect.Contains(ts, "sortChildBy(child: string, column: string): void {");   // the child grids still sort in the browser
        Expect.Contains(html, "let team of visibleRows");
        Expect.DoesNotContain(html, "mat-paginator");
    }

    [TestMethod]
    public async Task A_plain_table_is_written_as_before()
    {
        string cs = await RenderApi(Sample.Holiday());
        Expect.Contains(cs, "public class HolidayApi : CrudApi<Holiday, HolidayRepo>");
        string ts = (await Run("TS_Component_v1.tt", Sample.Holiday()))["holiday.component.ts"];
        Expect.Contains(ts, "extends PagedCrudScreen<Holiday>");
    }
}
