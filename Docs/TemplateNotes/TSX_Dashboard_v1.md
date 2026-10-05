# TSX_Dashboard_v1

The React dashboard, written only for a project with `Dashboard=true`: `api/dashboardApi.ts` (types and `dashboardApi.get(table?)`), `components/DashboardWidgetView.tsx` (one widget, any kind), `components/dashboard.css`, `pages/DashboardPage.tsx` and, with `DashboardStrip=true`, `components/DashboardStrip.tsx`. `TSX_Screens` adds `dashboard` as the first screen and wraps each table's page with its strip.

- **No chart package.** A card is a heading and a list of label and number pairs; a top list or breakdown is a list of labels, a bar made of two spans and the number; a trend is an inline SVG polyline with the numbers in a table under "Show the numbers"; the recent rows are a list. Every number is text and no meaning rests on colour alone.
- A widget title links to the table's screen. A failed load shows a message on the page and nothing on a strip (the grid under it is what the person came for).
- **Checked:** the page was generated for the SQL Server sample, built (`tsc` and `vite build`) and viewed in a browser against the running API: the cards, the two top lists and the strip above the Sales Invoice grid showed the numbers of `Dashboard.md`.
- **Not done:** no Vitest test is written for the page.
