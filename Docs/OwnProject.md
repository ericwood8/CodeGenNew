# Using the templates in your own project

[Back to the README](../README.md)

The templates were written against a layered sample (`MyApp.Common`, `MyApp.ApiService`, an Angular front end); the `TSX` family against a React front end with a small `request` helper. Your project's own names reach them in two ways, and the project file is the one to use.

## 1. A project file (use this)

Put the names in `Projects\<ProjectName>.config` (one `key=value` per line, `#` comments, lists comma-separated) and pass `--project <ProjectName>` (or pick it with the app's *Project* button). Every template reads it, including the Blazor, Uno, Python and Rust ones, which have no block of their own: a namespace you leave out is derived from `ProjectName`, so `ProjectName=InvoiceSystem` alone gives `InvoiceSystem.App.Views`, `InvoiceSystemContext` and so on. The keys and their defaults are in [Reference.md](Reference.md), section 8; the README's *Quick start* shows the command line.

## 2. The block at the top of a template

The templates that carry names of a project (53 of the shipped ones: most of the C#, API, Angular, React, WinUI3 and Uno templates) start with a block under a banner:

```
// ==========================================================================================
// PROJECT SETTINGS - the lines to change to use this template in your own project ...
// ==========================================================================================
string apiNamespace = "MyApp.ApiService.Apis";
string contextType = "MyAppContext";
string[] usings = { };
// A project settings file (...) overrides the values above.
contextType = Project.ContextName ?? contextType;
```

The values in the block are the defaults for a run with no project file. Open the `.tt` file (or the app's *Templates* button, then Edit), change those lines and save; the next generation uses them and nothing has to be rebuilt. A key in the project file wins over the block. The table shows the block's variable and the project key that overrides it.

| Block variable | Project key | In |
|---|---|---|
| `apiNamespace` | `ApiNamespace` | `API_Crud`, `API_Search`, `API_Csv`, `API_OData` |
| `entityNamespace` | `EntityNamespace` | `CS_Entity`, `CS_Validation`, `CS_Validator`, `CS_EfConfiguration`, `CS_Mapper`, `CS_DtoInput`, `CS_Faker`, `CS_CqrsHandlers` |
| `enumNamespace` | `EnumNamespace` | `CS_Enum` |
| `repositoryNamespace` | `RepoNamespace` | `CS_Repo`, `API_Search`, `CS_CqrsHandlers` |
| `contextType`, `contextNamespace` | `ContextName`, `ContextNamespace` | the API templates, `CS_Repo`, `CS_CqrsHandlers`, the `WinUI3_*` screens |
| `viewNamespace`, `viewModelNamespace`, `viewsFolder`, `viewModelsFolder` | `ViewNamespace`, `ViewModelNamespace`, `ViewsFolder`, `ViewModelsFolder` | the `WinUI3_*` and `UNO_*` screens |
| `dtoNamespace`, `validatorNamespace`, `fakerNamespace`, `applicationNamespace`, `fsharpNamespace` | `DtoNamespace`, `ValidatorNamespace`, `FakerNamespace`, `ApplicationNamespace`, `FSharpNamespace` | the `CS_Dto*`, `CS_Validator`, `CS_Faker`, `CS_CqrsHandlers` and `FS_Entity` templates |
| `usings` | `Usings` | the templates that write a `using` list (leave it empty if your project uses global usings) |
| `baseEntity`, `baseNameActiveEntity` | `BaseEntity`, `BaseNameActiveEntity` | `CS_Entity`: the base classes your entities derive from |
| `apiPrefix` | none | `TS_Service`, `TSX_Api`: the API URL prefix (`api`, `/api`) |

The Angular and React templates have no names in the block to edit: the folders they write into (`ApiFolder`, `ModelsFolder`, `ServicesFolder`, `ComponentsFolder`, `PagesFolder`) are project keys, relative to the output folder (`src\app` for Angular, `src` for React).

## Settings that only the project file has

- Which tables are enums, lookups or have no entity, and so get no navigation property, repository, API or screen: `EnumTables`, `NoNavigationTables`, `NoRepositoryTables`, `NoApiTables`, `NoLookupParents`.
- `TypeAheadRows` (default 1000) and `TypeAheadParents`: a parent with more rows, or listed, is found by typing instead of from a drop-down of every row (Angular, React, Blazor, WinUI3 and Uno; the API's `lookup` route takes `search` and `ids`).
- The switches for the extras (`ApiAuth`, `ApiCsv`, `OData`, `Dashboard`, `AccessMode`, `AngularVersion` and the rest) are in [Reference.md](Reference.md) and the README.

## What the templates assume

The C# templates assume the plumbing the essentials groups write: a repository base with `GetAll`, `GetByIdAsync`, `ExistsAsync` and `DeleteAsync`, and `CrudApi` and `BaseApi<T>` for the table APIs. Run the essentials once (`codegen essentials`, or `--essentials` on `generate`) or bring your own classes of that shape. The `TS` templates use Angular's own `HttpClient` and the `Crud` essentials group. The `TSX` templates need a shared `request<T>(path, init)` and `ApiError` pair (`import { request } from './client'`), which the React essentials write, because React has no HTTP client of its own.

Each template's header comment says what it assumes and what it deliberately does **not** write (collection navigations, hand-written business rules, detail grids), so you know what stays yours. The template notes in [TemplateNotes](TemplateNotes) go into more detail.

Column-name rules (which column means "created date", "is active", a display name, ...) live in `SpecialLogicColumns.config`; edit it to match your naming.
