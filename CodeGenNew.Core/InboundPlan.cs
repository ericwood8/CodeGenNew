namespace CodeGenNew.Core;

/// <summary> An inbound event landing table and the columns the receiving route uses. <c>EventId</c> and <c>Source</c> are optional; the others must exist. </summary>
public sealed record InboundShape(TableModel Table, ColumnModel Key, ColumnModel Body, ColumnModel? EventId, ColumnModel? Source, ColumnModel Received, ColumnModel Processed)
{
    /// <summary> The processed column is a yes / no flag; otherwise it is a date and time that is NULL until the event is processed. </summary>
    public bool ProcessedIsFlag => Processed.IsBooleanColumn;

    /// <summary> The characters the body column holds (0 when it has no limit). </summary>
    public int BodyLength => Body.CharacterLength > 0 ? Body.CharacterLength : 0;

    /// <summary> The name of the processed column in the JSON of the API. </summary>
    public string ProcessedJson => JsonNames.Camel(Processed.Name);

    /// <summary> The table also has the write-only collect route for a browser (<c>InboundClientTables</c>). </summary>
    public bool Client { get; init; }
}

/// <summary> The words of the inbound routes, so the generated routes and their tests agree. </summary>
public static class InboundWords
{
    public const string SignatureType = ProblemTypes.Prefix + "inbound-signature";
    public const string ExpiredType = ProblemTypes.Prefix + "inbound-expired";
    public const string TooLargeType = ProblemTypes.Prefix + "inbound-too-large";
    public const string SignatureHeader = "X-Signature";
    public const string EventHeader = "X-Event-Id";
    public const string UnconfiguredText = "The shared secret of the sender is not set on the server.";
    public const string SignatureText = "The message is not signed with the shared secret.";
    public const string ExpiredText = "The signed time is too far from the server's time.";
    public const string TooLargeText = "The body is larger than this route accepts.";
    public const string EmptyText = "The body is empty.";
    public const string NotSavedText = "The event could not be saved.";
    public const int UnprocessedRows = 100;
    public const string RateLimitedType = ProblemTypes.Prefix + "inbound-rate-limited";
    public const string RateLimitedText = "Too many messages from this address; try again in a minute.";
    public const string ClientSource = "client";
}

/// <summary> Which tables are inbound event landing tables (<c>InboundTables=WebhookEvent,ErrorReport:Payload:ExternalId</c>: a table and optionally its body and event id columns, found by name when left out): an insert-only table fed
/// by something other than a person, such as a webhook, a system error report, a search query or a page view. The receiving route is open to anyone and checks a signature instead of a login (the HMAC-SHA256 of
/// <c>time.body</c> with the shared secret named by <c>InboundSecret</c>), a replay window and a size limit; it keeps the raw body as text with a received-on stamp and applies an event id once. A signed-in worker lists the
/// unprocessed events and marks one processed. The table itself is a read-only list (<see cref="LogTablePlan"/>), so rows are never edited by hand. </summary>
public static class InboundPlan
{
    public const string DefaultSecret = "INBOUND_WEBHOOK_SECRET";

    private static readonly string[] BodyNames = ["Body", "Payload", "RawBody", "RawPayload", "Content", "Json", "Message"];
    private static readonly string[] EventIdNames = ["EventId", "ExternalId", "SenderEventId", "SourceEventId", "ExternalEventId", "MessageId"];
    private static readonly string[] SourceNames = ["Source", "Sender", "Provider", "Origin"];
    private static readonly string[] ProcessedNames = ["Processed", "IsProcessed"];

    public static IReadOnlyList<(string Table, string? Body, string? EventId)> Entries(ProjectSettings project) =>
        SettingParts.Items(project.InboundTables).Select(p => (p[0], SettingParts.At(p, 1), SettingParts.At(p, 2))).ToList();

    public static bool IsListed(string table, ProjectSettings project) => Entries(project).Any(e => e.Table.EqualsIgnoreCase(table));

    /// <summary> The table is an inbound table that also has the ordinary screens and routes (<c>InboundEditTables</c>), so it is not a read-only list. </summary>
    public static bool IsEditable(string table, ProjectSettings project) => project.InboundEditTables.Any(n => n.EqualsIgnoreCase(table)) && IsListed(table, project);

    /// <summary> The edit tables the project names that are not inbound tables (an empty list when all are). </summary>
    public static IReadOnlyList<string> EditProblems(ProjectSettings project) =>
        project.InboundEditTables.Where(name => !IsListed(name, project)).Select(name => $"InboundEditTables: {name} must also be a table of InboundTables.").ToList();

    public static InboundShape? For(TableModel table, ProjectSettings project) => IsListed(table.TableName, project) && Resolve(table, project, out _) is { } inbound ? inbound : null;

    /// <summary> Why a listed table cannot be an inbound landing table, or null when it can (or is not listed). </summary>
    public static string? Problem(TableModel table, ProjectSettings project)
    {
        if (!IsListed(table.TableName, project))
            return null;
        if (!IsEditable(table.TableName, project) && LogTablePlan.Problem(table, project) is { } listProblem)
            return listProblem.Replace("LogTables:", "InboundTables:");
        Resolve(table, project, out string? problem);
        return problem;
    }

    /// <summary> The client tables the project names that are not inbound tables of the database (an empty list when all are). </summary>
    public static IReadOnlyList<string> ClientProblems(DatabaseModel database, ProjectSettings project) =>
        project.InboundClientTables.Where(name => !IsListed(name, project) || !database.Tables.Any(t => t.TableName.EqualsIgnoreCase(name)))
            .Select(name => $"InboundClientTables: {name} must also be a table of InboundTables.").ToList();

    /// <summary> The senders that sign with a secret of their own: the source of <c>receive/{source}</c> and the environment variable that holds its secret. Entries that are not valid are left out (see <see cref="SourceSecretProblems"/>). </summary>
    public static IReadOnlyList<(string Source, string Secret)> SourceSecrets(ProjectSettings project) =>
        SettingParts.Items(project.InboundSourceSecrets).Where(p => ValidSourceSecret(p) is null).Select(p => (p[0], p[1])).DistinctBy(p => p.Item1, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary> The sender the generated tests post as when the table has a source column: one that has no secret of its own, so the shared secret signs it. </summary>
    public static string TestSource(ProjectSettings project)
    {
        string name = "stripe";
        while (SourceSecrets(project).Any(s => s.Source.EqualsIgnoreCase(name)))
            name += "-x";
        return name;
    }

    /// <summary> Whether the project asks for the partition script of the table (<c>PartitionTables</c>); the script is run by hand, so this says it was written, not that the table is partitioned. </summary>
    public static bool HasPartitionScript(string table, ProjectSettings project) => PartitionPlan.Entries(project).Any(e => e.Table.EqualsIgnoreCase(table));

    /// <summary> What is wrong with the senders named in <c>InboundSourceSecrets</c> (an empty list when all are fine). </summary>
    public static IReadOnlyList<string> SourceSecretProblems(ProjectSettings project)
    {
        var problems = SettingParts.Items(project.InboundSourceSecrets).Select(ValidSourceSecret).OfType<string>().ToList();
        problems.AddRange(SettingParts.Items(project.InboundSourceSecrets).GroupBy(p => p[0], StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)
            .Select(g => $"InboundSourceSecrets: {g.Key} is named twice."));
        return problems;
    }

    private static string? ValidSourceSecret(string[] parts)
    {
        if (SettingParts.At(parts, 1) is not { } secret)
            return $"InboundSourceSecrets: {parts[0]} needs the environment variable that holds its secret ({parts[0]}:{parts[0].ToUpperInvariant()}_WEBHOOK_SECRET).";
        if (!parts[0].All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            return $"InboundSourceSecrets: the source {parts[0]} may hold only letters, digits, - and _ (it is a part of the route).";
        if (!secret.All(c => char.IsAsciiLetterOrDigit(c) || c is '_'))
            return $"InboundSourceSecrets: {secret} may hold only letters, digits and _ (it names an environment variable).";
        return parts.Length > 2 ? $"InboundSourceSecrets: {parts[0]} has more than a source and a variable name." : null;
    }

    private static InboundShape? Resolve(TableModel table, ProjectSettings project, out string? problem)
    {
        problem = null;
        var entry = Entries(project).First(e => e.Table.EqualsIgnoreCase(table.TableName));
        string prefix = $"InboundTables: {table.TableName}";
        if (table.PrimaryKeyColumns is not [{ IsIntegerColumn: true } key])
        {
            problem = PartitionPlan.LooksPartitioned(table, project)
                ? $"{prefix} has the composite key of a table the partition script already converted; generate from the database before the script."
                : $"{prefix} needs a single whole-number key.";
            return null;
        }
        ColumnModel? Named(string name) => table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(name));
        ColumnModel? OneOf(string[] names) => table.Columns.FirstOrDefault(c => names.Any(n => c.Name.EqualsIgnoreCase(n)));
        var body = entry.Body is null ? OneOf(BodyNames) : Named(entry.Body);
        var eventId = entry.EventId is null ? OneOf(EventIdNames) : Named(entry.EventId);
        var source = OneOf(SourceNames);
        var received = table.Columns.FirstOrDefault(c => LogTablePlan.HasTimeOfDay(c) && c.Name.StartsWith("Received", StringComparison.OrdinalIgnoreCase))
            ?? (LogTablePlan.Guess(table) is { } guess && !guess.Name.StartsWith("Processed", StringComparison.OrdinalIgnoreCase) ? guess : null);
        var processed = OneOf(ProcessedNames) is { IsBooleanColumn: true } flag ? flag
            : table.Columns.FirstOrDefault(c => LogTablePlan.HasTimeOfDay(c) && c.Name.StartsWith("Processed", StringComparison.OrdinalIgnoreCase));
        var missing = new List<string>();
        if (body is not { IsStringColumn: true })
            missing.Add($"{entry.Body ?? "Body"} (text, the raw body of the message)");
        if (entry.EventId is not null && eventId is not { IsStringColumn: true })
            missing.Add($"{entry.EventId} (text, the sender's event id)");
        if (received is null)
            missing.Add("a received-on stamp (a date and time column)");
        if (processed is null)
            missing.Add("Processed (a yes / no column) or ProcessedOn (a date and time column)");
        if (processed is { IsBooleanColumn: false, IsNullable: false })
            missing.Add($"{processed.Name} to allow NULL (an unprocessed event has no processed time)");
        if (missing.Count > 0)
        {
            problem = $"{prefix} needs {string.Join(", ", missing)}.";
            return null;
        }
        if (eventId is { IsStringColumn: false })
            eventId = null;
        if (source is { IsStringColumn: false })
            source = null;
        var shape = new InboundShape(table, key, body!, eventId, source, received!, processed!) { Client = project.InboundClientTables.Any(n => n.EqualsIgnoreCase(table.TableName)) };
        var extra = ApiSampleValues.BodyColumns(table).Where(c => !IsManaged(shape, c) && !(c.IsPrimaryKey && c.IsIdentity) && c.DatabaseDefaultSql is null).Select(c => c.Name).ToList();
        if (extra.Count > 0)
        {
            problem = $"{prefix} has columns the receiving route cannot fill: {string.Join(", ", extra)} (make them nullable or give them a default).";
            return null;
        }
        return shape;
    }

    /// <summary> A column the receiving route or the worker sets. </summary>
    public static bool IsManaged(InboundShape s, ColumnModel c) =>
        new[] { s.Key, s.Body, s.EventId, s.Source, s.Received, s.Processed }.Any(m => m is not null && m.Name.EqualsIgnoreCase(c.Name));
}
