using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The Angular sign-in (Auth=true): the Auth essentials group, the interceptor in app.config.ts, the guard on every generated route and the user with Sign out in the shell. </summary>
[TestClass]
public class AuthTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static async Task<Dictionary<string, string>> Run(string template, params (string Key, string Value)[] values)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), Project(values));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return GeneratedFiles.Split(result.GeneratedText!).ToDictionary(f => Path.GetFileName(f.RelativePath), f => f.Content.ReplaceLineEndings("\n"));
    }

    private static readonly (string, string) On = ("Auth", "true");

    [TestMethod]
    public void Auth_is_a_check_box_setting_that_turns_on_the_Auth_group()
    {
        Assert.IsFalse(Project().Auth);
        Assert.IsTrue(Project(On).Auth);
        Assert.Contains("Auth", ProjectSettingsHints.BooleanKeys);
        CollectionAssert.AreEqual(new[] { "Auth" }, Project(On).ImpliedEssentialsGroups.ToArray());
        Assert.IsEmpty(Project().ImpliedEssentialsGroups.ToArray());

        var group = EssentialsCatalog.Groups(Repo.TemplatesDirectory, "Angular").Single(g => g.Name == "Auth");
        Assert.IsFalse(group.DefaultOn);   // not written for a project that did not ask
        Assert.IsFalse(EssentialsCatalog.IsOnFor(group, Project()));
        Assert.IsTrue(EssentialsCatalog.IsOnFor(group, Project(On)));
    }

    [TestMethod]
    public async Task The_Auth_group_writes_the_service_interceptor_guard_model_login_page_and_their_specs()
    {
        var files = await Run("TS_EssentialAuth_v1.tt");

        CollectionAssert.AreEquivalent(new[]
        {
            "auth.ts", "auth.service.ts", "auth.interceptor.ts", "auth.guard.ts", "login.component.ts", "login.component.html", "login.component.css",
            "auth.service.spec.ts", "auth.interceptor.spec.ts", "auth.guard.spec.ts", "login.component.spec.ts"
        }, files.Keys.ToArray());

        string service = files["auth.service.ts"];
        Expect.Contains(service, "const storageKey = 'acme.auth';");
        Expect.Contains(service, "return this.http.post<LoginResponse>('api/auth/login', { userName, password })");
        Expect.Contains(service, "sessionStorage.setItem(storageKey");
        Expect.Contains(service, "this.router.navigate(['/login']);");
        Expect.Contains(service, "import { AuthUser, LoginResponse } from '../models/auth';");

        string interceptor = files["auth.interceptor.ts"];
        Expect.Contains(interceptor, "request.url.startsWith('api/')");
        Expect.Contains(interceptor, "setHeaders: { Authorization: `Bearer ${token}` }");
        Expect.Contains(interceptor, "error.status === 401 && isApiCall && !request.url.endsWith('/auth/login')");
        Expect.Contains(files["auth.guard.ts"], "createUrlTree(['/login'])");
        Expect.Contains(files["login.component.ts"], "import { AuthService } from '../../services/auth.service';");
        Expect.Contains(files["login.component.ts"], "error.status === 429");
        Expect.Contains(files["login.component.html"], "[(ngModel)]=\"password\"");
    }

    [TestMethod]
    public async Task The_login_page_follows_the_Angular_version()
    {
        var old = await Run("TS_EssentialAuth_v1.tt", ("AngularVersion", "18"));
        Expect.Contains(old["login.component.ts"], "standalone: true,");
        Expect.Contains(old["login.component.html"], "@if (message) {");

        var plain = await Run("TS_EssentialAuth_v1.tt");
        Expect.Contains(plain["login.component.ts"], "import { NgIf } from '@angular/common';");
        Expect.Contains(plain["login.component.html"], "*ngIf=\"message\"");
    }

    [TestMethod]
    public async Task The_folders_are_the_projects_and_the_specs_use_no_Vitest_only_calls()
    {
        var files = await Run("TS_EssentialAuth_v1.tt", ("ServicesFolder", "api"), ("ModelsFolder", "types"), ("ComponentsFolder", "screens"));
        Expect.Contains(files["auth.service.ts"], "from '../types/auth';");
        Expect.Contains(files["login.component.ts"], "from '../../api/auth.service';");
        foreach (var spec in files.Where(f => f.Key.EndsWith(".spec.ts")))
        {
            Expect.DoesNotContain(spec.Value, "vi.");
            Expect.DoesNotContain(spec.Value, "toHaveBeenCalled");
        }
    }

    [TestMethod]
    public async Task Auth_adds_the_interceptor_to_the_app_config_and_nothing_without_it()
    {
        string plain = (await Run("TS_EssentialConfig_v1.tt"))["app.config.ts"];
        Expect.Contains(plain, "provideHttpClient(),");
        Expect.DoesNotContain(plain, "authInterceptor");

        string auth = (await Run("TS_EssentialConfig_v1.tt", On))["app.config.ts"];
        Expect.Contains(auth, "import { provideHttpClient, withInterceptors } from '@angular/common/http';");
        Expect.Contains(auth, "import { authInterceptor } from './services/auth.interceptor';");
        Expect.Contains(auth, "provideHttpClient(withInterceptors([authInterceptor])),");
    }

    [TestMethod]
    public async Task Auth_shows_the_menu_only_to_a_signed_in_person_and_adds_the_user_and_Sign_out()
    {
        var plain = await Run("TS_EssentialShell_v1.tt");
        Expect.DoesNotContain(plain["app.html"], "Sign out");
        Expect.DoesNotContain(plain["app.ts"], "AuthService");

        var auth = await Run("TS_EssentialShell_v1.tt", On);
        Expect.Contains(auth["app.ts"], "import { AuthService } from './services/auth.service';");
        Expect.Contains(auth["app.ts"], "protected readonly auth = inject(AuthService);");
        Expect.Contains(auth["app.ts"], "import { ChangeDetectionStrategy, Component, inject } from '@angular/core';");
        Expect.Contains(auth["app.html"], "menuOpen && auth.isLoggedIn()");
        Expect.Contains(auth["app.html"], "{{ auth.user()?.name }}");
        Expect.Contains(auth["app.html"], "(click)=\"auth.logout()\">Sign out</button>");
        Expect.Contains(auth["app.css"], ".topbar");
        Expect.DoesNotContain(plain["app.css"], ".topbar");

        var modern = await Run("TS_EssentialShell_v1.tt", On, ("AngularVersion", "18"));
        Expect.Contains(modern["app.html"], "@if (auth.isLoggedIn()) {");
    }

    [TestMethod]
    public async Task Auth_puts_the_guard_on_every_generated_route_and_adds_the_login_route()
    {
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [Sample.DonateLeave()] };

        async Task<string> Routes(params (string Key, string Value)[] values)
        {
            var result = await Repo.Cache.RunAsync(Repo.Template("TS_Screens_v1.tt"), database, Project(values));
            Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
            return result.GeneratedText!.ReplaceLineEndings("\n");
        }

        string plain = await Routes();
        Expect.DoesNotContain(plain, "authGuard");
        Expect.DoesNotContain(plain, "LoginComponent");
        Expect.Contains(plain, "...screens.map((s) => ({ path: s.path, component: s.component })),");

        string auth = await Routes(On);
        Expect.Contains(auth, "import { LoginComponent } from './components/login/login.component';");
        Expect.Contains(auth, "import { authGuard } from './services/auth.guard';");
        Expect.Contains(auth, "{ path: 'login', component: LoginComponent },");
        Expect.Contains(auth, "...screens.map((s) => ({ path: s.path, component: s.component, canActivate: [authGuard] })),");
    }
}
