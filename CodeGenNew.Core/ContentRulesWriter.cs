namespace CodeGenNew.Core;

/// <summary> The C# of the rules of a content table (<c>ContentTables</c>) that every app writing to the database needs: the slug made from the title, kept when the title changes and numbered when it is taken, and the status and
/// published time that only the routes change. The ASP.NET API (<c>API_RowShapes</c>) and the desktop app that reads the database itself (<c>WinUI3_Content</c>) both write this text, so a row saved by either follows the same rules. </summary>
public static class ContentRulesWriter
{
    /// <summary> The members <c>MakeSlug</c> and <c>PrepareAsync</c> of the table's static class (four spaces in). <c>PrepareAsync</c> answers a sentence when the slug the caller named is taken, else null. </summary>
    public static string Members(ContentShape content, string contextType)
    {
        string published = content.PublishedOn?.CSharpName() ?? "";
        string storedSelect = content.PublishedOn is null ? "" : $", x.{published}";
        string restorePublished = content.PublishedOn is null ? "" : $"        row.{published} = isNew ? null : stored!.{published};\n";
        string stamp = content.PublishedOn is null ? "" : $"\n                .SetProperty(x => x.{published}, x => x.{published} ?? now)";
        string now = content.PublishedOn is null ? "" : $"        var now = {content.PublishedOn.CSharpBase()}.Now;\n";
        return TemplateText.FillAll(Text + (content.NeedsApproval ? Approval : ""),
            ("§CTX", contextType), ("§T", content.Table.TableName), ("§KT", content.Key.CSharpBase()), ("§K", content.Key.CSharpName()), ("§TITLE", content.Title.CSharpName()), ("§SLUG", content.Slug.CSharpName()),
            ("§STATUS", content.Status.CSharpName()), ("§SLUGMAX", content.SlugLength.ToString()), ("§STOREDSELECT", storedSelect), ("§RESTOREPUBLISHED", restorePublished),
            ("§STAMPPUBLISHED", stamp), ("§NOWLINE", now), ("§PENDING", ContentWords.Pending), ("§PUBLISHED", ContentWords.Published),
            ("§DRAFT", ContentWords.Draft), ("§FALLBACK", ContentWords.FallbackSlug), ("§W_TAKEN", ContentWords.SlugTaken));
    }

    /// <summary> The members of a content table that is also in <c>RowApprovals</c>: the three changes of state the approval routes make in the cycle's own transaction. Asking for approval moves a row that is not
    /// published to <c>pending</c>; an approval publishes the row that is pending (and stamps the time the first time); a rejection or a withdrawal puts a pending row back to a draft. A row that was
    /// unpublished, archived or published meanwhile is left as it is, so a late decision changes nothing. </summary>
    private const string Approval = """
    /// <summary> A request for approval: a row that is not published waits as pending. </summary>
    public static Task<int> MarkPendingAsync(§CTX context, §KT id) =>
        context.Set<§T>().Where(x => x.§K == id && x.§STATUS != "§PUBLISHED")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.§STATUS, "§PENDING"));

    /// <summary> An approval: the pending row is published (the first time with its published time). </summary>
    public static async Task<int> PublishPendingAsync(§CTX context, §KT id)
    {
§NOWLINE        return await context.Set<§T>().Where(x => x.§K == id && x.§STATUS == "§PENDING")
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.§STATUS, "§PUBLISHED")§STAMPPUBLISHED);
    }

    /// <summary> A rejection or a withdrawal: the pending row is a draft again. </summary>
    public static Task<int> RevertPendingAsync(§CTX context, §KT id) =>
        context.Set<§T>().Where(x => x.§K == id && x.§STATUS == "§PENDING")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.§STATUS, "§DRAFT"));

""";

    private const string Text = """
    /// <summary> A slug is lower case ASCII letters and digits with single hyphens, at most maxLength characters; text with nothing to make one from gives "§FALLBACK". </summary>
    public static string MakeSlug(string? text, int maxLength)
    {
        var slug = new System.Text.StringBuilder();
        bool hyphen = false;
        foreach (char c in (text ?? "").Normalize(System.Text.NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                slug.Append(char.ToLowerInvariant(c));
                hyphen = false;
            }
            else if (!hyphen && slug.Length > 0)
            {
                slug.Append('-');
                hyphen = true;
            }
        }
        string made = slug.ToString().Trim('-');
        if (made.Length > maxLength)
            made = made[..maxLength].Trim('-');
        return made.Length == 0 ? "§FALLBACK" : made;
    }

    /// <summary> The status belongs to the routes: a new row is a draft and a save puts back the stored status. The slug is made from the title when it is blank on create, kept when it is blank or unchanged on update
    /// (so a new title does not move the page), numbered (-2, -3) when a made slug is taken and refused (the sentence is the answer) when a slug the caller named is taken. </summary>
    public static async Task<string?> PrepareAsync(§CTX context, §T row, bool isNew)
    {
        var stored = isNew ? null : await context.Set<§T>().AsNoTracking().Where(x => x.§K == row.§K).Select(x => new { x.§SLUG, x.§STATUS§STOREDSELECT }).FirstOrDefaultAsync();
        if (!isNew && stored is null)
            return null;
        row.§STATUS = isNew ? "§DRAFT" : stored!.§STATUS;
§RESTOREPUBLISHED        string? named = string.IsNullOrWhiteSpace(row.§SLUG) ? null : MakeSlug(row.§SLUG, §SLUGMAX);
        if (!isNew && (named is null || named == stored!.§SLUG))
        {
            row.§SLUG = stored!.§SLUG;
            return null;
        }
        string wanted = named ?? MakeSlug(row.§TITLE, §SLUGMAX);
        string candidate = wanted;
        for (int number = 2; await context.Set<§T>().AsNoTracking().AnyAsync(x => x.§SLUG == candidate && x.§K != row.§K); number++)
        {
            if (named is not null)
                return "§W_TAKEN";
            string suffix = "-" + number;
            candidate = (wanted.Length + suffix.Length > §SLUGMAX ? wanted[..(§SLUGMAX - suffix.Length)].TrimEnd('-') : wanted) + suffix;
        }
        row.§SLUG = candidate;
        return null;
    }

""";
}
