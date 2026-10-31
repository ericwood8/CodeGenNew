namespace CodeGenNew.Core;

/// <summary> The members routes of the ASP.NET API (<c>GET</c>, <c>POST</c>, <c>PUT</c> and <c>DELETE</c> under <c>/api/auth/members</c>): who works in the caller's tenant and in which role. Only an administrator
/// who works in a tenant may use them. They read and write the membership and user tables with the query filters off and always name the caller's tenant by hand. The same routes and answers as the Python and Rust APIs
/// (<see cref="PeoplePlan"/>). </summary>
public static class AspNetPeopleWriter
{
    /// <summary> The records the routes take and answer. </summary>
    public const string Records = """
/// <summary> One member of the caller's tenant: the person's key, sign-in name, the text that names them and their role in the tenant. </summary>
public record MemberAnswer(int PersonId, string Login, string Person, string? Role);

/// <summary> A person who is not yet a member of the caller's tenant. </summary>
public record MemberCandidate(int Id, string Name);

/// <summary> The body of POST and PUT /api/auth/members: the person to add and the role they have in the tenant (empty: their own role counts). </summary>
public record MemberBody(int? PersonId, string? Role);
""";

    public static string Mappings(PeoplePlan plan) => Fill(MappingText, ("§ROLEMAP", plan.Membership!.Role is null ? "" : RoleMapping));

    public static string Handlers(PeoplePlan plan, string contextType)
    {
        var membership = plan.Membership!;
        string name = plan.NameColumn is null ? "(string?)null" : $"p.{plan.NameColumn.CSharpName()}";
        string nameNullable = plan.NameColumn is null ? "(string?)null" : $"p.{plan.NameColumn.CSharpName()}";
        string roleProperty = membership.Role is null ? "" : membership.Role.CSharpName();
        string text = Fill(HandlersText, ("§CTX", contextType), ("§U", plan.User.Table.TableName), ("§UKEY", plan.Key.CSharpName()), ("§ULOGIN", plan.User.UserName.CSharpName()), ("§UNAME", nameNullable),
            ("§M", membership.Table.TableName), ("§MUSER", membership.User.CSharpName()), ("§MTENANT", membership.Tenant.CSharpName()),
            ("§MROLEVALUE", membership.Role is null ? "(string?)null" : $"m.{roleProperty}"), ("§MROLEASSIGN", membership.Role is null ? "" : $", {roleProperty} = (body.Role ?? \"\").Trim() is {{ Length: > 0 }} role ? role : null"),
            ("§ROLEHANDLER", membership.Role is null ? "" : Fill(RoleHandler, ("§CTX", contextType), ("§M", membership.Table.TableName), ("§MUSER", membership.User.CSharpName()), ("§MTENANT", membership.Tenant.CSharpName()), ("§MROLE", roleProperty))),
            ("§FORBIDDEN", HttpStatus.Forbidden.ToString()), ("§CONFLICT", HttpStatus.Conflict.ToString()),
            ("§ADMINS_TYPE", ProblemTypes.RoleNotAllowed), ("§ADMINS_TITLE", PeopleWords.MembersAdminsOnlyTitle), ("§ADMINS_DETAIL", PeopleWords.MembersAdminsOnlyDetail),
            ("§NOTENANT_TYPE", TenantWords.NoTenantType), ("§NOTENANT_TEXT", TenantWords.NoTenant),
            ("§EXISTS_TYPE", ProblemTypes.MemberExists), ("§EXISTS_TITLE", PeopleWords.MemberExistsTitle), ("§EXISTS_DETAIL", PeopleWords.MemberExistsDetail),
            ("§SELF_TYPE", ProblemTypes.MemberSelf), ("§SELF_TITLE", PeopleWords.MemberSelfTitle), ("§SELF_DETAIL", PeopleWords.MemberSelfDetail));
        return text.Replace("§NAMEVALUE", name);
    }

    private static string Fill(string text, params (string Token, string Value)[] values)
    {
        foreach (var (token, value) in values.OrderByDescending(v => v.Token.Length))
            text = text.Replace(token, value);
        return text;
    }

    private const string MappingText = """
        app.MapGet("/api/auth/members", ListMembersAsync)
        .WithName("ListMembers")
        .Produces<List<MemberAnswer>>()
        .ProducesProblem(403);

        app.MapGet("/api/auth/members/candidates", ListCandidatesAsync)
        .WithName("ListMemberCandidates")
        .Produces<List<MemberCandidate>>()
        .ProducesProblem(403);

        app.MapPost("/api/auth/members", AddMemberAsync)
        .WithName("AddMember")
        .Produces(204)
        .ProducesProblem(403)
        .ProducesProblem(404)
        .ProducesProblem(409);
§ROLEMAP
        app.MapDelete("/api/auth/members/{personId:int}", RemoveMemberAsync)
        .WithName("RemoveMember")
        .Produces(204)
        .ProducesProblem(403)
        .ProducesProblem(404);

""";

    private const string RoleMapping = """

        app.MapPut("/api/auth/members/{personId:int}", ChangeMemberRoleAsync)
        .WithName("ChangeMemberRole")
        .Produces(204)
        .ProducesProblem(403)
        .ProducesProblem(404);
""";

    private const string HandlersText = """

    private static string PersonText(string? name, string? login) => string.IsNullOrEmpty(name) ? login ?? "" : $"{name} ({login})";

    // only an administrator who works in a tenant manages its members
    private static IResult? MembersRefusal(ClaimsPrincipal user, ITenantScope scope)
    {
        if (!user.IsInRole(AuthNames.AdminRole))
            return Results.Problem(statusCode: §FORBIDDEN, type: "§ADMINS_TYPE", title: "§ADMINS_TITLE", detail: "§ADMINS_DETAIL");
        if (scope.TenantId is null)
            return Results.Problem(statusCode: §FORBIDDEN, type: "§NOTENANT_TYPE", title: "No tenant", detail: "§NOTENANT_TEXT");
        return null;
    }

    private static async Task<IResult> ListMembersAsync(ClaimsPrincipal user, [FromServices] ITenantScope scope, [FromServices] §CTX context, HttpContext http)
    {
        if (MembersRefusal(user, scope) is { } refusal)
            return refusal;
        int tenant = scope.TenantId!.Value;
        var rows = await context.Set<§M>().IgnoreQueryFilters().AsNoTracking().Where(m => m.§MTENANT == tenant)
            .Join(context.Set<§U>().IgnoreQueryFilters().AsNoTracking(), m => m.§MUSER, p => p.§UKEY, (m, p) => new { Id = p.§UKEY, Login = p.§ULOGIN, Name = §NAMEVALUE, Role = §MROLEVALUE })
            .ToListAsync(http.RequestAborted);
        return Results.Ok(rows.Select(r => new MemberAnswer(r.Id, r.Login ?? "", PersonText(r.Name, r.Login), r.Role))
            .OrderBy(m => m.Person, StringComparer.CurrentCultureIgnoreCase).ToList());
    }

    private static async Task<IResult> ListCandidatesAsync(ClaimsPrincipal user, [FromServices] ITenantScope scope, [FromServices] §CTX context, HttpContext http)
    {
        if (MembersRefusal(user, scope) is { } refusal)
            return refusal;
        int tenant = scope.TenantId!.Value;
        var inside = await context.Set<§M>().IgnoreQueryFilters().AsNoTracking().Where(m => m.§MTENANT == tenant).Select(m => m.§MUSER).ToListAsync(http.RequestAborted);
        var people = await context.Set<§U>().IgnoreQueryFilters().AsNoTracking().Where(p => !inside.Contains(p.§UKEY))
            .Select(p => new { Id = p.§UKEY, Login = p.§ULOGIN, Name = §NAMEVALUE }).ToListAsync(http.RequestAborted);
        return Results.Ok(people.Select(p => new MemberCandidate(p.Id, PersonText(p.Name, p.Login))).OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
    }

    private static async Task<IResult> AddMemberAsync([FromBody] MemberBody body, ClaimsPrincipal user, [FromServices] ITenantScope scope, [FromServices] §CTX context, HttpContext http)
    {
        if (MembersRefusal(user, scope) is { } refusal)
            return refusal;
        int tenant = scope.TenantId!.Value;
        if (body.PersonId is not int personId || !await context.Set<§U>().IgnoreQueryFilters().AnyAsync(p => p.§UKEY == personId, http.RequestAborted))
            return Results.NotFound();
        if (await context.Set<§M>().IgnoreQueryFilters().AnyAsync(m => m.§MUSER == personId && m.§MTENANT == tenant, http.RequestAborted))
            return Results.Problem(statusCode: §CONFLICT, type: "§EXISTS_TYPE", title: "§EXISTS_TITLE", detail: "§EXISTS_DETAIL");
        context.Set<§M>().Add(new §M { §MUSER = personId, §MTENANT = tenant§MROLEASSIGN });
        await context.SaveChangesAsync(http.RequestAborted);
        return Results.NoContent();
    }
§ROLEHANDLER
    private static async Task<IResult> RemoveMemberAsync(int personId, ClaimsPrincipal user, [FromServices] ITenantScope scope, [FromServices] §CTX context, HttpContext http)
    {
        if (MembersRefusal(user, scope) is { } refusal)
            return refusal;
        int tenant = scope.TenantId!.Value;
        string? login = await context.Set<§U>().IgnoreQueryFilters().AsNoTracking().Where(p => p.§UKEY == personId).Select(p => p.§ULOGIN).FirstOrDefaultAsync(http.RequestAborted);
        if (login is not null && string.Equals(login, user.FindFirstValue(AuthNames.NameClaim), StringComparison.OrdinalIgnoreCase))
            return Results.Problem(statusCode: §FORBIDDEN, type: "§SELF_TYPE", title: "§SELF_TITLE", detail: "§SELF_DETAIL");
        var membership = await context.Set<§M>().IgnoreQueryFilters().FirstOrDefaultAsync(m => m.§MUSER == personId && m.§MTENANT == tenant, http.RequestAborted);
        if (membership is null)
            return Results.NotFound();
        context.Set<§M>().Remove(membership);
        await context.SaveChangesAsync(http.RequestAborted);
        return Results.NoContent();
    }
""";

    private const string RoleHandler = """

    private static async Task<IResult> ChangeMemberRoleAsync(int personId, [FromBody] MemberBody body, ClaimsPrincipal user, [FromServices] ITenantScope scope, [FromServices] §CTX context, HttpContext http)
    {
        if (MembersRefusal(user, scope) is { } refusal)
            return refusal;
        int tenant = scope.TenantId!.Value;
        var membership = await context.Set<§M>().IgnoreQueryFilters().FirstOrDefaultAsync(m => m.§MUSER == personId && m.§MTENANT == tenant, http.RequestAborted);
        if (membership is null)
            return Results.NotFound();
        membership.§MROLE = (body.Role ?? "").Trim() is { Length: > 0 } role ? role : null;
        await context.SaveChangesAsync(http.RequestAborted);
        return Results.NoContent();
    }
""";
}
