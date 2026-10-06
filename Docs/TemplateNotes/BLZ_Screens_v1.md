# BLZ_Screens_v1

The Blazor stack: a standalone **Blazor WebAssembly** app (.NET 10) that calls the generated API with an `HttpClient`, the way the React and Angular apps do. `BLZ_Screens` is the whole-database template. It writes:

- `Layout/NavMenu.razor`: one link per screen, in the order of the project's `Screens` setting (the same list `TSX_Screens` uses);
- `Pages/Home.razor`: the address `/` goes to the first screen;
- `Services/ApiClients.cs`: `AddApiClients()`, which registers the client of every table that has one (the tables with an API of their own, a single int or guid key, not a name / active table).

The rest of the stack:

| Template | Writes |
|---|---|
| `BLZ_Model` | `Models/<Table>.cs`: a class with a property per column |
| `BLZ_Client` | `Services/<Table>Client.cs`: GetAll, GetPage (search, paging, sort), Get, Create, Update, Delete, Clone |
| `BLZ_Page` | `Pages/<Table>Page.razor`: the grid, search bar, sortable headers, paging and the add / edit panel |
| `BLZ_Screens` | the menu, the home page and the client registration (this note) |

The essentials groups (`codegen essentials --stack blazor`, or Essentials > Blazor essentials) write what no table drives, once, and keep it: `Project` (the `.csproj`, `Program.cs`, `App.razor`, `_Imports.razor`, `wwwroot/index.html`, `wwwroot/appsettings.json`, `Properties/launchSettings.json`), `Layout` (`MainLayout.razor` with the hamburger button, and `wwwroot/css/app.css`), `Support` (`Services/ApiSupport.cs`: `ApiException`, `PagedResult<T>`, the response helpers) and `Git` (`.gitignore`).

## Settings

- `Stacks` includes `Blazor`; `OutputBlazor` is the folder (default `<ProjectName>.Blazor`); `BuildBlazor` / `TestBlazor` override the build command (default `dotnet build -v q`, no tests).
- `ApiPort` (default 5080) is the API's address in `wwwroot/appsettings.json` (`ApiBaseUrl`); `DevPort` (default 5190 for Blazor) is the dev server's port.
- The API is called from another origin, so `API_EssentialProgram` allows `http://localhost:<DevPort>` when `Blazor` is in `Stacks` (with `ApiProduction=true` the origins come from `Cors:Origins`).

## What a page does

Search boxes for the searchable columns, headers that sort (click again to flip), 20 rows a page, Add New, Edit, Clone (a table that can be cloned), Delete with a confirmation, and an edit panel with an `EditForm`: text, number, date and check boxes, a drop-down for each foreign key whose parent has a display column, a drop-down for a column whose values the database lists. A failed save or delete shows a sentence (a 400 on delete is "in use").

## Limits

- The form is one panel; there are no tabs and no master-detail child grids (a master-detail table gets the plain page), no dashboard page and no enum-table drop-downs (an enum column is a number box).
- A foreign key to the table itself is a plain box. A foreign key to a table that has no client (a name / active table) needs that page written by hand.
- Binary columns are not on the form. Date columns edit the date only.
- No tests are generated for the Blazor app.

## Running it

```
dotnet run --project <ProjectName>.Api
dotnet run --project <ProjectName>.Blazor
```

The app is at `http://localhost:5190`.

**Checked:** generated for the PostgreSQL InvoiceSystem sample (six screens), built with 0 warnings and 0 errors, run against the generated API: the grid, search, sort, the foreign key drop-down of an invoice, and add, edit, clone and delete of a customer through the form.
