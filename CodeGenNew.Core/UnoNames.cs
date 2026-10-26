namespace CodeGenNew.Core;

/// <summary> What the Uno templates agree on: the namespaces of the app, and which tables get a model and an API client. The Uno app calls the same API as the Blazor app and reads the same wire
/// format, so the table rules and the names of the model, the client and the lookup row are <see cref="BlazorNames"/>'s; only the namespace differs. </summary>
public static class UnoNames
{
    /// <summary> The root namespace of the Uno app: the project's <see cref="ProjectSettings.AppNamespace"/> (<c>&lt;ProjectName&gt;.App</c> unless AppNamespace says otherwise). </summary>
    public static string Namespace(ProjectSettings project) => project.AppNamespace;

    /// <summary> The namespace of the model classes. </summary>
    public static string ModelsNamespace(ProjectSettings project) => Namespace(project) + ".Models";

    /// <summary> The namespace of the API clients and <c>ApiClients</c>. </summary>
    public static string ServicesNamespace(ProjectSettings project) => Namespace(project) + ".Services";

    /// <summary> The property of <c>ApiClients</c> that holds a table's client: the model's name (<c>Customer</c>). </summary>
    public static string ClientProperty(string table) => BlazorNames.TypeName(table);

    /// <summary> Whether the table gets a model and a client: the same rule as the Blazor stack. </summary>
    public static bool HasClient(TableModel table, ProjectSettings project) => Refusal(table, project) is null;

    /// <summary> Whether the table also gets a client over its OData entity set (not a name / active table). </summary>
    public static bool HasODataClient(TableModel table, ProjectSettings project) => BlazorNames.HasODataClient(table, project);

    /// <summary> The reason a table gets no client, or null. </summary>
    public static string? Refusal(TableModel table, ProjectSettings project) => BlazorNames.Refusal(table, project, "Uno");
}
