using System.Data;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The access mode (routines or EF Core LINQ), what the dialects differ in, and the templates that follow the mode. </summary>
[TestClass]
public class AccessModeTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static TableModel Customer(SqlDialect dialect = SqlDialect.Sqlite, bool withUnique = true) => Sample.Table("Customer",
    [
        Sample.Column("CustomerId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("StatusId", SqlDbType.Int, ordinal: 2),
        Sample.Column("AccountNumber", SqlDbType.VarChar, characters: 20, inUniqueIndex: withUnique, ordinal: 3),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 4),
        Sample.Column("Email", SqlDbType.VarChar, nullable: true, characters: 100, ordinal: 5),
        Sample.Column("CreateDate", SqlDbType.DateTime, createDateColumn: true, ordinal: 6),
        Sample.Column("CreateUser", SqlDbType.VarChar, characters: 30, createUserColumn: true, nullable: true, ordinal: 7),
        Sample.Column("Notes", SqlDbType.VarChar, characters: 1000, nullable: true, ordinal: 8)
    ],
    [Sample.ForeignKey("StatusId", "Status", "StatusId", "Description")], dialect: dialect);

    private static async Task<string> Render(string template, TableModel table, ProjectSettings? project = null)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, project ?? Project());
        if (!result.Success)
            Assert.Fail(template + ": " + string.Join(" | ", result.Errors).ReplaceLineEndings(" "));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    // ------------------------------------------------------------------ the mode

    [TestMethod]
    public void A_database_with_routines_defaults_to_them_and_sqlite_always_uses_ef()
    {
        Assert.AreEqual(AccessMode.Routines, Project().AccessModeFor(SqlDialect.SqlServer));
        Assert.AreEqual(AccessMode.Routines, Project().AccessModeFor(SqlDialect.PostgreSql));
        Assert.AreEqual(AccessMode.Routines, Project().AccessModeFor(SqlDialect.MySql));
        Assert.AreEqual(AccessMode.Ef, Project().AccessModeFor(SqlDialect.Sqlite));

        Assert.AreEqual(AccessMode.Ef, Project(("AccessMode", "Ef")).AccessModeFor(SqlDialect.PostgreSql));
        Assert.AreEqual(AccessMode.Ef, Project(("AccessMode", "ef")).AccessModeFor(SqlDialect.SqlServer));
        Assert.AreEqual(AccessMode.Routines, Project(("AccessMode", "Routines")).AccessModeFor(SqlDialect.MySql));
        Assert.AreEqual(AccessMode.Ef, Project(("AccessMode", "Routines")).AccessModeFor(SqlDialect.Sqlite), "there are no routines to call in SQLite");
        Assert.AreEqual(AccessMode.Routines, Project(("AccessMode", "nonsense")).AccessModeFor(SqlDialect.SqlServer));
    }

    // ------------------------------------------------------------------ what the dialects differ in

    [TestMethod]
    public void A_dialect_quotes_pages_and_buckets_in_its_own_way()
    {
        var sqlServer = DialectInfo.For(SqlDialect.SqlServer);
        var postgres = DialectInfo.For(SqlDialect.PostgreSql);
        var mySql = DialectInfo.For(SqlDialect.MySql);
        var sqlite = DialectInfo.For(SqlDialect.Sqlite);

        Assert.AreEqual("[a]]b]", sqlServer.Quote("a]b"));
        Assert.AreEqual("\"a\"\"b\"", postgres.Quote("a\"b"));
        Assert.AreEqual("`a``b`", mySql.Quote("a`b"));
        Assert.AreEqual("\"t\"", sqlite.QuoteTable("main", "t"), "SQLite has no schema to prefix");
        Assert.AreEqual("[dbo].[t]", sqlServer.QuoteTable("dbo", "t"));

        Assert.AreEqual("OFFSET @o ROWS FETCH NEXT @n ROWS ONLY", sqlServer.Paging("@o", "@n"));
        Assert.AreEqual("LIMIT @n OFFSET @o", sqlite.Paging("@o", "@n"));
        Assert.AreEqual("$2", postgres.Placeholder(2));
        Assert.AreEqual("?", sqlite.Placeholder(2));
        Assert.AreEqual("@p2", sqlServer.Placeholder(2));

        Assert.AreEqual("date_trunc('month', d)::date", postgres.DateBucketExpression("d", DateBucket.Month));
        Assert.AreEqual("strftime('%Y-%m-01', d)", sqlite.DateBucketExpression("d", DateBucket.Month));
        Assert.AreEqual("DATEFROMPARTS(YEAR(d), MONTH(d), 1)", sqlServer.DateBucketExpression("d", DateBucket.Month));
        Assert.AreEqual("DATE_FORMAT(d, '%Y-01-01')", mySql.DateBucketExpression("d", DateBucket.Year));

        Assert.IsFalse(sqlite.SupportsRoutines);
        Assert.IsTrue(sqlServer.SupportsRoutines);
        Assert.AreEqual("c ILIKE '%' || @p || '%' ESCAPE '\\'", postgres.ContainsCondition("c", "@p"));
        Assert.AreEqual("50\\% \\_off \\[x] a\\\\b", DialectInfo.EscapeLike("50% _off [x] a\\b"));
    }

    // ------------------------------------------------------------------ what a search does

    [TestMethod]
    public void The_search_plan_lists_the_filters_the_sorts_and_the_default_order()
    {
        var table = Customer();
        var navigations = EntityNavigations.Of(table, fk => false);
        var plan = SearchPlan.For(table, navigations);

        CollectionAssert.AreEqual(new[] { "accountNumber", "name", "email" }, plan.Filters.Select(f => f.ParameterName).ToArray(), "long text (Notes) is not searched");
        Assert.IsFalse(plan.Sorts.Any(s => s.Name == "Notes"));
        Assert.AreEqual("Status.Description", plan.Sorts.Single(s => s.Name == "StatusId").ParentPath);
        Assert.IsNull(plan.Sorts.Single(s => s.Name == "Name").ParentPath);
        Assert.AreEqual("AccountNumber", plan.DefaultOrder[0].Name, "the best display column comes first");
        Assert.AreEqual("CustomerId", plan.DefaultOrder[1].Name);

        var withoutNavigation = SearchPlan.For(table, EntityNavigations.Of(table, fk => true));
        Assert.IsNull(withoutNavigation.Sorts.Single(s => s.Name == "StatusId").ParentPath, "without a navigation property a foreign key sorts by its own value");
    }

    [TestMethod]
    public void A_navigation_is_named_after_its_key_unless_that_name_is_taken()
    {
        var table = Sample.Table("Ticket",
        [
            Sample.Column("TicketId", SqlDbType.Int, primaryKey: true, ordinal: 1),
            Sample.Column("OwnerId", SqlDbType.Int, ordinal: 2),
            Sample.Column("Owner", SqlDbType.VarChar, characters: 10, ordinal: 3),
            Sample.Column("Code", SqlDbType.Int, ordinal: 4)
        ],
        [Sample.ForeignKey("OwnerId", "User", "UserId", "Name"), Sample.ForeignKey("Code", "CodeTable", "CodeId", "Label")]);

        var navigations = EntityNavigations.Of(table, fk => false);

        Assert.HasCount(1, navigations, "OwnerId would be Owner, which is a column");
        Assert.AreEqual(("Code", "CodeRef", "CodeTable"), (navigations[0].Column.Name, navigations[0].Role, navigations[0].Type));
    }

    // ------------------------------------------------------------------ the templates follow the mode

    [TestMethod]
    public async Task The_search_query_filters_by_contains_and_sorts_by_a_fixed_list()
    {
        string cs = await Render("CS_SearchQuery_v1.tt", Customer());

        Expect.Contains(cs, "public static class CustomerSearchQuery");
        Expect.Contains(cs, "public static IQueryable<Customer> Filter(IQueryable<Customer> query, string? accountNumber = null, string? name = null, string? email = null)");
        Expect.Contains(cs, "string nameTerm = name.Trim().ToLower();\n            query = query.Where(t => t.Name.ToLower().Contains(nameTerm));");
        Expect.Contains(cs, "query = query.Where(t => t.Email != null && t.Email.ToLower().Contains(emailTerm));");
        Expect.Contains(cs, "switch (sortColumn is { Length: <= 128 } ? sortColumn.Trim().ToLowerInvariant() : null)");
        Expect.Contains(cs, "case \"name\":\n                ordered = descending ? query.OrderByDescending(t => t.Name) : query.OrderBy(t => t.Name);");
        Expect.Contains(cs, "case \"statusid\":\n                ordered = descending ? query.OrderByDescending(t => t.Status!.Description) : query.OrderBy(t => t.Status!.Description);");
        Expect.DoesNotContain(cs, "case \"notes\"");
        Expect.Contains(cs, "? query.OrderBy(t => t.AccountNumber).ThenBy(t => t.CustomerId)\n            : ordered.ThenBy(t => t.AccountNumber).ThenBy(t => t.CustomerId);");
        Expect.DoesNotContain(cs, "FromSqlRaw");
    }

    [TestMethod]
    public async Task A_database_enum_column_is_not_filtered_by_text()
    {
        var table = Sample.Table("Ticket",
        [
            Sample.Column("TicketId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Priority", SqlDbType.VarChar, characters: 10, enumType: "priority", choices: ["Low", "High"], ordinal: 2)
        ], dialect: SqlDialect.PostgreSql);

        string cs = await Render("CS_SearchQuery_v1.tt", table);

        Expect.Contains(cs, "// Priority is a database enum, which has no text comparison: it is not filtered here.");
        Expect.DoesNotContain(cs, "priorityTerm");
    }

    [TestMethod]
    public async Task The_repository_searches_and_clones_through_ef_in_ef_mode_and_through_routines_otherwise()
    {
        string ef = await Render("CS_Repo_v1.tt", Customer());
        Expect.Contains(ef, "var query = CustomerSearchQuery.Filter(_dbSet, accountNumber, name, email);");
        Expect.Contains(ef, "int totalCount = await query.CountAsync();");
        Expect.Contains(ef, ".Skip((Math.Max(pageNumber, 1) - 1) * size).Take(size).ToListAsync();");
        Expect.Contains(ef, "_dbSet.Add(copy);\n        await _context.SaveChangesAsync();\n        return copy.CustomerId;");
        Expect.DoesNotContain(ef, "FromSqlRaw");
        Expect.DoesNotContain(ef, "ExecuteSqlRawAsync");

        string routines = await Render("CS_Repo_v1.tt", Customer(SqlDialect.SqlServer));
        Expect.Contains(routines, "FromSqlRaw");
        Expect.Contains(routines, "EXEC [dbo].[Customer_Search]");
        Expect.DoesNotContain(routines, "CustomerSearchQuery");

        string forced = await Render("CS_Repo_v1.tt", Customer(SqlDialect.SqlServer), Project(("AccessMode", "Ef")));
        Expect.Contains(forced, "CustomerSearchQuery.Filter");
    }

    [TestMethod]
    public async Task The_search_endpoint_pages_through_the_query_class_in_ef_mode()
    {
        string ef = await Render("API_Search_v1.tt", Customer());

        Expect.Contains(ef, "using Acme.App.Repositories;");
        Expect.DoesNotContain(ef, "using Microsoft.Data.SqlClient;");
        Expect.Contains(ef, "var query = CustomerSearchQuery.Filter(context.Set<Customer>(), accountNumber, name, email);");
        Expect.Contains(ef, "var items = await CustomerSearchQuery.Sort(query, sortColumn, sortDescending)");
        Expect.DoesNotContain(ef, "SqlQueryRaw");

        string routines = await Render("API_Search_v1.tt", Customer(SqlDialect.SqlServer));
        Expect.Contains(routines, "using Microsoft.Data.SqlClient;");
        Expect.Contains(routines, "EXEC [dbo].[Customer_Search]");
    }

    private static TableModel Junction(SqlDialect dialect) => Sample.Table("CustomerTag",
    [
        Sample.Column("CustomerId", SqlDbType.Int, primaryKey: true, ordinal: 1),
        Sample.Column("TagId", SqlDbType.Int, primaryKey: true, ordinal: 2),
        Sample.Column("CreateDate", SqlDbType.DateTime, createDateColumn: true, ordinal: 3),
        Sample.Column("CreateUser", SqlDbType.VarChar, characters: 30, createUserColumn: true, ordinal: 4)
    ],
    [Sample.ForeignKey("CustomerId", "Customer", "CustomerId", "Name"), Sample.ForeignKey("TagId", "Tag", "TagId", "Label")], dialect: dialect);

    [TestMethod]
    public async Task The_junction_editor_lists_links_and_unlinks_through_the_context_in_ef_mode()
    {
        var table = Junction(SqlDialect.Sqlite);
        Assert.IsTrue(table.IsJunctionTable, "the fixture is a junction table");

        string api = await Render("API_Junction_v1.tt", table);
        Expect.Contains(api, "var linked = context.Set<CustomerTag>().Where(j => j.CustomerId == anchorId).Select(j => j.TagId);");
        Expect.Contains(api, "var rows = await context.Set<Tag>()\n            .OrderBy(t => t.Label)");
        Expect.Contains(api, ".Select(t => new CustomerTagJunctionItem { Label = t.Label, TargetId = t.TagId, IsSelected = linked.Contains(t.TagId) })");
        Expect.Contains(api, "if (!await context.Set<CustomerTag>().AnyAsync(j => j.CustomerId == request.AnchorId && j.TagId == request.TargetId))");
        Expect.Contains(api, "new CustomerTag { CustomerId = request.AnchorId, TagId = request.TargetId, CreateDate = DateTime.Now, CreateUser = \"\" }");
        Expect.Contains(api, "context.Set<CustomerTag>().RemoveRange(links);");
        Expect.DoesNotContain(api, "SqlQueryRaw");
        Expect.DoesNotContain(api, "ExecuteSqlInterpolatedAsync");

        string editor = await Render("WinUI3_JunctionEditor_v1.tt", table);
        Expect.Contains(editor, "var linked = _context.Set<CustomerTag>().Where(j => j.CustomerId == _anchorId)");
        Expect.Contains(editor, "if (!await _context.Set<CustomerTag>().AnyAsync(j => j.CustomerId == _anchorId && j.TagId == item.TargetId))");
        Expect.DoesNotContain(editor, "ExecuteSqlInterpolatedAsync");

        string routines = await Render("API_Junction_v1.tt", Junction(SqlDialect.SqlServer));
        Expect.Contains(routines, "EXEC [dbo].[CustomerTag_List]");
        Expect.DoesNotContain(routines, "RemoveRange");
    }

    [TestMethod]
    public void An_ef_clone_copies_by_the_same_rules_as_the_routine()
    {
        var table = Customer(withUnique: true);

        string cs = string.Join("\n", CloneEf.RepoMethod(table, "Customer"));

        Expect.Contains(cs, "string? uniqueAccountNumber = source.AccountNumber is null ? null : await SuggestUniqueAccountNumber(source.AccountNumber.Length > 17 ? source.AccountNumber[..17] : source.AccountNumber);");
        Expect.Contains(cs, "AccountNumber = uniqueAccountNumber ?? source.AccountNumber,");
        Expect.Contains(cs, "CreateDate = DateTime.Now,");
        Expect.Contains(cs, "CreateUser = createUser ?? \"\",");
        Expect.Contains(cs, "Name = source.Name,");
        Expect.DoesNotContain(cs, "CustomerId =");   // the database assigns the key
    }

    // ------------------------------------------------------------------ the plan follows the mode

    private static DatabaseModel DatabaseOf(SqlDialect dialect) => new()
    {
        DatabaseName = "Acme", SchemaName = dialect == SqlDialect.Sqlite ? "main" : "dbo", Dialect = dialect,
        Tables = [Customer(dialect), Sample.Table("Status", [Sample.Column("StatusId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Sample.Column("Description", SqlDbType.VarChar, characters: 20, ordinal: 2)], dialect: dialect)]
    };

    [TestMethod]
    public void The_plan_leaves_out_routines_and_adds_the_ef_queries_in_ef_mode()
    {
        var templates = TemplateCatalog.Discover(Repo.TemplatesDirectory);

        var sqlite = ProjectPlan.Build(templates, DatabaseOf(SqlDialect.Sqlite), Project(("Stacks", "Api")), ["Api"]).Select(s => s.Template.Name).ToList();
        Assert.IsFalse(sqlite.Any(n => n.StartsWith("SP_")), "SQLite has no routines: " + string.Join(", ", sqlite.Where(n => n.StartsWith("SP_"))));
        CollectionAssert.Contains(sqlite, "CS_SearchQuery");
        CollectionAssert.Contains(sqlite, "CS_Repo");

        var routines = ProjectPlan.Build(templates, DatabaseOf(SqlDialect.SqlServer), Project(("Stacks", "Api")), ["Api"]).Select(s => s.Template.Name).ToList();
        CollectionAssert.Contains(routines, "SP_Search");
        CollectionAssert.DoesNotContain(routines, "CS_SearchQuery");

        var forced = ProjectPlan.Build(templates, DatabaseOf(SqlDialect.SqlServer), Project(("Stacks", "Api"), ("AccessMode", "Ef")), ["Api"]).Select(s => s.Template.Name).ToList();
        CollectionAssert.DoesNotContain(forced, "SP_Search");
        CollectionAssert.Contains(forced, "CS_SearchQuery");

        var named = ProjectPlan.Build(templates, DatabaseOf(SqlDialect.SqlServer), Project(("Stacks", "Api"), ("AccessMode", "Ef"), ("PlanAlso", "SP_Search")), ["Api"]).Select(s => s.Template.Name).ToList();
        CollectionAssert.Contains(named, "SP_Search", "a template the project names is still written");
    }

    [TestMethod]
    public void A_routine_template_refuses_a_sqlite_table_with_the_reason()
    {
        var search = TemplateCatalog.Discover(Repo.TemplatesDirectory).Single(t => t.Name == "SP_Search");

        string? reason = search.Config.Refuse(Customer(SqlDialect.Sqlite));

        Assert.IsNotNull(reason);
        StringAssert.Contains(reason, "SQLite");
        Assert.IsNull(search.Config.Refuse(Customer(SqlDialect.PostgreSql)));
        Assert.IsFalse(search.AppliesTo(true, false, dialect: SqlDialect.Sqlite), "the menu does not offer it");
        Assert.IsTrue(search.AppliesTo(true, false, dialect: SqlDialect.MySql));
    }

    // ------------------------------------------------------------------ the SQLite provider in the generated project

    [TestMethod]
    public async Task The_sqlite_context_converts_the_types_it_cannot_order_and_the_essentials_name_the_provider()
    {
        var database = DatabaseOf(SqlDialect.Sqlite);
        var result = await Repo.Cache.RunAsync(Repo.Template("CS_DbContext_v1.tt"), database, Project());
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string cs = result.GeneratedText!.ReplaceLineEndings("\n");

        Expect.Contains(cs, "using Microsoft.EntityFrameworkCore.Storage.ValueConversion;");
        Expect.Contains(cs, "configurationBuilder.Properties<decimal>().HaveConversion<double>();");
        Expect.Contains(cs, "configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<DateTimeOffsetToBinaryConverter>();");
        Expect.Contains(cs, "options.UseSqlite(connectionString);");
        Expect.Contains(cs, "(SQLite)");
        Expect.DoesNotContain(cs, "UseSqlServer");

        var program = await Repo.Cache.RunAsync(Repo.Template("API_EssentialProgram_v1.tt"), Project(("DatabaseProvider", "Sqlite"), ("DatabaseName", "shop.db")));
        Assert.IsTrue(program.Success, string.Join(" | ", program.Errors));
        string text = program.GeneratedText!.ReplaceLineEndings("\n");
        Expect.Contains(text, "Microsoft.EntityFrameworkCore.Sqlite");
        Expect.Contains(text, "Data Source=shop.db");
        Expect.DoesNotContain(text, "Microsoft.EntityFrameworkCore.SqlServer");
    }
}
