# TS_Dashboard_v1

The Angular dashboard, written only for a project with `Dashboard=true`: `services/dashboard.service.ts`, the components `dashboard` (the page, with a spec), `dashboard-widget` (one widget, any kind) and, with `DashboardStrip=true`, `dashboard-strip` and `screen-with-strip`. `TS_Screens` adds `dashboard` as the first screen; with the strip, a table's route names `ScreenWithStripComponent` and carries the table and its screen in the route's data, so the generated screens stay as they are.

- Like the React page there is no chart package: bars are spans, the trend is an inline SVG with its numbers under "Show the numbers", and every number is text.
- The templates follow `AngularVersion`: `standalone: true` before 19, `@if` / `@for` from 18 (the html is written with `*ngIf` / `*ngFor` and converted by `AngularControlFlow`), `Eager` change detection from 22.
- **Checked:** generated for the SQL Server sample with Angular 22, `ng build` and `ng test` (the dashboard spec included) pass, and the page and the strip above the Sales Invoice screen were viewed in a browser against the running API.
