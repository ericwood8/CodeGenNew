using System.Data;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The React sign-in (Auth=true): the Auth group, the token on every call, the shell, the guarded routes with ReadRoles, and the pages that hide their write buttons (WriteRoles). </summary>
[TestClass]
public class ReactAuthTests
{
    private static readonly (string, string) Auth = ("Auth", "true");
    private static readonly (string, string) Api = ("ApiAuth", "true");

    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static async Task<string> Run(string template, object model, params (string, string)[] values)
    {
        var project = Project(values);
        var result = model switch
        {
            TableModel table => await Repo.Cache.RunAsync(Repo.Template(template), table, project),
            DatabaseModel database => await Repo.Cache.RunAsync(Repo.Template(template), database, project),
            _ => await Repo.Cache.RunAsync(Repo.Template(template), project)
        };
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    [TestMethod]
    public async Task The_Auth_group_writes_the_session_the_login_page_the_guard_and_their_tests()
    {
        string auth = await Run("TSX_EssentialAuth_v1.tt", new object(), Auth);

        foreach (string file in new[] { "src/api/auth.ts", "src/api/__tests__/auth.test.ts", "src/pages/LoginPage.tsx", "src/pages/__tests__/LoginPage.test.tsx",
                     "src/components/RequireAuth.tsx", "src/components/__tests__/RequireAuth.test.tsx", "src/components/auth.css" })
            Expect.Contains(auth, "@@@FILE " + file + "@@@");
        Expect.Contains(auth, "const storageKey = 'acme.auth';");
        Expect.Contains(auth, "useSyncExternalStore(subscribe, () => session)");
        Expect.Contains(auth, "export class LoginError extends Error {");
        Expect.Contains(auth, "fetch(`${API_BASE_URL}/api/auth/login`");
        Expect.Contains(auth, "error.status === 429");
        Expect.Contains(auth, "error.status === 401");
        Expect.Contains(auth, "The user name or password is not right.");
        Expect.Contains(auth, "if (!isLoggedIn) return <Navigate to=\"/login\" replace />;");
        Expect.Contains(auth, "if (roles && !hasRole(roles)) return <Navigate to=\"/\" replace />;");
        Assert.AreEqual("Auth", TemplateConfig.Load(Repo.Template("TSX_EssentialAuth_v1.tt.config")).EssentialsGroup);
        CollectionAssert.Contains(Project(Auth).ImpliedEssentialsGroups.ToArray(), "Auth");
    }

    [TestMethod]
    public async Task The_shared_request_helper_sends_the_token_and_signs_out_on_a_401_only_with_Auth()
    {
        string with = await Run("TSX_EssentialSupport_v1.tt", new object(), Auth);
        Expect.Contains(with, "import { getToken, logout } from './auth';");
        Expect.Contains(with, "...(token ? { Authorization: `Bearer ${token}` } : {}),");
        Expect.Contains(with, "if (response.status === 401 && token) logout();");

        string without = await Run("TSX_EssentialSupport_v1.tt", new object());
        Expect.DoesNotContain(without, "auth");
        Expect.DoesNotContain(without, "Authorization");
    }

    [TestMethod]
    public async Task The_shell_shows_the_sign_in_the_user_and_only_the_screens_the_role_may_read()
    {
        string shell = await Run("TSX_EssentialShell_v1.tt", new object(), Auth);

        Expect.Contains(shell, "import { LoginPage } from './pages/LoginPage';");
        Expect.Contains(shell, "import { RequireAuth } from './components/RequireAuth';");
        Expect.Contains(shell, "const open = screens.filter((s) => { const roles = rolesOf(s); return !roles || auth.hasRole(roles); });");
        Expect.Contains(shell, "<Route path=\"/login\" element={<LoginPage />} />");
        Expect.Contains(shell, "<RequireAuth roles={rolesOf(s)}>{s.element}</RequireAuth>");
        Expect.Contains(shell, "onClick={auth.logout}>Sign out</button>");

        string plain = await Run("TSX_EssentialShell_v1.tt", new object());
        Expect.DoesNotContain(plain, "useAuth");
        Expect.DoesNotContain(plain, "RequireAuth");
        Expect.Contains(plain, "<Route key={s.path} path={`/${s.path}`} element={s.element} />");
    }

    [TestMethod]
    public async Task A_screen_of_a_table_with_read_roles_carries_them()
    {
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [Sample.Holiday(), Sample.NaturalKey()] };
        string screens = await Run("TSX_Screens_v1.tt", database, Auth, Api, ("TableReadRoles", "Holiday:Hr"));

        Expect.Contains(screens, "{ path: 'holiday', label: 'Holiday', element: <HolidayPage />, roles: ['Administrator', 'Hr'] },");
        Expect.Contains(screens, "{ path: 'country', label: 'Country', element: <CountryPage /> },");
        Expect.DoesNotContain(await Run("TSX_Screens_v1.tt", database, Auth, Api), "roles:");
        Expect.DoesNotContain(await Run("TSX_Screens_v1.tt", database, Api, ("TableReadRoles", "Holiday:Hr")), "roles:");
    }

    [TestMethod]
    public async Task A_page_hides_Add_Edit_Clone_and_Delete_from_a_role_that_may_not_write_and_its_spec_checks_it()
    {
        string page = await Run("TSX_Page_v1.tt", Sample.Holiday(), Auth, Api, ("WriteRoles", "Manager"));

        Expect.Contains(page, "import { useAuth } from '../api/auth';");
        Expect.Contains(page, "const { hasRole } = useAuth();");
        Expect.Contains(page, "const canWrite = hasRole(['Administrator', 'Manager']);");
        Expect.Contains(page, "{canWrite && <button type=\"button\" onClick={add}>Add New Holiday</button>}");
        Expect.Contains(page, "{canWrite && <button type=\"button\" onClick={() => edit(holiday)}>Edit</button>}");
        Expect.Contains(page, "{canWrite && <button type=\"button\" onClick={() => void clone(holiday.holidayId!)}>Clone</button>}");
        Expect.Contains(page, "{canWrite && <button type=\"button\" onClick={() => void remove(holiday.holidayId!)}>Delete</button>}");
        Expect.Contains(page, "it('shows Add New to a role that may write'");
        Expect.Contains(page, "it('hides Add New from a role that may not write'");
        Expect.Contains(page, "setSession({ token: 'abc', user: { name: 'Pat', role: 'Manager' } });");
        Expect.Contains(page, "    setSession(null);");

        string plain = await Run("TSX_Page_v1.tt", Sample.Holiday(), Auth, Api);
        Expect.DoesNotContain(plain, "canWrite");
        Expect.DoesNotContain(plain, "useAuth");
        Expect.DoesNotContain(await Run("TSX_Page_v1.tt", Sample.Holiday(), Api, ("WriteRoles", "Manager")), "canWrite");

        string odata = await Run("TSX_ODataPage_v1.tt", Sample.Holiday(), ("OData", "true"), Auth, Api, ("WriteRoles", "Manager"));
        Expect.Contains(odata, "{canWrite && <button type=\"button\" onClick={() => open(holiday)}>Edit</button>}");

        string detail = await Run("TSX_DetailMasterPage_v1.tt", Sample.TimeSheetWithEmployeeAndDetail(), Auth, Api, ("WriteRoles", "Manager"));
        Expect.Contains(detail, "const canWrite = hasRole(['Administrator', 'Manager']);");
        Expect.Contains(detail, "{canWrite && <button type=\"button\" onClick={add}>Add New Time Sheet</button>}");
    }

    [TestMethod]
    public async Task A_name_active_page_hides_its_write_buttons_too()
    {
        var table = Sample.DepartmentTeam();
        string page = await Run("TSX_Page_v1.tt", table, Auth, Api, ("TableWriteRoles", "DepartmentTeam:Hr"));

        Expect.Contains(page, "const canWrite = hasRole(['Administrator', 'Hr']);");
        Expect.Contains(page, "{canWrite && <button type=\"button\" onClick={() => edit(departmentTeam)}>Edit</button>}");
        Expect.DoesNotContain(page, "Clone");
    }
}
