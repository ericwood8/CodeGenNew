using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> PermissionTables: roles and permissions kept in four tables, read at sign-in into role and permission claims, and a role list entry <c>@name</c> that asks the token for the permission. </summary>
[TestClass]
public class PermissionTablesTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static readonly (string, string)[] Settings =
    [
        ("ApiAuth", "true"), ("UserTable", "Employee"), ("UserNameColumn", "Login"), ("PermissionTables", "Role,Permission,RolePermission,UserRole")
    ];

    private static TableModel Keyed(string name, params ColumnModel[] more) => Sample.Table(name,
        [Sample.Column(name + "Id", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), .. more]);

    private static ColumnModel Text(string name, int ordinal, int characters = 40) => Sample.Column(name, SqlDbType.NVarChar, characters: characters, ordinal: ordinal);

    private static DatabaseModel Database(bool userRoleLinksRole = true, bool tenantOnUserRole = false, bool tenantOnRole = false, bool sharedRoles = false)
    {
        var employee = Keyed("Employee", Text("Login", 2), Text("PasswordHash", 3, 200));
        var role = Sample.Table("Role", [Sample.Column("RoleId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Text("Name", 2), .. TenantColumn(tenantOnRole, 3, sharedRoles)],
            tenantOnRole ? [Sample.ForeignKey("TenantId", "Tenant", "TenantId", "Name")] : []);
        var permission = Keyed("Permission", Text("Name", 2));
        var rolePermission = Sample.Table("RolePermission",
            [Sample.Column("RolePermissionId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Sample.Column("RoleId", SqlDbType.Int, ordinal: 2), Sample.Column("PermissionId", SqlDbType.Int, ordinal: 3)],
            [Sample.ForeignKey("RoleId", "Role", "RoleId", "Name"), Sample.ForeignKey("PermissionId", "Permission", "PermissionId", "Name")]);
        var userRole = Sample.Table("UserRole",
            [Sample.Column("UserRoleId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Sample.Column("EmployeeId", SqlDbType.Int, ordinal: 2), Sample.Column("RoleId", SqlDbType.Int, ordinal: 3), .. TenantColumn(tenantOnUserRole, 4)],
            [Sample.ForeignKey("EmployeeId", "Employee", "EmployeeId", "Login"),
                .. (userRoleLinksRole ? new[] { Sample.ForeignKey("RoleId", "Role", "RoleId", "Name") } : []),
                .. (tenantOnUserRole ? new[] { Sample.ForeignKey("TenantId", "Tenant", "TenantId", "Name") } : [])]);
        var tenant = Keyed("Tenant", Text("Name", 2));
        return new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [employee, role, permission, rolePermission, userRole, tenant] };
    }

    private static ColumnModel[] TenantColumn(bool present, int ordinal, bool nullable = false) => present ? [Sample.Column("TenantId", SqlDbType.Int, nullable: nullable, ordinal: ordinal)] : [];

    [TestMethod]
    public void The_four_tables_are_found_by_their_keys_and_names()
    {
        var (plan, problem) = PermissionTables.Resolve(Database(), Project(Settings));
        Assert.IsNull(problem);
        Assert.IsNotNull(plan);
        Assert.AreEqual("RoleId", plan.UserRoleRole.Name);
        Assert.AreEqual("EmployeeId", plan.UserRoleUser.Name);
        Assert.AreEqual("PermissionId", plan.RolePermissionPermission.Name);
        Assert.AreEqual("Name", plan.PermissionName.Name);
        Assert.AreEqual("EmployeeId", plan.UserKey.Name);
    }

    [TestMethod]
    public void The_two_junctions_get_the_junction_screens_that_assign_permissions_and_roles()
    {
        var database = Database();
        Assert.IsTrue(database.Tables.Single(t => t.TableName == "RolePermission").IsJunctionTable);
        Assert.IsTrue(database.Tables.Single(t => t.TableName == "UserRole").IsJunctionTable);
    }

    [TestMethod]
    public void Only_the_administrator_writes_the_permission_tables_unless_the_settings_say_otherwise()
    {
        var project = Project(Settings);
        foreach (string table in new[] { "Role", "Permission", "RolePermission", "UserRole" })
            CollectionAssert.AreEqual(new[] { "Administrator" }, WriteRolePlan.For(project, table).ToArray());
        Assert.IsEmpty(WriteRolePlan.For(project, "Customer"));
        CollectionAssert.AreEqual(new[] { "Administrator", "Hr" }, WriteRolePlan.For(Project([.. Settings, ("TableWriteRoles", "UserRole:Hr")]), "UserRole").ToArray());
        Assert.IsEmpty(WriteRolePlan.For(Project(("ApiAuth", "true")), "Role"));
    }

    [TestMethod]
    public void A_setting_that_does_not_fit_the_database_says_why()
    {
        Assert.AreEqual((null, null), PermissionTables.Resolve(Database(), Project(("ApiAuth", "true"))));
        Assert.Contains("ApiAuth", PermissionTables.Resolve(Database(), Project(("PermissionTables", "Role,Permission,RolePermission,UserRole"))).Problem!);
        Assert.Contains("four tables", PermissionTables.Resolve(Database(), Project(("ApiAuth", "true"), ("UserTable", "Employee"), ("UserNameColumn", "Login"), ("PermissionTables", "Role,Permission"))).Problem!);
        Assert.Contains("not a table", PermissionTables.Resolve(Database(), Project(("ApiAuth", "true"), ("UserTable", "Employee"), ("UserNameColumn", "Login"), ("PermissionTables", "Role,Permission,RolePermission,Nope"))).Problem!);
        Assert.Contains("UserRole needs exactly one foreign key to Role", PermissionTables.Resolve(Database(userRoleLinksRole: false), Project(Settings)).Problem!);
    }

    [TestMethod]
    public void A_permission_stays_in_the_role_list_when_roles_are_kept_in_tables()
    {
        var withTables = Project([.. Settings, ("Permissions", "edit_billing:Billing"), ("WriteRoles", "Manager,@edit_billing")]);
        CollectionAssert.AreEqual(new[] { "Administrator", "Manager", "Billing", "@edit_billing" }, WriteRolePlan.For(withTables, "Invoice").ToArray());
        var withoutTables = Project(("ApiAuth", "true"), ("Permissions", "edit_billing:Billing"), ("WriteRoles", "Manager,@edit_billing"));
        CollectionAssert.AreEqual(new[] { "Administrator", "Manager", "Billing" }, WriteRolePlan.For(withoutTables, "Invoice").ToArray());
    }

    [TestMethod]
    public async Task The_Python_and_Rust_sign_ins_carry_roles_and_permissions_and_their_role_checks_read_at_names()
    {
        var project = Project(Settings);
        var plain = Project(Settings.Where(s => s.Item1 != "PermissionTables").ToArray());

        var pyUsers = await Repo.Cache.RunAsync(Repo.Template("PY_UserDirectory_v1.tt"), Database(), project);
        Assert.IsTrue(pyUsers.Success, string.Join(" | ", pyUsers.Errors));
        Expect.Contains(pyUsers.GeneratedText!, "def roles_and_permissions(session: Session, key, tenant_id: int | None = None)");
        Expect.Contains(pyUsers.GeneratedText!, "held_roles, held_permissions = roles_and_permissions(session, row._mapping[\"EmployeeId\"])");
        Expect.DoesNotContain((await Repo.Cache.RunAsync(Repo.Template("PY_UserDirectory_v1.tt"), Database(), plain)).GeneratedText!, "roles_and_permissions");

        var pyAuth = await Repo.Cache.RunAsync(Repo.Template("PY_EssentialAuth_v1.tt"), project);
        Assert.IsTrue(pyAuth.Success, string.Join(" | ", pyAuth.Errors));
        Expect.Contains(pyAuth.GeneratedText!, "if not any(r.startswith(\"@\") and r[1:] in user.permissions or r == user.role or r in user.roles for r in roles):");
        Expect.DoesNotContain((await Repo.Cache.RunAsync(Repo.Template("PY_EssentialAuth_v1.tt"), plain)).GeneratedText!, "permissions");

        var rsUsers = await Repo.Cache.RunAsync(Repo.Template("RS_UserDirectory_v1.tt"), Database(), project);
        Assert.IsTrue(rsUsers.Success, string.Join(" | ", rsUsers.Errors));
        Expect.Contains(rsUsers.GeneratedText!, "const PERMISSIONS: &str =");
        Expect.Contains(rsUsers.GeneratedText!, "        permissions,");
        Expect.DoesNotContain((await Repo.Cache.RunAsync(Repo.Template("RS_UserDirectory_v1.tt"), Database(), plain)).GeneratedText!, "PERMISSIONS");

        var rsAuth = await Repo.Cache.RunAsync(Repo.Template("RS_EssentialAuth_v1.tt"), project);
        Assert.IsTrue(rsAuth.Success, string.Join(" | ", rsAuth.Errors));
        Expect.Contains(rsAuth.GeneratedText!, "roles.iter().any(|entry| match entry.strip_prefix('@')");
        Expect.DoesNotContain((await Repo.Cache.RunAsync(Repo.Template("RS_EssentialAuth_v1.tt"), plain)).GeneratedText!, "permissions");
    }

    [TestMethod]
    public void A_tenant_column_on_the_role_rows_or_the_role_table_makes_the_roles_per_tenant()
    {
        var withTenant = Project([.. Settings, ("TenantTable", "Tenant")]);
        Assert.IsFalse(PermissionTables.Resolve(Database(), withTenant).Plan!.PerTenant);
        var held = PermissionTables.Resolve(Database(tenantOnUserRole: true), withTenant).Plan!;
        Assert.AreEqual("TenantId", held.UserRoleTenant!.Name);
        Assert.IsNull(held.RoleTenant);
        Assert.AreEqual("TenantId", PermissionTables.Resolve(Database(tenantOnRole: true), withTenant).Plan!.RoleTenant!.Name);
        Assert.IsFalse(PermissionTables.Resolve(Database(tenantOnUserRole: true, tenantOnRole: true), Project(Settings)).Plan!.PerTenant);
    }

    [TestMethod]
    public async Task The_three_APIs_read_the_roles_of_the_tenant_in_the_token_at_sign_in_and_at_a_switch()
    {
        var project = Project([.. Settings, ("TenantTable", "Tenant"), ("TenantMembership", "UserTenant:EmployeeId:TenantId")]);
        var database = Database(tenantOnUserRole: true, tenantOnRole: true);

        var directory = await Repo.Cache.RunAsync(Repo.Template("API_UserDirectory_v1.tt"), database, project);
        Assert.IsTrue(directory.Success, string.Join(" | ", directory.Errors));
        string text = directory.GeneratedText!.ReplaceLineEndings("\n");
        Expect.Contains(text, "from ur in context.Set<UserRole>().IgnoreQueryFilters().AsNoTracking() join r in context.Set<Role>().IgnoreQueryFilters().AsNoTracking()");
        Expect.Contains(text, "where ur.EmployeeId == key && ur.TenantId == tenantId && r.TenantId == tenantId select r.Name");

        var pyUsers = await Repo.Cache.RunAsync(Repo.Template("PY_UserDirectory_v1.tt"), database, project);
        Assert.IsTrue(pyUsers.Success, string.Join(" | ", pyUsers.Errors));
        Expect.Contains(pyUsers.GeneratedText!, "def roles_and_permissions(session: Session, key, tenant_id: int | None = None)");
        Expect.Contains(pyUsers.GeneratedText!, "user_roles.c[\"TenantId\"] == tenant_id, roles.c[\"TenantId\"] == tenant_id");
        Expect.Contains(pyUsers.GeneratedText!, "def held_for(session: Session, user_name: str, tenant_id: int | None)");

        var rsUsers = await Repo.Cache.RunAsync(Repo.Template("RS_UserDirectory_v1.tt"), database, project);
        Assert.IsTrue(rsUsers.Success, string.Join(" | ", rsUsers.Errors));
        Expect.Contains(rsUsers.GeneratedText!, "pub async fn held_for(pool: &Pool, user_name: &str, tenant: Option<i32>)");
        Expect.Contains(rsUsers.GeneratedText!, ".bind(key).bind(tenant).bind(tenant).fetch_all(pool)");

        var plain = await Repo.Cache.RunAsync(Repo.Template("PY_UserDirectory_v1.tt"), Database(), Project(Settings));
        Expect.DoesNotContain(plain.GeneratedText!, "held_for");
    }

    [TestMethod]
    public async Task A_nullable_tenant_column_on_the_role_table_lets_a_NULL_role_serve_every_tenant()
    {
        var project = Project([.. Settings, ("TenantTable", "Tenant"), ("TenantMembership", "UserTenant:EmployeeId:TenantId")]);
        var database = Database(tenantOnRole: true, sharedRoles: true);
        Assert.IsTrue(PermissionTables.Resolve(database, project).Plan!.RoleTenant!.IsNullable);

        var directory = await Repo.Cache.RunAsync(Repo.Template("API_UserDirectory_v1.tt"), database, project);
        Assert.IsTrue(directory.Success, string.Join(" | ", directory.Errors));
        Expect.Contains(directory.GeneratedText!, "(r.TenantId == tenantId || r.TenantId == null)");
        Expect.Contains(directory.GeneratedText!, "if (tenantId is null)");

        var pyUsers = await Repo.Cache.RunAsync(Repo.Template("PY_UserDirectory_v1.tt"), database, project);
        Expect.Contains(pyUsers.GeneratedText!, "or_(roles.c[\"TenantId\"] == tenant_id, roles.c[\"TenantId\"].is_(None))");
        Expect.Contains(pyUsers.GeneratedText!, "from sqlalchemy import column, or_, select, table, update");

        var rsUsers = await Repo.Cache.RunAsync(Repo.Template("RS_UserDirectory_v1.tt"), database, project);
        Expect.Contains(rsUsers.GeneratedText!, "(r.[TenantId] = @p2 OR r.[TenantId] IS NULL)");
    }

    [TestMethod]
    public async Task The_role_list_shows_a_tenant_its_own_roles_and_the_shared_ones()
    {
        var shared = Project([.. Settings, ("TenantTable", "Tenant"), ("TenantMembership", "UserTenant:EmployeeId:TenantId"), ("NoTenantTables", "Role"), ("AccessMode", "Ef")]);
        var context = await Repo.Cache.RunAsync(Repo.Template("CS_DbContext_v1.tt"), Database(tenantOnRole: true, sharedRoles: true), shared);
        Assert.IsTrue(context.Success, string.Join(" | ", context.Errors));
        Expect.Contains(context.GeneratedText!, "modelBuilder.Entity<Role>().HasQueryFilter(e => (e.TenantId == CurrentTenantId || e.TenantId == null || IsPlatform));");

        var own = await Repo.Cache.RunAsync(Repo.Template("CS_DbContext_v1.tt"), Database(tenantOnRole: true), Project([.. Settings, ("TenantTable", "Tenant"), ("TenantMembership", "UserTenant:EmployeeId:TenantId"), ("AccessMode", "Ef")]));
        Expect.Contains(own.GeneratedText!, "modelBuilder.Entity<Role>().HasQueryFilter(e => e.TenantId == CurrentTenantId);");
    }

    [TestMethod]
    public async Task The_Python_and_Rust_role_lists_show_a_tenant_its_own_roles_and_the_shared_ones()
    {
        var project = Project([.. Settings, ("TenantTable", "Tenant"), ("TenantMembership", "UserTenant:EmployeeId:TenantId"), ("NoTenantTables", "Role")]);
        var database = Database(tenantOnRole: true, sharedRoles: true);

        var pyRoutes = await Repo.Cache.RunAsync(Repo.Template("PY_Routes_v1.tt"), database.Tables.First(t => t.TableName == "Role"), project);
        Assert.IsTrue(pyRoutes.Success, string.Join(" | ", pyRoutes.Errors));
        Expect.Contains(pyRoutes.GeneratedText!, "statement = only_tenant_or_shared(statement, Role.tenant_id, user)");
        Expect.Contains(pyRoutes.GeneratedText!, "refuse_other_tenant_or_shared(row.tenant_id, user)");

        var rsRoutes = await Repo.Cache.RunAsync(Repo.Template("RS_Routes_v1.tt"), database.Tables.First(t => t.TableName == "Role"), project);
        Assert.IsTrue(rsRoutes.Success, string.Join(" | ", rsRoutes.Errors));
        Expect.Contains(rsRoutes.GeneratedText!, "&Tenancy::of_shared(&user)");
        Expect.Contains(rsRoutes.GeneratedText!, "row.tenant_id = Some(require_tenant(&user)?);");
    }

    [TestMethod]
    public async Task The_Blazor_Uno_and_WinUI3_apps_read_at_names_from_the_roles_and_permissions_of_the_sign_in()
    {
        var project = Project([.. Settings, ("Auth", "true")]);
        foreach (string template in new[] { "BLZ_EssentialAuth_v1.tt", "UNO_EssentialAuth_v1.tt" })
        {
            var auth = await Repo.Cache.RunAsync(Repo.Template(template), project);
            Assert.IsTrue(auth.Success, template + ": " + string.Join(" | ", auth.Errors));
            Expect.Contains(auth.GeneratedText!, "string[]? Roles = null, string[]? Permissions = null");
            Expect.Contains(auth.GeneratedText!, "r.StartsWith('@') ? user.Permissions?.Contains(r[1..]) == true");
        }
        var blazor = await Repo.Cache.RunAsync(Repo.Template("BLZ_EssentialAuth_v1.tt"), project);
        Expect.Contains(blazor.GeneratedText!, "new Claim(ClaimTypes.Role, \"@\" + held)");
        Expect.DoesNotContain((await Repo.Cache.RunAsync(Repo.Template("BLZ_EssentialAuth_v1.tt"), Project([.. Settings.Where(s => s.Item1 != "PermissionTables"), ("Auth", "true")]))).GeneratedText!, "Permissions");

        string desktop = DesktopSignInCode.Session(Database(), project, "AcmeContext", [], "Acme");
        Expect.Contains(desktop, "public sealed record SignedInUser(string Name, string? Role, int? OwnerId, string[] HeldRoles, string[] Permissions)");
        Expect.Contains(desktop, "roles.Any(r => r.StartsWith('@') ? user.Permissions.Contains(r[1..]) : r == user.Role || user.HeldRoles.Contains(r))");
        Expect.Contains(desktop, "public static async Task<(string[] Roles, string[] Permissions)> HeldAsync(AcmeContext context, string userName, int? tenantId");
        Expect.Contains(desktop, "var (heldRoles, heldPermissions) = await HeldAsync(context, name, null, cancellationToken);");
        Expect.DoesNotContain(DesktopSignInCode.Session(Database(), Project([.. Settings.Where(s => s.Item1 != "PermissionTables"), ("Auth", "true")]), "AcmeContext", [], "Acme"), "HeldAsync");
    }

    [TestMethod]
    public async Task The_sign_in_files_carry_the_claims_and_the_gate_asks_for_the_permission()
    {
        var project = Project(Settings);
        var directory = await Repo.Cache.RunAsync(Repo.Template("API_UserDirectory_v1.tt"), Database(), project);
        Assert.IsTrue(directory.Success, string.Join(" | ", directory.Errors));
        string text = directory.GeneratedText!.ReplaceLineEndings("\n");
        Expect.Contains(text, "public class PermissionLoginClaims(AcmeContext context) : ITenantLoginClaims");
        Expect.Contains(text, "new Claim(AuthNames.PermissionClaim, p!)");
        Expect.Contains(text, "services.AddScoped<ITenantLoginClaims, PermissionLoginClaims>();");

        var plain = await Repo.Cache.RunAsync(Repo.Template("API_UserDirectory_v1.tt"), Database(), Project(Settings.Where(s => s.Item1 != "PermissionTables").ToArray()));
        Expect.DoesNotContain(plain.GeneratedText!, "PermissionLoginClaims");

        var jwt = await Repo.Cache.RunAsync(Repo.Template("API_EssentialJwt_v1.tt"), project);
        Assert.IsTrue(jwt.Success, string.Join(" | ", jwt.Errors));
        Expect.Contains(jwt.GeneratedText!, "public const string PermissionClaim = \"permission\";");
        Expect.Contains(jwt.GeneratedText!, "GetServices<ITenantLoginClaims>()");
        Expect.Contains(jwt.GeneratedText!, "role.StartsWith('@') ? caller.HasClaim(AuthNames.PermissionClaim, role[1..]) : caller.IsInRole(role)");
        var plainJwt = await Repo.Cache.RunAsync(Repo.Template("API_EssentialJwt_v1.tt"), Project(("ApiAuth", "true")));
        Expect.DoesNotContain(plainJwt.GeneratedText!, "PermissionClaim");
    }
}
