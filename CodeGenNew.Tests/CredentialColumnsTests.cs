using System.Data;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The user table's API must not hand out the password hash or take it from a caller, lets only an administrator change the administrator and role columns, and keeps the last administrator. </summary>
[TestClass]
public class CredentialColumnsTests
{
    private static readonly (string, string)[] On = [("ApiAuth", "true"), ("UserTable", "Employee"), ("UserOwnerColumn", "EmployeeId"), ("UserRoleColumn", "IsManager")];

    private static ProjectSettings Project(params (string Key, string Value)[] values)
    {
        var map = new Dictionary<string, string> { ["ProjectName"] = "Shop", ["ContextName"] = "ShopContext", ["DtoNamespace"] = "Shop.Dtos" };
        foreach (var (key, value) in values)
            map[key] = value;
        return ProjectSettings.FromValues(map);
    }

    private static TableModel Employee(bool withAdmin = true, bool withActive = true, bool nameActive = false)
    {
        var columns = new List<ColumnModel>
        {
            Sample.Column("EmployeeId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Name", SqlDbType.VarChar, characters: 50, ordinal: 2),
            Sample.Column("IsManager", SqlDbType.Bit, ordinal: 3),
            Sample.Column("Login", SqlDbType.VarChar, nullable: true, characters: 50, ordinal: 4),
            Sample.Column("PasswordHash", SqlDbType.VarChar, nullable: true, characters: 200, ordinal: 5)
        };
        if (withAdmin)
            columns.Add(Sample.Column("IsAdministrator", SqlDbType.Bit, ordinal: 6));
        if (withActive || nameActive)
            columns.Add(Sample.Column("IsActive", SqlDbType.Bit, ordinal: 7));
        return Sample.Table("Employee", columns);
    }

    private static async Task<string> Render(string template, TableModel table, params (string Key, string Value)[] values)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, Project(values));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    [TestMethod]
    public async Task The_entity_of_the_user_table_ignores_the_hash_in_json_and_no_other_column()
    {
        string entity = await Render("CS_Entity_v1.tt", Employee(), On);
        Expect.Contains(entity, "[System.Text.Json.Serialization.JsonIgnore]\n    public string? PasswordHash");
        Assert.AreEqual(1, entity.Split("JsonIgnore").Length - 1);

        Expect.DoesNotContain(await Render("CS_Entity_v1.tt", Employee()), "JsonIgnore");
        Expect.DoesNotContain(await Render("CS_Entity_v1.tt", Employee(), ("UserTable", "Employee")), "JsonIgnore");   // no ApiAuth: no sign-in table
        Expect.DoesNotContain(await Render("CS_Entity_v1.tt", Sample.Table("Customer", [Sample.Column("CustomerId", SqlDbType.Int, primaryKey: true, ordinal: 1), Sample.Column("PasswordHash", SqlDbType.VarChar, nullable: true, characters: 200, ordinal: 2)]), On), "JsonIgnore");
    }

    [TestMethod]
    public async Task The_transfer_class_and_the_input_of_the_user_table_leave_the_hash_out_of_answers_and_bodies()
    {
        Expect.Contains(await Render("CS_Dto_v1.tt", Employee(), On), "[System.Text.Json.Serialization.JsonIgnore]\n    public string? PasswordHash");

        var withDtos = Project([.. On, ("Dtos", "true")]);
        Assert.IsFalse(DtoShape.InputColumns(Employee(), withDtos).Any(c => c.Name == "PasswordHash"), "nobody sets a hash through a body");
        Assert.IsTrue(DtoShape.InputColumns(Employee(), withDtos).Any(c => c.Name == "IsAdministrator"), "the API, not the body shape, guards the flag");
        Assert.IsTrue(DtoShape.InputColumns(Employee()).Any(c => c.Name == "PasswordHash"), "a table that is not the user table keeps every column");

        string input = await Render("CS_DtoInput_v1.tt", Employee(), [.. On, ("Dtos", "true")]);
        Expect.DoesNotContain(input, "PasswordHash");
    }

    [TestMethod]
    public async Task The_api_of_the_user_table_restores_the_stored_hash_and_guards_the_administrator_and_role_columns()
    {
        string api = await Render("API_Crud_v1.tt", Employee(), On);

        Expect.Contains(api, "protected override async Task<IResult?> BeforeSaveAsync(ShopContext context, Employee row, bool isNew)");
        Expect.Contains(api, "row.PasswordHash = stored?.Hash;");
        Expect.Contains(api, "if (!Caller.IsInRole(AuthNames.AdminRole))");
        Expect.Contains(api, "row.IsAdministrator = false;");
        Expect.Contains(api, "row.IsManager = false;");
        Expect.Contains(api, "else if (!Equals(row.IsAdministrator, stored!.IsAdministrator))");
        Expect.Contains(api, "statusCode: 403, type: \"" + ProblemTypes.ColumnNotYours + "\"");
        Expect.Contains(api, "statusCode: 409, type: \"" + ProblemTypes.DuplicateValue + "\"");
        Expect.Contains(api, "login = login.Trim()");
        Expect.Contains(api, "statusCode: 422, type: \"" + ProblemTypes.LastAdministrator + "\"");
        Expect.Contains(api, "protected override async Task<IResult?> BeforeDeleteAsync(ShopContext context, int id)");
        Expect.Contains(api, "u.IsAdministrator == true && u.IsActive");
    }

    [TestMethod]
    public async Task Without_an_administrator_column_only_the_hash_login_and_role_rules_remain()
    {
        string api = await Render("API_Crud_v1.tt", Employee(withAdmin: false, withActive: false), On);

        Expect.Contains(api, "BeforeSaveAsync");
        Expect.Contains(api, "row.IsManager = false;");
        Expect.DoesNotContain(api, "LastAdministrator");
        Expect.DoesNotContain(api, "BeforeDeleteAsync");

        string noRole = await Render("API_Crud_v1.tt", Employee(), [("ApiAuth", "true"), ("UserTable", "Employee")]);
        Expect.DoesNotContain(noRole, "IsManager");
        Expect.Contains(noRole, "IsAdministrator");
    }

    [TestMethod]
    public async Task A_table_that_is_not_the_user_table_has_no_guard_and_without_ApiAuth_nobody_is_guarded()
    {
        Expect.DoesNotContain(await Render("API_Crud_v1.tt", Sample.Table("Customer", [Sample.Column("CustomerId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Sample.Column("Name", SqlDbType.VarChar, characters: 50, ordinal: 2)]), On), "BeforeSaveAsync");
        Expect.DoesNotContain(await Render("API_Crud_v1.tt", Employee(), [("UserTable", "Employee")]), "BeforeSaveAsync");
    }

    [TestMethod]
    public async Task The_user_table_that_is_a_name_and_active_table_gets_the_same_guard()
    {
        var table = Sample.Table("Employee",
        [
            Sample.Column("EmployeeId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Name", SqlDbType.VarChar, characters: 50, ordinal: 2),
            Sample.Column("IsActive", SqlDbType.Bit, ordinal: 3),
            Sample.Column("UserName", SqlDbType.VarChar, nullable: true, characters: 50, ordinal: 4),
            Sample.Column("PasswordHash", SqlDbType.VarChar, nullable: true, characters: 200, ordinal: 5),
            Sample.Column("IsAdmin", SqlDbType.Bit, ordinal: 6)
        ]);
        string api = await Render("API_Crud_v1.tt", table, [("ApiAuth", "true"), ("UserTable", "Employee")]);

        Expect.Contains(api, ": NameActiveCrudApi<Employee, EmployeeRepo>");
        Expect.Contains(api, "BeforeSaveAsync(ShopContext context, Employee row, bool isNew)");
        Expect.Contains(api, "row.IsAdmin = false;");
        Expect.Contains(api, "stored.IsAdmin == true && stored.Active");
        Expect.Contains(api, "u.UserName == login");
    }

    [TestMethod]
    public async Task Both_api_bases_offer_the_hooks_only_with_ApiAuth()
    {
        var withAuth = await Repo.Cache.RunAsync(Repo.Template("API_EssentialCrudApi_v1.tt"), Project(("ApiAuth", "true")));
        Assert.IsTrue(withAuth.Success, string.Join(" | ", withAuth.Errors));
        string text = withAuth.GeneratedText!.ReplaceLineEndings("\n");
        Assert.AreEqual(2, text.Split("protected virtual Task<IResult?> BeforeSaveAsync").Length - 1, "CrudApi and NameActiveCrudApi");
        Assert.AreEqual(2, text.Split("protected virtual Task<IResult?> BeforeDeleteAsync").Length - 1);
        Assert.AreEqual(2, text.Split("protected System.Security.Claims.ClaimsPrincipal Caller").Length - 1);
        Assert.AreEqual(4, text.Split("if (await BeforeSaveAsync(context, ").Length - 1, "a create and an update call it in each base");
        Assert.AreEqual(2, text.Split("if (await BeforeDeleteAsync(context, id)").Length - 1);

        var without = await Repo.Cache.RunAsync(Repo.Template("API_EssentialCrudApi_v1.tt"), Project());
        Assert.IsTrue(without.Success, string.Join(" | ", without.Errors));
        Expect.DoesNotContain(without.GeneratedText!, "BeforeSaveAsync");
    }

    [TestMethod]
    public void A_hash_column_with_an_unusual_name_is_hidden_from_forms_grids_and_searches_once_the_project_marks_it()
    {
        var table = Sample.Table("Employee",
        [
            Sample.Column("EmployeeId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Name", SqlDbType.VarChar, characters: 50, ordinal: 2),
            Sample.Column("Login", SqlDbType.VarChar, nullable: true, characters: 50, ordinal: 3),
            Sample.Column("Pword", SqlDbType.VarChar, nullable: true, characters: 200, ordinal: 4)
        ]);
        var project = Project(("ApiAuth", "true"), ("UserTable", "Employee"));

        Assert.IsFalse(table.Columns.Single(c => c.Name == "Pword").IsCredentialColumn, "by its name alone it looks ordinary");
        Assert.IsTrue(FormModel.For(table, project).Columns.Any(c => c.Name == "Pword"));

        UserTable.MarkCredentials(table, project);
        var form = FormModel.For(table, project);
        Assert.IsTrue(table.Columns.Single(c => c.Name == "Pword").IsCredentialColumn);
        Assert.IsFalse(form.Columns.Any(c => c.Name == "Pword"), "not a form field");
        Assert.IsFalse(form.GridColumns.Any(c => c.Name == "Pword"), "not a grid column");
        Assert.IsFalse(table.SearchableColumns.Any(c => c.Name == "Pword"), "not searched");

        UserTable.MarkCredentials(table, Project(("ApiAuth", "true")));
        Assert.IsFalse(table.Columns.Single(c => c.Name == "Pword").IsCredentialColumn, "another project starts clean");
    }
}
