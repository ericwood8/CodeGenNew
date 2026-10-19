# Using the templates in your own project

[Back to the README](../README.md)

The `SP`/`API`/`CS`/`TS`/`WinUI3` templates were written against a layered sample (`MyApp.Common`, `MyApp.ApiService`, an Angular front end); the `TSX` family against a React front end with a small `request` helper. **The names that belong to a project are a block at the very top of each template**, under a banner:

```
// ==========================================================================================
// PROJECT SETTINGS -- the lines to change to use this template in your own project ...
// ==========================================================================================
string apiNamespace = "MyApp.ApiService.Apis";
string contextType = "MyAppContext";
string[] usings = { };          // namespaces the generated file must "using"
```

Open the `.tt` file (or the app's *Templates* button → Edit), change those lines, and save; the next generation uses them — there is nothing to rebuild.

| Setting | In | Tells the template |
|---|---|---|
| `apiNamespace`, `entityNamespace`, `enumNamespace`, `repositoryNamespace` | `API_Crud`, `API_Search`, `CS_Entity`, `CS_Validation`, `CS_Enum`, `CS_Repo` | the `namespace` the generated file declares |
| `usings` | `API_Crud`, `API_Search`, `CS_Entity`, `CS_Validation`, `CS_Repo` | extra `using` lines to write (leave empty if your project uses global usings) |
| `contextType` | `API_Crud`, `API_Search`, `CS_Repo` | your `DbContext` class |
| `TypeAheadRows`, `TypeAheadParents` | `TS_*`, `TSX_*`, `BLZ_*`, `WinUI3_*` | a parent with more rows than `TypeAheadRows` (default 1000) or listed in `TypeAheadParents` is found by typing instead of from a drop-down of every row; the API's `lookup` route takes `search` and `ids` |
| `baseEntity`, `baseNameActiveEntity` | `CS_Entity` | the base classes your entities derive from |
| `modelsFolder`, `servicesFolder`, `componentsFolder`, `apiPrefix` | `TS_*` | where the Angular files go and the API URL prefix |
| `pagesFolder`, `apiFolder`, `componentsFolder`, `apiPrefix` | `TSX_*` | where the React files go and the API URL prefix |
| `NoNavigationTables`, `NoRepositoryTables`, `NoApiTables`, `NoLookupParents` | several | tables that are enums, lookups or have no entity, and so get no navigation property, repository, API or screen |

The templates also assume the shape of the sample project's plumbing: a generic repository with `GetAll`, `GetByIdAsync`, `ExistsAsync` and `DeleteAsync`, a `BaseApi<T>` that registers routes, Angular's own `HttpClient`, and — for `TSX_*` only, since React has no framework-supplied HTTP client — a shared `request<T>(path, init)`/`ApiError` pair the target project must already have (`import { request } from './client'`). Each template's header comment says what it assumes and what it deliberately does **not** write (collection navigations, hand-written business rules, detail grids), so you know what stays yours.

Column-name rules (which column means "created date", "is active", a display name, …) live in `SpecialLogicColumns.config`; edit it to match your naming.
