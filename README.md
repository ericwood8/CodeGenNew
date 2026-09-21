# CodeGenNew

A C# WinUI 3 desktop code generator for a developer's own box: point it at a SQL Server database, pick a table in a TreeView, right-click, and generate code (SQL stored procedures first; C# APIs/POCOs and other output formats over time) from T4 templates. Also ships a scriptable command-line interface for automation/CI and quick testing without the GUI.

Replaces a series of prior hand-rolled "write lines to a text file with substitutions and smart loops" code generators with a real templating engine (T4 via `Mono.TextTemplating`), while staying simple, portable (unpackaged, no installer, no MSIX), and easy to extend with new template types without code changes.

See **[Docs/specs.md](Docs/specs.md)** for the full specification — architecture, configuration file formats, the `TableModel`/`ColumnModel` schema, the special-logic column-detection system, the CLI, and what's explicitly in and out of scope for v1.

## Solution Structure

```
CodeGenNew.sln
├── CodeGenNew.App                 WinUI 3 UI, MVVM via CommunityToolkit.Mvvm (TreeView, Connection/Location/Template Management dialogs)
├── CodeGenNew.Connections          Connect + test SQL Server connections (SQL Login & Windows Auth)
├── CodeGenNew.SchemaIntrospection  Reads tables/columns/PK/FK from the database; builds TableModel
├── CodeGenNew.TemplateEngine       Wraps Mono.TextTemplating; discovers/runs .tt templates
├── CodeGenNew.Core                 Shared model classes (TableModel, ColumnModel, ForeignKeyModel, settings)
└── CodeGenNew.Cli                  Scriptable console entry point (bypasses the WinUI3 app)
```

## Known Dependencies

| Dependency | Used by | Notes |
|---|---|---|
| [.NET 10 (LTS) SDK](https://dotnet.microsoft.com/) | all projects | Target framework. Windows App SDK 2.4.0 compatibility confirmed (builds and launches clean) — no longer an open risk. |
| [Windows App SDK 2.4.0](https://github.com/microsoft/WindowsAppSDK) (WinUI 3) | `CodeGenNew.App` only | GUI framework. Its own native/XAML runtime is bundled (`WindowsAppSDKSelfContained=true`), unpackaged (no MSIX). .NET's own deployment is framework-dependent (`SelfContained=false`) — see Docs/specs.md section 3 for why. |
| [CommunityToolkit.Mvvm](https://www.nuget.org/packages/CommunityToolkit.Mvvm) 8.4.0 | `CodeGenNew.App` only | The standard WPF/WinUI-agnostic MVVM toolkit (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`). Uses the classic backing-field property style, not the newer partial-property style — see specs.md §9 for why. |
| [Microsoft.Data.SqlClient](https://www.nuget.org/packages/Microsoft.Data.SqlClient) | `CodeGenNew.Connections`, `CodeGenNew.SchemaIntrospection` | Modern, actively-maintained SQL Server ADO.NET provider (the older `System.Data.SqlClient` used by the prior .NET Framework 4.8 generator this project supersedes is not used here). |
| [Mono.TextTemplating](https://github.com/mono/t4) (`Mono.TextTemplating` NuGet package) | `CodeGenNew.TemplateEngine` | In-process T4 templating engine — modern reimplementation of Visual Studio's T4, runs outside Visual Studio, targets .NET Core/.NET 5+. This is also what EF Core itself uses internally for `dotnet ef dbcontext scaffold`. |
| A reachable SQL Server instance (read-only access is all this tool ever needs — see specs.md §1.1) | runtime, not a package | Not bundled; the developer points the tool at their own database. CodeGenNew never writes to it. |

CLI argument parsing is hand-rolled rather than pulling in an extra library, given the modest number of flags (see specs.md §10).

## Status

Pre-release / actively being built. Not yet in git — deferred until there's a working v1 (solo-developer project; see specs.md §4).

## License

Not yet decided (private, single-developer project as of this writing).
