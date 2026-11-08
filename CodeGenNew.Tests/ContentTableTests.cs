using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> ContentTables: posts, pages and articles with a status, a slug and a published time. </summary>
[TestClass]
public class ContentTableTests
{
    private static TableModel Post(int slugLength = 150, int statusLength = 12, bool publishedNullable = true, bool withPublishedOn = true)
    {
        var columns = new List<ColumnModel>
        {
            Sample.Column("PostId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Title", SqlDbType.NVarChar, characters: 150, ordinal: 2),
            Sample.Column("Slug", SqlDbType.NVarChar, characters: slugLength, ordinal: 3),
            Sample.Column("Status", SqlDbType.NVarChar, characters: statusLength, ordinal: 4),
        };
        if (withPublishedOn)
            columns.Add(Sample.Column("PublishedOn", SqlDbType.DateTime, nullable: publishedNullable, ordinal: 5));
        return Sample.Table("Post", columns, []);
    }

    private static ProjectSettings Project(params (string Key, string Value)[] extra)
    {
        var values = new Dictionary<string, string> { ["ProjectName"] = "Shop", ["ContentTables"] = "Post", ["AccessMode"] = "Ef" };
        foreach (var (key, value) in extra)
            values[key] = value;
        return ProjectSettings.FromValues(values);
    }

    private static string Written(TableModel table, ProjectSettings project) =>
        RowShapeWriter.File(new DatabaseModel { DatabaseName = "Shop", SchemaName = "dbo", Tables = [table] }, project, "ShopContext", []);

    [TestMethod]
    public void The_columns_are_found_by_their_names()
    {
        var content = ContentPlan.For(Post(), Project())!;
        Assert.AreEqual("Title", content.Title.Name);
        Assert.AreEqual("Slug", content.Slug.Name);
        Assert.AreEqual("Status", content.Status.Name);
        Assert.AreEqual("PublishedOn", content.PublishedOn!.Name);
        Assert.AreEqual(150, content.SlugLength);
    }

    [TestMethod]
    public void The_published_time_is_optional_and_the_columns_can_be_named()
    {
        Assert.IsNull(ContentPlan.For(Post(withPublishedOn: false), Project())!.PublishedOn);
        var named = ContentPlan.For(Post(), Project(("ContentTables", "Post:Title:Slug:Status:PublishedOn")))!;
        Assert.AreEqual("PublishedOn", named.PublishedOn!.Name);
        Assert.IsNotNull(ContentPlan.Problem(Post(), Project(("ContentTables", "Post:Nothing"))));
    }

    [TestMethod]
    public void A_table_that_cannot_hold_a_slug_or_a_state_is_refused_with_the_reason()
    {
        Assert.Contains("room for at least", ContentPlan.Problem(Post(slugLength: 8), Project())!);
        Assert.Contains("published has 9", ContentPlan.Problem(Post(statusLength: 6), Project())!);
        Assert.Contains("must be a date and time column that allows NULL", ContentPlan.Problem(Post(publishedNullable: false), Project())!);
        Assert.IsNull(ContentPlan.Problem(Post(), Project()));
        Assert.IsNull(ContentPlan.For(Post(slugLength: 8), Project()));
    }

    [TestMethod]
    public void A_table_without_a_slug_column_is_refused()
    {
        var table = Sample.Table("Post",
        [
            Sample.Column("PostId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Title", SqlDbType.NVarChar, characters: 150, ordinal: 2),
            Sample.Column("Status", SqlDbType.NVarChar, characters: 12, ordinal: 3)
        ], []);
        Assert.Contains("needs a text column Slug", ContentPlan.Problem(table, Project())!);
    }

    [TestMethod]
    public void The_shape_is_known_to_the_table_shapes_and_the_row_shape_plan()
    {
        var table = Post();
        Assert.IsTrue(TableShapes.For(table, Project()).Has<ContentShape>());
        Assert.IsTrue(RowShapePlan.Has(table, Project()));
        Assert.IsTrue(RowShapeWriter.HasSaveCheck(table, Project()));
        Assert.IsFalse(RowShapeWriter.HasDeleteCheck(table, Project()));
        Assert.Contains("API_RowShapes", Project().ImpliedPlanTemplates.ToList());
    }

    [TestMethod]
    public void A_table_the_database_does_not_have_is_reported()
    {
        var database = new DatabaseModel { DatabaseName = "Shop", SchemaName = "dbo", Tables = [Post()] };
        var problems = RowShapePlan.Problems(database, Project(("ContentTables", "Post,Missing")));
        Assert.Contains("ContentTables: Missing is not a table of the database.", problems);
    }

    [TestMethod]
    public void The_generated_check_makes_the_slug_and_owns_the_status()
    {
        string text = Written(Post(), Project());
        Expect.Contains(text, "public static string MakeSlug(string? text, int maxLength)");
        Expect.Contains(text, "row.Status = isNew ? \"draft\" : stored!.Status;");
        Expect.Contains(text, "row.PublishedOn = isNew ? null : stored!.PublishedOn;");
        Expect.Contains(text, "MakeSlug(row.Title, 150)");
        Expect.Contains(text, "return \"" + ContentWords.SlugTaken + "\";");
        Expect.Contains(text, "await PrepareAsync(context, row, isNew) is { } taken ? RowShapeProblems.SlugTaken(taken) : null;");
        Expect.Contains(text, "public static IResult SlugTaken(string detail) => Problem(409, \"" + ContentWords.SlugTakenType + "\"");
        Expect.Contains(text, ".SetProperty(x => x.PublishedOn, x => x.PublishedOn ?? now)");
        Expect.Contains(text, "var now = DateTime.Now;");
    }

    [TestMethod]
    public void The_routes_change_the_status_and_the_public_ones_need_no_sign_in_when_the_api_has_one()
    {
        string open = Written(Post(), Project());
        Expect.Contains(open, "app.MapPost(\"/api/posts/{id}/publish\"");
        Expect.Contains(open, "app.MapPost(\"/api/posts/{id}/unpublish\"");
        Expect.Contains(open, "app.MapPost(\"/api/posts/{id}/archive\"");
        Expect.Contains(open, "app.MapGet(\"/api/posts/public\"");
        Expect.Contains(open, "app.MapGet(\"/api/posts/public/{slug}\"");
        Expect.DoesNotContain(open, "RequireAuthorization");
        Expect.DoesNotContain(open, "AllowAnonymous");

        string signedIn = Written(Post(), Project(("ApiAuth", "true")));
        Expect.Contains(signedIn, "SetStatusAsync(context, id, \"published\"))" + ".RequireAuthorization();");
        Expect.Contains(signedIn, "PublicBySlugAsync(context, slug)).AllowAnonymous();");
    }

    [TestMethod]
    public void A_table_without_a_published_time_leaves_it_out()
    {
        string text = Written(Post(withPublishedOn: false), Project());
        Expect.DoesNotContain(text, "PublishedOn");
        Expect.Contains(text, ".OrderByDescending(x => x.PostId)");
    }

    private static TableModel TenantPost() => Sample.Table("Post",
    [
        Sample.Column("PostId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("TenantId", SqlDbType.Int, ordinal: 2),
        Sample.Column("Title", SqlDbType.NVarChar, characters: 150, ordinal: 3),
        Sample.Column("Slug", SqlDbType.NVarChar, characters: 150, ordinal: 4),
        Sample.Column("Status", SqlDbType.NVarChar, characters: 12, ordinal: 5)
    ], [Sample.ForeignKey("TenantId", "Tenant", "TenantId", "Name")]);

    [TestMethod]
    public void A_tenant_owned_table_needs_a_host_column_for_the_public_routes()
    {
        string problem = ContentPlan.Problem(TenantPost(), Project(("ApiAuth", "true"), ("TenantTable", "Tenant")))!;
        Assert.Contains("belongs to a tenant", problem);
        Assert.Contains("TenantHostColumn", problem);
        Assert.IsNull(ContentPlan.For(TenantPost(), Project(("ApiAuth", "true"), ("TenantTable", "Tenant"))));
        Assert.IsNull(ContentPlan.Problem(TenantPost(), Project(("ApiAuth", "true"), ("TenantTable", "Tenant"), ("TenantHostColumn", "Slug"))));
        Assert.IsNull(ContentPlan.Problem(TenantPost(), Project(("ApiAuth", "true"), ("TenantTable", "Tenant"), ("NoTenantTables", "Post"))));
    }

    [TestMethod]
    public void The_public_routes_of_a_tenant_owned_table_read_the_tenant_of_the_host()
    {
        var project = Project(("ApiAuth", "true"), ("TenantTable", "Tenant"), ("TenantHostColumn", "Slug"));
        Assert.AreEqual("TenantId", ContentPlan.For(TenantPost(), project)!.TenantColumn!.Name);
        string text = Written(TenantPost(), project);
        Expect.Contains(text, "PublicListAsync(ShopContext context, HttpContext http, int? pageNumber, int? pageSize)");
        Expect.Contains(text, "PublicBySlugAsync(ShopContext context, HttpContext http, string slug)");
        Expect.Contains(text, "app.MapGet(\"/api/posts/public/{slug}\", (ShopContext context, HttpContext http, string slug) => PublicBySlugAsync(context, http, slug)).AllowAnonymous();");
        Expect.Contains(text, "if (await TenantHost.FindAsync(context, http, http.RequestAborted) is not int tenant)");
        Expect.Contains(text, "context.Set<Post>().IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant).Where(x => x.Status == \"published\")");
        Expect.Contains(text, "return Results.NotFound();");
        Expect.Contains(text, "Results.Ok(Array.Empty<object>())");
        Expect.Contains(text, "int changed = await context.Set<Post>().Where(x => x.PostId == id)");
        Expect.DoesNotContain(Written(Post(), Project()), "TenantHost");
    }

    [TestMethod]
    public void The_public_routes_of_a_tenant_owned_soft_delete_table_leave_out_deleted_rows()
    {
        var table = TenantPost();
        table.Columns.Add(Sample.Column("IsDeleted", SqlDbType.Bit, ordinal: 6));
        var project = Project(("ApiAuth", "true"), ("TenantTable", "Tenant"), ("TenantHostColumn", "Slug"), ("SoftDeleteTables", "Post"));
        Expect.Contains(Written(table, project), ".Where(x => x.TenantId == tenant && !x.IsDeleted)");
    }

    [TestMethod]
    public void The_setting_is_on_a_tab_of_the_settings_screen_with_a_hint()
    {
        Assert.AreEqual("Table shapes", ProjectSettingGroups.TabOf("ContentTables"));
        Assert.IsFalse(string.IsNullOrWhiteSpace(ProjectSettingsHints.All["ContentTables"]));
    }

    [TestMethod]
    public void The_public_routes_answer_only_the_columns_listed_and_always_the_slug()
    {
        var project = Project(("ContentPublicColumns", "Post:Title+PublishedOn"));
        var content = ContentPlan.For(Post(), project)!;
        CollectionAssert.AreEqual(new[] { "Title", "PublishedOn", "Slug" }, content.PublicColumns!.Select(c => c.Name).ToArray());
        string text = Written(Post(), project);
        Expect.Contains(text, ".Take(size).Select(x => new { x.Title, x.PublishedOn, x.Slug }).ToListAsync()");
        Expect.Contains(text, ".Where(x => x.Slug == slug && x.Status == \"published\").Select(x => new { x.Title, x.PublishedOn, x.Slug }).FirstOrDefaultAsync()");
        Assert.IsNull(ContentPlan.For(Post(), Project())!.PublicColumns);
        Expect.DoesNotContain(Written(Post(), Project()), ".Take(size).Select(");
        Assert.Contains("unknown column Nothing", ContentPlan.Problem(Post(), Project(("ContentPublicColumns", "Post:Title+Nothing")))!);
        Assert.AreEqual("Table shapes", ProjectSettingGroups.TabOf("ContentPublicColumns"));
        Assert.IsFalse(string.IsNullOrWhiteSpace(ProjectSettingsHints.All["ContentPublicColumns"]));
    }

    [TestMethod]
    public void A_date_the_database_fills_may_stay_blank_in_a_desktop_form_of_a_new_row()
    {
        var created = Sample.Column("CreatedOn", SqlDbType.DateTime, defaultSql: "(getdate())", ordinal: 6);
        var plain = Sample.Column("Seen", SqlDbType.DateTime, ordinal: 7);
        Assert.IsTrue(DesktopFormCode.FillsBlankDate(created));
        Assert.IsFalse(DesktopFormCode.FillsBlankDate(plain));
        var table = Sample.Table("Post", [Sample.Column("PostId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), created, plain], []);
        var project = Project(("ContentTables", ""));
        var form = FormModel.For(table, project);
        Expect.Contains(DesktopFormCode.SaveLines(form, project, created), "if (_editing is null) entity.CreatedOn = DateTime.Now;");
        Expect.Contains(DesktopFormCode.SaveLines(form, project, plain), "Seen is required.");
        Expect.DoesNotContain(DesktopFormXaml.Field(form, project, table, created, false), "IsRequiredForForm");
        Expect.Contains(DesktopFormXaml.Field(form, project, table, plain, false), "IsRequiredForForm");
    }
}
