# WinUI3_GridColumns_v1

`ViewModels/GridColumnStore.cs` and `Views/GridCells.cs`: whole-database support code for the **Columns** menu of every WinUI3 list page. Written again on every run (a plain template of the WinUI3 plan, so an older project needs no essentials refresh). The text is `DesktopGridColumns` in Core, so the WinUI3 and Uno stacks write the same files.

- `GridColumnInfo`: a column the menu offers (name, header caption, whether the grid sorts by it).
- `GridColumnStore.Load(gridKey, offered, initial)`: the saved choice (`GridColumns.json` in the local application data folder, one list of column names per grid): the saved names the table still offers, in the table's order, or the starting columns when nothing valid was saved. `ChosenGridColumns.Resolve` in Core is the rule.
- `Save`: keeps the choice, or forgets it when it is the starting columns again (the file goes when no grid has a choice).
- `Toggle(chosen, offered, name)`: ticks or clears one column; the table's order is kept and the last shown column cannot be cleared.
- `Cut(text)`: a long or multi-line text as a grid cell shows it: the first 60 characters and three dots (`CutLength`).
- `GridCells`: the cells of one row, an equal-width column for each text, so the rows line up with the header the page builds.

Used by `WinUI3_MasterScreen`. Child grids in the master-detail dialogs do not have the menu yet.
