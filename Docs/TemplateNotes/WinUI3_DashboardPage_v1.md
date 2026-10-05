# WinUI3_DashboardPage_v1

The desktop dashboard, written only for a project with `Dashboard=true`: `Views/DashboardWidgetViews.cs` (draws one widget as plain controls), `Views/DashboardPage.cs` and, with `DashboardStrip=true`, `Views/DashboardStrip.cs`. They are code-only controls (no XAML), because one file has to draw every kind of widget. `WinUI3_Screens` adds the menu entry, a `GoTo` the cards' links use, and `WithStrip`, which puts a table's cards above its list page.

- The page and the strip read the database through a `new` context of their own, so a page still loading when the person moves to the next screen never shares a context with it. Refresh reads again (no cache).
- Cards, bars (two grid columns in the ratio of the value), a sparkline (`Polyline` on a `Canvas`) and the recent rows; the numbers are always text, and the trend's numbers are in an `Expander`. Brushes come from the theme (`SolidBackgroundFillColorSecondaryBrush`, `ControlStrokeColorSecondaryBrush`) with a fixed fallback.
- **Checked:** generated for the SQLite sample (and a PostgreSQL / SQL Server run of the same SQL), built, started, read through UI Automation (the numbers match `Dashboard.md`), and a card's link opened the Customer screen with its strip.
