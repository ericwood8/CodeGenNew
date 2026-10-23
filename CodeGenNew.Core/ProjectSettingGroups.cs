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
        new(General, "This tab has the context name, currency, the most grid columns, years, naming style, acronyms, title, app namespace, usings, the red * on required fields and when a parent is found by typing.",
            ["ContextName", "CurrencyCode", "MaxGridColumns", "MinYear", "MaxYear", "NamingStyle", "Acronyms", "ProjectTitle", "AppNamespace", "Usings", "RequiredMarks", "TypeAheadRows", "TypeAheadParents"]),
        new("Namespaces", "This tab has the context, API, enum, repo, entity, validator, application, faker, DTO and F# namespaces.",
            ["ContextNamespace", "ApiNamespace", "EnumNamespace", "RepoNamespace", "EntityNamespace", "ValidatorNamespace", "ApplicationNamespace", "FakerNamespace", "DtoNamespace", "FSharpNamespace"]),
        new("Tables", "This tab has screens, enum rules, the \"No...\" lists, base entity, DbSet names, ignored columns, editable child tables, seed tables and ERD tables.",
            ["Screens", "EnumTables", "EnumMaxRows", "EnumNameSuffixes", "HiddenParents", "NoLookupParents", "NoRepositoryTables", "NoApiTables", "NoNavigationTables", "NoCloneTables",
             "BaseEntity", "BaseNameActiveEntity", "DbSetNames", "IgnoredColumns", "ModelFileOverrides", "ChildGridTitles", "EditableChildren", "SeedTables", "ErdTables"]),
        new("Validation", "This tab has running the validation attributes in the API, the columns the project says are never negative, the date columns that cannot be in the future, the text columns that get no email, phone, URL, postal code or social security number check, the columns that are email addresses or web addresses whatever their name, and the columns that are or are not money.",
            ["ApiAnnotations", "NonNegativeColumns", "NotFutureDateColumns", "PlainTextColumns", "EmailColumns", "UrlColumns", "MoneyColumns", "NotMoneyColumns"]),
        new("Database", "This tab has the provider, server, name, user, access mode, replication, temporal, key-sequence and bulk-update settings.",
            ["DatabaseProvider", "DatabaseServer", "DatabaseName", "DatabaseUser", "AccessMode", "ReplicationTargets", "TemporalTables", "KeySequenceTables", "KeySequenceTable", "BulkUpdateColumns", "BulkUpdateExpression"]),
        new("API", "This tab has docs, HTTP, fakers, validation, CSV, OData, EF configurations, production profile, telemetry, project docs, folder, port, and DTOs.",
            ["ApiDocs", "ApiHttp", "ApiFakers", "ApiValidation", "ApiCsv", "OData", "ODataTables", "ODataMaxTop", "ODataPageSize", "ODataExpandDepth", "ODataScreens", "EfConfigurations", "ApiProduction", "ApiTelemetry", "ProjectDocs", "ApiFolder", "ApiPort", "Dtos"]),
        new("Tests", "This tab has the generated test projects: the API integration tests, the Blazor page tests and the Rust crate tests.",
            ["ApiTests", "BlazorTests", "RustTests"]),
        new("Sign-in", "This tab has the API sign-in, the owned tables and claim names, and the user table the people sign in against.",
            ["ApiAuth", "OwnedTables", "OwnerColumn", "OwnedChildren", "AuthRoleClaim", "AuthOwnerClaim", "AuthAdminRole", "UserTable", "UserNameColumn", "PasswordHashColumn", "UserOwnerColumn", "UserRoleColumn", "UserAdminColumn", "UserRoleWhenTrue", "UserRoleWhenFalse", "ChangeGates", "GuardedColumns"]),
        new("Roles", "This tab has the roles that may read and the roles that may write, for every table and for single tables.",
            ["ReadRoles", "TableReadRoles", "WriteRoles", "TableWriteRoles"]),
        new("Dashboard", "This tab has Dashboard, Dashboard Strip, Dashboard Measures and No Dashboard Tables.",
            ["Dashboard", "DashboardStrip", "DashboardMeasures", "NoDashboardTables"]),
        new("WinUI3", "This tab has the view and view-model namespaces and folders, detail-master tables and the directory listing.",
            ["ViewNamespace", "ViewModelNamespace", "ViewsFolder", "ViewModelsFolder", "DetailMasterTables", "ListingName", "ListingFolder", "ListingPattern"]),
        new("Web", "This tab has the React and Angular folders, Angular version, toasts, sign-in, the container's API variable, the list protocol and dev port.",
            ["ModelsFolder", "ServicesFolder", "ComponentsFolder", "PagesFolder", "AngularVersion", "Toasts", "Auth", "DockerUpstreamVariable", "ListProtocol", "PageParameter", "PageSizeParameter", "SortParameter", "SearchParameter", "PageBase", "ItemsMember", "TotalMember", "DefaultSorts", "SearchColumns", "DevPort"]),
        new("Output", "This tab has Stacks (which front ends and APIs are generated), plan-also, the output folders, and the Rust crate and port.",
            ["Stacks", "PlanAlso", "OutputApi", "OutputWinUI3", "OutputReact", "OutputAngular", "OutputBlazor", "OutputUno", "OutputRust", "OutputPython", "OutputApiTests", "OutputBlazorTests", "RustCrateName", "RustPort", "OutputSql"]),
        new("Build", "This tab has the build and test commands.",
            ["BuildApi", "BuildWinUI3", "BuildReact", "BuildAngular", "BuildBlazor", "BuildUno", "BuildPython", "BuildRust", "TestApi", "TestWinUI3", "TestReact", "TestAngular", "TestBlazor", "TestUno", "TestPython", "TestRust"]),
    ];

    /// <summary> The title of the tab a key is shown on. </summary>
    public static string TabOf(string key) =>
        All.FirstOrDefault(g => g.Keys.Contains(key, StringComparer.OrdinalIgnoreCase))?.Title ?? General;
}
