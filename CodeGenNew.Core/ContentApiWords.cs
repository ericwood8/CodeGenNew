namespace CodeGenNew.Core;

/// <summary> What the Python and Rust APIs say and size the same way for a content table (<c>ContentTables</c>), next to the states and the slug sentence of <see cref="ContentWords"/>. </summary>
public static class ContentApiWords
{
    public const string SlugTakenTitle = "Slug taken";

    /// <summary> The public list answers this many rows a page unless asked for another size, and never more than the maximum. </summary>
    public const int PublicPageSize = 20;
    public const int PublicMaxPageSize = 100;

    /// <summary> Why the Python and Rust APIs do not write a content table yet, or null when they do. </summary>
    public static string? NotBuilt(ContentShape content)
    {
        if (content.TenantColumn is not null)
            return $"ContentTables: {content.Table.TableName} belongs to a tenant, which only the ASP.NET API serves; the Python and Rust APIs do not write a tenant-owned content table yet.";
        if (content.NeedsApproval)
            return $"ContentTables: {content.Table.TableName} is also in RowApprovals, which only the ASP.NET API serves for a content table; the Python and Rust APIs do not write it yet.";
        return null;
    }
}
