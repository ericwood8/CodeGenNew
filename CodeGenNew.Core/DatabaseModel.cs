namespace CodeGenNew.Core;

/// <summary> Everything a database-level template needs: every table of one schema, each read the same way a single-table template reads
/// its table. Passed to a template as the <c>Database</c> parameter (TemplateConfig.DatabaseOnly). </summary>
public class DatabaseModel
{
    public required string DatabaseName { get; init; }
    public required string SchemaName { get; init; }
    public SqlDialect Dialect { get; init; } = SqlDialect.SqlServer;
    public required List<TableModel> Tables { get; init; }

    /// <summary> The tables that get an entity class, a repository and an API: a table with a single-column primary key. </summary>
    public List<TableModel> EntityTables => Tables.Where(t => t.PrimaryKeyColumns.Count == 1).OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary> The tables with a composite primary key (a junction table): CS_Entity writes an entity for them and the context names the key in OnModelCreating, but they get no repository or API. </summary>
    public List<TableModel> CompositeKeyTables => Tables.Where(t => t.PrimaryKeyColumns.Count > 1).OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary> Every table the context has a DbSet for: <see cref="EntityTables"/> and <see cref="CompositeKeyTables"/>, by name. </summary>
    public List<TableModel> ContextTables => EntityTables.Concat(CompositeKeyTables).OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary> The tables API_Crud writes an API for: a single int key, no name/active shape, and not an enum or other table the project
    /// says has no repository. API_Search is registered for the same tables. </summary>
    public List<TableModel> ApiTables(ProjectSettings project) => EntityTables.Where(t => t.HasCrudApi(project)).ToList();

    /// <summary> The tables that also get a search endpoint: the API tables minus bare lookup tables (a few rows, no pager worth having; the
    /// samples write no search function for them). </summary>
    public List<TableModel> SearchApiTables(ProjectSettings project) => ApiTables(project).Where(t => t.HasSearchApi(project)).ToList();

    /// <summary> The tables that get a screen and a menu entry, in menu order: the project's <c>Screens</c> list when it has one, else every table that has an API and
    /// a search endpoint, alphabetically. A listed name that is not a table with a single-column key is in <see cref="UnknownScreens"/>. </summary>
    public List<TableModel> ScreenTables(ProjectSettings project)
    {
        if (project.Screens.Length == 0)
            return SearchApiTables(project).OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase).ToList();

        return project.Screens
            .Select(name => EntityTables.FirstOrDefault(t => t.TableName.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .OfType<TableModel>().ToList();
    }

    public List<string> UnknownScreens(ProjectSettings project) => project.Screens
        .Where(name => !EntityTables.Any(t => t.TableName.Equals(name, StringComparison.OrdinalIgnoreCase))).ToList();

    /// <summary> Whether the table's screen is the master-detail kind (its add/edit dialog also shows the child tables): the project's <c>DetailMasterTables</c>
    /// when it lists any, else every table that has at least one child table. </summary>
    public static bool IsDetailMaster(TableModel table, ProjectSettings project) =>
        IsDetailMaster(table.TableName, table.HasAtLeastOneChildForeignKey, project);

    /// <summary> The same rule for a table known by name and by whether it has child tables (a child grid's link knows the child only that way). </summary>
    public static bool IsDetailMaster(string tableName, bool hasChildTables, ProjectSettings project) =>
        project.DetailMasterTables is { Length: > 0 } listed
            ? listed.Contains(tableName, StringComparer.OrdinalIgnoreCase)
            : hasChildTables;

    /// <summary> Child tables a master-detail screen in <paramref name="screens"/> links to, but that have no screen themselves (the link would open nothing). </summary>
    public static List<string> ChildrenWithoutScreen(IReadOnlyList<TableModel> screens, ProjectSettings project) => screens
        .Where(t => IsDetailMaster(t, project))
        .SelectMany(t => t.ChildForeignKeys.Select(c => $"{c.ReferencingTable} (a child of {t.TableName})"))
        .Where(text => !screens.Any(s => text.StartsWith(s.TableName + " ", StringComparison.Ordinal)))
        .Distinct().ToList();

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
        SqlDialect.Sqlite => new("UseSqlite", "Microsoft.EntityFrameworkCore.Sqlite", null, "A file database: the connection string is Data Source=<file>, so there is no password."),
        _ => new("UseSqlServer", "Microsoft.EntityFrameworkCore.SqlServer", null, null)
    };
}
