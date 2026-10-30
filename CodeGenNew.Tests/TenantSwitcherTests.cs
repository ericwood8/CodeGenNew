using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> The tenant name and switcher in the top bar of the React, Angular and Blazor apps (Auth with TenantTable and TenantMembership). </summary>
[TestClass]
public class TenantSwitcherTests
{
    private static readonly (string, string)[] Tenants =
    [
        ("Auth", "true"), ("ApiAuth", "true"), ("TenantTable", "Tenant"), ("TenantMembership", "EmployeeTenant:EmployeeId:TenantId:Role"), ("UserTable", "Employee"), ("BlazorTests", "true")
    ];

    private static async Task<string> Run(string template, params (string, string)[] values)
    {
        var project = ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Item1, v.Item2)).Append(new("ProjectName", "Acme")));
        var result = await Repo.Cache.RunAsync(Repo.Template(template), project);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    [TestMethod]
    public void The_switcher_needs_the_sign_in_the_API_sign_in_and_a_membership_table()
    {
        Assert.IsTrue(TenantPlan.HasSwitcher(ProjectSettings.FromValues(Tenants.Select(v => new KeyValuePair<string, string>(v.Item1, v.Item2)))));
        foreach (string missing in new[] { "Auth", "ApiAuth", "TenantTable", "TenantMembership" })
            Assert.IsFalse(TenantPlan.HasSwitcher(ProjectSettings.FromValues(Tenants.Where(v => v.Item1 != missing).Select(v => new KeyValuePair<string, string>(v.Item1, v.Item2)))), missing);
    }

    [TestMethod]
    public async Task React_shows_the_tenant_and_makes_every_screen_again_on_a_switch()
    {
        string auth = await Run("TSX_EssentialAuth_v1.tt", Tenants);
        Expect.Contains(auth, "@@@FILE src/components/TenantSwitcher.tsx@@@");
        Expect.Contains(auth, "@@@FILE src/components/__tests__/TenantSwitcher.test.tsx@@@");
        Expect.Contains(auth, "fetch(`${API_BASE_URL}/api/auth/tenants`");
        Expect.Contains(auth, "fetch(`${API_BASE_URL}/api/auth/switch-tenant`");
        Expect.Contains(auth, "<select className=\"tenant\" aria-label=\"Tenant\"");

        string shell = await Run("TSX_EssentialShell_v1.tt", Tenants);
        Expect.Contains(shell, "import { TenantSwitcher } from './components/TenantSwitcher';");
        Expect.Contains(shell, "<TenantSwitcher />");
        Expect.Contains(shell, "<Routes key={String(auth.user?.['tenantId'] ?? '')}>");

        string without = await Run("TSX_EssentialAuth_v1.tt", Tenants.Where(v => v.Item1 != "TenantMembership").ToArray());
        Expect.DoesNotContain(without, "TenantSwitcher");
        Expect.DoesNotContain(await Run("TSX_EssentialShell_v1.tt", Tenants.Where(v => v.Item1 != "TenantMembership").ToArray()), "Tenant");
    }

    [TestMethod]
    public async Task Angular_shows_the_tenant_and_starts_the_app_again_on_a_switch()
    {
        string auth = await Run("TS_EssentialAuth_v1.tt", Tenants);
        Expect.Contains(auth, "@@@FILE src/app/components/tenant-switcher/tenant-switcher.component.ts@@@");
        Expect.Contains(auth, "@@@FILE src/app/components/tenant-switcher/tenant-switcher.component.spec.ts@@@");
        Expect.Contains(auth, "this.http.get<TenantChoice[]>('api/auth/tenants')");
        Expect.Contains(auth, "this.http.post<LoginResponse>('api/auth/switch-tenant', { tenantId })");
        Expect.Contains(auth, "this.auth.keep(response);");
        Expect.Contains(auth, "export interface TenantChoice {");

        string shell = await Run("TS_EssentialShell_v1.tt", Tenants);
        Expect.Contains(shell, "<app-tenant-switcher></app-tenant-switcher>");
        Expect.Contains(shell, "TenantSwitcherComponent");

        string plain = await Run("TS_EssentialAuth_v1.tt", Tenants.Where(v => v.Item1 != "TenantMembership").ToArray());
        Expect.DoesNotContain(plain, "tenant");
        Expect.DoesNotContain(await Run("TS_EssentialShell_v1.tt", Tenants.Where(v => v.Item1 != "TenantMembership").ToArray()), "tenant");
    }

    [TestMethod]
    public async Task Blazor_shows_the_tenant_and_loads_the_app_again_on_a_switch()
    {
        string auth = await Run("BLZ_EssentialAuth_v1.tt", Tenants);
        Expect.Contains(auth, "@@@FILE Components/TenantSwitcher.razor@@@");
        Expect.Contains(auth, "public record SessionUser(string Name, string? Role, int? OwnerId, int? TenantId = null);");
        Expect.Contains(auth, "public async Task KeepAsync(SessionData data)");
        Expect.Contains(auth, "Navigation.NavigateTo(\"/\", forceLoad: true);");

        string layout = await Run("BLZ_EssentialLayout_v1.tt", Tenants);
        Expect.Contains(layout, "<TenantSwitcher />");
        Expect.Contains(layout, ".topbar .tenant");

        string tests = await Run("BLZ_EssentialTests_v1.tt", Tenants);
        Expect.Contains(tests, "@@@FILE TenantSwitcherTests.cs@@@");

        string plain = await Run("BLZ_EssentialAuth_v1.tt", Tenants.Where(v => v.Item1 != "TenantMembership").ToArray());
        Expect.DoesNotContain(plain, "TenantSwitcher");
        Expect.DoesNotContain(plain, "TenantId");
    }

    [TestMethod]
    public async Task Uno_shows_the_tenant_in_the_pane_and_makes_every_screen_again_on_a_switch()
    {
        string auth = await Run("UNO_EssentialAuth_v1.tt", Tenants);
        Expect.Contains(auth, "public record SessionUser(string Name, string? Role, int? OwnerId, int? TenantId = null);");
        Expect.Contains(auth, "public record TenantChoice(int Id, string Text, string? Role, bool Current);");
        Expect.Contains(auth, "http.GetFromJsonAsync<List<TenantChoice>>(\"/api/auth/tenants\", Web)");
        Expect.Contains(auth, "http.PostAsJsonAsync(\"/api/auth/switch-tenant\", new { tenantId })");
        Expect.Contains(auth, "Changed?.Invoke();");

        string app = await Run("UNO_EssentialApp_v1.tt", Tenants);
        Expect.Contains(app, "<TextBlock x:Name=\"TenantName\"");
        Expect.Contains(app, "<ComboBox x:Name=\"TenantList\" AutomationProperties.Name=\"" + TenantWords.SignInLabel + "\"");
        Expect.Contains(app, "_ = ShowTenantsAsync();");
        Expect.Contains(app, "await _session.SwitchTenantAsync(_http, choice.Id)");

        var plain = Tenants.Where(v => v.Item1 != "TenantMembership").ToArray();
        Expect.DoesNotContain(await Run("UNO_EssentialApp_v1.tt", plain), "Tenant");
        string plainAuth = await Run("UNO_EssentialAuth_v1.tt", plain);
        Expect.DoesNotContain(plainAuth, "TenantChoice");
        Expect.DoesNotContain(plainAuth, "SwitchTenantAsync");
    }

    [TestMethod]
    public async Task The_sign_in_has_a_tenant_box_in_every_front_end_that_signs_in_through_the_API()
    {
        var plain = Tenants.Where(v => v.Item1 != "TenantMembership").ToArray();
        Assert.IsTrue(TenantPlan.HasSignInBox(ProjectSettings.FromValues(Tenants.Select(v => new KeyValuePair<string, string>(v.Item1, v.Item2)))));
        Assert.IsFalse(TenantPlan.HasSignInBox(ProjectSettings.FromValues(plain.Select(v => new KeyValuePair<string, string>(v.Item1, v.Item2)))), "a person who belongs to one tenant has nothing to choose");

        string react = await Run("TSX_EssentialAuth_v1.tt", Tenants);
        Expect.Contains(react, "export async function login(userName: string, password: string, tenantId?: number): Promise<LoginResponse> {");
        Expect.Contains(react, "JSON.stringify(tenantId === undefined ? { userName, password } : { userName, password, tenantId })");
        Expect.Contains(react, "<label htmlFor=\"tenant\">" + TenantWords.SignInLabel + "</label>");
        Expect.Contains(react, "setMessage('" + TenantWords.SignInRefused + "')");
        Expect.Contains(react, "signs in to the tenant that is typed");
        string reactPlain = await Run("TSX_EssentialAuth_v1.tt", plain);
        Expect.DoesNotContain(reactPlain, "tenantId");
        Expect.DoesNotContain(reactPlain, "htmlFor=\"tenant\"");

        string angular = await Run("TS_EssentialAuth_v1.tt", Tenants);
        Expect.Contains(angular, "login(userName: string, password: string, tenantId?: number | null): Observable<LoginResponse> {");
        Expect.Contains(angular, "[(ngModel)]=\"tenant\"");
        Expect.Contains(angular, "this.auth.login(this.userName, this.password, this.tenant).subscribe({");
        Expect.Contains(angular, "this.message = '" + TenantWords.SignInRefused + "';");
        Expect.DoesNotContain(await Run("TS_EssentialAuth_v1.tt", plain), "tenantId");

        string blazor = await Run("BLZ_EssentialAuth_v1.tt", Tenants);
        Expect.Contains(blazor, "public async Task LoginAsync(HttpClient http, string userName, string password, int? tenantId = null)");
        Expect.Contains(blazor, "<input id=\"tenant\" type=\"number\"");
        Expect.Contains(blazor, "await Session.LoginAsync(Http, userName.Trim(), password, tenant);");
        Expect.Contains(blazor, "403 => \"" + TenantWords.SignInRefused + "\"");
        Expect.Contains(await Run("BLZ_EssentialTests_v1.tt", Tenants), "public class TenantSignInTests : PageTestContext");
        Expect.DoesNotContain(await Run("BLZ_EssentialAuth_v1.tt", plain), "tenantId");

        string uno = await Run("UNO_EssentialAuth_v1.tt", Tenants);
        Expect.Contains(uno, "public async Task LoginAsync(HttpClient http, string userName, string password, int? tenantId = null)");
        Expect.Contains(uno, "<NumberBox x:Name=\"TenantBox\" Header=\"" + TenantWords.SignInLabel + "\"");
        Expect.Contains(uno, "int.TryParse(TenantBox.Text.Trim(), out int tenant) ? tenant : null");
        Expect.DoesNotContain(await Run("UNO_EssentialAuth_v1.tt", plain), "TenantBox");
    }
}
