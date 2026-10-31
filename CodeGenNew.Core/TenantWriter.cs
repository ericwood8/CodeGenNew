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
        var host = TenantPlan.HostColumnOf(database, project);
        string hostParam = host is null ? "" : ", Microsoft.AspNetCore.Http.IHttpContextAccessor http";
        string activeJoin = active is null ? "" : $".Where(t => t.{active.CSharpName()})";
        string textExpression = TenantPlan.TextExpression(tenantTable, tenantKey);

        var o = new StringBuilder();
        foreach (string u in usings)
            o.Append($"using {u};\n");
        o.Append("using System.Security.Claims;\nusing Microsoft.AspNetCore.Mvc;\nusing Microsoft.EntityFrameworkCore;\nusing Microsoft.IdentityModel.JsonWebTokens;\nusing Microsoft.IdentityModel.Tokens;\n\n");
        o.Append(Fill(Shared, ("§NOTMEMBER_TYPE", TenantWords.NotMemberType), ("§NOMEMBER_TEXT", TenantWords.NotMember), ("§ONBOARD_TYPE", TenantWords.OnboardingType), ("§ONLYPLATFORM", TenantWords.OnlyPlatform),
            ("§NOTENANT_TYPE", TenantWords.NoTenantType), ("§NOTENANT_TEXT", TenantWords.NoTenant),
            ("§HOSTPROBLEM", host is null ? "" : Fill(HostProblem, ("§HOST_TYPE", TenantWords.HostType), ("§HOST_TEXT", TenantWords.Host)))));

        string register = "";
        var routes = new StringBuilder();
        if (membership is not null && userPlan is not null)
        {
            string roleSelect = membership.Role is null ? "(string?)null" : $"m.{membership.Role.CSharpName()}";
            o.Append(Fill(Members, ("§CTX", contextType), ("§U", userPlan.Table.TableName), ("§UNAME", userPlan.UserName.CSharpName()), ("§UKEY", userPlan.Table.PrimaryKeyColumns[0].CSharpName()),
                ("§M", membership.Table.TableName), ("§MUSER", membership.User.CSharpName()), ("§MTENANT", membership.Tenant.CSharpName()), ("§MROLE", roleSelect),
                ("§TT", tenantTable.TableName), ("§TKEY", tenantKey.CSharpName()), ("§ACTIVEJOIN", activeJoin), ("§TEXT", textExpression), ("§HOSTPARAM", hostParam),
                ("§HOSTLINES", host is null ? "" : HostLoginLines)));
            register = "        services.AddScoped<ILoginClaims, TenantLoginClaims>();\n";
            o.Append(Fill(Switch, ("§CTX", contextType), ("§ROLEPARAM", membership.Role is null ? "null" : "member.Role"), ("§HOSTSWITCH", host is null ? "" : HostSwitchLines)));
            routes.Append("        app.MapGet(\"/api/auth/tenants\", TenantSwitching.ListAsync);\n        app.MapPost(\"/api/auth/switch-tenant\", TenantSwitching.SwitchAsync);\n");
            // the sign-in screens pick the tenant by name: the tenants that can be signed in to (not switched off, not soft deleted), answered without a sign-in
            string softWhere = TenantPlan.DeletedFlagOf(tenantTable) is { } soft ? $".Where(t => !t.{soft.CSharpName()})" : "";
            o.Append(Fill(SignInList, ("§CTX", contextType), ("§TT", tenantTable.TableName), ("§TKEY", tenantKey.CSharpName()), ("§ACTIVEJOIN", activeJoin), ("§SOFTWHERE", softWhere), ("§TEXT", textExpression)));
            routes.Append($"        app.MapGet(\"{TenantWords.SignInListPath}\", TenantSignIn.ListAsync).AllowAnonymous();\n");
        }
        else if (userOwned is not null && userPlan is not null)
        {
            string activeTest = active is null ? "" : $" && context.Set<§TT>().IgnoreQueryFilters().Any(t => t.§TKEY == u.§UTENANT && t.{active.CSharpName()})";
            o.Append(Fill(OwnColumn, ("§CTX", contextType), ("§U", userPlan.Table.TableName), ("§UNAME", userPlan.UserName.CSharpName()), ("§UTENANT", userOwned.Column.CSharpName()), ("§HOSTPARAM", hostParam),
                ("§HOSTFIND", host is null ? "" : HostOwnFind), ("§HOSTCHECK", host is null ? "" : HostOwnCheck),
                ("§ACTIVETEST", Fill(activeTest, ("§TT", tenantTable.TableName), ("§TKEY", tenantKey.CSharpName()), ("§UTENANT", userOwned.Column.CSharpName())))));
            register = "        services.AddScoped<ILoginClaims, TenantLoginClaims>();\n";
        }

        if (active is not null)
        {
            o.Append(Fill(ActiveCheck, ("§CTX", contextType), ("§TT", tenantTable.TableName), ("§TKEY", tenantKey.CSharpName()), ("§ACTIVE", active.CSharpName()), ("§INACTIVE_TYPE", TenantWords.InactiveType), ("§INACTIVE_TEXT", TenantWords.Inactive)));
            routes.Append("        app.Use(TenantActiveCheck.InvokeAsync);\n");
        }
        if (host is not null)
        {
            o.Append(Fill(HostCheck, ("§CTX", contextType), ("§TT", tenantTable.TableName), ("§TKEY", tenantKey.CSharpName()), ("§HOST", host.CSharpName()),
                ("§HOSTTEST", host.IsNullable ? $"t.{host.CSharpName()} != null && t.{host.CSharpName()}.ToLower() == label" : $"t.{host.CSharpName()}.ToLower() == label"),
                ("§HOST_TYPE", TenantWords.HostType), ("§HOST_TEXT", TenantWords.Host)));
            routes.Append("        app.Use(TenantHost.InvokeAsync);\n");
        }
        string onboardRoute = ApiRequests.Route(tenantTable);
        string adminLines = membership is null ? "" : Fill(OnboardAdmin, ("§M", membership.Table.TableName), ("§MUSER", membership.User.CSharpName()), ("§MTENANT", membership.Tenant.CSharpName()),
            ("§MROLEASSIGN", membership.Role is null ? "" : $", {membership.Role.CSharpName()} = AuthNames.AdminRole"), ("§TKEY", tenantKey.CSharpName()));
        if (membership is not null && userPlan is not null)
            o.Append(Fill(Creator, ("§CTX", contextType), ("§U", userPlan.Table.TableName), ("§UNAME", userPlan.UserName.CSharpName()), ("§UKEY", userPlan.Table.PrimaryKeyColumns[0].CSharpName()),
                ("§M", membership.Table.TableName), ("§MUSER", membership.User.CSharpName()), ("§MTENANT", membership.Tenant.CSharpName()),
                ("§MROLEASSIGN", membership.Role is null ? "" : $", {membership.Role.CSharpName()} = AuthNames.AdminRole")));
        bool newPerson = TenantPlan.OnboardsNewAdmin(database, project);
        string adminParam = (membership is null ? "" : ", int? AdminId") + (newPerson ? $", {userPlan!.Table.TableName}? Admin, string? AdminPassword" : "");
        string adminCheck = "";
        if (newPerson)
        {
            string tenantLink = membership is not null ? "" : $"                person.{userOwned!.Column.CSharpName()} = tenant.{tenantKey.CSharpName()};\n";
            string adminFlag = membership?.Role is null && userPlan!.Admin is { } flag ? $"                person.{flag.CSharpName()} = true;\n" : "";
            string memberLines = membership is null ? "" : Fill(NewAdminMember, ("§M", membership.Table.TableName), ("§MUSER", membership.User.CSharpName()), ("§MTENANT", membership.Tenant.CSharpName()),
                ("§MROLEASSIGN", membership.Role is null ? "" : $", {membership.Role.CSharpName()} = AuthNames.AdminRole"), ("§TKEY", tenantKey.CSharpName()), ("§UKEY", userPlan!.Table.PrimaryKeyColumns[0].CSharpName()));
            adminLines += Fill(NewAdmin, ("§UKEY", userPlan!.Table.PrimaryKeyColumns[0].CSharpName()), ("§UHASH", userPlan.PasswordHash.CSharpName()), ("§TENANTLINK", tenantLink), ("§ADMINFLAG", adminFlag), ("§MEMBERLINES", memberLines));
            adminCheck = Fill(NewAdminCheck, ("§U", userPlan.Table.TableName), ("§UNAME", userPlan.UserName.CSharpName()), ("§ONBOARD_TYPE", TenantWords.OnboardingType), ("§BOTHCHECK", membership is null ? "" : BothAdmins), ("§PASSWORDMIN", "UserCommands.MinimumPasswordLength"));
        }
        o.Append(Fill(Onboard, ("§CTX", contextType), ("§TT", tenantTable.TableName), ("§TKEY", tenantKey.CSharpName()), ("§ADMINLINES", adminLines), ("§ADMINPARAM", adminParam), ("§ADMINCHECK", adminCheck)));
        routes.Append($"        app.MapPost(\"{onboardRoute}/onboard\", TenantOnboarding.OnboardAsync);\n");

        string removeAdmin = !newPerson ? "" : Fill(RemoveWithAdmin, ("§CTX", contextType), ("§TT", tenantTable.TableName), ("§TKEY", tenantKey.CSharpName()), ("§U", userPlan!.Table.TableName), ("§UNAME", userPlan.UserName.CSharpName()),
            ("§REMOVEMEMBERS", membership is null ? "" : $"        await context.Set<{membership.Table.TableName}>().IgnoreQueryFilters().Where(m => m.{membership.Tenant.CSharpName()} == tenantId).ExecuteDeleteAsync();\n"));
        o.Append(Fill(Support, ("§CTX", contextType), ("§TT", tenantTable.TableName), ("§TKEY", tenantKey.CSharpName()), ("§ACTIVEWHERE", activeJoin), ("§REMOVEADMIN", removeAdmin),
            ("§DEACTIVATE", active is null ? "" : Fill(Deactivate, ("§CTX", contextType), ("§TT", tenantTable.TableName), ("§TKEY", tenantKey.CSharpName()), ("§ACTIVE", active.CSharpName())))));
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
    public static IResult NoTenant() => Results.Problem(statusCode: 403, type: "§NOTENANT_TYPE", title: "No tenant", detail: "§NOTENANT_TEXT");§HOSTPROBLEM
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
public class TenantLoginClaims(§CTX context§HOSTPARAM) : ILoginClaims
{
    public async Task<LoginClaims> ForAsync(DirectoryUser user, LoginRequest request, CancellationToken cancellationToken = default)
    {
        var members = await TenantMemberships.OfAsync(context, user.UserName, cancellationToken);
        int? asked = request.TenantId;
§HOSTLINES        if (members.Count == 0)
            return asked is null ? new LoginClaims() : new LoginClaims(Refusal: TenantProblems.NotMember());
        var chosen = asked is { } wanted ? members.FirstOrDefault(m => m.TenantId == wanted) : members[0];
        if (chosen is null)
            return new LoginClaims(Refusal: TenantProblems.NotMember());
        return new LoginClaims([new Claim(ClaimsTenantScope.ClaimName, chosen.TenantId.ToString())], chosen.Role, chosen.TenantId);
    }
}

""";

    private const string OwnColumn = """
/// <summary> Puts the tenant of the person (the tenant column of their own row) into the token at sign-in; a person of an inactive tenant cannot sign in. </summary>
public class TenantLoginClaims(§CTX context§HOSTPARAM) : ILoginClaims
{
    public async Task<LoginClaims> ForAsync(DirectoryUser user, LoginRequest request, CancellationToken cancellationToken = default)
    {
§HOSTFIND        string name = user.UserName.Trim();
        var found = await context.Set<§U>().IgnoreQueryFilters().AsNoTracking().Where(u => u.§UNAME == name)
            .Select(u => new { Tenant = (int?)u.§UTENANT, Usable = true§ACTIVETEST })
            .FirstOrDefaultAsync(cancellationToken);
        if (found?.Tenant is not int tenant)
            return new LoginClaims();
§HOSTCHECK        return found.Usable ? new LoginClaims([new Claim(ClaimsTenantScope.ClaimName, tenant.ToString())], null, tenant) : new LoginClaims(Refusal: TenantProblems.NotMember());
    }
}

""";

    private const string HostProblem = """

    public static IResult OtherHost() => Results.Problem(statusCode: 403, type: "§HOST_TYPE", title: "Another tenant's address", detail: "§HOST_TEXT");
""";

    private const string HostLoginLines = """
        if (await TenantHost.FindAsync(context, http.HttpContext, cancellationToken) is int host)
        {
            if (asked is not null && asked != host)
                return new LoginClaims(Refusal: TenantProblems.OtherHost());
            asked = host;
        }
""";

    private const string HostOwnFind = """
        int? host = await TenantHost.FindAsync(context, http.HttpContext, cancellationToken);
""";

    private const string HostOwnCheck = """
        if (host is int onHost && onHost != tenant)
            return new LoginClaims(Refusal: TenantProblems.OtherHost());
""";

    private const string HostSwitchLines = """
        if (await TenantHost.FindAsync(context, http, http.RequestAborted) is int host && host != wanted)
            return TenantProblems.OtherHost();
""";

    /// <summary> The tenant a host name belongs to (TenantHostColumn), and the check that a token works only on its own tenant's host. </summary>
    private const string HostCheck = """
/// <summary> The tenant a host name belongs to: the first label of the name (acme in acme.example.com) compared with the tenant table's host column. A host that names no tenant (localhost, an address) has none, and is not restricted. A name is remembered a minute. </summary>
public static class TenantHost
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int? Tenant, DateTime At)> Known = new();

    public static async Task<int?> FindAsync(§CTX context, HttpContext? http, CancellationToken cancellationToken)
    {
        string host = http?.Request.Host.Host ?? "";
        int dot = host.IndexOf('.');
        if (dot <= 0 || System.Net.IPAddress.TryParse(host, out _))
            return null;
        string label = host[..dot].ToLowerInvariant();
        if (Known.TryGetValue(label, out var known) && DateTime.UtcNow - known.At < TimeSpan.FromMinutes(1))
            return known.Tenant;
        int? tenant = await context.Set<§TT>().IgnoreQueryFilters().AsNoTracking().Where(t => §HOSTTEST).Select(t => (int?)t.§TKEY).FirstOrDefaultAsync(cancellationToken);
        if (Known.Count > 1000)
            Known.Clear();
        Known[label] = (tenant, DateTime.UtcNow);
        return tenant;
    }

    /// <summary> Forgets what is known of a name, so the next call asks the database (the generated tests change a tenant's host name). </summary>
    public static void Forget() => Known.Clear();

    /// <summary> A token that works in one tenant is refused (403) on the host of another. A caller in no tenant (a platform administrator) is not restricted. </summary>
    public static async Task InvokeAsync(HttpContext http, Func<Task> next)
    {
        if (int.TryParse(http.User.FindFirst(ClaimsTenantScope.ClaimName)?.Value, out int mine)
            && await FindAsync(http.RequestServices.GetRequiredService<§CTX>(), http, http.RequestAborted) is int host && host != mine)
        {
            await Results.Problem(statusCode: 403, type: "§HOST_TYPE", title: "Another tenant's address", detail: "§HOST_TEXT").ExecuteAsync(http);
            return;
        }
        await next();
    }
}

""";

    private const string SignInList = """
/// <summary> One tenant a person can sign in to: the id to send with the sign-in and the name to show. </summary>
public sealed record SignInTenant(int Id, string Name);

public static class TenantSignIn
{
    /// <summary> The names for the sign-in screen's drop-down, by name. Needs no sign-in; the filters of the context are off, so the tenant table's own filter (the caller's tenant) does not hide the others. </summary>
    public static async Task<IResult> ListAsync(§CTX context, CancellationToken cancellationToken)
    {
        var rows = await context.Set<§TT>().IgnoreQueryFilters().AsNoTracking()§ACTIVEJOIN§SOFTWHERE
            .Select(t => new { Id = t.§TKEY, Text = §TEXT }).ToListAsync(cancellationToken);
        return Results.Ok(rows.OrderBy(x => x.Text, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Id).Select(x => new SignInTenant(x.Id, x.Text)).ToList());
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
§HOSTSWITCH
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

    private const string NewAdminCheck = """
        if (request.Admin is not null)
        {
§BOTHCHECK            if ((request.AdminPassword ?? "").Length < §PASSWORDMIN)
                return Results.Problem(statusCode: 400, title: "Password too short", detail: $"The first administrator needs a password of at least {§PASSWORDMIN} characters.");
            string login = (request.Admin.§UNAME ?? "").Trim();
            if (login.Length == 0 || await context.Set<§U>().IgnoreQueryFilters().AnyAsync(u => u.§UNAME == login, http.RequestAborted))
                return Results.Problem(statusCode: 400, title: "User name", detail: "The first administrator needs a user name that no one has; send adminId for a person who exists.");
        }

""";

    private const string BothAdmins = """
            if (request.AdminId is not null)
                return Results.Problem(statusCode: 400, title: "One first administrator", detail: "Send adminId (a person who exists) or admin (a new person), not both.");

""";

    private const string NewAdmin = """
            if (request.Admin is { } person)
            {
                person.§UKEY = default;
                person.§UHASH = PasswordService.Hash(request.AdminPassword!);
§TENANTLINK§ADMINFLAG                context.Add(person);
                await context.SaveChangesAsync(http.RequestAborted);
§MEMBERLINES            }

""";

    private const string NewAdminMember = """
                context.Add(new §M { §MUSER = person.§UKEY, §MTENANT = tenant.§TKEY§MROLEASSIGN });
                await context.SaveChangesAsync(http.RequestAborted);

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
§ADMINCHECK        await using var transaction = await context.Database.BeginTransactionAsync(http.RequestAborted);
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

    private const string ActiveCheck = """
/// <summary> Answers 401 to a token whose tenant has been switched off since it was issued (the front ends sign the person out on a 401). The answer for a tenant is remembered for ten seconds, so a switch-off takes that long to reach a token already in use. </summary>
public static class TenantActiveCheck
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, (bool Active, DateTime At)> Known = new();

    public static async Task InvokeAsync(HttpContext http, Func<Task> next)
    {
        if (int.TryParse(http.User.FindFirst(ClaimsTenantScope.ClaimName)?.Value, out int tenant) && !await IsActiveAsync(http, tenant))
        {
            await Results.Problem(statusCode: 401, type: "§INACTIVE_TYPE", title: "Tenant switched off", detail: "§INACTIVE_TEXT").ExecuteAsync(http);
            return;
        }
        await next();
    }

    /// <summary> Forgets what is known of a tenant, so the next call asks the database (the generated tests switch a tenant off and expect the next call to be refused). </summary>
    public static void Forget(int tenant) => Known.TryRemove(tenant, out _);

    private static async Task<bool> IsActiveAsync(HttpContext http, int tenant)
    {
        if (Known.TryGetValue(tenant, out var known) && DateTime.UtcNow - known.At < TimeSpan.FromSeconds(10))
            return known.Active;
        var context = http.RequestServices.GetRequiredService<§CTX>();
        bool active = await context.Set<§TT>().IgnoreQueryFilters().AsNoTracking().AnyAsync(t => t.§TKEY == tenant && t.§ACTIVE, http.RequestAborted);
        Known[tenant] = (active, DateTime.UtcNow);
        return active;
    }
}

""";

    private const string Deactivate = """
    public static async Task DeactivateAsync(IServiceProvider services, int tenantId)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<§CTX>();
        await context.Set<§TT>().IgnoreQueryFilters().Where(t => t.§TKEY == tenantId).ExecuteUpdateAsync(s => s.SetProperty(t => t.§ACTIVE, false));
        TenantActiveCheck.Forget(tenantId);
    }

""";

    private const string Creator = """
public static class TenantCreator
{
    /// <summary> Makes the person who created a tenant (a platform administrator who is a row of the user table) its first administrator; nothing happens for a caller who is not a row (the bootstrap administrator). </summary>
    public static async Task AttachAsync(§CTX context, HttpContext http, int tenantId)
    {
        string name = (http.User.Identity?.Name ?? "").Trim();
        var person = await context.Set<§U>().IgnoreQueryFilters().AsNoTracking().Where(u => u.§UNAME == name).Select(u => (int?)u.§UKEY).FirstOrDefaultAsync(http.RequestAborted);
        if (person is null)
            return;
        context.Add(new §M { §MUSER = person.Value, §MTENANT = tenantId§MROLEASSIGN });
        await context.SaveChangesAsync(http.RequestAborted);
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

    public static async Task RunAsync(IServiceProvider services, string sql)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<§CTX>();
        await context.Database.ExecuteSqlRawAsync(sql);
    }

§DEACTIVATE§REMOVEADMIN
    public static async Task RemoveAsync(IServiceProvider services, int tenantId)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<§CTX>();
        await context.Set<§TT>().IgnoreQueryFilters().Where(t => t.§TKEY == tenantId).ExecuteDeleteAsync();
    }
}

""";

    private const string RemoveWithAdmin = """

    /// <summary> Removes a tenant onboarded with a new first administrator: the membership rows, the person (by user name) and the tenant. </summary>
    public static async Task RemoveWithAdminAsync(IServiceProvider services, int tenantId, string userName)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<§CTX>();
§REMOVEMEMBERS        await context.Set<§U>().IgnoreQueryFilters().Where(u => u.§UNAME == userName).ExecuteDeleteAsync();
        await context.Set<§TT>().IgnoreQueryFilters().Where(t => t.§TKEY == tenantId).ExecuteDeleteAsync();
    }

""";

    private const string Setup = """
/// <summary> The scope of the API: the <see cref="ClaimName"/> claim of the signed-in caller. The desktop app has its own (DesktopTenantScope), so the context file holds only <c>ITenantScope</c>. </summary>
public class ClaimsTenantScope(Microsoft.AspNetCore.Http.IHttpContextAccessor accessor) : ITenantScope
{
    public const string ClaimName = "tenant";

    public int? TenantId => int.TryParse(accessor.HttpContext?.User.FindFirst(ClaimName)?.Value, out int id) ? id : null;

    public bool IsPlatform => TenantId is null && accessor.HttpContext?.User.IsInRole(AuthNames.AdminRole) == true;
}

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
