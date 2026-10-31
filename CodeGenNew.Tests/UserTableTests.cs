using System.Data;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> Signing in against a table of the project's own (UserTable=Employee): the rule that resolves the settings, the generated directory, and what Program.cs does with it. </summary>
[TestClass]
public class UserTableTests
{
    private static readonly (string, string)[] AllSettings =
    [
        ("ApiAuth", "true"), ("UserTable", "Employee"), ("UserNameColumn", "Login"), ("PasswordHashColumn", "PasswordHash"),
        ("UserOwnerColumn", "EmployeeId"), ("UserRoleColumn", "IsManager")
    ];

    private static ProjectSettings Project(IEnumerable<(string Key, string Value)> values, params (string Key, string Value)[] overrides)
    {
        var map = new Dictionary<string, string> { ["ProjectName"] = "Shop", ["ContextName"] = "ShopContext" };
        foreach (var (key, value) in values.Concat(overrides))
            map[key] = value;
        return ProjectSettings.FromValues(map);
    }

    private static TableModel Employee(Func<ColumnModel, ColumnModel>? change = null, int hashLength = 200, string roleName = "IsManager", SqlDbType roleType = SqlDbType.Bit, bool withAdmin = false)
    {
        var columns = new List<ColumnModel>
        {
            Sample.Column("EmployeeId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Name", SqlDbType.VarChar, characters: 50, ordinal: 2),
            Sample.Column(roleName, roleType, characters: roleType == SqlDbType.VarChar ? 20 : null, ordinal: 3),
            Sample.Column("Login", SqlDbType.VarChar, nullable: true, characters: 50, ordinal: 4),
            Sample.Column("PasswordHash", SqlDbType.VarChar, nullable: true, characters: hashLength, ordinal: 5)
        };
        if (withAdmin)
            columns.Add(Sample.Column("IsAdministrator", SqlDbType.Bit, ordinal: 6));
        return Sample.Table("Employee", change == null ? columns : columns.Select(change).ToList());
    }

    private static DatabaseModel Database(params TableModel[] tables) => new() { DatabaseName = "Shop", SchemaName = "dbo", Tables = tables.ToList() };

    [TestMethod]
    public void Without_a_user_table_there_is_no_plan_and_no_complaint()
    {
        var (plan, problem) = UserTable.Resolve(Database(Employee()), Project([("ApiAuth", "true")]));
        Assert.IsNull(plan);
        Assert.IsNull(problem);
        Assert.IsFalse(UserTable.IsConfigured(Project([("ApiAuth", "true")])));
    }

    [TestMethod]
    public void The_columns_are_found_by_name_and_a_yes_no_role_column_gives_two_roles()
    {
        var (plan, problem) = UserTable.Resolve(Database(Employee()), Project(AllSettings));

        Assert.IsNull(problem);
        Assert.AreEqual("Login", plan!.UserName.Name);
        Assert.AreEqual("PasswordHash", plan.PasswordHash.Name);
        Assert.AreEqual("EmployeeId", plan.Owner!.Name);
        Assert.IsTrue(plan.RoleIsFlag);
        Assert.AreEqual("Manager", plan.RoleWhenTrue);
        Assert.AreEqual("Employee", plan.RoleWhenFalse);

        var named = UserTable.Resolve(Database(Employee()), Project(AllSettings, ("UserRoleWhenTrue", "Boss"), ("UserRoleWhenFalse", "Worker"), ("UserTable", "employee"))).Plan!;
        Assert.AreEqual("Boss", named.RoleWhenTrue);
        Assert.AreEqual("Worker", named.RoleWhenFalse);
    }

    [TestMethod]
    public void The_owner_and_role_columns_are_optional_and_a_text_role_column_is_the_role_itself()
    {
        var bare = UserTable.Resolve(Database(Employee()), Project(AllSettings.Where(s => s.Item1 is not ("UserOwnerColumn" or "UserRoleColumn")))).Plan!;
        Assert.IsNull(bare.Owner);
        Assert.IsNull(bare.Role);

        var text = UserTable.Resolve(Database(Employee(roleName: "RoleName", roleType: SqlDbType.VarChar)), Project(AllSettings, ("UserRoleColumn", "RoleName"))).Plan!;
        Assert.IsFalse(text.RoleIsFlag);
    }

    [TestMethod]
    [DataRow("UserTable", "Staff", "not a table")]
    [DataRow("UserNameColumn", "Nope", "not a column of Employee")]
    [DataRow("UserNameColumn", "EmployeeId", "must be a text column")]
    [DataRow("PasswordHashColumn", "EmployeeId", "must be a text column")]
    [DataRow("UserOwnerColumn", "Name", "whole-number")]
    [DataRow("UserRoleColumn", "EmployeeId", "yes/no column")]
    public void A_setting_that_does_not_fit_the_table_is_refused_with_a_sentence_that_names_it(string key, string value, string expected)
    {
        var (plan, problem) = UserTable.Resolve(Database(Employee()), Project(AllSettings, (key, value)));
        Assert.IsNull(plan);
        StringAssert.Contains(problem, expected);
        StringAssert.Contains(problem, key);
    }

    [TestMethod]
    public void A_missing_column_setting_a_narrow_hash_column_and_a_missing_ApiAuth_are_refused()
    {
        var noHashColumn = Employee(change: c => c.Name == "PasswordHash" ? Sample.Column("Secret", SqlDbType.VarChar, nullable: true, characters: 200, ordinal: 5) : c);
        StringAssert.Contains(UserTable.Resolve(Database(noHashColumn), Project(AllSettings.Where(s => s.Item1 != "PasswordHashColumn"))).Problem, "PasswordHashColumn is needed");
        StringAssert.Contains(UserTable.Resolve(Database(Employee(hashLength: 50)), Project(AllSettings)).Problem, "at least 90");
        StringAssert.Contains(UserTable.Resolve(Database(Employee()), Project(AllSettings, ("ApiAuth", "false"))).Problem, "ApiAuth=true");
        Assert.IsNull(UserTable.Resolve(Database(Employee(hashLength: -1)), Project(AllSettings)).Problem, "varchar(max) is wide enough");
    }

    [TestMethod]
    public void An_administrator_column_is_optional_and_must_be_yes_no()
    {
        Assert.IsNull(UserTable.Resolve(Database(Employee()), Project(AllSettings)).Plan!.Admin);

        var plan = UserTable.Resolve(Database(Employee(withAdmin: true)), Project(AllSettings, ("UserAdminColumn", "IsAdministrator"))).Plan!;
        Assert.AreEqual("IsAdministrator", plan.Admin!.Name);

        var (none, problem) = UserTable.Resolve(Database(Employee(withAdmin: true)), Project(AllSettings, ("UserAdminColumn", "Name")));
        Assert.IsNull(none);
        StringAssert.Contains(problem, "UserAdminColumn");
        StringAssert.Contains(problem, "yes/no");
        StringAssert.Contains(UserTable.Resolve(Database(Employee()), Project(AllSettings, ("UserAdminColumn", "Nope"))).Problem, "not a column of Employee");
    }

    [TestMethod]
    public async Task A_true_administrator_flag_makes_the_person_the_administrator_role_whatever_the_role_column_says()
    {
        string text = await RenderDirectory(Project(AllSettings, ("UserAdminColumn", "IsAdministrator")), Employee(withAdmin: true));
        Expect.Contains(text, "Admin = u.IsAdministrator,");
        Expect.Contains(text, "row.Admin == true ? AuthNames.AdminRole : (row.Role == true ? \"Manager\" : \"Employee\")");

        string alone = await RenderDirectory(Project(AllSettings.Where(s => s.Item1 != "UserRoleColumn"), ("UserAdminColumn", "IsAdministrator")), Employee(withAdmin: true));
        Expect.Contains(alone, "row.Admin == true ? AuthNames.AdminRole : (null)");

        string noFlag = await RenderDirectory(Project(AllSettings), Employee());
        Expect.DoesNotContain(noFlag, "row.Admin");
        Expect.DoesNotContain(noFlag, "Admin = u.");
    }

    private static TableModel UserTableNamed(string nameColumn, string hashColumn, string? adminColumn, params string[] more)
    {
        var columns = new List<ColumnModel>
        {
            Sample.Column("EmployeeId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column(nameColumn, SqlDbType.VarChar, nullable: true, characters: 50, ordinal: 2),
            Sample.Column(hashColumn, SqlDbType.VarChar, nullable: true, characters: 200, ordinal: 3)
        };
        if (adminColumn != null)
            columns.Add(Sample.Column(adminColumn, SqlDbType.Bit, ordinal: 4));
        columns.AddRange(more.Select((n, i) => Sample.Column(n, SqlDbType.Bit, ordinal: 5 + i)));
        return Sample.Table("Employee", columns);
    }

    [TestMethod]
    [DataRow("Login", "PasswordHash", "IsAdministrator")]   // one usual set of names
    [DataRow("UserName", "PasswordHash", "IsAdmin")]        // another
    [DataRow("login", "PWORD", "ISADMIN")]                  // another case
    [DataRow("LoginName", "Pword", "Admin")]
    public void Left_out_the_user_name_hash_and_administrator_columns_are_found_by_their_usual_names(string name, string hash, string admin)
    {
        var (plan, problem) = UserTable.Resolve(Database(UserTableNamed(name, hash, admin)), Project([("ApiAuth", "true"), ("UserTable", "Employee")]));

        Assert.IsNull(problem);
        Assert.AreEqual(name, plan!.UserName.Name);
        Assert.AreEqual(hash, plan.PasswordHash.Name);
        Assert.AreEqual(admin, plan.Admin!.Name);
    }

    [TestMethod]
    public void No_administrator_column_means_nobody_is_an_administrator_and_two_candidates_are_an_error()
    {
        var plain = UserTable.Resolve(Database(UserTableNamed("Login", "PasswordHash", null)), Project([("ApiAuth", "true"), ("UserTable", "Employee")]));
        Assert.IsNull(plain.Problem);
        Assert.IsNull(plain.Plan!.Admin);

        var twoText = Employee(change: c => c.Name == "IsManager" ? Sample.Column("UserName", SqlDbType.VarChar, nullable: true, characters: 50, ordinal: 3) : c);
        var ambiguous = UserTable.Resolve(Database(twoText), Project([("ApiAuth", "true"), ("UserTable", "Employee")]));
        Assert.IsNull(ambiguous.Plan);
        StringAssert.Contains(ambiguous.Problem, "more than one column");
        StringAssert.Contains(ambiguous.Problem, "UserNameColumn");

        var bothFlags = UserTable.Resolve(Database(UserTableNamed("Login", "PasswordHash", "IsAdmin", "IsAdministrator")), Project([("ApiAuth", "true"), ("UserTable", "Employee")]));
        Assert.IsNull(bothFlags.Plan);
        StringAssert.Contains(bothFlags.Problem, "more than one administrator flag");
        Assert.IsNotNull(UserTable.Resolve(Database(UserTableNamed("Login", "PasswordHash", "IsAdmin", "IsAdministrator")),
            Project([("ApiAuth", "true"), ("UserTable", "Employee"), ("UserAdminColumn", "IsAdministrator")])).Plan, "naming the column settles it");
    }

    [TestMethod]
    public void The_plan_adds_the_directory_only_with_ApiAuth_and_a_user_table()
    {
        CollectionAssert.Contains(Project(AllSettings).ImpliedPlanTemplates.ToArray(), "API_UserDirectory");
        Assert.IsFalse(Project([("ApiAuth", "true")]).ImpliedPlanTemplates.Contains("API_UserDirectory"));
        Assert.IsFalse(Project(AllSettings, ("ApiAuth", "false")).ImpliedPlanTemplates.Contains("API_UserDirectory"));
    }

    private static async Task<string> RenderDirectory(ProjectSettings project, params TableModel[] tables)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("API_UserDirectory_v1.tt"), Database(tables), project);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    [TestMethod]
    public async Task The_directory_looks_people_up_by_name_and_maps_the_flag_owner_and_hash()
    {
        string text = await RenderDirectory(Project(AllSettings), Employee());

        Expect.Contains(text, "public class TableUserDirectory : IUserDirectory");
        Expect.Contains(text, "private readonly ShopContext _context;");
        Expect.Contains(text, "_context.Set<Employee>().AsNoTracking()");
        Expect.Contains(text, ".Where(u => u.Login == name && u.PasswordHash != null && u.PasswordHash != \"\")");
        Expect.Contains(text, "Role = u.IsManager,");
        Expect.Contains(text, "Owner = u.EmployeeId");
        Expect.Contains(text, "new DirectoryUser(row.Name!, row.Hash!, row.Role == true ? \"Manager\" : \"Employee\", (int?)row.Owner)");
        Expect.Contains(text, "row.PasswordHash = PasswordService.Hash(password);");
        Expect.Contains(text, "services.AddScoped<IUserDirectory>(provider => provider.GetRequiredService<TableUserDirectory>());");
        Expect.Contains(text, "Environment.GetEnvironmentVariable(PasswordVariable)");
        Expect.DoesNotContain(text, "args[2]");
    }

    [TestMethod]
    public async Task Without_an_owner_or_a_role_column_the_token_gets_neither_and_a_text_role_is_used_as_it_is()
    {
        string bare = await RenderDirectory(Project(AllSettings.Where(s => s.Item1 is not ("UserOwnerColumn" or "UserRoleColumn"))), Employee());
        Expect.Contains(bare, "Role = (string?)null,");
        Expect.Contains(bare, "Owner = (int?)null");
        Expect.Contains(bare, "row.Hash!, null, null)");

        string text = await RenderDirectory(Project(AllSettings, ("UserRoleColumn", "RoleName")), Employee(roleName: "RoleName", roleType: SqlDbType.VarChar));
        Expect.Contains(text, "Role = u.RoleName,");
        Expect.Contains(text, "row.Hash!, row.Role, (int?)row.Owner)");
    }

    [TestMethod]
    public async Task A_wrong_setting_stops_the_template_with_the_sentence()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("API_UserDirectory_v1.tt"), Database(Employee()), Project(AllSettings, ("UserNameColumn", "Nope")));
        Assert.IsFalse(result.Success);
        StringAssert.Contains(string.Join(" | ", result.Errors), "UserNameColumn names Nope");
    }

    [TestMethod]
    public async Task Without_UserTable_the_template_writes_nothing()
    {
        string text = await RenderDirectory(Project([("ApiAuth", "true")]), Employee());
        Assert.AreEqual("", text.Trim());
    }

    [TestMethod]
    public async Task Program_registers_the_directory_and_runs_set_password_only_with_a_user_table()
    {
        var with = await Repo.Cache.RunAsync(Repo.Template("API_EssentialProgram_v1.tt"), Project(AllSettings));
        Assert.IsTrue(with.Success, string.Join(" | ", with.Errors));
        string program = GeneratedFiles.Split(with.GeneratedText!).First(f => f.RelativePath.EndsWith("Program.cs")).Content.ReplaceLineEndings("\n");
        Expect.Contains(program, "builder.Services.AddTableUserDirectory();");
        Expect.Contains(program, "if (await UserCommands.TryRunAsync(args, app.Services))");
        Assert.IsTrue(program.IndexOf("AddTableUserDirectory", StringComparison.Ordinal) < program.IndexOf("builder.AddGeneratedAuth();", StringComparison.Ordinal),
            "the directory is registered before the default one can be added");

        foreach (var plain in new[] { Project([("ApiAuth", "true")]), Project([]) })
        {
            var result = await Repo.Cache.RunAsync(Repo.Template("API_EssentialProgram_v1.tt"), plain);
            string text = GeneratedFiles.Split(result.GeneratedText!).First(f => f.RelativePath.EndsWith("Program.cs")).Content;
            Expect.DoesNotContain(text, "TableUserDirectory");
            Expect.DoesNotContain(text, "UserCommands");
        }
    }

    [TestMethod]
    public void The_user_table_settings_are_on_the_sign_in_tab()
    {
        foreach (string key in new[] { "ApiAuth", "OwnedTables", "AuthAdminRole", "UserTable", "UserNameColumn", "PasswordHashColumn", "UserOwnerColumn", "UserRoleColumn", "UserAdminColumn", "UserRoleWhenTrue", "UserRoleWhenFalse" })
            Assert.AreEqual("Sign-in", ProjectSettingGroups.TabOf(key), key);
    }
}
