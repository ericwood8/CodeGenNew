using System.Text;

namespace CodeGenNew.Core;

/// <summary> The C# of the tenant sign-in and routes (<c>TenantTable</c>): the claim the sign-in adds to the token, <c>GET /api/auth/tenants</c> and <c>POST /api/auth/switch-tenant</c> for a person in several tenants, and
/// <c>POST &lt;tenants&gt;/onboard</c> for a platform administrator. The scope the context reads (<c>ITenantScope</c>) is written by <c>CS_DbContext</c>. The text uses <c>§</c> tokens so the code reads as code. </summary>
public static class TenantWriter
{
    public static string File(DatabaseModel database, ProjectSettings project, string contextType, string[] usings)
    {
        var tenantTable = TenantPlan.TenantTableOf(database, project)!;
        var tenantKey = TenantPlan.KeyOf(tenantTable)!;
        var membership = TenantPlan.MembershipOf(database, project);
        var (userPlan, _) = UserTable.Resolve(database, project);
        var userOwned = userPlan is null ? null : TenantPlan.OwnedBy(userPlan.Table, project);
        var active = tenantTable.IsActiveColumn;
        var text = tenantTable.Columns.FirstOrDefault(c => c.IsStringColumn && !c.IsPrimaryKey && (c.Name.EqualsIgnoreCase("Name") || c.Name.EqualsIgnoreCase("Title")));

        string activeJoin = active is null ? "" : $".Where(t => t.{active.CSharpName()})";
        string textExpression = text is null ? "t.{KEY}.ToString()".Replace("{KEY}", tenantKey.CSharpName()) : text.IsNullable ? $"t.{text.CSharpName()} ?? \"\"" : $"t.{text.CSharpName()}";

        var o = new StringBuilder();
        foreach (string u in usings)
            o.Append($"using {u};\n");
        o.Append("using System.Security.Claims;\nusing Microsoft.AspNetCore.Mvc;\nusing Microsoft.EntityFrameworkCore;\nusing Microsoft.IdentityModel.JsonWebTokens;\nusing Microsoft.IdentityModel.Tokens;\n\n");
        o.Append(Fill(Shared, ("§NOTMEMBER_TYPE", TenantWords.NotMemberType), ("§NOMEMBER_TEXT", TenantWords.NotMember), ("§ONBOARD_TYPE", TenantWords.OnboardingType), ("§ONLYPLATFORM", TenantWords.OnlyPlatform),
            ("§NOTENANT_TYPE", TenantWords.NoTenantType), ("§NOTENANT_TEXT", TenantWords.NoTenant)));

        string register = "";
        var routes = new StringBuilder();
        if (membership is not null && userPlan is not null)
        {
            string roleSelect = membership.Role is null ? "(string?)null" : $"m.{membership.Role.CSharpName()}";
            o.Append(Fill(Members, ("§CTX", contextType), ("§U", userPlan.Table.TableName), ("§UNAME", userPlan.UserName.CSharpName()), ("§UKEY", userPlan.Table.PrimaryKeyColumns[0].CSharpName()),
                ("§M", membership.Table.TableName), ("§MUSER", membership.User.CSharpName()), ("§MTENANT", membership.Tenant.CSharpName()), ("§MROLE", roleSelect),
                ("§TT", tenantTable.TableName), ("§TKEY", tenantKey.CSharpName()), ("§ACTIVEJOIN", activeJoin), ("§TEXT", textExpression)));
            register = "        services.AddScoped<ILoginClaims, TenantLoginClaims>();\n";
            o.Append(Fill(Switch, ("§CTX", contextType), ("§ROLEPARAM", membership.Role is null ? "null" : "member.Role")));
            routes.Append("        app.MapGet(\"/api/auth/tenants\", TenantSwitching.ListAsync);\n        app.MapPost(\"/api/auth/switch-tenant\", TenantSwitching.SwitchAsync);\n");
        }
        else if (userOwned is not null && userPlan is not null)
        {
            string activeTest = active is null ? "" : $" && context.Set<§TT>().IgnoreQueryFilters().Any(t => t.§TKEY == u.§UTENANT && t.{active.CSharpName()})";
            o.Append(Fill(OwnColumn, ("§CTX", contextType), ("§U", userPlan.Table.TableName), ("§UNAME", userPlan.UserName.CSharpName()), ("§UTENANT", userOwned.Column.CSharpName()),
                ("§ACTIVETEST", Fill(activeTest, ("§TT", tenantTable.TableName), ("§TKEY", tenantKey.CSharpName()), ("§UTENANT", userOwned.Column.CSharpName())))));
            register = "        services.AddScoped<ILoginClaims, TenantLoginClaims>();\n";
        }

        string onboardRoute = ApiRequests.Route(tenantTable);
        string adminLines = membership is null ? "" : Fill(OnboardAdmin, ("§M", membership.Table.TableName), ("§MUSER", membership.User.CSharpName()), ("§MTENANT", membership.Tenant.CSharpName()),
            ("§MROLEASSIGN", membership.Role is null ? "" : $", {membership.Role.CSharpName()} = AuthNames.AdminRole"), ("§TKEY", tenantKey.CSharpName()));
        o.Append(Fill(Onboard, ("§CTX", contextType), ("§TT", tenantTable.TableName), ("§TKEY", tenantKey.CSharpName()), ("§ADMINLINES", adminLines), ("§ADMINPARAM", membership is null ? "" : ", int? AdminId")));
        routes.Append($"        app.MapPost(\"{onboardRoute}/onboard\", TenantOnboarding.OnboardAsync);\n");

        o.Append(Fill(Support, ("§CTX", contextType), ("§TT", tenantTable.TableName), ("§TKEY", tenantKey.CSharpName()), ("§ACTIVEWHERE", activeJoin)));
        o.Append(Fill(Setup, ("§REGISTER", register)));
        o.Append("/// <summary> Maps the tenant routes. Generated by API_Tenants. </summary>\npublic static class TenantRoutes\n{\n    public static void Register(WebApplication app)\n    {\n" + routes + "    }\n}\n");
        return o.ToString();
    }

    private static string Fill(string text, params (string Token, string Value)[] values)
    {
        foreach (var (token, value) in values.OrderByDescending(v => v.Token.Length))
            text = text.Replace(token, value);
        return text;
    }

    private const string Shared = """
public static class TenantProblems
{
    public static IResult NotMember() => Results.Problem(statusCode: 403, type: "§NOTMEMBER_TYPE", title: "Not a member", detail: "§NOMEMBER_TEXT");
    public static IResult OnlyPlatform() => Results.Problem(statusCode: 403, type: "§ONBOARD_TYPE", title: "Platform administrators only", detail: "§ONLYPLATFORM");
    public static IResult NoTenant() => Results.Problem(statusCode: 403, type: "§NOTENANT_TYPE", title: "No tenant", detail: "§NOTENANT_TEXT");
}

""";

    private const string Members = """
/// <summary> A tenant a person belongs to, with the role they have in it (null: their own role stays). </summary>
public sealed record TenantMember(int TenantId, string Text, string? Role);

public static class TenantMemberships
{
    /// <summary> The active tenants the person belongs to, by tenant id. The query filters are ignored: at sign-in there is no tenant yet. </summary>
    public static async Task<List<TenantMember>> OfAsync(§CTX context, string userName, CancellationToken cancellationToken)
    {
        string name = userName.Trim();
        var person = await context.Set<§U>().IgnoreQueryFilters().AsNoTracking().Where(u => u.§UNAME == name).Select(u => (int?)u.§UKEY).FirstOrDefaultAsync(cancellationToken);
        if (person is null)
            return [];
        var rows = await context.Set<§M>().IgnoreQueryFilters().AsNoTracking().Where(m => m.§MUSER == person)
            .Join(context.Set<§TT>().IgnoreQueryFilters().AsNoTracking()§ACTIVEJOIN, m => m.§MTENANT, t => t.§TKEY, (m, t) => new { Tenant = t.§TKEY, Text = §TEXT, Role = §MROLE })
            .OrderBy(x => x.Tenant).ToListAsync(cancellationToken);
        return rows.Select(x => new TenantMember(x.Tenant, x.Text, x.Role)).ToList();
    }
}

/// <summary> Puts the tenant (and the role the person has in it) into the token at sign-in: the tenant the login asks for, else the person's first one. A person in no tenant (a platform administrator) signs in to none. </summary>
public class TenantLoginClaims(§CTX context) : ILoginClaims
{
    public async Task<LoginClaims> ForAsync(DirectoryUser user, LoginRequest request, CancellationToken cancellationToken = default)
    {
        var members = await TenantMemberships.OfAsync(context, user.UserName, cancellationToken);
        if (members.Count == 0)
            return request.TenantId is null ? new LoginClaims() : new LoginClaims(Refusal: TenantProblems.NotMember());
        var chosen = request.TenantId is { } wanted ? members.FirstOrDefault(m => m.TenantId == wanted) : members[0];
        if (chosen is null)
            return new LoginClaims(Refusal: TenantProblems.NotMember());
        return new LoginClaims([new Claim(ClaimsTenantScope.ClaimName, chosen.TenantId.ToString())], chosen.Role, chosen.TenantId);
    }
}

""";

    private const string OwnColumn = """
/// <summary> Puts the tenant of the person (the tenant column of their own row) into the token at sign-in; a person of an inactive tenant cannot sign in. </summary>
public class TenantLoginClaims(§CTX context) : ILoginClaims
{
    public async Task<LoginClaims> ForAsync(DirectoryUser user, LoginRequest request, CancellationToken cancellationToken = default)
    {
        string name = user.UserName.Trim();
        var found = await context.Set<§U>().IgnoreQueryFilters().AsNoTracking().Where(u => u.§UNAME == name)
            .Select(u => new { Tenant = (int?)u.§UTENANT, Usable = true§ACTIVETEST })
            .FirstOrDefaultAsync(cancellationToken);
        if (found?.Tenant is not int tenant)
            return new LoginClaims();
        return found.Usable ? new LoginClaims([new Claim(ClaimsTenantScope.ClaimName, tenant.ToString())], null, tenant) : new LoginClaims(Refusal: TenantProblems.NotMember());
    }
}

""";

    private const string Switch = """
public sealed record SwitchTenantRequest(int? TenantId);

/// <summary> One tenant of the caller, for the switcher: the id, the name to show, the role in it and whether the token is working in it now. </summary>
public sealed record TenantChoice(int Id, string Text, string? Role, bool Current);

public static class TenantSwitching
{
    public static async Task<IResult> ListAsync(§CTX context, HttpContext http, ITenantScope scope)
    {
        var members = await TenantMemberships.OfAsync(context, http.User.Identity?.Name ?? "", http.RequestAborted);
        return Results.Ok(members.Select(m => new TenantChoice(m.TenantId, m.Text, m.Role, m.TenantId == scope.TenantId)).ToList());
    }

    /// <summary> A new token for the same person, working in another tenant they belong to. </summary>
    public static async Task<IResult> SwitchAsync([FromBody] SwitchTenantRequest request, §CTX context, HttpContext http, JwtOptions jwt)
    {
        if (request.TenantId is not int wanted)
            return TenantProblems.NotMember();
        var members = await TenantMemberships.OfAsync(context, http.User.Identity?.Name ?? "", http.RequestAborted);
        var member = members.FirstOrDefault(m => m.TenantId == wanted);
        if (member is null)
            return TenantProblems.NotMember();

        string? role = §ROLEPARAM ?? http.User.FindFirstValue(AuthNames.RoleClaim);
        var claims = http.User.Claims.Where(c => c.Type != ClaimsTenantScope.ClaimName && c.Type != AuthNames.RoleClaim && c.Type is not ("exp" or "iat" or "nbf" or "iss" or "aud")).ToList();
        claims.Add(new Claim(ClaimsTenantScope.ClaimName, wanted.ToString()));
        if (!string.IsNullOrEmpty(role))
            claims.Add(new Claim(AuthNames.RoleClaim, role));
        DateTime expires = DateTime.UtcNow.AddMinutes(jwt.ExpiresMinutes);
        string token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Expires = expires,
            SigningCredentials = new SigningCredentials(AuthSetup.SigningKey(jwt), SecurityAlgorithms.HmacSha256)
        });
        int? owner = int.TryParse(http.User.FindFirstValue(AuthNames.OwnerClaim), out int ownerId) ? ownerId : null;
        return Results.Ok(new LoginResponse(token, expires, new AuthUser(http.User.Identity?.Name ?? "", role, owner, wanted)));
    }
}

""";

    private const string Onboard = """
/// <summary> The body of POST onboard: the tenant row and, when the project has a membership table, the person who becomes its first administrator. </summary>
public sealed record OnboardRequest(§TT? Tenant§ADMINPARAM);

public static class TenantOnboarding
{
    /// <summary> Creates a tenant. Only a platform administrator may: the administrator role, in no tenant. The tenant and its first administrator are saved together. </summary>
    public static async Task<IResult> OnboardAsync([FromBody] OnboardRequest request, §CTX context, HttpContext http, ITenantScope scope)
    {
        if (!http.User.IsInRole(AuthNames.AdminRole) || scope.TenantId is not null)
            return TenantProblems.OnlyPlatform();
        if (request.Tenant is not { } tenant)
            return Results.Problem(statusCode: 400, title: "A tenant is required", detail: "The body needs a tenant.");
        tenant.§TKEY = default;
        await using var transaction = await context.Database.BeginTransactionAsync(http.RequestAborted);
        try
        {
            context.Add(tenant);
            await context.SaveChangesAsync(http.RequestAborted);
§ADMINLINES            await transaction.CommitAsync(http.RequestAborted);
        }
        catch (DbUpdateException)
        {
            return Results.Problem(statusCode: 400, title: "Bad request", detail: "The tenant could not be saved (a value is missing, a name is taken or the person is unknown).");
        }
        return Results.Created($"{http.Request.Path.Value!.Replace("/onboard", "")}/{tenant.§TKEY}", tenant);
    }
}

""";

    private const string OnboardAdmin = """
            if (request.AdminId is { } admin)
            {
                context.Add(new §M { §MUSER = admin, §MTENANT = tenant.§TKEY§MROLEASSIGN });
                await context.SaveChangesAsync(http.RequestAborted);
            }

""";

    private const string Support = """
/// <summary> Used by the generated tests: the first active tenants of the database (they work in the first and use the second to prove one tenant cannot reach another's rows), and the removal of a tenant they onboarded. </summary>
public static class TenantTestSupport
{
    public static async Task<List<int>> FirstTenantsAsync(IServiceProvider services, int take)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<§CTX>();
        return await context.Set<§TT>().IgnoreQueryFilters().AsNoTracking()§ACTIVEWHERE.OrderBy(t => t.§TKEY).Select(t => t.§TKEY).Take(take).ToListAsync();
    }

    public static async Task RemoveAsync(IServiceProvider services, int tenantId)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<§CTX>();
        await context.Set<§TT>().IgnoreQueryFilters().Where(t => t.§TKEY == tenantId).ExecuteDeleteAsync();
    }
}

""";

    private const string Setup = """
public static class TenantSetup
{
    /// <summary> Registers what the sign-in needs to put the tenant into the token. The scope itself (ITenantScope) is registered with the context. </summary>
    public static IServiceCollection AddTenants(this IServiceCollection services)
    {
§REGISTER        return services;
    }
}

""";
}
