using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests;

/// <summary> The Rust API's sign-in (ApiAuth): src/auth.rs, the user table's src/users.rs, the owner scope and the guards in the routes and the repositories. Compiling and running the crate needs cargo
/// and the C++ build tools, so the text is checked here and the crate was built and driven over HTTP by hand (Docs/TemplateNotes/RS_Routes_v1.md). </summary>
[TestClass]
public class RustAuthTests
{
    private const string Prefix = "Shop.Rust/";

    private static string CreateDatabase()
    {
        string path = Path.Combine(Path.GetTempPath(), "codegen_rsauth_" + Guid.NewGuid().ToString("N") + ".db");
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

    private static async Task<Dictionary<string, string>> Generate(params (string Key, string Value)[] settings)
    {
        string database = CreateDatabase();
        string output = Path.Combine(Path.GetTempPath(), "codegen_rsauth_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            var map = new Dictionary<string, string>
            {
                ["ProjectName"] = "Shop", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["EnumTables"] = "none", ["Stacks"] = "Rust", ["RustPort"] = "6002", ["ApiAuth"] = "true"
            };
            foreach (var (key, value) in settings)
                map[key] = value;
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            var report = await Repo.GenerateAsync(provider, new GenerateOptions
            {
                Project = ProjectSettings.FromValues(map), Stacks = ["Rust"], OutputDirectory = output, DatabaseName = database, Schema = "main", Essentials = true
            });
            Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
            return Directory.GetFiles(output, "*", SearchOption.AllDirectories)
                .ToDictionary(f => Path.GetRelativePath(output, f).Replace('\\', '/'), f => File.ReadAllText(f).Replace("\r\n", "\n"));
        }
        finally
        {
            File.Delete(database);
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [TestMethod]
    public async Task Without_ApiAuth_no_route_asks_for_a_token_and_the_crate_has_no_crypto_packages()
    {
        var files = await Generate(("ApiAuth", "false"));

        CollectionAssert.DoesNotContain(files.Keys.ToArray(), Prefix + "src/auth.rs");
        CollectionAssert.DoesNotContain(files.Keys.ToArray(), Prefix + "src/users.rs");
        Expect.DoesNotContain(files[Prefix + "src/lib.rs"], "require_user");
        Expect.DoesNotContain(files[Prefix + "src/routes/employee.rs"], "CurrentUser");
        Expect.DoesNotContain(files[Prefix + "Cargo.toml"], "hmac");
        Expect.DoesNotContain(files[Prefix + "src/support.rs"], "Unauthorized");
    }

    [TestMethod]
    public async Task With_ApiAuth_and_no_user_table_the_bootstrap_administrator_signs_in_and_every_route_needs_a_token()
    {
        var files = await Generate();

        string auth = files[Prefix + "src/auth.rs"];
        Expect.Contains(auth, "Router::new().route(\"/api/auth/login\", post(login))");
        Expect.Contains(auth, "Bootstrap__AdminUserName");
        Expect.Contains(auth, "pub const ADMIN_ROLE: &str = \"Administrator\";");
        Expect.Contains(auth, "pub const ROLE_CLAIM: &str = \"role\";");
        Expect.Contains(auth, "pub const OWNER_CLAIM: &str = \"employeeId\";");
        Expect.Contains(auth, "const SCHEME: &str = \"pbkdf2-sha256\";");
        Expect.Contains(auth, "const ITERATIONS: u32 = 600_000;");
        Expect.Contains(auth, "ApiError::Problem(401, PROBLEM_WRONG_CREDENTIALS, \"Wrong user name or password\", None)");
        Expect.Contains(auth, "ApiError::Problem(429, PROBLEM_TOO_MANY_ATTEMPTS, \"Too many attempts\"");
        Expect.Contains(auth, "ApiError::Problem(400, PROBLEM_SIGN_IN_INCOMPLETE");
        Expect.Contains(auth, "\"user\": { \"name\": user.user_name, \"role\": user.role, \"ownerId\": user.owner_id }");
        Expect.DoesNotContain(auth, "crate::users");
        CollectionAssert.DoesNotContain(files.Keys.ToArray(), Prefix + "src/users.rs");

        string lib = files[Prefix + "src/lib.rs"];
        Expect.Contains(lib, "mod auth;");
        Expect.Contains(lib, "routes::router().route_layer(axum::middleware::from_fn(auth::require_user)).merge(auth::router())");
        Expect.Contains(lib, "auth::check_configuration()?;");
        Expect.Contains(lib, "into_make_service_with_connect_info::<std::net::SocketAddr>()");
        Expect.DoesNotContain(lib, "mod users;");

        string cargo = files[Prefix + "Cargo.toml"];
        foreach (string crate in new[] { "base64 = ", "hmac = ", "pbkdf2 = ", "sha2 = " })
            Expect.Contains(cargo, crate);
        Expect.Contains(cargo, "[profile.dev.package.\"*\"]");
        Expect.Contains(files[Prefix + "src/support.rs"], "Problem(u16, &'static str, &'static str, Option<&'static str>),");
        Expect.Contains(files[Prefix + ".env.example"], "Bootstrap__AdminUserName");
        Expect.DoesNotContain(files[Prefix + "src/routes/employee.rs"], "owner_of");
    }

    [TestMethod]
    public async Task With_a_user_table_the_directory_command_and_hash_hiding_are_written()
    {
        var files = await Generate(("UserTable", "Employee"), ("UserOwnerColumn", "EmployeeId"), ("UserRoleColumn", "IsManager"));

        string users = files[Prefix + "src/users.rs"];
        Expect.Contains(users, "SELECT \"Login\" AS name, \"PwdHash\" AS hash, \"EmployeeId\" AS owner, \"IsManager\" AS role, \"IsAdministrator\" AS admin FROM \"Employee\" WHERE \"Login\" = ? AND \"PwdHash\" IS NOT NULL AND \"PwdHash\" <> ''");
        Expect.Contains(users, "UPDATE \"Employee\" SET \"PwdHash\" = ? WHERE \"Login\" = ?");
        Expect.Contains(users, "\"Manager\" } else { \"Employee\" }.to_string()");
        Expect.Contains(users, "Some(ADMIN_ROLE.to_string())");
        Expect.Contains(users, "pub async fn command(args: &[String]) -> Option<i32>");
        Expect.Contains(users, "const PASSWORD_VARIABLE: &str = \"NEW_PASSWORD\";");
        Expect.DoesNotContain(users, "password=");
        Expect.Contains(files[Prefix + "src/auth.rs"], "crate::users::find_user(pool, name).await");
        Expect.DoesNotContain(files[Prefix + "src/auth.rs"], "Bootstrap__AdminUserName");
        string lib = files[Prefix + "src/lib.rs"];
        Expect.Contains(lib, "mod users;");
        Expect.Contains(lib, "users::command(&args).await");

        string model = files[Prefix + "src/models/employee.rs"];
        Expect.Contains(model, "#[serde(skip)]\n    #[allow(dead_code)] // only the sign-in reads it, through its own statement\n    #[sqlx(rename = \"PwdHash\")]\n    pub pwd_hash: Option<String>,");
        string repo = files[Prefix + "src/repos/employee.rs"];
        Expect.DoesNotContain(repo.Split("const INSERT")[1].Split("VALUES")[0], "PwdHash");
        Expect.DoesNotContain(repo.Split("const UPDATE")[1].Split(" WHERE ")[0], "PwdHash");
        Expect.Contains(repo, "pub async fn login_taken(");
        Expect.Contains(repo, "pub async fn other_administrators(");
        Expect.Contains(repo, "\"IsAdministrator\" = 1 AND \"IsActive\" = 1 AND \"EmployeeId\" <> ?");

        string routes = files[Prefix + "src/routes/employee.rs"];
        Expect.Contains(routes, "async fn guard_user_row(pool: &Pool, user: &CurrentUser, row: &mut Employee, current: Option<&Employee>) -> ApiResult<()> {");
        Expect.Contains(routes, "if !user.is_admin() {");
        Expect.Contains(routes, "None => row.is_administrator = false,");
        Expect.Contains(routes, "None => row.is_manager = false,");
        Expect.Contains(routes, "Some(current) if row.is_administrator != current.is_administrator => {");
        Expect.Contains(routes, "PROBLEM_COLUMN_NOT_YOURS");
        Expect.Contains(routes, "PROBLEM_DUPLICATE_VALUE");
        Expect.Contains(routes, "return Err(last_administrator());");
        Expect.Contains(routes, "guard_user_row(&pool, &user, &mut row, None).await?;");
        Expect.Contains(routes, "guard_user_row(&pool, &user, &mut row, Some(&current)).await?;");
        Expect.Contains(routes, "// a copy of a person is made by an administrator");
        Expect.DoesNotContain(routes, "owner_of");
    }

    [TestMethod]
    public async Task An_owned_table_and_its_child_are_scoped_to_the_owner_the_caller_may_act_for()
    {
        var files = await Generate(("UserTable", "Employee"), ("OwnedTables", "TimeSheet"));

        string own = files[Prefix + "src/routes/time_sheet.rs"];
        Expect.Contains(own, "use crate::auth::{CurrentUser, Scope, not_yours};");
        Expect.Contains(own, "Ok(Some(i64::from(row.employee_id)))");
        Expect.Contains(own, "Scope::Owner(owner) => repo::Scoped::Own(owner as i32),");
        Expect.Contains(own, "Ok(Json(repo::get_all_scoped(&pool, &scope_of(&pool, &user).await?).await?))");
        Expect.Contains(own, "if row.employee_id == 0 {");
        Expect.Contains(own, "allowed(&pool, &user, &row).await?;");
        string repo = files[Prefix + "src/repos/time_sheet.rs"];
        Expect.Contains(repo, "pub enum Scoped {");
        Expect.Contains(repo, "builder.push(r#\" AND t.\"EmployeeId\" = \"#).push_bind(*owner);");
        Expect.Contains(repo, "apply_scope(&mut count, scope);");
        Expect.Contains(repo, "apply_scope(&mut items, scope);");

        string child = files[Prefix + "src/routes/time_sheet_line.rs"];
        Expect.Contains(child, "crate::repos::time_sheet::get_by_id(pool, row.time_sheet_id).await?.map(|parent| i64::from(parent.employee_id))");
        Expect.Contains(child, "repo::Scoped::Keys(");
        Expect.Contains(files[Prefix + "src/repos/time_sheet_line.rs"], "    Keys(Vec<i32>),");
    }
}
