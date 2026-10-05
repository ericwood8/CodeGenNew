namespace CodeGenNew.Core;

/// <summary> One tab of the Project Settings screen: its title, the text that says what to expect on it, and the settings it shows. </summary>
public sealed record ProjectSettingGroup(string Title, string Description, string[] Keys);

/// <summary> Which tab of the Project Settings screen each setting is shown on. A key that no group lists falls into the first group, so a new setting is never hidden. </summary>
public static class ProjectSettingGroups
{
    public const string General = "General";

    /// <summary> The most settings one tab may hold; a tab that outgrows it is split. </summary>
    public const int MaxKeysPerTab = 20;

    public static readonly IReadOnlyList<ProjectSettingGroup> All =
    [
        new(General, "This tab has the context name, currency, years, naming style, acronyms, title, app namespace and usings.",
            ["ContextName", "CurrencyCode", "MinYear", "MaxYear", "NamingStyle", "Acronyms", "ProjectTitle", "AppNamespace", "Usings"]),
        new("Namespaces", "This tab has the context, API, enum, repo, entity, validator, faker, DTO and F# namespaces.",
            ["ContextNamespace", "ApiNamespace", "EnumNamespace", "RepoNamespace", "EntityNamespace", "ValidatorNamespace", "FakerNamespace", "DtoNamespace", "FSharpNamespace"]),
        new("Tables", "This tab has screens, enum rules, the \"No...\" lists, base entity, DbSet names, ignored columns and ERD tables.",
            ["Screens", "EnumTables", "EnumMaxRows", "EnumNameSuffixes", "HiddenParents", "NoLookupParents", "NoRepositoryTables", "NoApiTables", "NoNavigationTables", "NoCloneTables",
             "BaseEntity", "BaseNameActiveEntity", "DbSetNames", "IgnoredColumns", "NonNegativeColumns", "ModelFileOverrides", "ChildGridTitles", "ErdTables"]),
        new("Database", "This tab has the provider, server, name, user, access mode, replication, key-sequence and bulk-update settings.",
            ["DatabaseProvider", "DatabaseServer", "DatabaseName", "DatabaseUser", "AccessMode", "ReplicationTargets", "KeySequenceTables", "KeySequenceTable", "BulkUpdateColumns", "BulkUpdateExpression"]),
        new("API", "This tab has docs, HTTP, fakers, validation, production profile, project docs, folder and port.",
            ["ApiDocs", "ApiHttp", "ApiFakers", "ApiValidation", "ApiProduction", "ProjectDocs", "ApiFolder", "ApiPort"]),
        new("Dashboard", "This tab has Dashboard, Dashboard Strip, Dashboard Measures and No Dashboard Tables.",
            ["Dashboard", "DashboardStrip", "DashboardMeasures", "NoDashboardTables"]),
        new("WinUI3", "This tab has the view and view-model namespaces and folders, detail-master tables and the directory listing.",
            ["ViewNamespace", "ViewModelNamespace", "ViewsFolder", "ViewModelsFolder", "DetailMasterTables", "ListingName", "ListingFolder", "ListingPattern"]),
        new("Web", "This tab has the React and Angular folders, Angular version and dev port.",
            ["ModelsFolder", "ServicesFolder", "ComponentsFolder", "PagesFolder", "AngularVersion", "DevPort"]),
        new("Output", "This tab has Stacks (which front ends and APIs are generated), plan-also, the output folders, and the Rust crate and port.",
            ["Stacks", "PlanAlso", "OutputApi", "OutputWinUI3", "OutputReact", "OutputAngular", "OutputRust", "RustCrateName", "RustPort", "OutputSql"]),
        new("Build", "This tab has the build and test commands.",
            ["BuildApi", "BuildWinUI3", "BuildReact", "BuildAngular", "TestApi", "TestWinUI3", "TestReact", "TestAngular"]),
    ];

    /// <summary> The title of the tab a key is shown on. </summary>
    public static string TabOf(string key) =>
        All.FirstOrDefault(g => g.Keys.Contains(key, StringComparer.OrdinalIgnoreCase))?.Title ?? General;
}
