# BLZ_GridColumns_v1

`Services/GridColumns.cs`: whole-database support code for the **Columns** list every Blazor grid (REST and OData) has. One file for the whole project, written again on every run (a plain template of the Blazor plan; the essentials are written once and kept, so a new helper lives here and an older project needs no refresh).

- `GridColumn`: a column the list offers (name, words, JSON property, whether the server sorts by it).
- `GridColumns.LoadAsync(js, key, offered, initial)` / `Parse`: the saved choice (localStorage, `<project>.columns.<page>`, a JSON list of names): the saved names the table still offers in the table's order, or the starting columns for nothing saved, a text that is not a list of names, or names none of which is a column any more; storage that is blocked is the same as nothing saved. `ChosenGridColumns.Resolve` in Core is the rule.
- `SaveAsync`: keeps the choice, or forgets it when it is the starting columns again.
- `Toggle(chosen, offered, name)`: ticks or clears one column; the table's order is kept and the last shown column cannot be cleared.
- `Cut(text)`: a long or multi-line text as a grid cell shows it: the first 60 characters and three dots (`CutLength`).

Used by `BLZ_Page` and `BLZ_ODataPage`.
