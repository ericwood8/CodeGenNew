# UNO_Dashboard_v1

`Services/DashboardClient.cs`, `Views/DashboardWidgetViews.cs` and `Views/DashboardPage.cs`, written for a project with `Dashboard=true`. The page shows what `GET /api/dashboard` answers (`API_Dashboard`): cards first, then bar lists, a sparkline and a list of recent rows, drawn in code, so no chart package is added. Every chart has its numbers beside it as text (the trend has a "Show the numbers" expander). The drawing is `WinUI3_DashboardPage`'s; the data comes through `DashboardClient` (the contract of `DashboardWire`, the same classes `BLZ_Dashboard` writes) instead of a context.

`UNO_Screens` adds the "Dashboard" entry as the first screen, passes the page a `GoTo(tag)` so a widget's title (a link to the widget's table) selects that screen's menu entry, and holds the client in `ApiClients.Dashboard`. With `Auth=true` the call carries the token and the API leaves out the widgets of tables the role may not read.

Limits: there is no strip of cards above each list page (`DashboardStrip` is for the WinUI 3 stack); money shows in the current culture's currency format, as on WinUI 3. See `UNO_Screens_v1.md`.
