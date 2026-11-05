using static CodeGenNew.Core.TemplateText;
using System.Text;

namespace CodeGenNew.Core;

/// <summary> The C# of feature flags kept in tables (<c>FeatureTables</c>): <c>FeatureFlags.AllAsync</c> and <c>EnabledAsync</c> for the API's own code, the <c>FeatureEnabledAsync</c> call on <c>HttpContext</c>,
/// <c>GET /api/features</c> and <c>PUT /api/features/{name}</c>. The text uses <c>§</c> tokens so the code reads as code. </summary>
public static class FeatureFlagWriter
{
    public static string File(DatabaseModel database, ProjectSettings project, string contextType, string[] usings)
    {
        var plan = FeatureFlagTables.Resolve(database, project).Plan!;
        var tenantTable = plan.OverrideTenant is null ? null : TenantPlan.TenantTableOf(database, project);
        var user = plan.OverridePerson is null ? null : UserTable.Resolve(database, project).Plan!;

        var (tenantRows, personRows) = Rows(plan);

        var o = new StringBuilder();
        foreach (string u in usings)
            o.Append($"using {u};\n");
        o.Append("using System.Collections.Concurrent;\nusing System.Security.Claims;\nusing Microsoft.AspNetCore.Mvc;\nusing Microsoft.EntityFrameworkCore;\n\n");

        (string, string)[] names =
        [
            ("§CTX", contextType), ("§F", plan.Flag.TableName), ("§FKEY", plan.FlagKey.CSharpName()), ("§FNAME", plan.FlagName.CSharpName()), ("§FDEFAULT", plan.FlagDefault.CSharpName()),
            ("§O", plan.Override.TableName), ("§OFLAG", plan.OverrideFlag.CSharpName()), ("§OON", plan.OverrideEnabled.CSharpName()),
            ("§TENANTROWS", tenantRows), ("§PERSONROWS", personRows),
            ("§UNKNOWN_TEXT", CSharpText.Escape(FeatureFlagTables.UnknownText)), ("§ADMIN_TEXT", CSharpText.Escape(FeatureFlagTables.OnlyAdministratorText)),
            ("§SCOPE_TEXT", CSharpText.Escape(FeatureFlagTables.NeedsScopeText)), ("§PLATFORM_TEXT", CSharpText.Escape(FeatureFlagTables.OnlyPlatformText)),
            ("§NOTARGET_TEXT", CSharpText.Escape(FeatureFlagTables.NoTargetText)), ("§UNKNOWNPERSON_TEXT", CSharpText.Escape(FeatureFlagTables.UnknownPersonText)),
            ("§UNKNOWNTENANT_TEXT", CSharpText.Escape(SubscriptionWords.UnknownTenant)),
            ("§PATH", FeatureFlagTables.Path), ("§SECONDS", project.FeatureCacheSeconds.ToString())
        ];

        // the tenant of the caller: only a project with tenants has a scope to ask
        string tenantOfCall = TenantPlan.IsEnabled(project) ? "http.RequestServices.GetService<ITenantScope>()?.TenantId" : "(int?)null";
        string personOfCall = plan.OverridePerson is null ? "(int?)null" : "(int.TryParse(http.User.FindFirstValue(AuthNames.OwnerClaim), out int personId) ? personId : (int?)null)";
        string planRead = plan.PlanLink is not { } link ? "        var planFlags = new HashSet<int>();\n"
            : FillAll(PlanRead, names.Concat([("§PF", link.Table.TableName), ("§PFFLAG", link.Flag.CSharpName()), ("§PFPLAN", link.Plan.CSharpName())]).ToArray());
        o.Append(FillAll(Flags, names.Concat([("§TENANTOFCALL", tenantOfCall), ("§PERSONOFCALL", personOfCall), ("§PLANREAD", planRead)]).ToArray()));

        string tenantCheck = tenantTable is null ? "" : FillAll(TenantCheck, names.Concat([("§TT", tenantTable.TableName), ("§TKEY", TenantPlan.KeyOf(tenantTable)!.CSharpName())]).ToArray());
        string personCheck = user is null ? "" : FillAll(PersonCheck, names.Concat([("§UT", user.Table.TableName), ("§UKEY", user.Table.PrimaryKeyColumns[0].CSharpName())]).ToArray());
        string tenantArm = plan.OverrideTenant is null ? "" : FillAll(TenantArm, names.Concat([("§OTENANT", plan.OverrideTenant.CSharpName()), ("§PERSONNULL", plan.OverridePerson is null ? "" : $" && r.{plan.OverridePerson.CSharpName()} == null")]).ToArray());
        string personArm = plan.OverridePerson is null ? "" : FillAll(PersonArm, names.Concat([("§OPERSON", plan.OverridePerson.CSharpName()), ("§TENANTNULL", plan.OverrideTenant is null ? "" : $" && r.{plan.OverrideTenant.CSharpName()} == null")]).ToArray());
        string newTenant = plan.OverrideTenant is null ? "" : $", {plan.OverrideTenant.CSharpName()} = tenantTarget";
        string newPerson = plan.OverridePerson is null ? "" : $", {plan.OverridePerson.CSharpName()} = personTarget";
        o.Append(FillAll(Api, names.Concat([("§TENANTCHECK", tenantCheck), ("§PERSONCHECK", personCheck), ("§TENANTARM", tenantArm), ("§PERSONARM", personArm),
            ("§NEWTENANT", newTenant), ("§NEWPERSON", newPerson), ("§TENANTOFCALL", tenantOfCall), ("§PERSONOFCALL", personOfCall),
            ("§HASTENANT", plan.OverrideTenant is null ? "false" : "true"), ("§HASPERSON", plan.OverridePerson is null ? "false" : "true")]).ToArray()));

        string removeLinks = plan.PlanLink is not { } linked ? "" : $"        await context.Set<{linked.Table.TableName}>().IgnoreQueryFilters().Where(r => r.{linked.Flag.CSharpName()} == flag).ExecuteDeleteAsync();\n";
        string linkPlan = plan.PlanLink is not { } toLink ? "" : FillAll(LinkPlan, names.Concat([("§PF", toLink.Table.TableName), ("§PFFLAG", toLink.Flag.CSharpName()), ("§PFPLAN", toLink.Plan.CSharpName())]).ToArray());
        o.Append(FillAll(Support, names.Concat([("§REMOVEPLANLINKS", removeLinks), ("§LINKPLAN", linkPlan)]).ToArray()));

        o.Append("/// <summary> Maps the feature routes. Generated by API_Features. </summary>\npublic static class FeatureRoutes\n{\n    public static void Register(WebApplication app)\n    {\n");
        o.Append("        app.MapGet(\"§PATH\", FeatureEndpoints.GetAsync);\n".Replace("§PATH", FeatureFlagTables.Path));
        o.Append("        app.MapPut(\"§PATH/{name}\", FeatureEndpoints.PutAsync);\n".Replace("§PATH", FeatureFlagTables.Path));
        o.Append("    }\n}\n");
        return o.ToString();
    }

    /// <summary> The conditions that pick the override rows of a tenant and of a person: a row names one of them, never both. </summary>
    public static (string TenantRows, string PersonRows) Rows(FeatureFlagPlan plan)
    {
        if (plan.OverrideTenant is { } t && plan.OverridePerson is { } p)
            return ($"o.{t.CSharpName()} == tenant && o.{p.CSharpName()} == null", $"o.{p.CSharpName()} == person && o.{t.CSharpName()} == null");
        return (plan.OverrideTenant is { } onlyTenant ? $"o.{onlyTenant.CSharpName()} == tenant" : "false", plan.OverridePerson is { } onlyPerson ? $"o.{onlyPerson.CSharpName()} == person" : "false");
    }

    /// <summary> The tokens of the flag and override tables, shared by the API and the apps that read the database themselves. </summary>
    public static (string, string)[] Names(FeatureFlagPlan plan, string contextType) =>
    [
        ("§CTX", contextType), ("§F", plan.Flag.TableName), ("§FKEY", plan.FlagKey.CSharpName()), ("§FNAME", plan.FlagName.CSharpName()), ("§FDEFAULT", plan.FlagDefault.CSharpName()),
        ("§O", plan.Override.TableName), ("§OFLAG", plan.OverrideFlag.CSharpName()), ("§OON", plan.OverrideEnabled.CSharpName())
    ];

    private const string PlanRead = """
        var planFlags = new HashSet<int>();
        if (tenant is { } working && (await SubscriptionState.OfAsync(context, working, cancellationToken)).PlanId is { } planId)
            planFlags = (await context.Set<§PF>().IgnoreQueryFilters().AsNoTracking().Where(r => r.§PFPLAN == planId).Select(r => r.§PFFLAG).ToListAsync(cancellationToken)).ToHashSet();

""";

    private const string LinkPlan = """
    /// <summary> Lists a flag for a plan, as a row of the plan link table. </summary>
    public static async Task LinkPlanAsync(IServiceProvider services, int plan, int flag)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<§CTX>();
        context.Add(new §PF { §PFPLAN = plan, §PFFLAG = flag });
        await context.SaveChangesAsync();
        FeatureFlags.Clear();
    }

""";

    private const string Support = """
/// <summary> Used by the generated tests: a flag of their own and its removal with every override that points at it. </summary>
public static class FeatureTestSupport
{
    public static async Task<int> AddFlagAsync(IServiceProvider services, string name, bool enabled)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<§CTX>();
        var flag = new §F { §FNAME = name, §FDEFAULT = enabled };
        context.Add(flag);
        await context.SaveChangesAsync();
        FeatureFlags.Clear();
        return flag.§FKEY;
    }

§LINKPLAN    public static async Task RemoveFlagAsync(IServiceProvider services, int flag)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<§CTX>();
        await context.Set<§O>().IgnoreQueryFilters().Where(o => o.§OFLAG == flag).ExecuteDeleteAsync();
§REMOVEPLANLINKS        await context.Set<§F>().IgnoreQueryFilters().Where(f => f.§FKEY == flag).ExecuteDeleteAsync();
        FeatureFlags.Clear();
    }
}

""";

    private const string Flags = """
/// <summary> The feature flags as a tenant and a person see them: a flag is on or off by default, a flag the tenant's plan lists is on, the tenant's override replaces that and the person's override replaces both. </summary>
public static class FeatureFlags
{
    /// <summary> How long what was read for a tenant and a person is kept (FeatureCacheSeconds; zero reads the tables on every call). A change through the feature routes forgets it at once; another instance of the API
    /// sees a change made elsewhere only when its own copy has expired. </summary>
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(§SECONDS);

    private static readonly ConcurrentDictionary<(int?, int?), (DateTime At, Dictionary<string, bool> Flags)> Cache = new();

    /// <summary> Forgets what was read, for a change made to the tables by other means than the routes. </summary>
    public static void Clear() => Cache.Clear();

    /// <summary> Every flag by name with its value for the tenant and the person (either may be null). </summary>
    public static async Task<Dictionary<string, bool>> AllAsync(§CTX context, int? tenant, int? person, CancellationToken cancellationToken = default)
    {
        if (CacheFor > TimeSpan.Zero && Cache.TryGetValue((tenant, person), out var hit) && DateTime.UtcNow - hit.At < CacheFor)
            return new Dictionary<string, bool>(hit.Flags, StringComparer.OrdinalIgnoreCase);
        var read = await ReadAsync(context, tenant, person, cancellationToken);
        if (CacheFor > TimeSpan.Zero)
            Cache[(tenant, person)] = (DateTime.UtcNow, read);
        return new Dictionary<string, bool>(read, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<Dictionary<string, bool>> ReadAsync(§CTX context, int? tenant, int? person, CancellationToken cancellationToken)
    {
        var flags = await context.Set<§F>().IgnoreQueryFilters().AsNoTracking().Select(f => new { Id = f.§FKEY, Name = f.§FNAME, On = f.§FDEFAULT }).ToListAsync(cancellationToken);
        var tenantRows = await context.Set<§O>().IgnoreQueryFilters().AsNoTracking().Where(o => §TENANTROWS).Select(o => new { Flag = o.§OFLAG, On = o.§OON }).ToListAsync(cancellationToken);
        var personRows = await context.Set<§O>().IgnoreQueryFilters().AsNoTracking().Where(o => §PERSONROWS).Select(o => new { Flag = o.§OFLAG, On = o.§OON }).ToListAsync(cancellationToken);
§PLANREAD        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var flag in flags)
        {
            bool on = flag.On || planFlags.Contains(flag.Id);
            if (tenant is not null && tenantRows.FirstOrDefault(r => r.Flag == flag.Id) is { } byTenant)
                on = byTenant.On;
            if (person is not null && personRows.FirstOrDefault(r => r.Flag == flag.Id) is { } byPerson)
                on = byPerson.On;
            result[flag.Name] = on;
        }
        return result;
    }

    /// <summary> Whether the named flag is on for the tenant and the person; a name that is not a flag is off. </summary>
    public static async Task<bool> EnabledAsync(§CTX context, string name, int? tenant, int? person, CancellationToken cancellationToken = default) =>
        (await AllAsync(context, tenant, person, cancellationToken)).TryGetValue(name, out bool on) && on;

    /// <summary> Whether the named flag is on for the caller of this request: <c>if (await http.FeatureEnabledAsync("new-invoice-screen")) ...</c> </summary>
    public static async Task<bool> FeatureEnabledAsync(this HttpContext http, string name)
    {
        var context = http.RequestServices.GetRequiredService<§CTX>();
        return await EnabledAsync(context, name, §TENANTOFCALL, §PERSONOFCALL, http.RequestAborted);
    }
}

""";

    private const string TenantCheck = """
        if (tenantTarget is not null && !await context.Set<§TT>().IgnoreQueryFilters().AnyAsync(t => t.§TKEY == tenantTarget, cancellationToken))
            return Results.Problem(statusCode: 404, title: "Not found", detail: "§UNKNOWNTENANT_TEXT");
""";

    private const string PersonCheck = """
        if (personTarget is not null && !await context.Set<§UT>().IgnoreQueryFilters().AnyAsync(u => u.§UKEY == personTarget, cancellationToken))
            return Results.Problem(statusCode: 404, title: "Not found", detail: "§UNKNOWNPERSON_TEXT");
""";

    private const string TenantArm = """
        if (tenantTarget is not null)
            rows = rows.Where(r => r.§OTENANT == tenantTarget§PERSONNULL);
""";

    private const string PersonArm = """
        if (personTarget is not null)
            rows = rows.Where(r => r.§OPERSON == personTarget§TENANTNULL);
""";

    private const string Api = """
/// <summary> The body of PUT /api/features/{name}: on or off (null removes the override) for a tenant or a person. A tenant's administrator changes their own tenant; a platform administrator names a tenant or a person. </summary>
public sealed record FeatureChange(bool? Enabled, int? TenantId, int? PersonId);

public static class FeatureEndpoints
{
    /// <summary> Every flag by name as the caller sees it (their tenant's and their own overrides applied). </summary>
    public static async Task<IResult> GetAsync(§CTX context, HttpContext http) =>
        Results.Ok(await FeatureFlags.AllAsync(context, §TENANTOFCALL, §PERSONOFCALL, http.RequestAborted));

    /// <summary> Switches a flag for the caller's tenant (an administrator of the tenant), or for the tenant or person named in the body (a platform administrator). </summary>
    public static async Task<IResult> PutAsync(string name, [FromBody] FeatureChange request, §CTX context, HttpContext http)
    {
        var cancellationToken = http.RequestAborted;
        if (!http.User.IsInRole(AuthNames.AdminRole))
            return Results.Problem(statusCode: 403, title: "Administrators only", detail: "§ADMIN_TEXT");
        var flag = await context.Set<§F>().IgnoreQueryFilters().AsNoTracking().Where(f => f.§FNAME == name).Select(f => new { Id = f.§FKEY }).FirstOrDefaultAsync(cancellationToken);
        if (flag is null)
            return Results.Problem(statusCode: 404, title: "Not found", detail: "§UNKNOWN_TEXT");

        int? working = §TENANTOFCALL;
        int? tenantTarget = null;
        int? personTarget = null;
        if (working is not null)
        {
            if (!§HASTENANT || request.PersonId is not null || request.TenantId is not null && request.TenantId != working)
                return Results.Problem(statusCode: 403, title: "Platform administrators only", detail: "§PLATFORM_TEXT");
            tenantTarget = working;
        }
        else
        {
            if (!§HASTENANT && request.TenantId is not null || !§HASPERSON && request.PersonId is not null)
                return Results.Problem(statusCode: 400, title: "Bad request", detail: "§NOTARGET_TEXT");
            if (!§HASTENANT && !§HASPERSON || (request.TenantId is null) == (request.PersonId is null))
                return Results.Problem(statusCode: 400, title: "Bad request", detail: "§SCOPE_TEXT");
            tenantTarget = request.TenantId;
            personTarget = request.PersonId;
        }
§TENANTCHECK§PERSONCHECK
        var rows = context.Set<§O>().IgnoreQueryFilters().Where(r => r.§OFLAG == flag.Id);
§TENANTARM§PERSONARM        var row = await rows.FirstOrDefaultAsync(cancellationToken);
        if (request.Enabled is not { } on)
        {
            if (row is not null)
                context.Remove(row);
        }
        else if (row is null)
            context.Add(new §O { §OFLAG = flag.Id, §OON = on§NEWTENANT§NEWPERSON });
        else
            row.§OON = on;
        await context.SaveChangesAsync(cancellationToken);
        FeatureFlags.Clear();
        return Results.Ok(await FeatureFlags.AllAsync(context, tenantTarget ?? §TENANTOFCALL, personTarget ?? §PERSONOFCALL, cancellationToken));
    }
}

""";
}
