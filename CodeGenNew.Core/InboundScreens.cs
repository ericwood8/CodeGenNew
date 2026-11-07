namespace CodeGenNew.Core;

/// <summary> What the list screens of an inbound event table (<c>InboundTables</c>) add to the read-only list: a Processed button on a row that is not processed yet, which calls <c>POST {id}/processed</c> of the API (or the same
/// guarded UPDATE, in the desktop app that reads the database). The words and the test rows are here so every front end and its tests agree. </summary>
public static class InboundScreens
{
    public const string Button = "Processed";

    public const string UnprocessedLabel = "Unprocessed: ";
    public const string OldestLabel = ". Oldest unprocessed: #";
    public const string PartitionText = ". Partition script: written";

    /// <summary> The part of the status sentence the generator knows: the senders with a secret of their own. </summary>
    public static string SendersText(ProjectSettings project) =>
        InboundPlan.SourceSecrets(project) is { Count: > 0 } own ? ". Senders with a secret of their own: " + string.Join(", ", own.Select(s => s.Source)) : "";

    /// <summary> The part of the status sentence about the collect route. </summary>
    public static string CollectText(ProjectSettings project) => $". Collect route: on, {project.InboundClientPerMinute} a minute";

    /// <summary> What the generator knows about the table, as the end of the status sentence (the part before the closing period). </summary>
    public static string Tail(InboundShape inbound, ProjectSettings project) =>
        SendersText(project) + (inbound.Client ? CollectText(project) : "") + (InboundPlan.HasPartitionScript(inbound.Table.TableName, project) ? PartitionText : "");

    public static string FailedText(string words) => $"Failed to mark the {words} processed.";

    /// <summary> The test for a row that is not processed yet, in TypeScript (the flag is false or null, the stamp is null). </summary>
    public static string UnprocessedTs(InboundShape inbound, string row) => $"!{row}.{inbound.ProcessedJson}";

    /// <summary> The test for a row that is not processed yet, in C# on the property <paramref name="property"/> of <paramref name="row"/>. </summary>
    public static string UnprocessedCs(InboundShape inbound, string row, string property) =>
        inbound.ProcessedIsFlag ? $"{row}.{property} != true" : $"{row}.{property} == null";

    /// <summary> The C# value the guarded UPDATE of a front end that reads the database sets the processed column to: true for a flag, now for a time stamp. </summary>
    public static string ProcessedValue(InboundShape inbound)
    {
        string type = inbound.Processed.CSharpBase() + (inbound.Processed.IsNullable || !inbound.ProcessedIsFlag ? "?" : "");
        return inbound.ProcessedIsFlag ? $"({type})true" : $"({type})DateTime.Now";
    }

    /// <summary> A row of the table as an object literal, with <paramref name="pair"/> turning a property name and a literal into one member (TypeScript: <c>name: value</c>, JSON: <c>"name": value</c>). The row is processed or not. </summary>
    public static string Row(InboundShape inbound, ProjectSettings project, int key, bool processed, Func<string, string, string> pair)
    {
        string ProcessedValue() => inbound.ProcessedIsFlag ? (processed ? "true" : "false") : (processed ? "\"2026-10-26T08:12:00\"" : "null");
        return "{ " + string.Join(", ", inbound.Table.Columns.Select(c =>
            c.Name.EqualsIgnoreCase(inbound.Key.Name) ? pair(JsonNames.Camel(c.Name), key.ToString())
            : c.Name.EqualsIgnoreCase(inbound.Processed.Name) ? pair(JsonNames.Camel(c.Name), ProcessedValue())
            : pair(JsonNames.Camel(c.Name), c.ValidExample(project, inbound.Table.TableName)))) + " }";
    }
}
