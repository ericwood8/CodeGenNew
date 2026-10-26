# UNO_Screens_v1

The Uno stack: a single-project **Uno Platform** app (the `Uno.Sdk`, .NET 10) that calls the generated API with an `HttpClient`, the way the Blazor, React and Angular apps do. Its screens are the WinUI 3 screens' XAML (Uno implements the WinUI 3 API, so the dialogs, `NumberBox`, `CalendarDatePicker`, `TabView` and `x:Bind` view models run unchanged) with a data layer that reads and writes through API clients instead of an EF context. `UNO_Screens` is the whole-database template. It writes:

- `MainPage.Screens.cs`: the other half of the partial `MainPage`: the menu entries, `FirstScreen` and the switch that shows a screen, in the order of the project's `Screens` setting (the dashboard first with `Dashboard=true`, an "(OData)" entry after the REST ones with `OData=true`; with `Auth=true` a list of screens with the roles that may read each, and `AddScreens()` fills the menu for the signed-in role);
- `Services/ApiClients.cs`: one client per table that has one (the tables with an API of their own, a single int, long, guid or text key, not a name / active table), made when first asked for, over one `HttpClient`; also the dashboard client, the OData clients, the junction clients and, with `Auth=true`, the session.

The rest of the stack:

| Template | Writes |
|---|---|
| `UNO_Model` | `Models/<Table>.cs`: a class with a property per column |
| `UNO_Client` | `Services/<Table>Client.cs`: GetAll, GetLookup, Get, Create, Update, Delete, GetPage (search, paging, sort), Clone |
| `UNO_MasterScreen` | `Views/<Table>ListPage.xaml` and `.xaml.cs`, `ViewModels/<Table>ListViewModel.cs`: the list page |
| `UNO_DetailScreen` | `Views/<Table>DetailDialog.xaml` and `.xaml.cs`, `ViewModels/<Table>DetailViewModel.cs`: the add / edit dialog |
| `UNO_DetailMasterScreen` | `Views/<Table>DetailMasterDialog.xaml` and `.xaml.cs`, `ViewModels/<Table>DetailMasterViewModel.cs`: the same dialog with a grid per child table (a table that has child tables) |
| `UNO_Dashboard` | `Services/DashboardClient.cs`, `Views/DashboardPage.cs`, `Views/DashboardWidgetViews.cs` (`Dashboard=true`) |
| `UNO_JunctionEditor` | `Services/<Table>JunctionClient.cs`, `Views/<Table>JunctionEditor.xaml` and `.xaml.cs`, `ViewModels/<Table>JunctionEditorViewModel.cs` for each junction table |
| `UNO_ODataClient`, `UNO_ODataPage` | `Services/<Table>ODataClient.cs` and `Views/<Table>ODataListPage.xaml` (and `.xaml.cs`, `ViewModels/<Table>ODataListViewModel.cs`) for each served table (`OData=true`) |
| `UNO_Screens` | the menu and the client holder (this note) |

The essentials groups (`codegen essentials --stack uno`, or Essentials > Uno essentials) write what no table drives, once, and keep it: `Project` (the `.csproj`, `global.json`, `Directory.Build.props`, `GlobalUsings.cs`, the Windows manifests, the desktop and browser entry points, `launchSettings.json`), `App` (`App.xaml`, `App.xaml.cs`, `MainPage.xaml`, `MainPage.xaml.cs`), `Support` (`Services/ApiSupport.cs`: `ApiException`, `PagedResult<T>`, the response helpers), `Git` (`.gitignore`), and the two ticked by a setting: `Auth` (`Auth=true`: the session and the sign-in page, `UNO_EssentialAuth_v1.md`) and `OData` (`OData=true`: `Services/ODataSupport.cs`, `UNO_ODataPage_v1.md`).

## Settings

- `Stacks` includes `Uno`; `OutputUno` is the folder (default `<ProjectName>.Uno`); `BuildUno` / `TestUno` override the build command (default `dotnet build -v q -f net10.0-desktop`, no tests).
- The namespaces are the WinUI 3 stack's: `AppNamespace` (default `<ProjectName>.App`), `ViewNamespace`, `ViewModelNamespace`, `ViewsFolder`, `ViewModelsFolder`; the models are in `<AppNamespace>.Models` and the clients in `<AppNamespace>.Services`.
- `ApiPort` (default 5080) is the API's address in `MainPage.xaml.cs`; the `API_BASE_URL` environment variable overrides it (an Android emulator reaches the machine it runs on as `10.0.2.2`, a phone needs the machine's address). `DevPort` (default 5195 for Uno) is the browser head's port; the API allows `http://localhost:<DevPort>` when `Uno` is in `Stacks`. The desktop head has no origin, so it needs no CORS entry.

## What a screen does

The same as the WinUI 3 screens: a list page with search boxes, headers that sort (click again to flip; the sort is remembered between runs), paging with a choice of page size, Add New, Edit, Clone (a table that can be cloned), Delete with a confirmation; a dialog with tabs, drop-downs for foreign keys (a type-ahead box for a parent with too many rows), drop-downs for listed values, number boxes limited by column type, currency boxes in the project's `CurrencyCode`, a calendar picker and the shape checks of the validation rules. A table that has child tables gets the master-detail dialog (`UNO_DetailMasterScreen_v1.md`). A failed call shows a sentence in the page's error bar (a 400 on delete is "in use"; an unreachable server is "The server could not be reached.").

## Limits

- With `EditableChildren` a child's rows are boxes in the master dialog, saved with the row (`UNO_DetailMasterScreen`). With `DashboardStrip=true` a table that has cards shows them above its list page (`UNO_Dashboard` writes `Views/DashboardStrip.cs`). The junction editor is not opened from a page by the generator (like the WinUI 3 editor). The OData page has no filter row and sorts by one column. The session is kept in memory only.
- The project written by `Project` has the heads `net10.0-desktop` (Skia: Windows, macOS, Linux), `net10.0-browserwasm` (needs `dotnet workload install wasm-tools`) and `net10.0-windows10.0.26100` (WinAppSDK). Add `net10.0-android` or `net10.0-ios` to `TargetFrameworks` for the phone heads (their workloads are needed, and a Mac for iOS); the pages are the desktop layout, not a phone layout.
- A grid with many columns is as cramped as the WinUI 3 grid at a narrow window width.
- No tests are generated for the Uno app.

## Running it

```
cd <ProjectName>.Api   && dotnet run --urls http://localhost:5080
cd <ProjectName>.Uno   && dotnet run -f net10.0-desktop
```

**Checked** (2026-10-18): generated for the SQL Server InvoiceSystem sample (six screens) with the API, built for the desktop, browser and Windows heads with 0 errors; the desktop app run against the generated API over `InvoiceSystem_test` (a login that is not an owner): the customer list (71 rows, 4 pages), the search by account number, the edit dialog with its tabs, required marks, lookup and currency, a changed customer name saved and read back from the database, and the delete of a customer that has invoices refused with "This row cannot be deleted because it is in use.".

The second round (sign-in, guarded columns, master-detail dialog, dashboard, junction editor, OData page) was checked the same way, each in its own note: the sign-in page with a wrong and a right password, the screens without write buttons for a non-administrator, the guard note for a non-manager, an Item dialog with its two child grids and the child's own dialog from Edit, the dashboard's cards and bar lists, a junction editor moving a tag in and out of a SQLite table, and an OData list page with a search.
