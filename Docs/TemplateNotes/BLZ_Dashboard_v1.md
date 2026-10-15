# BLZ_Dashboard_v1

The Blazor dashboard, written only for a project with `Dashboard=true`: `Services/DashboardClient.cs` (the answer's types and `GetAsync()`), `Components/DashboardWidgetView.razor` (one widget, any kind) and `Pages/Dashboard.razor` (route `/dashboard`, with its styles in a `<style>` block so no essential file changes). `BLZ_Screens` adds the Dashboard link first in the menu, makes `/` open it and registers `DashboardClient`.

- Same widgets, same words and same layout as the React and Angular pages: cards, bar lists, a sparkline with "Show the numbers", a wide Latest list. No chart package.
- Money widgets (`isMoney`) show the project's currency with thousands separators and two decimals; the symbol is `$` for USD, else the code.
- No strip above each grid: `DashboardStrip` does not apply to Blazor.
- The API must allow the app's origin (`DevPort`, default 5190).
- **Checked:** generated for the SQL Server sample, built, and viewed in the browser against the running API: the cards, bars, sparkline and Latest list showed the same numbers as the React page.
- With `Auth=true` the page asks for a signed-in person (`@attribute [Authorize]`); the API leaves out the widgets of tables the role may not read (`ReadRoles`, `TableReadRoles`), so the page draws only what comes back.
- **Not done:** no test project for the page.
