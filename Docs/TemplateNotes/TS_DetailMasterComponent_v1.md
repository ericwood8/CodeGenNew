# TS_DetailMasterComponent_v1

## Editable child rows (`EditableChildren`)

The project setting `EditableChildren=Department.DepartmentTeam,ProjectTask` makes the listed child tables part of the parent's form instead of a read-only grid under it: a table of inputs inside the form (one column per child column except its key, the link to the parent, computed, identity and audit columns), **Add** and **Remove** buttons, and the rows go in the request body of **Submit** with the row, in one request. A child's own foreign key is a drop-down over the parent table's lookup list (a uniqueidentifier key keeps a plain box), a date is a date box, a yes/no column a check box. Rows are plain objects held in `<child>Rows`; `prepare` puts the list into the body under the child's name in the plural (`departmentTeams`, `EditableChild.JsonName`). A child that is not listed keeps its read-only grid, under the form, beside the editable one.

The API half is `CrudApi` / `NameActiveCrudApi` (`API_EssentialCrudApi_v1.md`): `API_Crud` writes `EditableChildren` on the parent's class from the same rule (`EditableChild` in Core), and create and update read the lists: a row with its key is changed, a row without one is added, a row left out is removed, all in one transaction with the parent. A row left out that is still in use (another table points at it) refuses the whole save with a 400 and nothing changes; the form stays open with the API's message. A body that has no list leaves the child rows alone.

A child qualifies when it has a single identity whole-number key and one NOT NULL link to the parent of the parent's key type, and is the parent's only link to that table. Name it `Parent.Child` when the child also points at another table (a team points at its department and its region); a plain child name counts only for a child that belongs to one table. Columns the form leaves out but the database requires without a default (a NOT NULL audit column) are the project's to fill. Angular only so far; the React detail-master page keeps its read-only grids.

The full design notes that used to head the template. The template keeps a short summary.

```text
TS_Component.tt's own grid + add/edit form - identical field selection, lookups and rules, see that
template for the reasoning - PLUS one read-only grid per table in TableModel.ChildForeignKeys, shown
under the form while editing an existing row. The Angular counterpart of WinUI3_DetailMasterScreen.tt,
for the gap TS_Component.tt's own header comment names explicitly ("not written: a detail grid -
Department's teams, Project's tasks, TimeSheet's lines").

Per the same answer that shaped WinUI3_DetailMasterScreen.tt ("just have the generated code assume all
are grids and the developer can cut out what grids are not needed"): EVERY table in ChildForeignKeys
gets a grid, unconditionally.

Why a child grid calls the child table's API directly (this.http.get<any[]>('api/<child>s')) instead
of importing a generated <Child>Service/<Child> model, the way every other lookup in this template
does: TableModel is built for exactly one table per generation run, so this template never sees the
child table's own column list or knows what its generated (or hand-written) service looks like -
TS_Service.tt's own header comment calls out a real example (TimeSheetDetailService) whose actual
hand-written service has neither a getAll() method nor the standard method names. Calling the raw API
URL directly (same "api/" + lower-cased-plural-name convention TS_Service.tt itself uses) sidesteps
that entirely - same reasoning TS_JunctionComponent.tt already used for its own HttpClient calls, and
the same "read the shape at run time, not generation time" idea WinUI3_DetailMasterScreen.tt uses EF
Core metadata for. Rows are loosely typed (any[]); columns are whatever keys the first loaded row has
(Object.keys), so renaming or excluding one is a one-line edit in the generated file once you know
the child's real shape - this exists to get you unblocked, not to be the final word. Loads the WHOLE
child table and filters client-side by the foreign key column, same "no paging" simplification
TS_Component.tt already makes for its own grid.

Only loaded once the row being edited has a real key (a brand-new, unsaved row has no child rows
yet); the child sections show a "save first" message instead while adding.

Requires a primary key that is a single int, uniqueidentifier or text column (same as TS_Component.tt; for a text key see there: the key box, `keyChosen`, and the child grids shown once the row is saved, `!adding`) and
at least one table in TableModel.ChildForeignKeys (TableModel.HasAtLeastOneChildForeignKey) - a table
with none is exactly what TS_Component.tt is for.
```

## Now a short class on PagedCrudScreen

Same change as TS_Component: the class extends `PagedCrudScreen<T>`; it adds `add()` and `edit()` overrides that clear or load the child grids, and keeps the child-grid members (rows, columns, per-grid sort, captions, cells) as before.

## Name and IsActive parents, repeated children, self references

- A parent with a Name and an IsActive column (a department with its teams) is on `NameActiveCrudScreen` like `TS_Component` describes: the same name box, "Active only" check box and `visibleRows`, no paging or sorting of the parent grid. The child grids are unchanged.
- A child table that references the parent twice (a donation has a donor and a recipient) gets one grid per foreign key; the second and later grids are named by the column's role (`donateFromEmployee`) and titled `Donate Leave (Donate From Employee)`.
- A foreign key to the table itself (an employee's manager) is a drop-down read through the screen's own service.

## Required fields and the form

The form carries `ngNativeValidate`, which gives back the browser's own check that Angular's `novalidate` switches off: a blank required field (a drop-down whose foreign key starts unset, an empty name) stops the submit with the browser's message instead of being posted and refused by the API. In a tabbed form the Submit button first opens the tab of the first invalid field (`revealInvalidTab`, from the `data-tab` of each panel), because a field on a hidden tab would otherwise block the submit silently. Nothing in the essentials changed.

## Credentials and long text

A text column named like a credential (`ColumnModel.IsCredentialColumn`: a name with `Password` in it, or ending in `Salt`, `Secret`, `ApiKey` or `Token`) is in neither the grid, the form nor the search bar. A row that is edited keeps the value it was read with; a new row starts with `''`. A table that must not be read at all belongs in `NoApiTables`. A grid that would show fewer than `GridColumns.MinColumns` (3) columns because its text is 100 characters or longer is filled up with the first of those columns (a column of 500 characters or more, or one named like a note, stays out).

## Stamps, money, short boxes, bold buttons

A column the system writes (`ColumnModel.IsSystemStampColumn`: CreateDate, CreateUser, ModifiedDate, ModifiedBy, LastDateChanged) is a label with the value once the row exists, never a box, and its timestamp is not cut to a date when the row is opened. A money column (a money type, or a decimal, float or real whose name has a Fee, Balance, Amount, Surcharge, Upcharge, Discount, Price, Total, Average, Tax, Cost or Rate word and no percent, quantity, hours or ratio word) is edited in a money box showing $1,234.50 (React `CurrencyInput`, Angular `appCurrency`; WinUI3 already had a currency NumberBox) and the grid shows at least two decimals. A whole number with short values (a month, a year, a tinyint) gets a short box (`ProjectSettings.NumberBoxCharacters`). Every button's caption is bold (the Styles / Layout / App essentials: refresh them with `--replace`). An editable child with more than six columns is one block of labelled fields per row (`EditableChild.UsesCards`).

## Required fields

A required field (NOT NULL, other than a yes/no box and the stamps the system writes; `FormModel.IsRequired`) gets the native `required` attribute and, unless `RequiredMarks=false`, a red `*` written after its caption: `<span aria-hidden="true" style=...>*</span>`. Screen readers skip the mark because the field itself says it is required. There is no legend line. Save is not disabled; the browser refuses an incomplete submit as before.

The caption of an editable child column carries the same mark (in the column header of a table, over the field of a block).

## The Columns list

The grid starts with the columns `GridColumns` picks and offers every column of the form in a **Columns** button above it (a native `<details>` of check boxes and a **Reset**): the key and a credential are never offered, a long-text column is offered but not shown at first, and the last shown column cannot be cleared. Which columns are offered and shown first is `ChosenGridColumns` in Core, the one rule the React and Blazor grids also read. A name / active list has no such button.

- The screen holds a `GridColumns` (`grid-columns.ts`, `TS_GridColumns`): the columns the table offers, then the ones the grid starts with, and the person's choice kept in the browser (`gridColumns:<grid>` in local storage). Its `sortable` and `properties` arrays change in place, so the screen's `sortableColumns` and (OData) `selectColumns` are those arrays and the base classes need no change.
- Every header, filter cell and cell carries `*ngIf="columns.has('Name')"`, so a hidden column draws nothing. A long or multi-line text cell shows its first 60 characters (`cut`).
- A sort on a column that is then hidden is dropped (`clearSort()` on a REST screen; the saved sorts on an OData screen), and so is a filter typed for it.
- **OData:** the page asks (`$select`) for the shown columns and the key and reads again when the choice changes. **REST:** rows are whole, so a column that is shown costs no call; the server sorts by every column that is not long text or binary (`SearchSort`), so a column the grid did not start with can sort too.
- The parent lists of the form are read when the screen opens, so a foreign key shown late needs no extra call.
