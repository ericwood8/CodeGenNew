# TS_JunctionComponent_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates the four files of an Angular standalone component, in components/<table>-junction/ (the
paths are written by the template itself, see GeneratedFiles):
    <table>-junction.component.css    empty, as TS_Component.tt's CSS is
    <table>-junction.component.html   the two-list shuttle control
    <table>-junction.component.spec.ts  the standard "should create" test
    <table>-junction.component.ts     the standalone component

The Angular counterpart to WinUI3_JunctionEditor.tt for a many-to-many junction table
(TableModel.IsJunctionTable, RequiresJunctionTable=true): two <select multiple> lists ("Available"/
"Selected") with >>/>/</<< buttons, calling API_Junction.tt's three endpoints directly via
HttpClient (no separate TS_Service-style service class - unlike a plain table's five-method CRUD
service, a junction has only List/Link/Unlink, and folding them straight into the component matches
the "plain component shape" decision already made for TS_Component.tt). Each button click commits
immediately (one HTTP call per moved row) rather than batching for a separate Save button, the same
choice WinUI3_JunctionEditor.tt made.

Like TS_Component.tt: the table name without its "E_"/"SY_" prefix names the selector/class/files
(E_TimeSheet -> components/timesheet-junction/timesheet-junction.component.ts, class
TimeSheetJunctionComponent). anchorId is an @Input() - this component is meant to be embedded in
whatever screen already has the anchor row selected (e.g. a generated detail screen, once a
WinUI3/Angular detail-screen template exists), not routed to on its own.

Requires TableModel.IsJunctionTable (RequiresJunctionTable=true) and the target table's display
columns (NeedsReferencedDisplayColumns=true), the same restrictions SP_Junction.tt/API_Junction.tt
have - generate those first.
```
