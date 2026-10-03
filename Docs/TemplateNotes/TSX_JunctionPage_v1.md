# TSX_JunctionPage_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates: components/<Table>Junction.tsx   (a reusable function component; the path is written by the
template itself, see GeneratedFiles - point the output folder at the React app's src folder), plus a
colocated components/__tests__/<Table>Junction.test.tsx.

The React counterpart of TS_JunctionComponent.tt for a many-to-many junction table
(TableModel.IsJunctionTable, RequiresJunctionTable=true): two <select multiple> lists ("Available"/
"Selected") with >>/>/</<< buttons, calling API_Junction.tt's three endpoints directly via the shared
request<T> helper (imported from "../api/client", same assumption TSX_Api.tt makes) - no separate
TSX_Api-style module, since a junction has only List/Link/Unlink, folded straight into the component the
same way TSX_Page.tt's own "plain page shape" already combines grid and form. Each button click commits
immediately (one HTTP call per moved row) rather than batching for a separate Save button, the same
choice TS_JunctionComponent.tt/WinUI3_JunctionEditor.tt already made.

Like TS_JunctionComponent.tt: the table name without its "E_"/"SY_" prefix names the component/file
(E_TimeSheet -> components/TimeSheetJunction.tsx, function TimeSheetJunction). anchorId is a required
prop (React's equivalent of Angular's @Input()) - this component is meant to be embedded in whatever
page already has the anchor row selected (e.g. a generated detail page), not routed to on its own. It is
placed in componentsFolder rather than pagesFolder for that reason - matching the same "small reusable
piece vs. a data-fetching page" split a components/ vs. pages/ folder layout already uses.

Requires TableModel.IsJunctionTable (RequiresJunctionTable=true) and the target table's display columns
(NeedsReferencedDisplayColumns=true), the same restrictions SP_Junction.tt/API_Junction.tt/
TS_JunctionComponent.tt have - generate those first.
```
