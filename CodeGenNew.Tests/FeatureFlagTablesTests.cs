using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> FeatureTables: feature flags in two tables, a default per flag and an override per tenant or person, with the API's reads and the one route that writes them. </summary>
[TestClass]
public class FeatureFlagTablesTests
{
    private static readonly (string, string)[] Base =
    [
        ("ApiAuth", "true"), ("UserTable", "Employee"), ("UserNameColumn", "Login"), ("UserOwnerColumn", "EmployeeId"), ("TenantTable", "Tenant"), ("FeatureTables", "Feature,FeatureOverride")
    ];

    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static ProjectSettings Project(IEnumerable<(string, string)> settings, params (string, string)[] more) =>
        Project(settings.Concat(more).GroupBy(s => s.Item1).Select(g => g.Last()).ToArray());

    private static ColumnModel Key(string name) => Sample.Column(name, SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1);

    private static ColumnModel Text(string name, int ordinal) => Sample.Column(name, SqlDbType.NVarChar, characters: 60, ordinal: ordinal);

    private static DatabaseModel Database(bool tenant = true, bool person = true, bool nullableTenant = true, bool nullablePerson = true, bool defaultColumn = true, bool extraRequired = false)
    {
        var employee = Sample.Table("Employee", [Key("EmployeeId"), Text("Login", 2), Sample.Column("PasswordHash", SqlDbType.NVarChar, characters: 200, ordinal: 3)]);
        var tenantTable = Sample.Table("Tenant", [Key("TenantId"), Text("Name", 2)]);
        var feature = Sample.Table("Feature", [Key("FeatureId"), Text("Name", 2), .. defaultColumn ? new[] { Sample.Column("DefaultEnabled", SqlDbType.Bit, ordinal: 3) } : []]);
        List<ColumnModel> columns = [Key("FeatureOverrideId"), Sample.Column("FeatureId", SqlDbType.Int, ordinal: 2), Sample.Column("IsEnabled", SqlDbType.Bit, ordinal: 3)];
        List<ForeignKeyModel> keys = [Sample.ForeignKey("FeatureId", "Feature", "FeatureId", "Name")];
        if (tenant)
        {
            columns.Add(Sample.Column("TenantId", SqlDbType.Int, nullable: nullableTenant, ordinal: 4));
            keys.Add(Sample.ForeignKey("TenantId", "Tenant", "TenantId", "Name"));
        }
        if (person)
        {
            columns.Add(Sample.Column("EmployeeId", SqlDbType.Int, nullable: nullablePerson, ordinal: 5));
            keys.Add(Sample.ForeignKey("EmployeeId", "Employee", "EmployeeId", "Login"));
        }
        if (extraRequired)
            columns.Add(Text("Reason", 6));
        return new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [employee, tenantTable, feature, Sample.Table("FeatureOverride", columns, keys)] };
    }

    [TestMethod]
    public void The_two_tables_are_found_by_their_keys_names_and_yes_no_columns()
    {
        var (plan, problem) = FeatureFlagTables.Resolve(Database(), Project(Base));
        Assert.IsNull(problem);
        Assert.IsNotNull(plan);
        Assert.AreEqual("FeatureId", plan.FlagKey.Name);
        Assert.AreEqual("Name", plan.FlagName.Name);
        Assert.AreEqual("DefaultEnabled", plan.FlagDefault.Name);
        Assert.AreEqual("FeatureId", plan.OverrideFlag.Name);
        Assert.AreEqual("IsEnabled", plan.OverrideEnabled.Name);
        Assert.AreEqual("TenantId", plan.OverrideTenant?.Name);
        Assert.AreEqual("EmployeeId", plan.OverridePerson?.Name);
    }

    [TestMethod]
    public void An_override_table_with_only_a_tenant_or_only_a_person_column_is_enough()
    {
        var (tenantOnly, _) = FeatureFlagTables.Resolve(Database(person: false, nullableTenant: false), Project(Base));
        Assert.IsNotNull(tenantOnly);
        Assert.IsNull(tenantOnly.OverridePerson);
        var (personOnly, _) = FeatureFlagTables.Resolve(Database(tenant: false, nullablePerson: false), Project(Base));
        Assert.IsNotNull(personOnly);
        Assert.IsNull(personOnly.OverrideTenant);
    }

    [TestMethod]
    public void A_problem_names_what_is_missing()
    {
        var project = Project(Base);
        StringAssert.Contains(FeatureFlagTables.Resolve(Database(tenant: false, person: false), project).Problem, "to say who the override is for");
        StringAssert.Contains(FeatureFlagTables.Resolve(Database(defaultColumn: false), project).Problem, "yes / no column for the default");
        StringAssert.Contains(FeatureFlagTables.Resolve(Database(nullableTenant: false), project).Problem, "both must allow NULL");
        StringAssert.Contains(FeatureFlagTables.Resolve(Database(extraRequired: true), project).Problem, "cannot add an override row");
        StringAssert.Contains(FeatureFlagTables.Resolve(Database(), Project(Base, ("FeatureTables", "Feature"))).Problem, "names two tables");
        StringAssert.Contains(FeatureFlagTables.Resolve(Database(), Project(Base, ("FeatureTables", "Feature,Nothing"))).Problem, "Nothing, which is not a table");
        StringAssert.Contains(FeatureFlagTables.Resolve(Database(), Project(Base, ("ApiAuth", "false"))).Problem, "needs ApiAuth=true");
        Assert.IsNull(FeatureFlagTables.Resolve(Database(), Project(Base, ("FeatureTables", ""))).Problem);
    }

    [TestMethod]
    public void The_person_column_is_used_only_when_the_owner_claim_is_the_key_of_the_user_table()
    {
        var (plan, _) = FeatureFlagTables.Resolve(Database(), Project(Base, ("UserOwnerColumn", "")));
        Assert.IsNotNull(plan);
        Assert.IsNull(plan.OverridePerson);
    }

    [TestMethod]
    public void The_flag_tables_get_no_ordinary_api_and_the_template_is_in_the_plan()
    {
        var project = Project(Base);
        var database = Database();
        foreach (string name in new[] { "Feature", "FeatureOverride" })
        {
            var table = database.Tables.Single(t => t.TableName == name);
            Assert.IsFalse(table.HasCrudApi(project), name);
            Assert.IsTrue(project.NoApi(name, table.LookupShape));
        }
        Assert.IsTrue(database.Tables.Single(t => t.TableName == "Employee").HasCrudApi(project));
        CollectionAssert.Contains(project.ImpliedPlanTemplates.ToList(), "API_Features");
        var without = Project(Base, ("FeatureTables", ""));
        CollectionAssert.DoesNotContain(without.ImpliedPlanTemplates.ToList(), "API_Features");
        Assert.IsTrue(database.Tables.Single(t => t.TableName == "Feature").HasCrudApi(without));
    }

    [TestMethod]
    public void The_api_reads_the_default_then_the_tenant_then_the_person()
    {
        string code = FeatureFlagWriter.File(Database(), Project(Base), "AcmeContext", []);
        StringAssert.Contains(code, "public static async Task<Dictionary<string, bool>> AllAsync(AcmeContext context, int? tenant, int? person");
        StringAssert.Contains(code, "o.TenantId == tenant && o.EmployeeId == null");
        StringAssert.Contains(code, "o.EmployeeId == person && o.TenantId == null");
        StringAssert.Contains(code, "public static async Task<bool> FeatureEnabledAsync(this HttpContext http, string name)");
        StringAssert.Contains(code, "app.MapGet(\"/api/features\", FeatureEndpoints.GetAsync);");
        StringAssert.Contains(code, "app.MapPut(\"/api/features/{name}\", FeatureEndpoints.PutAsync);");
        Assert.IsFalse(code.Contains('§'), "every token is filled");
        Assert.IsTrue(code.IndexOf("byTenant", StringComparison.Ordinal) < code.IndexOf("byPerson", StringComparison.Ordinal), "the person's value is applied last");
    }

    [TestMethod]
    public void A_project_with_one_kind_of_override_reads_only_that_kind()
    {
        string tenantOnly = FeatureFlagWriter.File(Database(person: false, nullableTenant: false), Project(Base), "AcmeContext", []);
        StringAssert.Contains(tenantOnly, "Where(o => o.TenantId == tenant)");
        StringAssert.Contains(tenantOnly, "Where(o => false)");
        Assert.IsFalse(tenantOnly.Contains("UnknownPerson") || tenantOnly.Contains("personTarget is not null && !await"), "no person check without a person column");
        string personOnly = FeatureFlagWriter.File(Database(tenant: false, nullablePerson: false), Project(Base), "AcmeContext", []);
        StringAssert.Contains(personOnly, "Where(o => o.EmployeeId == person)");
        Assert.IsFalse(personOnly.Contains("tenantTarget is not null && !await"), "no tenant check without a tenant column");
    }

    [TestMethod]
    public void Every_stack_that_serves_the_api_is_asked_for_the_flags_and_the_screens_need_the_sign_in()
    {
        var plans = Project(Base).ImpliedPlanTemplates.ToList();
        CollectionAssert.IsSubsetOf(new[] { "API_Features", "PY_Features", "RS_Features" }, plans);
        CollectionAssert.DoesNotContain(plans, "TSX_Features");
        CollectionAssert.IsSubsetOf(new[] { "TSX_Features", "TS_Features", "BLZ_Features" }, Project(Base, ("Auth", "true")).ImpliedPlanTemplates.ToList());
        CollectionAssert.DoesNotContain(Project(Base, ("Auth", "true"), ("FeatureTables", "")).ImpliedPlanTemplates.ToList(), "BLZ_Features");
    }
}
