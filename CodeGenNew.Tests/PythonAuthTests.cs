using System.Diagnostics;
using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests;

/// <summary> The Python API's sign-in (ApiAuth): app/auth.py, the user table's app/users.py, the owner scope and the guards in the routes. Running the API needs FastAPI, so the text is checked here and
/// every file is byte-compiled when a Python interpreter is on the path. </summary>
[TestClass]
public class PythonAuthTests
{
    private const string Prefix = "Shop.Python/";

    private static string CreateDatabase()
    {
        string path = Path.Combine(Path.GetTempPath(), "codegen_pyauth_" + Guid.NewGuid().ToString("N") + ".db");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
        connection.Open();
        foreach (string statement in new[]
        {
            """
            CREATE TABLE Employee (
                EmployeeId INTEGER PRIMARY KEY, FullName VARCHAR(50) NOT NULL, IsManager BIT NOT NULL DEFAULT 0, IsAdministrator BIT NOT NULL DEFAULT 0, IsActive BIT NOT NULL DEFAULT 1,
                Login VARCHAR(50) NULL, PwdHash VARCHAR(200) NULL)
            """,
            "CREATE TABLE TimeSheet (TimeSheetId INTEGER PRIMARY KEY, EmployeeId INTEGER NOT NULL REFERENCES Employee (EmployeeId), Notes VARCHAR(100) NULL)",
            "CREATE TABLE TimeSheetLine (TimeSheetLineId INTEGER PRIMARY KEY, TimeSheetId INTEGER NOT NULL REFERENCES TimeSheet (TimeSheetId), Hours INTEGER NOT NULL)"
        })
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
        return path;
    }

    private static async Task<(Dictionary<string, string> Files, string Output)> Generate(params (string Key, string Value)[] settings)
    {
        string database = CreateDatabase();
        string output = Path.Combine(Path.GetTempPath(), "codegen_pyauth_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            var map = new Dictionary<string, string>
            {
                ["ProjectName"] = "Shop", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["EnumTables"] = "none", ["Stacks"] = "Python,Blazor", ["Screens"] = "Employee",
                ["ApiPort"] = "6001", ["ApiAuth"] = "true"
            };
            foreach (var (key, value) in settings)
                map[key] = value;
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            var report = await Repo.GenerateAsync(provider, new GenerateOptions
            {
                Project = ProjectSettings.FromValues(map), Stacks = ["Python"], OutputDirectory = output, DatabaseName = database, Schema = "main", Essentials = true
            });
            Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
            var files = Directory.GetFiles(output, "*", SearchOption.AllDirectories)
                .ToDictionary(f => Path.GetRelativePath(output, f).Replace('\\', '/'), f => File.ReadAllText(f).Replace("\r\n", "\n"));
            return (files, output);
        }
        finally
        {
            File.Delete(database);
        }
    }

    private static async Task CompileAsync(string output)
    {
        try
        {
            var start = new ProcessStartInfo("python", $"-m compileall -q \"{Path.Combine(output, "Shop.Python")}\"")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
            };
            using var process = Process.Start(start)!;
            string text = await process.StandardOutput.ReadToEndAsync() + await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode == 9009 || text.Contains("was not found", StringComparison.OrdinalIgnoreCase) && process.ExitCode != 0)
                return;
            Assert.AreEqual(0, process.ExitCode, "python -m compileall: " + text);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // no interpreter on the path
        }
    }

    private static void Clean(string output)
    {
        if (Directory.Exists(output))
            Directory.Delete(output, recursive: true);
    }

    [TestMethod]
    public async Task Without_ApiAuth_no_route_asks_for_a_token()
    {
        var (files, output) = await Generate(("ApiAuth", "false"));
        try
        {
            CollectionAssert.DoesNotContain(files.Keys.ToArray(), Prefix + "app/auth.py");
            CollectionAssert.DoesNotContain(files.Keys.ToArray(), Prefix + "app/users.py");
            Expect.DoesNotContain(files[Prefix + "app/main.py"], "require_user");
            Expect.DoesNotContain(files[Prefix + "app/routes/employee.py"], "require_user");
            Expect.DoesNotContain(files[Prefix + "app/support.py"], "Bearer");
        }
        finally
        {
            Clean(output);
        }
    }

    [TestMethod]
    public async Task With_ApiAuth_and_no_user_table_the_bootstrap_administrator_signs_in_and_every_router_needs_a_token()
    {
        var (files, output) = await Generate();
        try
        {
            string auth = files[Prefix + "app/auth.py"];
            Expect.Contains(auth, "@auth_router.post(\"/api/auth/login\")");
            Expect.Contains(auth, "Bootstrap__AdminUserName");
            Expect.Contains(auth, "ADMIN_ROLE = \"Administrator\"");
            Expect.Contains(auth, "ROLE_CLAIM = \"role\"");
            Expect.Contains(auth, "OWNER_CLAIM = \"employeeId\"");
            Expect.Contains(auth, "SCHEME = \"pbkdf2-sha256\"");
            Expect.Contains(auth, "ITERATIONS = 600_000");
            Expect.Contains(auth, "raise ApiError(401, \"Wrong user name or password\", problem_type=\"" + ProblemTypes.WrongCredentials + "\")");
            Expect.Contains(auth, "raise ApiError(429, \"Too many attempts\"");
            Expect.Contains(auth, "raise ApiError(400, \"Sign-in needs a user name and a password\"");
            Expect.Contains(auth, "\"user\": {\"name\": user.user_name, \"role\": user.role, \"ownerId\": user.owner_id}");
            Expect.DoesNotContain(auth, "from app.users");

            string main = files[Prefix + "app/main.py"];
            Expect.Contains(main, "from app.auth import auth_router, require_user, signing_key");
            Expect.Contains(main, "app.include_router(auth_router)");
            Expect.Contains(main, "app.include_router(router, dependencies=[Depends(require_user)])");
            CollectionAssert.DoesNotContain(files.Keys.ToArray(), Prefix + "app/users.py");
            Expect.Contains(files[Prefix + "app/support.py"], "class ApiError(Exception):");
            Expect.Contains(files[Prefix + "app/support.py"], "headers=headers");
            Expect.Contains(files[Prefix + ".env.example"], "Bootstrap__AdminUserName");
            Expect.DoesNotContain(files[Prefix + "app/routes/employee.py"], "owner_of");

            await CompileAsync(output);
        }
        finally
        {
            Clean(output);
        }
    }

    [TestMethod]
    public async Task With_a_user_table_the_directory_command_and_hash_hiding_are_written()
    {
        var (files, output) = await Generate(("UserTable", "Employee"), ("UserOwnerColumn", "EmployeeId"), ("UserRoleColumn", "IsManager"));
        try
        {
            string users = files[Prefix + "app/users.py"];
            Expect.Contains(users, "users = table(\"Employee\", column(\"Login\"), column(\"PwdHash\"), column(\"EmployeeId\"), column(\"IsManager\"), column(\"IsAdministrator\"))");
            Expect.Contains(users, "\"Manager\" if row._mapping[\"IsManager\"] else \"Employee\"");
            Expect.Contains(users, "ADMIN_ROLE if row._mapping[\"IsAdministrator\"] else (");
            Expect.Contains(users, "None if owner is None else int(owner)");
            Expect.Contains(users, "def set_password(session: Session, user_name: str, password: str) -> bool:");
            Expect.Contains(users, "args[0].lower() != \"set-password\"");
            Expect.Contains(users, "PASSWORD_VARIABLE = \"NEW_PASSWORD\"");
            Expect.Contains(users, "return 0 if found else 1");
            Expect.DoesNotContain(users, "password=");

            Expect.Contains(files[Prefix + "app/auth.py"], "from app.users import find_user as find_in_table");
            Expect.DoesNotContain(files[Prefix + "app/auth.py"], "Bootstrap__AdminUserName");
            Expect.DoesNotContain(files[Prefix + ".env.example"], "Bootstrap__AdminUserName");

            string schema = files[Prefix + "app/schemas/employee.py"];
            Expect.DoesNotContain(schema, "pwd_hash");
            Expect.Contains(schema, "is_administrator: bool = Field(alias=\"isAdministrator\")");
            Expect.Contains(files[Prefix + "app/models/employee.py"], "pwd_hash: Mapped[str | None] = mapped_column(\"PwdHash\"");

            string routes = files[Prefix + "app/routes/employee.py"];
            Expect.Contains(routes, "def guard_user_row(session: Session, user: CurrentUser, body: EmployeeSchema, current: Employee | None) -> None:");
            Expect.Contains(routes, "if not user.is_admin:");
            Expect.Contains(routes, "body.is_administrator = False");
            Expect.Contains(routes, "body.is_manager = False");
            Expect.Contains(routes, "elif bool(body.is_administrator) != bool(current.is_administrator):");
            Expect.Contains(routes, "problem_type=\"" + ProblemTypes.ColumnNotYours + "\"");
            Expect.Contains(routes, "raise ApiError(409, \"Duplicate value\", problem_type=\"" + ProblemTypes.DuplicateValue + "\"");
            Expect.Contains(routes, "raise last_administrator()");
            Expect.Contains(routes, "return bool(row.is_administrator) and bool(row.is_active)");
            Expect.Contains(routes, "guard_user_row(session, user, body, None)");
            Expect.Contains(routes, "guard_user_row(session, user, body, row)");
            Expect.Contains(routes, "if is_administrator(row) and not other_administrators(session, row.employee_id):");

            await CompileAsync(output);
        }
        finally
        {
            Clean(output);
        }
    }

    [TestMethod]
    public async Task An_owned_table_and_its_child_are_scoped_to_the_owners_the_caller_may_act_for()
    {
        var (files, output) = await Generate(("UserTable", "Employee"), ("OwnedTables", "TimeSheet"));
        try
        {
            string own = files[Prefix + "app/routes/time_sheet.py"];
            Expect.Contains(own, "from app.auth import CurrentUser, not_yours, require_user, visible_owners");
            Expect.Contains(own, "return row.employee_id");
            Expect.Contains(own, "statement if owners is None else statement.where(TimeSheet.employee_id.in_(owners))");
            Expect.Contains(own, "def get_all(session: Session = Depends(get_session), user: CurrentUser = Depends(require_user)):");
            Expect.Contains(own, "if body.employee_id == 0 and len(owners) == 1:");
            Expect.Contains(own, "refuse_other_owners(session, user, row)");
            Expect.Contains(own, "raise not_yours()");

            string child = files[Prefix + "app/routes/time_sheet_line.py"];
            Expect.Contains(child, "from app.models.time_sheet import TimeSheet");
            Expect.Contains(child, "select(TimeSheet.employee_id).where(TimeSheet.time_sheet_id == row.time_sheet_id)");
            Expect.Contains(child, "TimeSheetLine.time_sheet_id.in_(select(TimeSheet.time_sheet_id).where(TimeSheet.employee_id.in_(owners)))");

            await CompileAsync(output);
        }
        finally
        {
            Clean(output);
        }
    }
}
