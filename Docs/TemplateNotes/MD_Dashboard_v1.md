# MD_Dashboard_v1

`Dashboard.md`: the dashboard as a page to read before any screen exists, written only for a project with `Dashboard=true` (the `Api` stack, into `docs`).

- Every widget the plan chose, in display order, each with its kind and table, the schema fact that made it a candidate (`TotalAmount is a money column...`) and the statement that fills it, written for the database that was read.
- Then a table of the candidates the caps left out, with the same reasons. Reviewing this page is how to change the choice: `DashboardMeasures` adds or replaces a widget, `NoDashboardTables` removes a table.
- The statements are the ones `CS_Dashboard` carries and `SP_Dashboard` writes as a script, so what is reviewed here is what runs.
