namespace CodeGenNew.Core;

/// <summary> Everything a database-level template needs: every table of one schema, each read the same way a single-table template reads
/// its table. Passed to a template as the <c>Database</c> parameter (TemplateConfig.DatabaseOnly). </summary>
public class DatabaseModel
{
    public required string DatabaseName { get; init; }
    public required string SchemaName { get; init; }
    public SqlDialect Dialect { get; init; } = SqlDialect.SqlServer;
    public required List<TableModel> Tables { get; init; }

    /// <summary> The tables that get an entity class and a DbSet: a table with a single-column primary key (CS_Entity refuses any other). </summary>
    public List<TableModel> EntityTables => Tables.Where(t => t.PrimaryKeyColumns.Count == 1).OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary> The tables API_Crud writes an API for: a single int key, no name/active shape, and not an enum or other table the project
    /// says has no repository. API_Search is registered for the same tables. </summary>
    public List<TableModel> ApiTables(ProjectSettings project) => EntityTables
        .Where(t => t.PrimaryKeyShape == PrimaryKeyShape.SingleInt && !t.IsNameActiveTable && project.NoRepository(t.TableName, t.LookupShape) != true)
        .ToList();

    /// <summary> The tables that also get a search endpoint: the API tables minus bare lookup tables (a few rows, no pager worth having; the
    /// samples write no search function for them). </summary>
    public List<TableModel> SearchApiTables(ProjectSettings project) => ApiTables(project).Where(t => !t.LookupShape.LooksLikeLookup).ToList();

    /// <summary> The enum / lookup tables the project (or the schema's shape) says have no API of their own. </summary>
    public List<TableModel> EnumTables(ProjectSettings project) => EntityTables
        .Where(t => project.NoRepository(t.TableName, t.LookupShape) == true)
        .ToList();
}

/// <summary> What an EF Core provider needs for one database: the extension method, NuGet package and, where the .NET driver does not read it
/// itself, the environment variable that carries the password so appsettings.json never holds one. </summary>
public sealed record EfProviderInfo(string UseMethod, string Package, string? PasswordVariable, string? Note)
{
    public static EfProviderInfo For(SqlDialect dialect) => dialect switch
    {
        SqlDialect.PostgreSql => new("UseNpgsql", "Npgsql.EntityFrameworkCore.PostgreSQL", "PGPASSWORD",
            "Npgsql reads PGPASSWORD itself when the connection string has no password."),
        SqlDialect.MySql => new("UseMySQL", "MySql.EntityFrameworkCore", "MYSQL_PWD",
            "Oracle's provider (Pomelo has no EF Core 10 release yet). The driver does not read MYSQL_PWD, so the generated code adds it."),
        _ => new("UseSqlServer", "Microsoft.EntityFrameworkCore.SqlServer", null, null)
    };
}
