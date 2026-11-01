using System.Text;

namespace CodeGenNew.Core;

/// <summary> The C# of plans, subscriptions and entitlements (<c>SubscriptionTable</c>): the check that pauses a tenant's writes while its subscription is not active, the plan limits on creates, <c>GET /api/subscription</c>,
/// <c>PUT /api/subscription/{tenantId}</c> for a platform administrator, and the helpers the owner's webhook handler and the generated tests call. The text uses <c>§</c> tokens so the code reads as code. </summary>
public static class SubscriptionWriter
{
    public static string File(DatabaseModel database, ProjectSettings project, string contextType, string[] usings)
    {
        var cleared = SubscriptionPlan.ClearedTables(database, project);
        var subscription = SubscriptionPlan.SubscriptionOf(database, project)!;
        var tenantTable = TenantPlan.TenantTableOf(database, project)!;
        var tenantKey = TenantPlan.KeyOf(tenantTable)!;
        var limit = SubscriptionPlan.LimitTableOf(database, project);
        var limited = limit is null ? [] : SubscriptionPlan.LimitedTables(database, project);
        var gateway = subscription.Gateway;
        string planName = subscription.PlanName is null ? $"p.{subscription.PlanKey.CSharpName()}.ToString()"
            : subscription.PlanName.IsNullable ? $"p.{subscription.PlanName.CSharpName()} ?? \"\"" : $"p.{subscription.PlanName.CSharpName()}";

        var o = new StringBuilder();
        foreach (string u in usings)
            o.Append($"using {u};\n");
        o.Append("using System.Security.Claims;\nusing Microsoft.AspNetCore.Mvc;\nusing Microsoft.EntityFrameworkCore;\n\n");

        string statuses = string.Join(", ", SubscriptionPlan.ActiveStatuses(project).Select(s => CSharpText.Quote(s.ToLowerInvariant())));
        (string, string)[] names =
        [
            ("§CTX", contextType), ("§S", subscription.Table.TableName), ("§SKEY", subscription.Key.CSharpName()), ("§STENANT", subscription.Tenant.CSharpName()), ("§SPLAN", subscription.Plan.CSharpName()),
            ("§SSTATUS", subscription.Status.CSharpName()), ("§P", subscription.PlanTable.TableName), ("§PKEY", subscription.PlanKey.CSharpName()), ("§PNAME", planName),
            ("§TT", tenantTable.TableName), ("§TKEY", tenantKey.CSharpName()), ("§STATUSES", statuses), ("§REQUIRED", project.SubscriptionRequired ? "true" : "false"),
            ("§INACTIVE_TYPE", SubscriptionWords.InactiveType), ("§INACTIVE_TEXT", CSharpText.Escape(SubscriptionWords.InactiveText)), ("§TRIAL_TEXT", CSharpText.Escape(SubscriptionWords.TrialEndedText)),
            ("§NONE_TEXT", CSharpText.Escape(SubscriptionWords.NoSubscriptionText)), ("§LIMIT_TYPE", SubscriptionWords.LimitType), ("§LIMIT_TEXT", CSharpText.Escape(SubscriptionWords.LimitText)),
            ("§PLATFORM_TYPE", SubscriptionWords.OnlyPlatformType), ("§PLATFORM_TEXT", CSharpText.Escape(SubscriptionWords.OnlyPlatform)),
            ("§NEEDS_TEXT", CSharpText.Escape(SubscriptionWords.NeedsPlanAndStatus)), ("§UNKNOWNPLAN_TEXT", CSharpText.Escape(SubscriptionWords.UnknownPlan)), ("§UNKNOWNTENANT_TEXT", CSharpText.Escape(SubscriptionWords.UnknownTenant)),
            ("§ADMIN_TEXT", CSharpText.Escape(SubscriptionWords.OnlyAdministrator)),
            ("§PRICESELECT", subscription.PlanPrice is null ? "(decimal?)null" : $"(decimal?)p.{subscription.PlanPrice.CSharpName()}"),
            ("§PERIODSELECT", subscription.PlanPeriod is null ? "(string?)null" : $"p.{subscription.PlanPeriod.CSharpName()}")
        ];

        string endSelect = subscription.PeriodEnd is null ? "(DateTime?)null" : $"s.{subscription.PeriodEnd.CSharpName()}";
        string gatewaySelect = gateway is null ? "(string?)null" : $"s.{gateway.CSharpName()}";
        o.Append(Fill(State, names.Concat([("§ENDSELECT", endSelect), ("§GATEWAYSELECT", gatewaySelect)]).ToArray()));

        string keysArray = string.Join(", ", limited.Select(l => CSharpText.Quote(l.LimitKey)));
        string keyCases = string.Join("", limited.Select(l => l.IsMembership
            ? $"        if (path.Equals({CSharpText.Quote(l.CreatePath)}, StringComparison.OrdinalIgnoreCase))\n            return {CSharpText.Quote(l.LimitKey)};\n"
            : $"        if (IsCreate(path, {CSharpText.Quote(l.CreatePath)}))\n            return {CSharpText.Quote(l.LimitKey)};\n"));
        string countCases = string.Join("", limited.Select(l =>
        {
            string deleted = l.Deleted is null ? "" : $" && !r.{l.Deleted.CSharpName()}";
            return $"            {CSharpText.Quote(l.LimitKey)} => context.Set<{l.Table.TableName}>().IgnoreQueryFilters().AsNoTracking().CountAsync(r => r.{l.Tenant.CSharpName()} == tenant{deleted}, cancellationToken),\n";
        }));
        if (limit is null)
            o.Append(Fill(NoLimits, names));
        else
            o.Append(Fill(Limits, names.Concat([("§LIMITT", limit.Table.TableName), ("§LPLAN", limit.Plan.CSharpName()), ("§LKEY", limit.Key.CSharpName()), ("§LVALUE", limit.Value.CSharpName()),
                ("§KEYS", keysArray), ("§KEYCASES", keyCases), ("§COUNTCASES", countCases)]).ToArray()));

        o.Append(Fill(Check, names));

        string period = subscription.PeriodEnd is null ? "" : $"        if (request.PeriodEnd is {{ }} end)\n            row.{subscription.PeriodEnd.CSharpName()} = end;\n";
        string gatewayLine = gateway is null ? "" : $"        if (request.GatewayId is {{ }} gatewayId)\n            row.{gateway.CSharpName()} = gatewayId.Trim();\n";
        string startInit = subscription.PeriodStart is null ? "" : $", {subscription.PeriodStart.CSharpName()} = DateTime.UtcNow";
        string planLimits = limit is null ? PlanLimitsNone
            : Fill(PlanLimitsOf, ("§LIMITT", limit.Table.TableName), ("§LKEY", limit.Key.CSharpName()), ("§LPLAN", limit.Plan.CSharpName()), ("§LVALUE", limit.Value.CSharpName()));
        o.Append(Fill(Api, names.Concat([("§PERIOD", period), ("§GATEWAYLINE", gatewayLine), ("§STARTINIT", startInit), ("§PLANLIMITS", planLimits)]).ToArray()));

        if (gateway is not null)
            o.Append(Fill(Gateway, names.Concat([("§SGATEWAY", gateway.CSharpName()), ("§PERIODSET", subscription.PeriodEnd is null ? "" : $"        if (periodEnd is {{ }} end)\n            row.{subscription.PeriodEnd.CSharpName()} = end;\n")]).ToArray()));

        bool webhook = SubscriptionPlan.HasWebhook(database, project);
        if (webhook)
            o.Append(Fill(Webhook, names.Concat([("§SECRETNAME", project.SubscriptionWebhookSecret!), ("§HEADER", SubscriptionWords.WebhookHeader), ("§TOLERANCE", SubscriptionWords.WebhookToleranceSeconds.ToString()),
                ("§SIGNATURE_TYPE", SubscriptionWords.WebhookSignatureType), ("§EXPIRED_TYPE", SubscriptionWords.WebhookExpiredType), ("§SIGNATURE_TEXT", SubscriptionWords.WebhookSignatureText),
                ("§EXPIRED_TEXT", SubscriptionWords.WebhookExpiredText), ("§UNCONFIGURED_TEXT", SubscriptionWords.WebhookUnconfiguredText), ("§BODY_TEXT", SubscriptionWords.WebhookBodyText),
                ("§UNKNOWN_TEXT", SubscriptionWords.WebhookUnknownText)]).ToArray()));

        string limitSupport = limit is null ? "" : Fill(SupportLimit, names.Concat([("§LIMITT", limit.Table.TableName), ("§LPLAN", limit.Plan.CSharpName()), ("§LKEY", limit.Key.CSharpName()), ("§LVALUE", limit.Value.CSharpName()),
            ("§LBASE", limit.Value.CSharpBase()), ("§LTYPE", limit.Value.CSharp())]).ToArray());
        string removeLimits = limit is null ? "" : Fill("        await context.Set<§LIMITT>().IgnoreQueryFilters().Where(l => l.§LPLAN == planId).ExecuteDeleteAsync();\n", names.Concat([("§LIMITT", limit.Table.TableName), ("§LPLAN", limit.Plan.CSharpName())]).ToArray());
        string clearLines = string.Join("", cleared.Select(c => $"        await context.Set<{c.Table.TableName}>().IgnoreQueryFilters().Where(r => r.{c.Tenant.CSharpName()} == tenant).ExecuteDeleteAsync();\n"));
        o.Append(Fill(Support, names.Concat([("§LIMITSUPPORT", limitSupport), ("§REMOVELIMITS", removeLimits), ("§CLEARLINES", clearLines)]).ToArray()));

        o.Append("/// <summary> Maps the subscription routes and the check that pauses a tenant's writes. Generated by API_Subscriptions. </summary>\npublic static class SubscriptionRoutes\n{\n    public static void Register(WebApplication app)\n    {\n");
        o.Append("        app.Use(SubscriptionCheck.InvokeAsync);\n");
        o.Append("        app.MapGet(\"/api/subscription\", SubscriptionEndpoints.GetAsync);\n");
        o.Append("        app.MapPut(\"/api/subscription/{tenantId:int}\", SubscriptionEndpoints.PutAsync);\n");
        if (webhook)
            o.Append($"        app.MapPost(\"{SubscriptionWords.WebhookPath}\", SubscriptionWebhook.PostAsync).AllowAnonymous();\n");
        o.Append("    }\n}\n");
        return o.ToString();
    }

    private static string Fill(string text, params (string Token, string Value)[] values)
    {
        foreach (var (token, value) in values.OrderByDescending(v => v.Token.Length))
            text = text.Replace(token, value);
        return text;
    }

    private const string PlanLimitsNone = """
        var values = new[] { new { Plan = 0, Key = "", Value = (long?)null } }.Take(0).ToList();

""";

    private const string PlanLimitsOf = """
        var keys = SubscriptionLimits.Keys;
        var values = await context.Set<§LIMITT>().IgnoreQueryFilters().AsNoTracking().Where(l => keys.Contains(l.§LKEY))
            .Select(l => new { Plan = l.§LPLAN, Key = l.§LKEY, Value = (long?)l.§LVALUE }).ToListAsync(cancellationToken);

""";

    private const string State = """
/// <summary> What a tenant's subscription says now: whether it has one, the plan, the status and whether the tenant may write. </summary>
public sealed record SubscriptionSnapshot(bool Exists, int? PlanId, string? Status, DateTime? PeriodEnd, string? GatewayId, bool Writable, string? Why);

public static class SubscriptionState
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, (SubscriptionSnapshot Value, DateTime At)> Known = new();
    public static readonly string[] ActiveStatuses = { §STATUSES };
    public const bool Required = §REQUIRED;

    /// <summary> The tenant's subscription (its newest row), remembered ten seconds so a status change takes that long to reach a request in use unless <see cref="Forget"/> is called. </summary>
    public static async Task<SubscriptionSnapshot> OfAsync(§CTX context, int tenant, CancellationToken cancellationToken, bool fresh = false)
    {
        if (!fresh && Known.TryGetValue(tenant, out var known) && DateTime.UtcNow - known.At < TimeSpan.FromSeconds(10))
            return known.Value;
        var row = await context.Set<§S>().IgnoreQueryFilters().AsNoTracking().Where(s => s.§STENANT == tenant).OrderByDescending(s => s.§SKEY)
            .Select(s => new { Plan = (int?)s.§SPLAN, Status = s.§SSTATUS, End = §ENDSELECT, Gateway = §GATEWAYSELECT }).FirstOrDefaultAsync(cancellationToken);
        var snapshot = row is null
            ? new SubscriptionSnapshot(false, null, null, null, null, !Required, Required ? string.Format("§NONE_TEXT") : null)
            : Evaluate(row.Plan, row.Status, row.End, row.Gateway, DateTime.UtcNow);
        Known[tenant] = (snapshot, DateTime.UtcNow);
        return snapshot;
    }

    /// <summary> The rule: a status outside <see cref="ActiveStatuses"/> pauses writes, and a trialing subscription also stops at its period end. </summary>
    public static SubscriptionSnapshot Evaluate(int? plan, string status, DateTime? end, string? gateway, DateTime now)
    {
        if (!ActiveStatuses.Contains(status.Trim(), StringComparer.OrdinalIgnoreCase))
            return new SubscriptionSnapshot(true, plan, status, end, gateway, false, string.Format("§INACTIVE_TEXT", status));
        if (end is { } ends && ends < now && status.Trim().Equals("trialing", StringComparison.OrdinalIgnoreCase))
            return new SubscriptionSnapshot(true, plan, status, end, gateway, false, string.Format("§TRIAL_TEXT", ends.ToString("yyyy-MM-dd")));
        return new SubscriptionSnapshot(true, plan, status, end, gateway, true, null);
    }

    public static void Forget(int tenant) => Known.TryRemove(tenant, out _);
}

""";

    private const string Limits = """
public static class SubscriptionLimits
{
    /// <summary> The names of the limits the project counts. </summary>
    public static readonly string[] Keys = { §KEYS };

    /// <summary> What the plan allows for a limit, or null when it is unlimited (no row, or a NULL value). </summary>
    public static async Task<long?> AllowedAsync(§CTX context, int plan, string key, CancellationToken cancellationToken) =>
        await context.Set<§LIMITT>().IgnoreQueryFilters().AsNoTracking().Where(l => l.§LPLAN == plan && l.§LKEY == key).Select(l => (long?)l.§LVALUE).FirstOrDefaultAsync(cancellationToken);

    public static Task<int> UsedAsync(§CTX context, int tenant, string key, CancellationToken cancellationToken) => key switch
    {
§COUNTCASES        _ => Task.FromResult(0)
    };

    /// <summary> The limit a create route is counted against, or null when the path adds no row to a limited table. </summary>
    public static string? KeyOf(string path)
    {
        path = path.TrimEnd('/');
§KEYCASES        return null;
    }

    private static bool IsCreate(string path, string route) =>
        path.Equals(route, StringComparison.OrdinalIgnoreCase) || path.Equals(route + "/import", StringComparison.OrdinalIgnoreCase)
        || (path.StartsWith(route + "/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/clone", StringComparison.OrdinalIgnoreCase));

    /// <summary> The problem to answer when the path adds a row to a table whose limit the tenant has already reached; null when the add may go ahead. </summary>
    public static async Task<IResult?> RefusalAsync(§CTX context, int tenant, SubscriptionSnapshot state, string path, CancellationToken cancellationToken)
    {
        if (state.PlanId is not { } plan || KeyOf(path) is not { } key || await AllowedAsync(context, plan, key, cancellationToken) is not { } allowed)
            return null;
        int used = await UsedAsync(context, tenant, key, cancellationToken);
        return used >= allowed ? Results.Problem(statusCode: 402, type: "§LIMIT_TYPE", title: "Plan limit reached", detail: string.Format("§LIMIT_TEXT", key, allowed, used)) : null;
    }
}

""";

    private const string NoLimits = """
public static class SubscriptionLimits
{
    public static readonly string[] Keys = { };

    public static Task<long?> AllowedAsync(§CTX context, int plan, string key, CancellationToken cancellationToken) => Task.FromResult<long?>(null);

    public static Task<int> UsedAsync(§CTX context, int tenant, string key, CancellationToken cancellationToken) => Task.FromResult(0);

    public static Task<IResult?> RefusalAsync(§CTX context, int tenant, SubscriptionSnapshot state, string path, CancellationToken cancellationToken) => Task.FromResult<IResult?>(null);
}

""";

    private const string Check = """
/// <summary> Pauses the writes of a tenant whose subscription is not active (402) and refuses a create that would pass a plan limit (402). Sign-in, the tenant switch and the subscription routes are never paused; reads never are. </summary>
public static class SubscriptionCheck
{
    public static async Task InvokeAsync(HttpContext http, Func<Task> next)
    {
        if (!IsWrite(http.Request.Method) || !int.TryParse(http.User.FindFirst(ClaimsTenantScope.ClaimName)?.Value, out int tenant) || Exempt(http.Request.Path))
        {
            await next();
            return;
        }
        var context = http.RequestServices.GetRequiredService<§CTX>();
        var state = await SubscriptionState.OfAsync(context, tenant, http.RequestAborted);
        if (!state.Writable)
        {
            await Results.Problem(statusCode: 402, type: "§INACTIVE_TYPE", title: "Subscription not active", detail: state.Why).ExecuteAsync(http);
            return;
        }
        if (HttpMethods.IsPost(http.Request.Method) && await SubscriptionLimits.RefusalAsync(context, tenant, state, http.Request.Path.Value ?? "", http.RequestAborted) is { } refusal)
        {
            await refusal.ExecuteAsync(http);
            return;
        }
        await next();
    }

    private static bool IsWrite(string method) => HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

    private static bool Exempt(PathString path) =>
        path.StartsWithSegments("/api/subscription") || (path.StartsWithSegments("/api/auth") && !path.StartsWithSegments("/api/auth/members"));
}

""";

    private const string Api = """
public sealed record LimitInfo(string Key, long? Allowed, int Used);

/// <summary> What a plan allows of one limit (null: unlimited). </summary>
public sealed record PlanLimitInfo(string Key, long? Allowed);

/// <summary> A plan the tenant could choose: its price and billing period when the plan table has them, and what it allows. </summary>
public sealed record PlanInfo(int PlanId, string Name, decimal? Price, string? Period, bool Current, List<PlanLimitInfo> Limits);

public sealed record SubscriptionInfo(bool HasSubscription, int? PlanId, string? PlanName, decimal? PlanPrice, string? PlanPeriod, string? Status, DateTime? PeriodEnd, string? GatewayId, bool Writable, string? Reason, List<LimitInfo> Limits, List<PlanInfo> Plans);

/// <summary> The body of PUT /api/subscription/{tenantId}: what to change. A tenant with no subscription needs a plan and a status. PeriodEnd and GatewayId are ignored when the table has no such column. </summary>
public sealed record SubscriptionChange(int? PlanId, string? Status, DateTime? PeriodEnd, string? GatewayId);

public static class SubscriptionEndpoints
{
    /// <summary> The subscription of the caller's tenant for its administrator: the plan, the status and, for each limit, what the plan allows and what the tenant has. </summary>
    public static async Task<IResult> GetAsync(§CTX context, HttpContext http, ITenantScope scope)
    {
        if (!http.User.IsInRole(AuthNames.AdminRole))
            return Results.Problem(statusCode: 403, title: "Administrators only", detail: "§ADMIN_TEXT");
        if (scope.TenantId is not { } tenant)
            return TenantProblems.NoTenant();
        return Results.Ok(await ViewAsync(context, tenant, http.RequestAborted));
    }

    public static async Task<SubscriptionInfo> ViewAsync(§CTX context, int tenant, CancellationToken cancellationToken)
    {
        var state = await SubscriptionState.OfAsync(context, tenant, cancellationToken, fresh: true);
        var plans = await PlansAsync(context, state.PlanId, cancellationToken);
        var current = plans.FirstOrDefault(p => p.Current);
        var limits = new List<LimitInfo>();
        foreach (string key in SubscriptionLimits.Keys)
            limits.Add(new LimitInfo(key, state.PlanId is { } planId ? await SubscriptionLimits.AllowedAsync(context, planId, key, cancellationToken) : null, await SubscriptionLimits.UsedAsync(context, tenant, key, cancellationToken)));
        return new SubscriptionInfo(state.Exists, state.PlanId, current?.Name, current?.Price, current?.Period, state.Status, state.PeriodEnd, state.GatewayId, state.Writable, state.Why, limits, plans);
    }

    /// <summary> Every plan, cheapest first (a plan with no price last), with what it allows of each limit; <paramref name="current"/> marks the tenant's own. </summary>
    public static async Task<List<PlanInfo>> PlansAsync(§CTX context, int? current, CancellationToken cancellationToken)
    {
        var rows = await context.Set<§P>().IgnoreQueryFilters().AsNoTracking().Select(p => new { Id = p.§PKEY, Name = §PNAME, Price = §PRICESELECT, Period = §PERIODSELECT }).ToListAsync(cancellationToken);
§PLANLIMITS        return rows.OrderBy(r => r.Price ?? decimal.MaxValue).ThenBy(r => r.Id)
            .Select(r => new PlanInfo(r.Id, r.Name, r.Price, r.Period, r.Id == current, SubscriptionLimits.Keys.Select(k => new PlanLimitInfo(k, values.FirstOrDefault(v => v.Plan == r.Id && v.Key == k)?.Value)).ToList())).ToList();
    }

    /// <summary> Sets the plan, status, period end or gateway id of a tenant's subscription, making the row when the tenant has none. Only a platform administrator may (the gateway's webhook handler calls <c>SubscriptionGateway</c> instead). </summary>
    public static async Task<IResult> PutAsync(int tenantId, [FromBody] SubscriptionChange request, §CTX context, HttpContext http, ITenantScope scope)
    {
        if (!http.User.IsInRole(AuthNames.AdminRole) || scope.TenantId is not null)
            return Results.Problem(statusCode: 403, type: "§PLATFORM_TYPE", title: "Platform administrators only", detail: "§PLATFORM_TEXT");
        var cancellationToken = http.RequestAborted;
        if (!await context.Set<§TT>().IgnoreQueryFilters().AnyAsync(t => t.§TKEY == tenantId, cancellationToken))
            return Results.Problem(statusCode: 404, title: "Not found", detail: "§UNKNOWNTENANT_TEXT");
        if (request.PlanId is { } wanted && !await context.Set<§P>().IgnoreQueryFilters().AnyAsync(p => p.§PKEY == wanted, cancellationToken))
            return Results.Problem(statusCode: 400, title: "Bad request", detail: "§UNKNOWNPLAN_TEXT");
        var row = await context.Set<§S>().IgnoreQueryFilters().Where(s => s.§STENANT == tenantId).OrderByDescending(s => s.§SKEY).FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            if (request.PlanId is not { } plan || string.IsNullOrWhiteSpace(request.Status))
                return Results.Problem(statusCode: 400, title: "Bad request", detail: "§NEEDS_TEXT");
            row = new §S { §STENANT = tenantId, §SPLAN = plan, §SSTATUS = request.Status.Trim()§STARTINIT };
            context.Add(row);
        }
        else
        {
            if (request.PlanId is { } plan)
                row.§SPLAN = plan;
            if (!string.IsNullOrWhiteSpace(request.Status))
                row.§SSTATUS = request.Status.Trim();
        }
§PERIOD§GATEWAYLINE        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Results.Problem(statusCode: 400, title: "Bad request", detail: "The subscription could not be saved (a value is too long or missing).");
        }
        SubscriptionState.Forget(tenantId);
        return Results.Ok(await ViewAsync(context, tenantId, cancellationToken));
    }
}

""";

    private const string Gateway = """
/// <summary> For the owner's webhook handler (the payment gateway is outside the generator): sets the status, and optionally the period end, of the subscription the gateway calls by its own id. Answers false when no subscription has that id. </summary>
public static class SubscriptionGateway
{
    public static async Task<bool> ApplyAsync(IServiceProvider services, string gatewayId, string status, DateTime? periodEnd = null)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<§CTX>();
        var row = await context.Set<§S>().IgnoreQueryFilters().Where(s => s.§SGATEWAY == gatewayId).FirstOrDefaultAsync();
        if (row is null)
            return false;
        row.§SSTATUS = status.Trim();
§PERIODSET        await context.SaveChangesAsync();
        SubscriptionState.Forget(row.§STENANT);
        return true;
    }
}

""";

    private const string Webhook = """
/// <summary> POST /api/subscription/webhook: the payment gateway tells the API a subscription's new status. The message is signed (X-Gateway-Signature: t=unix seconds,v1=hex of the HMAC-SHA256 of "t.body" with the shared secret named by
/// §SECRETNAME), must be no older than §TOLERANCE seconds, and is applied once per event id; the body is { id?, gatewayId, status, periodEnd? }. Open to anyone, since the signature is the check. </summary>
public static class SubscriptionWebhook
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> Seen = new();

    public static async Task<IResult> PostAsync(HttpContext http, IConfiguration configuration, IServiceProvider services)
    {
        string? secret = configuration["§SECRETNAME"];
        if (string.IsNullOrEmpty(secret))
            return Results.Problem(statusCode: 503, title: "Webhook not configured", detail: "§UNCONFIGURED_TEXT");
        using var reader = new StreamReader(http.Request.Body);
        string body = await reader.ReadToEndAsync(http.RequestAborted);

        long? signedAt = null;
        string? signature = null;
        foreach (string part in http.Request.Headers["§HEADER"].ToString().Split(','))
        {
            var pair = part.Trim().Split('=', 2);
            if (pair.Length == 2 && pair[0] == "t" && long.TryParse(pair[1], out long seconds))
                signedAt = seconds;
            else if (pair.Length == 2 && pair[0] == "v1")
                signature = pair[1];
        }
        byte[] expected = System.Security.Cryptography.HMACSHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret), System.Text.Encoding.UTF8.GetBytes($"{signedAt}.{body}"));
        bool signed = false;
        if (signedAt is not null && signature is not null)
        {
            try
            {
                signed = System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(signature));
            }
            catch (FormatException)
            {
            }
        }
        if (!signed)
            return Results.Problem(statusCode: 401, type: "§SIGNATURE_TYPE", title: "Bad signature", detail: "§SIGNATURE_TEXT");
        if (Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - signedAt!.Value) > §TOLERANCE)
            return Results.Problem(statusCode: 400, type: "§EXPIRED_TYPE", title: "Message expired", detail: "§EXPIRED_TEXT");

        System.Text.Json.Nodes.JsonObject? message;
        try
        {
            message = System.Text.Json.Nodes.JsonNode.Parse(body) as System.Text.Json.Nodes.JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            message = null;
        }
        string? gatewayId = message?["gatewayId"]?.GetValue<string>()?.Trim();
        string? status = message?["status"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrEmpty(gatewayId) || string.IsNullOrEmpty(status))
            return Results.Problem(statusCode: 400, title: "Bad request", detail: "§BODY_TEXT");
        if (message?["id"]?.GetValue<string>() is { Length: > 0 } eventId)
        {
            foreach (var old in Seen.Where(e => e.Value < DateTime.UtcNow.AddMinutes(-10)).Select(e => e.Key).ToList())
                Seen.TryRemove(old, out _);
            if (!Seen.TryAdd(eventId, DateTime.UtcNow))
                return Results.Ok(new { applied = false, duplicate = true });
        }
        DateTime? periodEnd = message?["periodEnd"]?.GetValue<DateTime>();
        if (!await SubscriptionGateway.ApplyAsync(services, gatewayId, status, periodEnd))
            return Results.Problem(statusCode: 404, title: "Not found", detail: "§UNKNOWN_TEXT");
        return Results.Ok(new { applied = true, duplicate = false });
    }
}

""";

    private const string SupportLimit = """

    public static async Task SetLimitAsync(IServiceProvider services, int planId, string key, long? value)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<§CTX>();
        var row = await context.Set<§LIMITT>().IgnoreQueryFilters().Where(l => l.§LPLAN == planId && l.§LKEY == key).FirstOrDefaultAsync();
        if (row is null)
            context.Add(row = new §LIMITT { §LPLAN = planId, §LKEY = key });
        row.§LVALUE = value is { } v ? (§LBASE)v : default(§LTYPE);
        await context.SaveChangesAsync();
    }

""";

    private const string Support = """
/// <summary> Used by the generated tests: a plan made from a JSON body, its limits, and the removal of the plan and everything that points at it. </summary>
public static class SubscriptionTestSupport
{
    public static async Task<int> AddPlanAsync(IServiceProvider services, string json)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<§CTX>();
        var plan = System.Text.Json.JsonSerializer.Deserialize<§P>(json, System.Text.Json.JsonSerializerOptions.Web)!;
        plan.§PKEY = default;
        context.Add(plan);
        await context.SaveChangesAsync();
        return plan.§PKEY;
    }
§LIMITSUPPORT
    /// <summary> Removes the rows a test put in a tenant of its own, so the tenant itself can be removed. </summary>
    public static async Task ClearAsync(IServiceProvider services, int tenant)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<§CTX>();
§CLEARLINES    }

    public static async Task RemovePlanAsync(IServiceProvider services, int planId, params int[] tenants)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<§CTX>();
        await context.Set<§S>().IgnoreQueryFilters().Where(s => s.§SPLAN == planId).ExecuteDeleteAsync();
§REMOVELIMITS        await context.Set<§P>().IgnoreQueryFilters().Where(p => p.§PKEY == planId).ExecuteDeleteAsync();
        foreach (int tenant in tenants)
            SubscriptionState.Forget(tenant);
    }
}

""";
}
