using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> The base of the generated API classes (API_EssentialCrudApi) and the short class API_Crud writes on it. </summary>
[TestClass]
public class CrudApiTests
{
    /// <summary> Apis/CrudApi.cs as the essentials group writes it for a project with the given settings. </summary>
    internal static async Task<string> Essential(params (string Key, string Value)[] values)
    {
        var project = ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));
        var result = await Repo.Cache.RunAsync(Repo.Template("API_EssentialCrudApi_v1.tt"), project);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    private static async Task<string> RenderApi(TableModel table, ProjectSettings? project = null)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("API_Crud_v1.tt"), table, project ?? ProjectSettings.FromValues([new("ProjectName", "Acme")]));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    [TestMethod]
    public async Task The_base_registers_the_five_endpoints_once_for_every_table()
    {
        string cs = await Essential();

        Expect.Contains(cs, "@@@FILE Apis/CrudApi.cs@@@");
        Expect.Contains(cs, "namespace Acme.ApiService.Apis;");
        Expect.Contains(cs, "public abstract class CrudApi<TEntity, TRepo> : BaseApi<TEntity>");
        Expect.Contains(cs, "where TRepo : GenericRepo<TEntity>");
        foreach (string call in new[] { "app.MapGet(apiSubDir,", "app.MapGet(apiSubDir + \"/{id:int}\",", "app.MapPost(apiSubDir,", "app.MapPut(apiSubDir + \"/{id:int}\",", "app.MapDelete(apiSubDir + \"/{id:int}\"," })
            Expect.Contains(cs, call);
        Expect.Contains(cs, "AcmeContext context");
    }

    [TestMethod]
    public async Task A_table_with_a_Name_is_found_by_name_at_its_route_and_one_without_is_not()
    {
        string cs = await Essential();
        Expect.Contains(cs, "protected virtual bool CanFindByName => false;");
        Expect.Contains(cs, "app.MapGet(apiSubDir + \"/{name}\",");
        Expect.Contains(cs, "if (CanFindByName)");

        string holiday = await RenderApi(Sample.Holiday());
        Expect.Contains(holiday, "protected override bool CanFindByName => true;");
        Expect.Contains(holiday, "FindByNameAsync(HolidayRepo repo, string name) => repo.GetByName(name);");
        Expect.DoesNotContain(await RenderApi(Sample.DonateLeave()), "CanFindByName");
    }

    [TestMethod]
    public async Task The_base_gives_the_same_answers_the_per_table_classes_used_to()
    {
        string cs = await Essential();

        Expect.Contains(cs, "if (KeyOf(updatedRow) != id)");              // 400 when the id in the route and in the body disagree
        Expect.Contains(cs, "if (!await repo.ExistsAsync(id))");           // 404 when there is no row to update
        Expect.Contains(cs, "return row != null ? Results.Ok(row) : Results.NotFound();");
        Expect.Contains(cs, "NewRepo(context).DeleteAsync(typeof(TEntity).Name, id)");
        Expect.Contains(cs, "return Results.Created($\"/api{_route}/{KeyOf(newRow)}\", newRow);");
    }

    [TestMethod]
    public async Task The_base_answers_a_page_when_asked_and_the_whole_table_otherwise()
    {
        string cs = await Essential();

        Expect.Contains(cs, "[FromQuery] int? pageNumber, [FromQuery] int? pageSize");
        Expect.Contains(cs, "if (pageNumber is not null || pageSize is not null)");
        Expect.Contains(cs, "int size = Math.Clamp(pageSize ?? 100, 1, MaxPageSize);");
        Expect.Contains(cs, "private const int MaxPageSize = 1000;");
        Expect.Contains(cs, "return Results.Problem(statusCode: 400, title: \"Invalid page\", detail: \"pageNumber must be 1 or greater.\");");
        Expect.Contains(cs, "context.Set<TEntity>().AsNoTracking()");
        Expect.Contains(cs, "public record CrudPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);");
        Expect.Contains(cs, "all.OrderByDescending(newest).ThenBy(Key)");   // the page is ordered by the key too, so pages do not overlap
        Expect.Contains(cs, "await repo.GetAllOrderByDescending(order) : await repo.GetAll()");   // no paging parameter: every row as an array
    }

    [TestMethod]
    public async Task The_base_runs_the_validators_only_when_asked()
    {
        string plain = await Essential();
        string validated = await Essential(("ApiValidation", "true"));

        Expect.DoesNotContain(plain, "ValidationFilter");
        Assert.AreEqual(4, validated.Split("AddEndpointFilter<ValidationFilter<TEntity>>()").Length - 1, "the create and the update of CrudApi and of NameActiveCrudApi, not the reads or the delete");
        Expect.Contains(validated, ".WithName($\"Create{singular}\")\n        .WithOpenApi()\n        .AddEndpointFilter<ValidationFilter<TEntity>>()\n        .ProducesProblem(500);");
    }

    [TestMethod]
    public async Task The_base_offers_clone_only_to_a_class_that_asks_for_it()
    {
        string cs = await Essential();

        Expect.Contains(cs, "protected virtual bool CanClone => false;");
        Expect.Contains(cs, "if (CanClone)");
        Expect.Contains(cs, "app.MapPost(apiSubDir + \"/{id:int}/clone\",");
        Expect.Contains(cs, "return Results.Created($\"/api{_route}/{newId}\", copy);");
    }

    [TestMethod]
    public async Task The_class_for_a_table_names_only_its_repository_key_and_date_column()
    {
        string cs = await RenderApi(Sample.DonateLeave());

        Expect.Contains(cs, "protected override Expression<Func<E_DonateLeave, int>> Key => c => c.DonateLeaveId;");
        Expect.Contains(cs, "protected override bool CanClone => true;");   // this sample table can be cloned
        Expect.DoesNotContain(cs, "ValidationFilter");
        Assert.IsLessThan(25, cs.Split('\n').Length, "a short class");
    }
}
