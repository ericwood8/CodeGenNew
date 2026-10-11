# TSX_DetailMasterPage_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
TSX_Page_v1.tt's own grid + add/edit form - identical field selection, lookups, error-as-state and
rail/no-invented-classes rules, see that template for the reasoning - PLUS one read-only grid per table
in TableModel.ChildForeignKeys, shown under the form while editing an existing row. The React counterpart
of TS_DetailMasterComponent.tt, closing the same gap TSX_Page.tt's own header comment names explicitly
("not written: a detail grid").

Per the same answer that shaped WinUI3_DetailMasterScreen.tt/TS_DetailMasterComponent.tt ("just have the
generated code assume all are grids and the developer can cut out what grids are not needed"): EVERY
table in ChildForeignKeys gets a grid, unconditionally.

A child grid calls the child table's API directly (request<any[]>('/api/<child>s'), filtering client-side
by the foreign key column) instead of importing a generated <Child>Api module the way every other lookup
in this template does - same reason TS_DetailMasterComponent.tt's own header comment gives: TableModel is
built for exactly one table per generation run, so this template never sees the child table's own column
list or knows what its generated (or hand-written) api module looks like. Rows are loosely typed (any[]);
columns are whatever keys the first loaded row has (Object.keys), discovered at run time rather than
generation time - the same "read the shape at run time, not generation time" idea the Angular and WinUI3
families already use for this exact gap.

Only loaded once the row being edited has a real key (a brand-new, unsaved row has no child rows yet);
the child sections show a "save first" message instead while adding. A text key is typed on a new row,
so the key cannot say whether the row is new: the page has an `adding` flag (set by Add New, cleared by
Edit) that picks the heading, the create or update call, the read-only key box and the child grids. The
links to a child's own page and back (?edit=<key>&back=...) encode a text key, the parent's and the
child's, and so does the child's delete route.

Requires a primary key that is a single int, uniqueidentifier or text column (same as TSX_Page.tt) and at
least one table in TableModel.ChildForeignKeys (TableModel.HasAtLeastOneChildForeignKey) - a table with
none is exactly what TSX_Page.tt is for.
```

## Credentials and long text

A text column named like a credential (`ColumnModel.IsCredentialColumn`: a name with `Password` in it, or ending in `Salt`, `Secret`, `ApiKey` or `Token`) is in neither the grid, the form nor the search bar. A row that is edited keeps the value it was read with. A grid that would show fewer than `GridColumns.MinColumns` (3) columns because its text is 100 characters or longer is filled up with the first of those columns.

## The Search list protocol

With `ListProtocol=Search` the search bar is one box (`filters.search`, placeholder `Search`) instead of one box per searchable column; the API decides which columns it matches (`SearchColumns`, `SearchBox`). Paging, the Clear button and the sort are as before.

## Editable child rows (`EditableChildren`)

A child table the project lists in `EditableChildren` (`Project.ProjectTask`) is a table of inputs inside the form instead of a read-only grid under it, the same rule as the Angular page (`EditableChild` in Core: the columns, which child is editable). One input per child column except its key, the link to the parent, computed, identity and audit columns: a text box, a date, a number with its limits, a check box, a drop-down of the values the database lists, and for the child's own foreign key a drop-down read from `GET /api/<parents>/lookup` (a uniqueidentifier key keeps a plain box). **Add** appends a row with a value for every NOT NULL column, **Remove** drops it; nothing is saved until **Submit**, which sends the rows under the child's plural name (`projectTasks`) in the body of the create or update. The API makes the table match the list (a row with its key is changed, a row without one is added, a row left out is removed, all in one transaction), so the grid and the form stay in step after Submit. The inputs carry `required` like the main fields, so the browser refuses a blank one; a refused save (400) keeps the dialog open with the message. A child that is not listed keeps its read-only grid and its "Save this row first" note. The page needs the `CrudApi` group refreshed with `--replace` for a project that already has it. Run in a scratch React app: edit a row (change one task, remove one, add one), add a row with a task, and a blank required field refused.

## Stamps, money, short boxes, bold buttons

A column the system writes (`ColumnModel.IsSystemStampColumn`: CreateDate, CreateUser, ModifiedDate, ModifiedBy, LastDateChanged) is a label with the value once the row exists, never a box, and its timestamp is not cut to a date when the row is opened. A money column (a money type, or a decimal, float or real whose name has a Fee, Balance, Amount, Surcharge, Upcharge, Discount, Price, Total, Average, Tax, Cost or Rate word and no percent, quantity, hours or ratio word) is edited in a money box showing $1,234.50 (React `CurrencyInput`, Angular `appCurrency`; WinUI3 already had a currency NumberBox) and the grid shows at least two decimals. A whole number with short values (a month, a year, a tinyint) gets a short box (`ProjectSettings.NumberBoxCharacters`). Every button's caption is bold (the Styles / Layout / App essentials: refresh them with `--replace`). An editable child with more than six columns is one block of labelled fields per row (`EditableChild.UsesCards`).
