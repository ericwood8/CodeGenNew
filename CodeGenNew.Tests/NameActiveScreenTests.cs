using System.Data;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> A table with a NOT NULL Name and a NOT NULL IsActive column on the Blazor, Uno and WinUI3 stacks: its API is the NameActiveCrudApi (the whole list, no search, no paging, a 409 for a
/// duplicate name), so the clients and screens read the list whole and the Active only check box narrows it. The WinUI3 screens are in TemplateRenderingTests. </summary>
[TestClass]
public class NameActiveScreenTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static async Task<string> Run(string template, TableModel table, params (string, string)[] values)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, Project(values));
        Assert.IsTrue(result.Success, $"{template} failed for {table.TableName}: {string.Join(" | ", result.Errors)}");
        return result.GeneratedText!.Replace("\r\n", "\n");
    }

    /// <summary> A name / active table with a plain required text column besides Name, so the generated test finds a row to look for. </summary>
    private static TableModel Region() => Sample.Table("Region",
    [
        Sample.Column("RegionId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 40, ordinal: 2),
        Sample.Column("IsActive", SqlDbType.Bit, ordinal: 3),
        Sample.Column("Code", SqlDbType.NVarChar, characters: 10, ordinal: 4),
        Sample.Column("Note", SqlDbType.NVarChar, nullable: true, characters: 50, ordinal: 5)
    ]);

    [TestMethod]
    public void A_name_active_table_has_a_client_but_no_OData_client()
    {
        var table = Sample.DepartmentTeam();
        var project = Project();

        Assert.IsNull(BlazorNames.Refusal(table, project));
        Assert.IsNull(UnoNames.Refusal(table, project));
        Assert.IsFalse(BlazorNames.HasODataClient(table, project));
        Assert.IsTrue(BlazorNames.HasODataClient(Sample.DonateLeave(), project));
    }

    [TestMethod]
    public void The_Blazor_client_reads_the_whole_list_and_the_active_rows_and_has_no_search_or_clone()
    {
        string client = ApiClientCode.Client(Sample.DepartmentTeam(), Project(), "Acme.Blazor.Models", "Acme.Blazor.Services");

        Expect.Contains(client, "public async Task<List<DepartmentTeam>> GetAllAsync()");
        Expect.Contains(client, "public async Task<List<DepartmentTeam>> GetActiveAsync()");
        Expect.Contains(client, "{Url}/active");
        Expect.DoesNotContain(client, "GetPageAsync");
        Expect.DoesNotContain(client, "CloneAsync");
    }

    [TestMethod]
    public void The_Uno_client_pages_the_whole_list_in_the_client_with_the_active_filter_and_a_switch_for_the_sort()
    {
        string client = ApiClientCode.Client(Sample.DepartmentTeam(), Project(), "Acme.Models", "Acme.Services", pagedHere: true);

        Expect.Contains(client, "public async Task<PagedResult<DepartmentTeam>> GetPageAsync(int page, int pageSize,");
        Expect.Contains(client, "\"isactive\" => wanted == \"true\" ? rows.Where(r => r.IsActive) : rows,");
        Expect.Contains(client, "object? Key(DepartmentTeam r) => sortBy switch");
        Expect.Contains(client, "\"Name\" => r.Name,");
        Expect.Contains(client, "Skip((page - 1) * pageSize).Take(pageSize)");
        Expect.DoesNotContain(client, "GetType()");
    }

    [TestMethod]
    public async Task The_Blazor_page_of_a_name_active_table_narrows_the_whole_list_and_tells_a_duplicate_name()
    {
        string page = await Run("BLZ_Page_v1.tt", Sample.DepartmentTeam());

        Expect.Contains(page, "aria-label=\"Search by Name\" @bind=\"searchText\"");
        Expect.Contains(page, "<label class=\"active-only\"><input type=\"checkbox\" @bind=\"activeOnly\" /> Active only</label>");
        Expect.Contains(page, "@foreach (var item in VisibleRows)");
        Expect.Contains(page, "items = await Client.GetAllAsync();");
        Expect.Contains(page, "ex.Status == 409) { message = \"A department team with this name already exists!\"; }");
        foreach (string absent in new[] { "Previous", "PageSizeChoices", "column-picker", "SortAsync", "CloneAsync", "GetPageAsync", "pageNumber" })
            Expect.DoesNotContain(page, absent);
    }

    [TestMethod]
    public async Task The_Blazor_page_of_a_name_active_table_has_no_OData_form_even_when_the_table_is_served()
    {
        string odata = await Run("BLZ_ODataPage_v1.tt", Sample.DepartmentTeam(), ("OData", "true"));
        string rest = await Run("BLZ_Page_v1.tt", Sample.DepartmentTeam(), ("OData", "true"), ("ODataScreens", "Replace"));

        Assert.AreEqual("", odata.Trim());
        Expect.Contains(rest, "@page \"/");
    }

    [TestMethod]
    public async Task The_Blazor_test_of_a_name_active_table_checks_the_name_box_the_conflict_and_the_delete()
    {
        string tests = await Run("BLZ_Test_v1.tt", Region(), ("BlazorTests", "true"));

        Expect.Contains(tests, "public void Narrows_the_list_with_the_name_box_and_Clear_brings_it_back()");
        Expect.Contains(tests, "public void Says_a_name_already_exists_when_the_API_answers_a_conflict_to_a_Save()");
        Expect.Contains(tests, "Api.Answer(\"/api/regions\", list ?? \"[\" + Row + \"]\");");
        Expect.DoesNotContain(tests, "Pages_forward_with_Next");
    }

    [TestMethod]
    public async Task The_Uno_screens_of_a_name_active_table_have_the_Active_only_box_and_tell_a_duplicate_name()
    {
        string list = await Run("UNO_MasterScreen_v1.tt", Sample.DepartmentTeam());
        string dialog = await Run("UNO_DetailScreen_v1.tt", Sample.DepartmentTeam());

        Expect.Contains(list, "<CheckBox Content=\"Active only\" IsChecked=\"{x:Bind ViewModel.ActiveOnly, Mode=TwoWay}\" />");
        Expect.Contains(list, "[\"isActive\"] = ActiveOnly ? \"true\" : null,");
        Expect.DoesNotContain(list, "OnCloneClick");
        Expect.Contains(dialog, "catch (ApiException ex) when (ex.Status == 409)");
        Expect.Contains(dialog, "with this name already exists.");
    }

    [TestMethod]
    public async Task The_repository_of_a_name_active_table_pages_the_list_in_memory_in_every_access_mode()
    {
        foreach (string mode in new[] { "Ef", "Routines" })
        {
            string repo = await Run("CS_Repo_v1.tt", Sample.DepartmentTeam(), ("AccessMode", mode));

            Expect.Contains(repo, "bool activeOnly = false)");
            Expect.Contains(repo, "var rows = (await GetAll()).AsEnumerable();");
            Expect.Contains(repo, "rows = rows.Where(t => t.IsActive);");
            Expect.DoesNotContain(repo, "FromSqlRaw");
        }
    }

    [TestMethod]
    public void A_name_active_table_without_a_screen_or_with_an_OData_page_left_out_is_noted()
    {
        var table = Sample.DepartmentTeam();
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [table] };
        var plain = Project();
        var listed = Project(("Screens", table.TableName), ("OData", "true"));

        var withoutScreen = TableShapeNotes.Of([], database, plain, ["Api", "React"]);
        var withScreen = TableShapeNotes.Of([], database, listed, ["Api", "Blazor", "Uno", "React"]);

        CollectionAssert.Contains(withoutScreen, "DepartmentTeam: has a Name and an IsActive column, so it gets a screen only when the Screens setting lists it.");
        CollectionAssert.Contains(withScreen, "DepartmentTeam: has a Name and an IsActive column, so Blazor and Uno write only its REST screen, not an OData page.");
    }
}
