# TSX_GridColumns_v1

`components/gridColumns.tsx` and `components/__tests__/gridColumns.test.tsx`: whole-database support code for the **Columns** list every React grid (REST and OData) has. It does not depend on a table, so it is the same text for every database; it is a template so that it is written the same way as the screens and kept in step with them (a plain template of the React plan, like `TSX_GridSort`).

- `loadColumns(gridKey, offered, initial)`: the saved choice of a grid (local storage, `gridColumns:<grid>`, a list of column names): the saved names the table still offers, in the table's order, or the starting columns when nothing valid was saved (nothing saved, a value that is not a list of names, names none of which is a column any more, storage that cannot be read). This is `ChosenGridColumns.Resolve` in Core, which the Angular and Blazor helpers also follow.
- `saveColumns(gridKey, chosen, initial)`: keeps the choice, or forgets it when it is the starting columns again.
- `toggleColumn(chosen, offered, name)`: ticks or clears one column; the table's order is kept and the last shown column cannot be cleared (`ChosenGridColumns.Toggle`).
- `onlyShown(typed, chosen)`: what was typed per column, without the columns that are no longer shown, so a filter on a hidden column does not stay active where it cannot be seen.
- `shortText(value)`: a long or multi-line text as a grid cell shows it: the first 60 characters (`ChosenGridColumns.CutLength`) and three dots.
- `ColumnPicker`: the **Columns** button, a native `<details>` with one check box per column the table offers and a **Reset** that puts the starting columns back; the only shown column's box is disabled.

Used by `TSX_Page`, `TSX_DetailMasterPage` and their OData forms (`TSX_ODataPage`, `TSX_ODataDetailMasterPage`). Needs no other file.
