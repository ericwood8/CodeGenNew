# WinUI3_MasterScreen_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates a WinUI3 Page listing every row of the table - the "master" of a master/detail pair (see
WinUI3_DetailScreen.tt, which this page opens for Add/Edit, and WinUI3_DetailMasterScreen.tt, the
detail form's own master-of-its-children alternative):
    Views/<TableName>ListPage.xaml         a header row, a grid of rows, Add New / Edit / Delete
    Views/<TableName>ListPage.xaml.cs       opens <TableName>DetailDialog, confirms before deleting
    ViewModels/<TableName>ListViewModel.cs  loads rows via <TableName>Repo, resolves FK ids to names

`Views/PaginationBar.xaml` and `.xaml.cs` (the bar under the grid) and `ViewModels/GridSortStore.cs` (the remembered sort) come from the `Controls` essentials group (`WinUI3_EssentialControls`), not from this template: run that group once before generating screens.

The grid's columns are exactly WinUI3_DetailScreen.tt's own field list (every column except the primary
key, computed and identity columns), so the two screens always agree on what a row looks like. A
foreign key column shows the parent's display column (looked up once per parent into a dictionary, the
same idea as TS_Component's "name of" methods) instead of the raw id - unless it's listed in
the NoLookupParents setting, which shows the id number instead.

Not a DataGrid: WinUI3 has none built in without a third-party package this template can't assume your
project has, so each row is a plain Grid of TextBlocks with the same column widths as the header, laid
out via a generic <TableName>ListRow.Cells list rather than one named XAML property per column - the
only way to keep the number of columns generic (it depends on the table) while still using compiled
x:Bind. If you have Community Toolkit's DataGrid or WinUI's TreeView/ListView with grid-style headers
already, feel free to replace this with that.

Requires a primary key that is a single, non-composite INT column. A "name/active" table (a NOT NULL text Name plus a NOT NULL bit IsActive) gets the page too:
its repository (NameActiveRepo) has no search routine, so CS_Repo writes a SearchAsync that reads the whole list and narrows, sorts and pages it in memory, the search bar
gains an Active only check box, and there is no Clone button. A foreign key column sorts by its key, not by the parent's name. Only a table the Screens setting lists gets a screen.

What it does NOT do: sorting, or opening from a NavigationView/Frame - add the Page to your own
navigation the way you would any other.

The bar under the grid (found by moving a real server to server paging): the label reads "Page 2 of 5 (98 rows)", or "Nothing found." once the first page has
arrived and no row matches; a "Rows per page" box (10, 20, 50, 100) reads the first page again with the size picked (PageSize is a view model property, the
bar raises PageSizeChanged); and a delete that empties the last page falls back to the last page there is instead of showing "Page 3 of 2". The bar is
generated here and by WinUI3_DetailMasterScreen with the same text (a test keeps the two equal).

The bar under the grid (found by moving a real server to server paging): the label reads "Page 2 of 5 (98 rows)", or "Nothing found." once the first page has
arrived and no row matches; a "Rows per page" box (10, 20, 50, 100) reads the first page again with the size picked (PageSize is a view model property, the
bar raises PageSizeChanged); and a delete that empties the last page falls back to the last page there is instead of showing "Page 3 of 2". The bar is
generated here and by WinUI3_DetailMasterScreen with the same text (a test keeps the two equal).

Pagination: the grid always loads one page at a time
through CS_Repo.tt's SearchAsync instead of GetAll(), with a PaginationBar underneath. **Update
(2026-09-28):** pagination no longer requires a searchable column - a table with none (e.g. a
monthly-summary table keyed only by ids/numbers) still gets it, found live needing exactly this;
pagination and searchability are separate concerns, and SearchAsync already accepts zero filter values.

Search: when the table has a searchable column, a plain
TextBox per TableModel.SearchableColumns column (AutoSuggestBox was considered and declined - it's
built for incremental single-query autocomplete, not several independent typed filter fields) plus a
Search button above the grid, calling ViewModel.LoadAsync() with PageNumber reset to 1. Bespoke per
table, like the add/edit form - there is no shared "search bar" UserControl the way PaginationBar is,
since the field list differs per table. A blank field is sent as no filter for that column (every row),
not filtered on the empty string. A table with no searchable column gets no search UI at all, but still
pages through every row via the plain, unfiltered SearchAsync call.

Unlike TS_Component.tt (a real, already-installed Angular Material dependency) there is no comparable
WinUI3 library already in a real target project to point at (no real WinUI3 project has ever been
checked the way the other stacks were). Community Toolkit's
DataGrid (CommunityToolkit.WinUI.UI.Controls.DataGrid) is the project owner's chosen future grid
control, but swapping the ListView/Cells rendering for it is its own separate follow-up pass, not part
of this one - this pass only adds a PaginationBar under the existing grid, exactly the same scope
split TSX_Page.tt/TS_Component.tt made.

Grid/toolbar styling (2026-09-27, brushes fixed 2026-09-30): every WinUI3 grid (this page's own row
grid and WinUI3_DetailMasterScreen.tt's child grids) is boxed in a ControlStrokeColorSecondaryBrush
Border with a line under the header and under every row, so it reads as a table instead of loose rows
of text. The Add New/Refresh bar, the search bar, and the pagination bar all share one look - a
SolidBackgroundFillColorSecondaryBrush Border with a matching ControlStrokeColorSecondaryBrush outline
- so the three toolbars read as one family instead of plain unstyled StackPanels. Both are theme
resources, so they follow the app's light/dark theme automatically instead of a hardcoded color.
Originally used the Card* brushes (CardStrokeColorDefaultBrush/CardBackgroundFillColorDefaultBrush) -
switched away 2026-09-30 after a live user report of no visible shading at all: Card* is a near-
transparent tint meant to sit on top of another surface, and CardBackgroundFillColorDefault is
#B3FFFFFF in the Light theme dictionary (70%-opacity white), invisible over this page's own plain
white background. SolidBackgroundFillColorSecondary (#EEEEEE Light / #1C1C1C Dark) is fully opaque and
reads as real shading against a plain page background.

PaginationBar (Views/PaginationBar.xaml and .xaml.cs) is written by the Controls essentials group
(WinUI3_EssentialControls.tt), once for all tables: a plain, purely-presentational UserControl (all page-number and enabled-state
computation lives on the ViewModel, the same ObservableObject/[NotifyPropertyChangedFor] pattern this
template already uses for HasError). Referenced through its own "views:" xmlns prefix (viewNamespace), not "local:" - that
prefix is already claimed by viewModelNamespace for <TableName>ListRow's own DataTemplate, and reusing it
for PaginationBar fails to compile (WMC0001: Unknown type 'PaginationBar' in XML namespace
'...ViewModels').
```

The row id (the ListRow's Id and the Edit / Clone / Delete buttons' Tag) has the key's C# type (int, long, Guid or string, Core's KeyType). A text key is also the first column
of the grid: the person typed it and it means something.


## Type-ahead for a big parent

A parent with more rows than `TypeAheadRows` (default 1000, read from the database when the schema is read) or listed in `TypeAheadParents` is not read whole. The search-key filter is an `AutoSuggestBox` that asks the context for the first ten rows whose text contains what was typed (`Suggest<Column>FilterAsync`); typing takes the filter off, picking a row filters the grid to it. The grid reads only the names of the rows of the page. A `SemaphoreSlim` makes a load and the suggestions take turns on the one DbContext.
The rule is `FormParent.TypeAhead` in Core, the same for every stack; `TypeAheadRows=0` turns the automatic choice off. The API answers `GET <route>/lookup?search=text&take=10` (the first rows whose lookup text contains it) and `GET <route>/lookup?ids=1,2` (the names of those keys): see API_EssentialCrudApi.

**Columns menu.** The header is built in code for the shown columns (`BuildHeader`), and the row draws their texts in `GridCells`. The Columns button beside Refresh lists every column of the form (`ChosenGridColumns`, the same rule as the web) with a check mark; the grid starts with the columns `GridColumns` picks, and the choice is kept between runs (`GridColumnStore`, `GridColumns.json`; Reset forgets it). A long or multi-line text is offered and shows its first 60 characters. Hiding the sorted column drops the sort. The text of the members is `DesktopGridColumns` in Core.
