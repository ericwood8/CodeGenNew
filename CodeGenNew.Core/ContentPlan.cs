namespace CodeGenNew.Core;

/// <summary> The words of a content table (posts, pages, articles): the three states, the sentence of a slug that is taken and the problem type, the same in every stack that writes the routes. </summary>
public static class ContentWords
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Archived = "archived";

    /// <summary> The state of a row whose publication waits for a second person (the table is also in <c>RowApprovals</c>). </summary>
    public const string Pending = "pending";

    public const string SlugTakenType = ProblemTypes.Prefix + "content-slug-taken";
    public const string SlugTaken = "Another row already uses that slug.";

    /// <summary> What the slug is when the title leaves nothing to make one from (a title of punctuation only). </summary>
    public const string FallbackSlug = "untitled";

    /// <summary> The longest state is <c>published</c>; a status column shorter than that cannot hold it. </summary>
    public static int LongestStatus => new[] { Draft, Published, Archived, Pending }.Max(s => s.Length);

    /// <summary> The room a slug gets when its column has no length limit. </summary>
    public const int UnlimitedSlugLength = 200;
}

/// <summary> A table the project lists in <c>ContentTables</c>: <c>Title</c> is the text the slug is made from, <c>Slug</c> the column that holds it, <c>Status</c> the text column that holds draft, published or archived,
/// and <c>PublishedOn</c> (optional) the stamp set the first time the row is published. </summary>
public sealed record ContentShape(TableModel Table, ColumnModel Key, ColumnModel Title, ColumnModel Slug, ColumnModel Status, ColumnModel? PublishedOn)
{
    /// <summary> The columns the public routes answer (<c>ContentPublicColumns</c>), or null for every column. </summary>
    public IReadOnlyList<ColumnModel>? PublicColumns { get; init; }

    /// <summary> Whether the table is also in <c>RowApprovals</c>: publishing then asks for a second person's approval, and the row waits in the state <c>pending</c>. </summary>
    public bool NeedsApproval { get; init; }

    /// <summary> The tenant column of a tenant-owned table: the public routes read the rows of the tenant the request host names, and a slug is unique within one tenant. </summary>
    public ColumnModel? TenantColumn { get; init; }

    /// <summary> How many characters the slug column holds. </summary>
    public int SlugLength => Slug.CharacterLength > 0 ? Slug.CharacterLength : ContentWords.UnlimitedSlugLength;
}

/// <summary> <c>ContentTables=Post</c> or <c>Post:Title:Slug:Status:PublishedOn</c> (the table, then the title, slug, status and published-on columns; each defaults to the column of that name): posts, pages and articles with a state
/// and a URL slug. The server makes the slug from the title on create (lower case, ASCII, hyphens, a number added when it is taken), keeps it when the title changes, and owns the state: a create is a draft and a save never
/// changes the status. <c>POST {id}/publish</c>, <c>unpublish</c> and <c>archive</c> change it, <c>GET public</c> and <c>GET public/{slug}</c> answer the published rows without a sign-in. A tenant-owned table needs
/// <c>TenantHostColumn</c>: the public routes answer the rows of the tenant the request host names (none on a host that names no tenant), and everything else stays in the caller's tenant. </summary>
public static class ContentPlan
{
    /// <summary> The smallest slug column that holds a short slug and the number added to a repeated one. </summary>
    public const int MinSlugLength = 14;

    public static IReadOnlyList<(string Table, string? Title, string? Slug, string? Status, string? PublishedOn)> Entries(ProjectSettings project) =>
        SettingParts.Items(project.ContentTables).Select(p => (p[0], SettingParts.At(p, 1), SettingParts.At(p, 2), SettingParts.At(p, 3), SettingParts.At(p, 4))).ToList();

    /// <summary> The content tables that are also in <c>RowApprovals</c>, by name: publishing them asks for a second person's approval. </summary>
    public static IReadOnlyList<string> ApprovalTables(ProjectSettings project) =>
        Entries(project).Select(e => e.Table).Where(t => project.RowApprovals.Any(a => a.EqualsIgnoreCase(t))).ToList();

    public static bool IsListed(string table, ProjectSettings project) => Entries(project).Any(e => e.Table.EqualsIgnoreCase(table));

    public static ContentShape? For(TableModel table, ProjectSettings project) => IsListed(table.TableName, project) && Resolve(table, project, out _) is { } content ? content : null;

    /// <summary> Why a listed table cannot be a content table, or null when it can (or is not listed). </summary>
    public static string? Problem(TableModel table, ProjectSettings project)
    {
        if (!IsListed(table.TableName, project))
            return null;
        Resolve(table, project, out string? problem);
        return problem;
    }

    private static ContentShape? Resolve(TableModel table, ProjectSettings project, out string? problem)
    {
        problem = null;
        var entry = Entries(project).First(e => e.Table.EqualsIgnoreCase(table.TableName));
        string prefix = $"ContentTables: {table.TableName}";
        if (RowShapeWords.NeverChanged(table, project) is { } never)
        {
            problem = $"{prefix} {never}, so it has no rows to publish.";
            return null;
        }
        if (table.PrimaryKeyColumns is not [{ IsIntegerColumn: true } key])
        {
            problem = $"{prefix} needs a single whole-number key.";
            return null;
        }
        var owned = TenantPlan.OwnedBy(table, project);
        if (owned is not null && project.TenantHostColumn is null)
        {
            problem = $"{prefix} belongs to a tenant: the public routes have no signed-in tenant, so they read the tenant of the request host. Set TenantHostColumn (a text column of {owned.TenantTable} whose value is the first label of the host name) or list the table in NoTenantTables.";
            return null;
        }
        var title = Named(table, entry.Title, "Title", "Name", "Headline");
        var slug = Named(table, entry.Slug, "Slug");
        var status = Named(table, entry.Status, "Status");
        if (title is not { IsStringColumn: true } || title.IsPrimaryKey)
        {
            problem = $"{prefix} needs a text column to make the slug from ({ContentSyntax(table)}; the default is Title, Name or Headline).";
            return null;
        }
        if (slug is not { IsStringColumn: true, IsNullable: false } || slug.Name.EqualsIgnoreCase(title.Name))
        {
            problem = $"{prefix} needs a text column Slug that cannot be NULL ({ContentSyntax(table)}).";
            return null;
        }
        if (slug.CharacterLength is > 0 and < MinSlugLength)
        {
            problem = $"{prefix}: {slug.Name} holds {slug.CharacterLength} characters; a slug needs room for at least {MinSlugLength}.";
            return null;
        }
        if (status is not { IsStringColumn: true, IsNullable: false } || status.Name.EqualsIgnoreCase(slug.Name))
        {
            problem = $"{prefix} needs a text column Status that cannot be NULL ({ContentSyntax(table)}).";
            return null;
        }
        if (status.CharacterLength > 0 && status.CharacterLength < ContentWords.LongestStatus)
        {
            problem = $"{prefix}: {status.Name} holds {status.CharacterLength} characters, but {ContentWords.Published} has {ContentWords.Published.Length}.";
            return null;
        }
        ColumnModel? published = null;
        if (entry.PublishedOn is not null || table.Columns.Any(c => c.Name.EqualsIgnoreCase("PublishedOn") || c.Name.EqualsIgnoreCase("PublishedAt")))
        {
            published = Named(table, entry.PublishedOn, "PublishedOn", "PublishedAt");
            if (published is not { IsNullable: true } || !LogTablePlan.HasTimeOfDay(published))
            {
                problem = $"{prefix}: {entry.PublishedOn ?? "PublishedOn"} must be a date and time column that allows NULL (a draft has not been published).";
                return null;
            }
        }
        IReadOnlyList<ColumnModel>? publicColumns = null;
        if (SettingParts.Items(project.ContentPublicColumns).FirstOrDefault(p => p[0].EqualsIgnoreCase(table.TableName)) is { } listed)
        {
            var names = (SettingParts.At(listed, 1) ?? "").Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var found = names.Select(n => table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(n))).ToList();
            if (names.Length == 0 || found.Any(c => c is null))
            {
                string unknown = names.Length == 0 ? "no columns" : "the unknown column " + names[found.FindIndex(c => c is null)];
                problem = $"ContentPublicColumns: {table.TableName} names {unknown} (ContentPublicColumns={table.TableName}:Title+Slug+Body).";
                return null;
            }
            publicColumns = found.OfType<ColumnModel>().Append(slug).Distinct().ToList();   // a reader finds a row by its slug, so it is always answered
        }
        return new ContentShape(table, key, title, slug, status, published) { PublicColumns = publicColumns, TenantColumn = owned?.Column, NeedsApproval = project.RowApprovals.Any(t => t.EqualsIgnoreCase(table.TableName)) };
    }

    /// <summary> The sample value of the slug (a slug the rules leave as it is) or the status (<c>draft</c>, what a new row is) of a content table, by the table's name, or null for any other column. </summary>
    public static string? SampleText(string table, ColumnModel column, ProjectSettings project)
    {
        var found = Entries(project).Where(e => e.Table.EqualsIgnoreCase(table)).ToList();
        if (found.Count == 0)
            return null;
        var entry = found[0];
        if (column.Name.EqualsIgnoreCase(entry.Slug ?? "Slug"))
            return "sample-slug";
        return column.Name.EqualsIgnoreCase(entry.Status ?? "Status") ? ContentWords.Draft : null;
    }

    private static string ContentSyntax(TableModel table) => $"ContentTables={table.TableName}:TitleColumn:SlugColumn:StatusColumn:PublishedOnColumn";

    private static ColumnModel? Named(TableModel table, string? given, params string[] defaults)
    {
        if (given is not null)
            return table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(given));
        foreach (string name in defaults)
            if (table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(name)) is { } column)
                return column;
        return null;
    }
}
