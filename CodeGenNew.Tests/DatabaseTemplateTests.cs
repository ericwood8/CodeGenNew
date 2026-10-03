using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The database-level templates (CS_DbContext, API_Registration): one file for the whole database, fed every table as
/// <c>Database</c>. Checked per provider, with no database. </summary>
[TestClass]
public class DatabaseTemplateTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static TableModel In(TableModel t, SqlDialect dialect, LookupShape? lookup = null) => new()
    {
        SchemaName = t.SchemaName, TableName = t.TableName, QuotedName = t.QuotedName, Dialect = dialect,
        Columns = t.Columns, PrimaryKeyColumns = t.PrimaryKeyColumns, ForeignKeys = t.ForeignKeys, ChildForeignKeys = t.ChildForeignKeys,
        DisplayColumns = t.DisplayColumns, HasRowData = t.HasRowData, Rows = t.Rows, LookupShape = lookup ?? t.LookupShape
    };

    private static DatabaseModel Database(SqlDialect dialect) => new()
    {
        DatabaseName = "Acme",
        SchemaName = "dbo",
        Dialect = dialect,
        // a plain table, a table with a composite key (no entity, no API) and a small lookup table the project lists as an enum
        Tables = [In(Sample.DonateLeave(), dialect), In(Sample.CompositeKey(), dialect), In(Sample.Roles(), dialect)]
    };

    private static async Task<string> Render(string template, DatabaseModel database, ProjectSettings? project = null)
    {
        var result = await TemplateRunner.RunAsync(Repo.Template(template), database, project ?? Project());
        Assert.IsTrue(result.Success, $"{template} failed: {string.Join(" | ", result.Errors)}");
        return result.GeneratedText!.Replace("\r\n", "\n");
    }

    [TestMethod]
    public async Task The_sql_server_context_has_a_dbset_per_keyed_table_and_uses_sql_server()
    {
        string cs = await Render("CS_DbContext_v1.tt", Database(SqlDialect.SqlServer));

        Expect.Contains(cs, "public partial class AcmeContext : DbContext");
        Expect.Contains(cs, "public DbSet<E_DonateLeave> E_DonateLeave => Set<E_DonateLeave>();");
        Expect.Contains(cs, "public DbSet<SY_Role> SY_Role => Set<SY_Role>();");
        Expect.Contains(cs, "public DbSet<Junction> Junction => Set<Junction>();");   // a composite key has an entity too, with its key named in OnModelCreating
        Expect.Contains(cs, "modelBuilder.Entity<Junction>().HasKey(e => new { e.LeftId, e.RightId });");
        Expect.Contains(cs, "OnModelCreatingPartial(modelBuilder);");
        Expect.Contains(cs, "partial void OnModelCreatingPartial(ModelBuilder modelBuilder);");
        Expect.Contains(cs, "options.UseSqlServer(connectionString);");
        Expect.DoesNotContain(cs, "ConfigureConventions");
        Expect.Contains(cs, "public AcmeContext()");
        Expect.Contains(cs, "AddJsonFile(\"appsettings.json\")");
    }

    [TestMethod]
    public async Task The_postgres_context_uses_npgsql_and_plain_timestamps()
    {
        string cs = await Render("CS_DbContext_v1.tt", Database(SqlDialect.PostgreSql));

        Expect.Contains(cs, "options.UseNpgsql(connectionString);");
        Expect.Contains(cs, "Npgsql.EntityFrameworkCore.PostgreSQL");
        Expect.Contains(cs, "configurationBuilder.Properties<DateTime>().HaveColumnType(\"timestamp\");");
        Expect.Contains(cs, "PGPASSWORD");
        Expect.DoesNotContain(cs, "UseSqlServer");
    }

    [TestMethod]
    public async Task The_mysql_context_adds_the_password_from_the_environment()
    {
        string cs = await Render("CS_DbContext_v1.tt", Database(SqlDialect.MySql));

        Expect.Contains(cs, "new MySql.Data.MySqlClient.MySqlConnectionStringBuilder(connectionString)");
        Expect.Contains(cs, "Environment.GetEnvironmentVariable(\"MYSQL_PWD\")");
        Expect.Contains(cs, "options.UseMySQL(builder.ConnectionString);");
        Expect.DoesNotContain(cs, "ConfigureConventions");
    }

    [TestMethod]
    public async Task The_context_takes_its_names_from_the_project()
    {
        string cs = await Render("CS_DbContext_v1.tt", Database(SqlDialect.SqlServer),
            Project(("ContextName", "SalesContext"), ("ContextNamespace", "Acme.Data"), ("Usings", "Acme.Entities")));

        Expect.Contains(cs, "namespace Acme.Data;");
        Expect.Contains(cs, "using Acme.Entities;");
        Expect.Contains(cs, "public partial class SalesContext : DbContext");
    }

    [TestMethod]
    public async Task The_registration_lists_an_api_for_each_table_that_has_one_and_none_for_an_enum()
    {
        string cs = await Render("API_Registration_v1.tt", Database(SqlDialect.PostgreSql), Project(("EnumTables", "SY_Role")));

        Expect.Contains(cs, "new E_DonateLeaveApi<E_DonateLeave>().Register(app);");
        Expect.Contains(cs, "new E_DonateLeaveSearchApi<E_DonateLeave>().Register(app);");
        Expect.DoesNotContain(cs, "SY_Role");     // an enum has an entity and a DbSet but no API
        Expect.DoesNotContain(cs, "Junction");    // a composite key has none of them
        Expect.Contains(cs, "AddDbContext<AcmeContext>(options => AcmeContext.UseProvider(options,");
        Expect.Contains(cs, "public static void RegisterGeneratedApis(this WebApplication app)");
    }

    [TestMethod]
    public async Task A_lookup_table_gets_an_api_but_no_search_endpoint()
    {
        string cs = await Render("API_Registration_v1.tt", new DatabaseModel
        {
            DatabaseName = "Acme", SchemaName = "dbo",
            Tables = [In(Sample.Roles(), SqlDialect.SqlServer, new LookupShape(LooksLikeLookup: true, RowCount: 3))]
        }, Project(("EnumTables", "none")));

        Expect.Contains(cs, "new SY_RoleApi<SY_Role>().Register(app);");
        Expect.DoesNotContain(cs, "SY_RoleSearchApi");
    }

    [TestMethod]
    public void A_database_level_template_is_not_offered_on_a_table_but_is_flagged_by_its_config()
    {
        foreach (string name in new[] { "CS_DbContext_v1.tt.config", "API_Registration_v1.tt.config" })
        {
            var config = TemplateConfig.Load(Repo.Template(name));
            Assert.IsTrue(config.DatabaseOnly, name);

            var template = new TemplateInfo { FilePath = "X.tt", Name = "X", Config = config };
            Assert.IsFalse(template.AppliesTo(tableHasPrimaryKey: true, isView: false), name);
        }
    }

    [TestMethod]
    public void A_database_level_template_is_named_after_the_context()
    {
        var config = TemplateConfig.Load(Repo.Template("CS_DbContext_v1.tt.config"));
        var template = new TemplateInfo { FilePath = "CS_DbContext_v1.tt", Name = "CS_DbContext", Config = config };

        Assert.AreEqual("InvoiceSystemContext.cs", template.BuildDatabaseFileName("InvoiceSystemContext"));
    }

    [TestMethod]
    public async Task A_context_without_a_composite_key_has_no_OnModelCreating()
    {
        var db = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [Sample.DonateLeave(), Sample.Roles()] };

        string cs = await Render("CS_DbContext_v1.tt", db);

        Expect.DoesNotContain(cs, "OnModelCreating");
    }

    [TestMethod]
    public async Task Plural_db_set_names_pluralize_the_property_and_keep_the_table_name_when_another_table_already_has_the_plural()
    {
        var holiday = Sample.Holiday();
        var holidays = Sample.Table("Holidays", [Sample.Column("HolidaysId", System.Data.SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1)]);
        var db = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [holiday, holidays, Sample.DonateLeave()] };

        string plain = await Render("CS_DbContext_v1.tt", db);
        string plural = await Render("CS_DbContext_v1.tt", db, Project(("DbSetNames", "Plural")));

        Expect.Contains(plain, "public DbSet<E_DonateLeave> E_DonateLeave => Set<E_DonateLeave>();");
        Expect.Contains(plural, "public DbSet<E_DonateLeave> E_DonateLeaves => Set<E_DonateLeave>();");
        Expect.Contains(plural, "public DbSet<Holiday> Holiday => Set<Holiday>();");   // Holidays is another table's name
        Expect.Contains(plural, "public DbSet<Holidays> Holidays => Set<Holidays>();");   // already plural: left alone
    }
}
