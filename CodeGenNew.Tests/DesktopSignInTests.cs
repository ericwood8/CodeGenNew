using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> The sign-in, tenant box and tenant switcher of the WinUI3 app (Auth with the WinUI3 stack and a UserTable), and the first administrator of an onboarded tenant as a new person. </summary>
[TestClass]
public class DesktopSignInTests
{
    private static Dictionary<string, string> Winui(Dictionary<string, string>? extra = null)
    {
        var values = new Dictionary<string, string> { ["Stacks"] = "winui3", ["Auth"] = "true" };
        foreach (var (key, value) in extra ?? [])
            values[key] = value;
        return values;
    }

    [TestMethod]
    public void The_sign_in_needs_Auth_the_WinUI3_stack_and_the_user_table_of_the_API_sign_in()
    {
        ProjectSettings Project(params (string, string)[] values) =>
            ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Item1, v.Item2)));

        var all = new[] { ("Auth", "true"), ("ApiAuth", "true"), ("UserTable", "Employee"), ("Stacks", "api,winui3") };
        Assert.IsTrue(TenantPlan.HasDesktopSignIn(Project(all)));
        foreach (string missing in new[] { "Auth", "ApiAuth", "UserTable" })
            Assert.IsFalse(TenantPlan.HasDesktopSignIn(Project(all.Where(v => v.Item1 != missing).ToArray())), missing);
        Assert.IsFalse(TenantPlan.HasDesktopSignIn(Project(all.Select(v => v.Item1 == "Stacks" ? ("Stacks", "api,uno") : v).ToArray())), "no WinUI3 stack");
        Assert.IsFalse(TenantPlan.HasDesktopTenants(Project(all)));
        var tenants = all.Append(("TenantTable", "Tenant")).ToArray();
        Assert.IsTrue(TenantPlan.HasDesktopTenants(Project(tenants)));
        Assert.IsFalse(TenantPlan.HasDesktopSwitcher(Project(tenants)));
        Assert.IsTrue(TenantPlan.HasDesktopSwitcher(Project(tenants.Append(("TenantMembership", "EmployeeTenant:EmployeeId:TenantId:Role")).ToArray())));
    }

    [TestMethod]
    public async Task The_app_checks_the_password_against_the_user_table_and_picks_the_tenant()
    {
        var (success, errors, read, files) = await TenantPlanTests.GenerateAsync(Winui(), "winui3");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string signIn = read("Repositories/DesktopSignIn.cs");
        Expect.Contains(signIn, "public static bool Verify(string password, string stored)");
        Expect.Contains(signIn, "parts[0] != \"pbkdf2-sha256\"");
        Expect.Contains(signIn, "context.Set<Employee>().IgnoreQueryFilters().AsNoTracking()");
        Expect.Contains(signIn, ".Where(u => u.Login == name && u.PasswordHash != null && u.PasswordHash != \"\")");
        Expect.Contains(signIn, "row.Admin == true ? \"Administrator\"");
        Expect.Contains(signIn, "public sealed record SignedInUser(string Name, string? Role, int? OwnerId, int? TenantId, string? OwnRole)");
        Expect.Contains(signIn, "public bool IsPlatform => TenantId is null && Role == \"Administrator\";");
        Expect.Contains(signIn, "public static async Task<SignedInUser> SignInAsync(ShopContext context, string userName, string password, int? tenantId = null");
        Expect.Contains(signIn, "role = chosen.Role ?? role;");
        Expect.Contains(signIn, "public static async Task<SignedInUser?> SwitchAsync(");
        Expect.Contains(signIn, "throw new SignInException(SignInProblem.TooManyTries);");

        string login = read("Views/LoginView.xaml");
        Expect.Contains(login, "x:Class=\"Shop.App.Views.LoginView\"");
        Expect.Contains(login, "<ComboBox x:Name=\"TenantBox\" Header=\"Tenant\" DisplayMemberPath=\"Name\"");
        Expect.Contains(login, "<PasswordBox x:Name=\"PasswordBoxField\"");
        string code = read("Views/LoginView.xaml.cs");
        Expect.Contains(code, "await DesktopSignIn.SignInAsync(context, UserNameBox.Text, PasswordBoxField.Password, (TenantBox.SelectedItem as SignInTenant)?.Id);");
        Expect.Contains(code, "TenantBox.ItemsSource = await DesktopSignIn.SignInTenantsAsync(context);");
        Expect.Contains(signIn, "public static async Task<List<SignInTenant>> SignInTenantsAsync(ShopContext context");
        Expect.Contains(code, "SignInProblem.NotMember => \"You are not a member of that tenant, or it is switched off.\"");
        Expect.Contains(code, "\"The user name or password is not right.\"");
        Assert.IsTrue(files.Any(f => f.EndsWith("Repositories/DesktopSignIn.cs")));
    }

    [TestMethod]
    public async Task The_window_shows_the_sign_in_first_then_the_menu_the_tenant_and_Sign_out()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(Winui(), "winui3");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string xaml = read("MainWindow.xaml");
        Expect.Contains(xaml, "<NavigationView x:Name=\"NavView\" IsSettingsVisible=\"False\" IsBackButtonVisible=\"Collapsed\" Visibility=\"Collapsed\"");
        Expect.Contains(xaml, "<ContentControl x:Name=\"SignInHost\"");
        Expect.Contains(xaml, "<Button x:Name=\"SignOutButton\" Content=\"Sign out\" Click=\"OnSignOutClick\" />");
        Expect.Contains(xaml, "<ComboBox x:Name=\"TenantList\" AutomationProperties.Name=\"Tenant\" DisplayMemberPath=\"Text\"");
        string code = read("MainWindow.xaml.cs");
        Expect.Contains(code, "private ShopContext _context = new();");
        Expect.Contains(code, "SignInHost.Content = new LoginView(OnSignedIn);");
        Expect.Contains(code, "DesktopTenantScope.Current.Use(_user?.TenantId, _user?.IsPlatform ?? false, _user?.Name);");
        Expect.Contains(code, "await DesktopSignIn.SwitchAsync(context, _user, choice.Id)");
        Expect.DoesNotContain(code, "DesktopCaller");

        var (_, _, readPlain, _) = await TenantPlanTests.GenerateAsync(Winui(new() { ["Auth"] = "false" }), "winui3");
        string plain = readPlain("MainWindow.xaml.cs");
        Expect.Contains(plain, "private readonly ShopContext _context = new();");
        Expect.DoesNotContain(plain, "LoginView");
        Expect.DoesNotContain(readPlain("MainWindow.xaml"), "SignInHost");
    }

    [TestMethod]
    public async Task The_context_stamps_the_tenant_on_a_new_row_and_never_moves_a_row_and_holds_no_ASP_NET_type()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(Winui(), "winui3");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string context = read("Data/ShopContext.cs");
        Expect.Contains(context, "_tenant = DesktopTenantScope.Current;");
        Expect.Contains(context, "public sealed class DesktopTenantScope : ITenantScope");
        Expect.Contains(context, "[typeof(Project)] = \"TenantId\",");
        Expect.Contains(context, "entry.Property(column).CurrentValue = tenant;");
        Expect.Contains(context, "entry.Property(column).IsModified = false;");
        Expect.Contains(context, "public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)");
        Expect.DoesNotContain(context, "Microsoft.AspNetCore");
        Expect.DoesNotContain(context, "AuthNames");

        var (_, _, readApi, _) = await TenantPlanTests.GenerateAsync();
        Expect.DoesNotContain(readApi("Data/ShopContext.cs"), "StampTenant");
    }

    [TestMethod]
    public async Task A_sign_in_that_sets_the_caller_tells_the_guards_and_approvals_who_it_is()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(Winui(new() { ["RowApprovals"] = "Project" }), "winui3", ["CREATE TABLE ProjectApproval (ProjectApprovalId INTEGER PRIMARY KEY AUTOINCREMENT, ProjectId INTEGER NOT NULL REFERENCES Project (ProjectId), ApprovalStatusId INTEGER NOT NULL DEFAULT 1, Reason VARCHAR(500) NULL, RequestedBy VARCHAR(50) NOT NULL, RequestedOn DATETIME NOT NULL, DecidedBy VARCHAR(50) NULL, DecidedOn DATETIME NULL, DecisionNote VARCHAR(500) NULL, Revision INTEGER NOT NULL DEFAULT 1)"]);

        Assert.IsTrue(success, string.Join(" | ", errors));
        string code = read("MainWindow.xaml.cs");
        Expect.Contains(code, "DesktopCaller.Name = user.Name;");
        Expect.Contains(code, "DesktopCaller.Name = Environment.UserName;");
    }

    [TestMethod]
    public async Task Onboarding_takes_the_first_administrator_as_a_new_person_with_a_password()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync();

        Assert.IsTrue(success, string.Join(" | ", errors));
        string tenants = read("Apis/Tenants.cs");
        Expect.Contains(tenants, "public sealed record OnboardRequest(Tenant? Tenant, int? AdminId, Employee? Admin, string? AdminPassword);");
        Expect.Contains(tenants, "Send adminId (a person who exists) or admin (a new person), not both.");
        Expect.Contains(tenants, "(request.AdminPassword ?? \"\").Length < UserCommands.MinimumPasswordLength");
        Expect.Contains(tenants, "AnyAsync(u => u.Login == login, http.RequestAborted)");
        Expect.Contains(tenants, "person.PasswordHash = PasswordService.Hash(request.AdminPassword!);");
        Expect.Contains(tenants, "context.Add(new EmployeeTenant { EmployeeId = person.EmployeeId, TenantId = tenant.TenantId, Role = AuthNames.AdminRole });");
        Expect.Contains(tenants, "public static async Task RemoveWithAdminAsync(IServiceProvider services, int tenantId, string userName)");
    }

    [TestMethod]
    public async Task A_person_who_belongs_to_a_tenant_by_their_own_row_gets_the_new_tenant_and_the_administrator_flag()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(new() { ["TenantMembership"] = "", ["UserTable"] = "Person", ["UserAdminColumn"] = "IsAdministrator" }, more:
            ["CREATE TABLE Person (PersonId INTEGER PRIMARY KEY AUTOINCREMENT, TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId), FullName VARCHAR(40) NOT NULL, Login VARCHAR(30) NOT NULL, PasswordHash VARCHAR(200) NOT NULL DEFAULT 'x', IsManager BOOLEAN NOT NULL DEFAULT 0, IsAdministrator BOOLEAN NOT NULL DEFAULT 0)"]);

        Assert.IsTrue(success, string.Join(" | ", errors));
        string tenants = read("Apis/Tenants.cs");
        Expect.Contains(tenants, "public sealed record OnboardRequest(Tenant? Tenant, Person? Admin, string? AdminPassword);");
        Expect.Contains(tenants, "person.TenantId = tenant.TenantId;");
        Expect.Contains(tenants, "person.IsAdministrator = true;");
        Expect.DoesNotContain(tenants, "AdminId");
    }

    [TestMethod]
    public async Task A_tenant_shows_only_its_own_members_of_the_user_table_when_the_project_has_a_membership_table()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync();

        Assert.IsTrue(success, string.Join(" | ", errors));
        string context = read("Data/ShopContext.cs");
        Expect.Contains(context, "modelBuilder.Entity<Employee>().HasQueryFilter(e => (IsPlatform || Set<EmployeeTenant>().Any(m => m.EmployeeId == e.EmployeeId && m.TenantId == CurrentTenantId)));");
        Expect.Contains(context, "ChangeTracker.Entries<Employee>().Where(e => e.State == EntityState.Added)");
        Expect.Contains(context, "Set<EmployeeTenant>().Add(new EmployeeTenant { EmployeeId = person.EmployeeId, TenantId = CurrentTenantId!.Value });");
        Expect.Contains(context, "var leavers = leaving.Count > 0 ? await LeavingMembers(leaving).ToListAsync(cancellationToken) : [];");
        Expect.Contains(context, "KeepSharedPeople(await OtherMemberships(leaving).ToListAsync(cancellationToken));");
        Expect.Contains(context, "entry.State = EntityState.Unchanged;");

        var (_, _, readPython, _) = await TenantPlanTests.GenerateAsync(new() { ["Stacks"] = "python" }, "python");
        Expect.Contains(readPython("app/routes/employee.py"), "statement = only_members(statement, Employee.employee_id, user)");
        Expect.Contains(readPython("app/routes/employee.py"), "attach_person(session, row.employee_id, user)");
        Expect.Contains(readPython("app/routes/tenancy.py"), "def only_members(statement, key_column, user: CurrentUser):");
        var (_, _, readRust, _) = await TenantPlanTests.GenerateAsync(new() { ["Stacks"] = "rust" }, "rust");
        Expect.Contains(readRust("src/routes/employee.rs"), "Tenancy::of_members(&user)");
        Expect.Contains(readRust("src/tenancy.rs"), "pub async fn detach_person(pool: &Pool, person_id: i32, user: &CurrentUser) -> ApiResult<bool> {");

        var (_, _, readOwn, _) = await TenantPlanTests.GenerateAsync(new() { ["TenantMembership"] = "" });
        Expect.DoesNotContain(readOwn("Data/ShopContext.cs"), "AddedPeople");
        Expect.DoesNotContain(readOwn("Data/ShopContext.cs"), "Entity<Employee>().HasQueryFilter");
    }

    [TestMethod]
    public async Task The_menu_and_the_buttons_that_write_follow_the_roles_of_the_signed_in_person()
    {
        var roles = new Dictionary<string, string> { ["WriteRoles"] = "Manager", ["TableWriteRoles"] = "Project:Administrator", ["TableReadRoles"] = "Country:Administrator", ["Screens"] = "Tenant,Project,Country" };
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(Winui(roles), "winui3");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string screens = read("MainWindow.Screens.cs");
        Expect.Contains(screens, "private static readonly (string Tag, string Label, string[]? Roles)[] Screens =");
        Expect.Contains(screens, "(\"Country\", \"Country\", [\"Administrator\"]),");
        Expect.Contains(screens, "(\"Project\", \"Project\", null),");
        Expect.Contains(screens, "if (roles is null || DesktopSession.HasRole(roles))");
        Expect.Contains(screens, "NavView.MenuItems.Clear();");
        Expect.Contains(screens, "private string FirstScreen =>");
        Expect.Contains(read("Repositories/DesktopSignIn.cs"), "public static bool HasRole(IEnumerable<string> roles) => User?.Role is { } role && roles.Contains(role);");

        string window = read("MainWindow.xaml.cs");
        Expect.Contains(window, "DesktopSession.User = user;");
        Expect.Contains(window, "DesktopSession.User = null;");
        Expect.Contains(window, "DesktopSession.User = switched;");
        Expect.Contains(window, "        AddScreens();\n        ShowFirstScreen();");

        string project = read("ViewModels/ProjectListViewModel.cs");
        Expect.Contains(project, "public Visibility WriteVisibility => DesktopSession.HasRole([\"Administrator\"]) ? Visibility.Visible : Visibility.Collapsed;");
        Expect.Contains(project, "WriteVisibility = WriteVisibility");
        string page = read("Views/ProjectListPage.xaml");
        Expect.Contains(page, "<Button Content=\"Add New\" Click=\"OnAddClick\" Visibility=\"{x:Bind ViewModel.WriteVisibility}\" />");
        Expect.Contains(page, "Click=\"OnEditClick\" Visibility=\"{x:Bind WriteVisibility}\" />");
        Expect.Contains(page, "Click=\"OnDeleteClick\" Visibility=\"{x:Bind WriteVisibility}\" />");

        var (_, _, readPlain, _) = await TenantPlanTests.GenerateAsync(Winui(), "winui3");
        Expect.DoesNotContain(readPlain("MainWindow.Screens.cs"), "DesktopSession");
        Expect.DoesNotContain(readPlain("Repositories/DesktopSignIn.cs"), "DesktopSession");
        Expect.DoesNotContain(readPlain("Views/ProjectListPage.xaml"), "WriteVisibility");
        Expect.Contains(readPlain("MainWindow.xaml.cs"), "ShowFirstScreen();");

        var (_, _, readNoSignIn, _) = await TenantPlanTests.GenerateAsync(Winui(new(roles) { ["UserTable"] = "" }), "winui3");
        Expect.DoesNotContain(readNoSignIn("MainWindow.Screens.cs"), "DesktopSession");
    }

    [TestMethod]
    public async Task The_dashboard_leaves_out_the_widgets_of_tables_the_signed_in_role_may_not_read()
    {
        var values = new Dictionary<string, string> { ["Dashboard"] = "true", ["DashboardStrip"] = "true", ["TableReadRoles"] = "Country:Administrator" };
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(Winui(values), "winui3");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string page = read("Views/DashboardPage.cs");
        Expect.Contains(page, "[\"Country\"] = [\"Administrator\"],");
        Expect.Contains(page, "DesktopSession.HasRole(roles)");
        Expect.Contains(page, "result.Widgets.RemoveAll(w => !DashboardRoles.Allows(w.Table));");
        Expect.Contains(read("Views/DashboardStrip.cs"), "result.Widgets.RemoveAll(w => !DashboardRoles.Allows(w.Table));");

        var (_, _, readPlain, _) = await TenantPlanTests.GenerateAsync(Winui(new() { ["Dashboard"] = "true", ["DashboardStrip"] = "true" }), "winui3");
        Expect.DoesNotContain(readPlain("Views/DashboardPage.cs"), "DashboardRoles");
    }

    [TestMethod]
    public async Task Only_a_platform_administrator_adds_a_tenant_in_the_app_and_becomes_its_first_administrator()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(Winui(new() { ["Screens"] = "Tenant,Project" }), "winui3");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string list = read("ViewModels/TenantListViewModel.cs");
        Expect.Contains(list, "public Visibility CreateVisibility => DesktopTenantScope.Current.IsPlatform ? Visibility.Visible : Visibility.Collapsed;");
        Expect.Contains(list, "CreateVisibility = CreateVisibility");
        string page = read("Views/TenantListPage.xaml");
        Expect.Contains(page, "<Button Content=\"Add New\" Click=\"OnAddClick\" Visibility=\"{x:Bind ViewModel.CreateVisibility}\" />");
        Expect.DoesNotContain(read("Views/ProjectListPage.xaml"), "CreateVisibility");

        string context = read("Data/ShopContext.cs");
        Expect.Contains(context, "IsPlatform ? ChangeTracker.Entries<Tenant>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList() : [];");
        Expect.Contains(context, "(_tenant as DesktopTenantScope)?.UserName");
        Expect.Contains(context, "Set<EmployeeTenant>().Add(new EmployeeTenant { EmployeeId = person.Value, TenantId = tenant.TenantId, Role = \"Administrator\" });");
        Expect.Contains(context, "return (AttachPeople(people) | AttachCreators(tenants, tenants.Count > 0 ? await CreatorKeys().FirstOrDefaultAsync(cancellationToken) : null)) ? saved + await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken) : saved;");
        Expect.Contains(context, "public void Use(int? tenantId, bool isPlatform, string? userName = null)");
        Expect.Contains(read("MainWindow.xaml.cs"), "DesktopTenantScope.Current.Use(_user?.TenantId, _user?.IsPlatform ?? false, _user?.Name);");

        var (_, _, readApi, _) = await TenantPlanTests.GenerateAsync();
        Expect.DoesNotContain(readApi("Data/ShopContext.cs"), "AttachCreators");
    }
}
