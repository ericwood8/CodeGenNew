using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> The people of the WinUI3 app: the user table's screen (a Set password box, the administrator flag for administrators only), the first administrator created by the sign-in of an empty user table,
/// and the members page where an administrator says who works in the current tenant. </summary>
[TestClass]
public class UserScreenTests
{
    private static Dictionary<string, string> Winui(Dictionary<string, string>? extra = null)
    {
        var values = new Dictionary<string, string> { ["Stacks"] = "winui3", ["Auth"] = "true" };
        foreach (var (key, value) in extra ?? [])
            values[key] = value;
        return values;
    }

    [TestMethod]
    public async Task The_user_screen_hashes_a_typed_password_and_never_shows_the_hash()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(Winui(), "winui3");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string dialog = read("Views/EmployeeDetailMasterDialog.xaml");
        Expect.Contains(dialog, "<PasswordBox x:Name=\"NewPasswordBox\" Header=\"Set password\"");
        Expect.Contains(dialog, "Password=\"{x:Bind ViewModel.NewPassword, Mode=TwoWay}\"");
        Expect.DoesNotContain(dialog, "ViewModel.PasswordHash");
        string model = read("ViewModels/EmployeeDetailMasterViewModel.cs");
        Expect.Contains(model, "entity.PasswordHash = DesktopSignIn.Hash(NewPassword);");
        Expect.Contains(model, "NewPassword.Length < DesktopSignIn.MinimumPasswordLength");
        Expect.Contains(model, "A new person needs a password.");
        Expect.Contains(model, "private bool MaySetPassword => SignedInIsAdministrator() || (_editing is not null && string.Equals(_editing.Login, DesktopSession.User?.Name");
        Expect.DoesNotContain(model, "private string? _passwordHash");
    }

    [TestMethod]
    public async Task The_administrator_flag_is_shown_and_changed_by_administrators_only()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(Winui(), "winui3");

        Assert.IsTrue(success, string.Join(" | ", errors));
        Expect.Contains(read("Views/EmployeeDetailMasterDialog.xaml"), "<CheckBox Visibility=\"{x:Bind ViewModel.AdminOnlyVisibility}\" Content=\"Is Administrator?\"");
        string model = read("ViewModels/EmployeeDetailMasterViewModel.cs");
        Expect.Contains(model, "private static bool SignedInIsAdministrator() => DesktopSession.User?.Role == \"Administrator\";");
        Expect.Contains(model, "if (SignedInIsAdministrator())\n        {\n            entity.IsAdministrator = IsAdministrator ?? false;\n        }");
        string list = read("ViewModels/EmployeeListViewModel.cs");
        Expect.Contains(list, "public bool IsOffered(string name) => name != \"IsAdministrator\" || DesktopSession.User?.Role == \"Administrator\";");
        Expect.Contains(list, "public bool IsShown(string name) => IsOffered(name) && _chosenColumns.Contains(name);");
        Expect.Contains(read("Views/EmployeeListPage.xaml.cs"), "OfferedColumns.Where(c => ViewModel.IsOffered(c.Name))");
    }

    [TestMethod]
    public async Task Another_table_has_none_of_it()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(Winui(), "winui3");

        Assert.IsTrue(success, string.Join(" | ", errors));
        Expect.DoesNotContain(read("Views/CountryDetailDialog.xaml"), "NewPasswordBox");
        Expect.DoesNotContain(read("ViewModels/CountryDetailViewModel.cs"), "SignedInIsAdministrator");
        Expect.Contains(read("ViewModels/CountryListViewModel.cs"), "public bool IsOffered(string name) => true;");
    }

    [TestMethod]
    public async Task The_sign_in_of_an_empty_user_table_creates_the_first_administrator()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(Winui(), "winui3");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string signIn = read("Repositories/DesktopSignIn.cs");
        Expect.Contains(signIn, "public static async Task<bool> NoPeopleAsync(ShopContext context");
        Expect.Contains(signIn, "public static async Task<bool> CreateFirstAsync(ShopContext context, string userName, string password");
        Expect.Contains(signIn, "Login = name.Length > 30 ? name[..30] : name,");
        Expect.Contains(signIn, "PasswordHash = Hash(password),");
        Expect.Contains(signIn, "IsAdministrator = true,");
        Expect.Contains(signIn, "IsManager = true,");
        Expect.Contains(signIn, "FullName = name.Length > 40 ? name[..40] : name");
        Expect.Contains(signIn, "context.Set<EmployeeTenant>().Add(new EmployeeTenant { EmployeeId = person.EmployeeId, TenantId = tenant, Role = \"Administrator\" });");
        Expect.Contains(signIn, "public static string Hash(string password)");

        string login = read("Views/LoginView.xaml");
        Expect.Contains(login, "<PasswordBox x:Name=\"ConfirmBox\" Header=\"Confirm password\" Visibility=\"Collapsed\"");
        Expect.Contains(login, "x:Name=\"FirstHint\"");
        string code = read("Views/LoginView.xaml.cs");
        Expect.Contains(code, "await ShowFirstPersonOfferAsync();");
        Expect.Contains(code, "TitleText.Text = \"Create the first administrator\";");
        Expect.Contains(code, "if (!await DesktopSignIn.CreateFirstAsync(context, UserNameBox.Text, PasswordBoxField.Password))");
        Expect.Contains(code, "The two passwords are not the same.");
    }

    [TestMethod]
    public async Task The_members_page_is_in_the_menu_for_administrators_and_edits_the_membership_of_the_current_tenant()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(Winui(), "winui3");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string menu = read("MainWindow.Screens.cs");
        Expect.Contains(menu, "if (DesktopSession.HasRole([\"Administrator\"]))");
        Expect.Contains(menu, "Tag = \"Members\"");
        Expect.Contains(menu, "\"Members\" => new MembersPage(_context),");

        string page = read("Views/MembersPage.xaml");
        Expect.Contains(page, "x:Class=\"Shop.App.Views.MembersPage\"");
        Expect.Contains(page, "Content=\"Add member\"");
        Expect.Contains(page, "Content=\"Edit role\"");
        Expect.Contains(page, "Content=\"Remove\"");
        string code = read("Views/MembersPage.xaml.cs");
        Expect.Contains(code, "_context.Set<EmployeeTenant>().IgnoreQueryFilters().AsNoTracking().Where(m => m.TenantId == tenant)");
        Expect.Contains(code, "Where(t => !memberIds.Contains(t.EmployeeId))");
        Expect.Contains(code, "_context.Set<EmployeeTenant>().Add(new EmployeeTenant { EmployeeId = chosen.Id, TenantId = tenant, Role = role });");
        Expect.Contains(code, "You cannot remove yourself from the tenant.");
        Expect.Contains(code, "Switch to a tenant to manage its members.");
    }

    [TestMethod]
    public async Task A_project_without_tenants_has_no_members_page()
    {
        var values = Winui(new() { ["TenantTable"] = "", ["TenantMembership"] = "" });
        var (success, errors, _, files) = await TenantPlanTests.GenerateAsync(values, "winui3");

        Assert.IsTrue(success, string.Join(" | ", errors));
        Assert.IsFalse(files.Any(f => f.EndsWith("MembersPage.xaml")));
    }

    [TestMethod]
    public async Task The_ASP_NET_API_adds_the_first_person_and_changes_a_password_for_an_administrator_or_the_person()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync();

        Assert.IsTrue(success, string.Join(" | ", errors));
        string directory = read("Apis/TableUserDirectory.cs");
        Expect.Contains(directory, "app.MapGet(\"/api/auth/first-person\"");
        Expect.Contains(directory, "app.MapPost(\"/api/auth/first-person\", CreateFirstAsync)");
        Expect.Contains(directory, "app.MapPut(\"/api/auth/password\", SetPasswordAsync)");
        Expect.Contains(directory, "PasswordHash = PasswordService.Hash(password),");
        Expect.Contains(directory, "context.Set<EmployeeTenant>().Add(new EmployeeTenant { EmployeeId = person.EmployeeId, TenantId = tenant, Role = \"Administrator\" });");
        Expect.Contains(directory, "if (!own && !user.IsInRole(AuthNames.AdminRole))");
        Expect.Contains(directory, "type: \"" + ProblemTypes.FirstPersonTaken + "\"");
        Expect.Contains(directory, "type: \"" + ProblemTypes.PasswordNotYours + "\"");
        Expect.Contains(read("Program.cs"), "app.MapUserEndpoints();");
    }

    [TestMethod]
    public async Task A_project_without_a_user_table_has_neither_route()
    {
        var (success, errors, read, files) = await TenantPlanTests.GenerateAsync(new() { ["UserTable"] = "", ["TenantTable"] = "", ["TenantMembership"] = "" });

        Assert.IsTrue(success, string.Join(" | ", errors));
        Assert.IsFalse(files.Any(f => f.EndsWith("TableUserDirectory.cs")));
        Expect.DoesNotContain(read("Program.cs"), "MapUserEndpoints");
    }

    private static Dictionary<string, string> Web(string stack, Dictionary<string, string>? extra = null)
    {
        var values = new Dictionary<string, string> { ["Stacks"] = stack, ["Auth"] = "true" };
        foreach (var (key, value) in extra ?? [])
            values[key] = value;
        return values;
    }

    [TestMethod]
    public async Task The_React_app_offers_the_first_administrator_a_Set_password_box_and_a_members_page()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(Web("react"), "react");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string login = read("pages/LoginPage.tsx");
        Expect.Contains(login, "void firstPersonOpen().then((open) => { if (current) setFirst(open); });");
        Expect.Contains(login, "await createFirstPerson(userName, password);");
        Expect.Contains(login, "<label htmlFor=\"confirm\">Confirm password</label>");
        Expect.Contains(read("api/auth.ts"), "export async function firstPersonOpen(): Promise<boolean>");
        Expect.Contains(read("api/people.ts"), "export const changePassword = (userName: string, password: string): Promise<void> =>");
        Expect.Contains(read("api/people.ts"), "export const removeMember = (personId: number): Promise<void> =>");
        string form = read("pages/EmployeeDetailMasterPage.tsx");
        Expect.Contains(form, "const isAdmin = signedIn?.['role'] === 'Administrator';");
        Expect.Contains(form, "{isAdmin && (");
        Expect.Contains(form, "<label htmlFor=\"newPassword\">Set password</label>");
        Expect.Contains(form, "await changePassword(String(employee.login ?? ''), newPassword);");
        Expect.Contains(read("pages/MembersPage.tsx"), "export function MembersPage()");
        Expect.Contains(read("screens.tsx"), "{ path: 'members', label: 'Members', element: <MembersPage />, roles: ['Administrator'] },");
    }

    [TestMethod]
    public async Task The_Angular_app_offers_the_first_administrator_a_Set_password_box_and_a_members_screen()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(Web("angular", new() { ["AngularVersion"] = "22" }), "angular");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string login = read("login/login.component.ts");
        Expect.Contains(login, "this.auth.firstPersonOpen().subscribe((open) => { this.first = open; });");
        Expect.Contains(login, "this.auth.createFirstPerson(this.userName, this.password).pipe(switchMap(() => signedIn))");
        Expect.Contains(read("login/login.component.html"), "Create the first administrator");
        Expect.Contains(read("services/auth.service.ts"), "firstPersonOpen(): Observable<boolean>");
        string screen = read("employee-detail-master/employee-detail-master.component.ts");
        Expect.Contains(screen, "override submit(row: Employee): void {");
        Expect.Contains(screen, "this.peopleService.changePassword(row.login ?? '', this.newPassword)");
        string html = read("employee-detail-master/employee-detail-master.component.html");
        Expect.Contains(html, "<label for=\"newPassword\">Set password</label>");
        Expect.Contains(read("services/people.service.ts"), "listCandidates(): Observable<MemberCandidate[]>");
        Expect.Contains(read("members/members.component.ts"), "export class MembersComponent implements OnInit");
        Expect.Contains(read("app.routes.ts"), "{ path: 'members', label: 'Members', component: MembersComponent, roles: ['Administrator'] },");
        Expect.Contains(read("components/crud-screen.ts"), "protected followUp(row: T): Observable<unknown> | null {");
    }

    [TestMethod]
    public async Task The_Blazor_app_offers_the_first_administrator_a_Set_password_box_and_a_members_page()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(Web("blazor", new() { ["BlazorTests"] = "true" }), "blazor");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string login = read("Pages/Login.razor");
        Expect.Contains(login, "<h1>@(first ? \"Create the first administrator\" : \"Sign in\")</h1>");
        Expect.Contains(login, "var created = await Http.PostAsJsonAsync(\"/api/auth/first-person\"");
        string page = read("Pages/EmployeePage.razor");
        Expect.Contains(page, "@if (IsAdmin) {");
        Expect.Contains(page, "<label for=\"newPassword\">Set password</label>");
        Expect.Contains(page, "await PeopleApi.ChangePasswordAsync(Http, row.Login ?? \"\", newPassword);");
        Expect.Contains(read("Pages/Members.razor"), "@page \"/members\"");
        Expect.Contains(read("Services/PeopleApi.cs"), "public static async Task ChangePasswordAsync(HttpClient http, string userName, string password)");
        Expect.Contains(read("MembersTests.cs"), "public class MembersTests : PageTestContext");
        Expect.Contains(read("FirstPersonTests.cs"), "public class FirstPersonSignInTests : PageTestContext");
    }

    [TestMethod]
    public async Task The_Uno_app_offers_the_first_administrator_a_Set_password_box_and_a_members_page()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(Web("uno"), "uno");

        Assert.IsTrue(success, string.Join(" | ", errors));
        Expect.Contains(read("Views/LoginPage.xaml"), "<PasswordBox x:Name=\"ConfirmBox\" Header=\"Confirm password\" Visibility=\"Collapsed\"");
        Expect.Contains(read("Views/LoginPage.xaml.cs"), "await ShowFirstPersonOfferAsync();");
        string model = read("ViewModels/EmployeeDetailMasterViewModel.cs");
        Expect.Contains(model, "private bool SignedInIsAdministrator() => _api.Session.HasRole([\"Administrator\"]);");
        Expect.Contains(model, "await PeopleApi.ChangePasswordAsync(_api.Http, entity.Login ?? \"\", NewPassword);");
        Expect.Contains(read("Views/EmployeeDetailMasterDialog.xaml"), "<PasswordBox x:Name=\"NewPasswordBox\" Header=\"Set password\"");
        Expect.Contains(read("Views/MembersPage.xaml.cs"), "public sealed partial class MembersPage : Page");
        Expect.DoesNotContain(read("Views/MembersPage.xaml.cs"), "§");
        Expect.Contains(read("MainPage.Screens.cs"), "(\"Members\", \"Members\", [\"Administrator\"]),");
        Expect.Contains(read("MainPage.Screens.cs"), "\"Members\" => new MembersPage(_api),");
        Expect.Contains(read("Services/ApiClients.cs"), "public HttpClient Http => http;");
    }

    [TestMethod]
    public async Task The_Python_and_Rust_APIs_have_the_same_people_routes()
    {
        var (success, errors, readPython, _) = await TenantPlanTests.GenerateAsync(new() { ["Stacks"] = "python" }, "python");
        Assert.IsTrue(success, string.Join(" | ", errors));
        string py = readPython("app/people.py");
        Expect.Contains(py, "@public_router.get(\"/api/auth/first-person\")");
        Expect.Contains(py, "@public_router.post(\"/api/auth/first-person\", status_code=204)");
        Expect.Contains(py, "@router.put(\"/api/auth/password\", status_code=204)");
        Expect.Contains(py, "@router.get(\"/api/auth/members\")");
        Expect.Contains(py, "@router.delete(\"/api/auth/members/{person_id}\", status_code=204)");
        Expect.Contains(py, "session.execute(members.insert().values({MEMBER_USER: person_id, MEMBER_TENANT: first, MEMBER_ROLE: ADMIN_ROLE}))");
        Expect.Contains(readPython("app/main.py"), "app.include_router(people_public_router)");

        var (rustSuccess, rustErrors, readRust, _) = await TenantPlanTests.GenerateAsync(new() { ["Stacks"] = "rust" }, "rust");
        Assert.IsTrue(rustSuccess, string.Join(" | ", rustErrors));
        string rs = readRust("src/people.rs");
        Expect.Contains(rs, "async fn create_first_person(State(pool): State<Pool>, ApiJson(body): ApiJson<FirstPersonBody>) -> ApiResult<StatusCode>");
        Expect.Contains(rs, ".route(\"/api/auth/members\", get(list_members).post(add_member))");
        Expect.Contains(rs, ".route(\"/api/auth/members/{person_id}\", put(change_role).delete(remove_member))");
        Expect.Contains(readRust("src/routes/mod.rs"), ".merge(crate::people::router())");
        Expect.Contains(readRust("src/auth.rs"), ".merge(crate::people::public_router())");
    }

    [TestMethod]
    public async Task The_ASP_NET_API_has_the_members_routes()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync();

        Assert.IsTrue(success, string.Join(" | ", errors));
        string directory = read("Apis/TableUserDirectory.cs");
        Expect.Contains(directory, "app.MapGet(\"/api/auth/members\", ListMembersAsync)");
        Expect.Contains(directory, "app.MapPut(\"/api/auth/members/{personId:int}\", ChangeMemberRoleAsync)");
        Expect.Contains(directory, "app.MapDelete(\"/api/auth/members/{personId:int}\", RemoveMemberAsync)");
        Expect.Contains(directory, "public record MemberAnswer(int PersonId, string Login, string Person, string? Role);");
        Expect.Contains(directory, "if (!user.IsInRole(AuthNames.AdminRole))");
    }
}
