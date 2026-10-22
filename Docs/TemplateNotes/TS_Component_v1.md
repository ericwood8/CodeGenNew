# TS_Component_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates the four files of an Angular component, in the folder components/<table>/ (the paths are written by the
template itself, see GeneratedFiles - point the output folder at the Angular app's src/app folder):
    <table>.component.css        empty (styling comes from the global styles)
    <table>.component.html       the grid, search box and add/edit form
    <table>.component.spec.ts    the standard "should create" test
    <table>.component.ts         the standalone component
a grid of the table's rows with Edit and
Delete buttons, an "Add New" button, a search-by-name box (only when the table has a text Name column) and an add/edit
form that opens under the grid.

  - Names: the table name without its "E_" / "SY_" prefix, lower-cased for the folder, files and selector
    (E_TimeSheet -> components/timesheet/timesheet.component.ts, selector app-timesheet, class TimeSheetComponent). It uses
    <Table>Service (TS_Service) and the <Table> interface (TS_Model), so generate those first.
  - Grid and form: one column / one field per column of the table except the key, computed and identity columns, and
    foreign keys to the display-style table SY_Display; "Name" comes first, the rest in table order. The control follows
    the type: text box (a text area from 100 characters up, which the grid leaves out unless that would leave it empty), number box, checkbox for bit, date
    box for date/datetime, and a drop-down for a foreign key.
  - Foreign keys: a drop-down of the parent's rows, loaded from the parent's service (its getAll), showing the parent's
    display column (the one CodeGenNew picks for a Lookup: Name, ShortDescr ...); the grid shows the same name instead of
    the id. A parent that is an enum/lookup table, or whose screen needs cascading drop-downs, is listed in
    the NoLookupParents setting and its foreign key stays a plain number box.
  - Key (read from `KeyType`): an int key is a number, a uniqueidentifier key a string (the route takes {id:guid}; the api assigns the value on
    create - a new row is sent with the empty GUID, as an int key is sent as 0). A text key (a country or currency code) is a string the
    person types: it is the first field and a column of the grid, editable on a new row and read-only on an existing one (`[readonly]="!adding"`),
    a new row starts with `''`, and the screen says `keyChosen = true` so the key is sent as typed. The heading and the child-grid test use the base's
    `adding` (a typed key is set on a new row too, so the key cannot say whether the row is new). A refused duplicate (409) shows the API's own words and
    keeps the form open. Any other key (composite, a date) is refused. The screen loads ALL rows (no paging), so it suits tables of
    hundreds or a few thousand rows, not tens of thousands.
  - Dates: the API sends and takes "2025-12-25T00:00:00" strings; the date box wants "2025-12-25", so edit() trims the
    time off and the grid formats with the date pipe. A new row starts today, a new number at 0, a new text at "", a new
    bit at its database default.
  - Errors: alert(), as in the other screens: 400 = bad value (the API's own rejection), 404 = gone, delete's 400 = "in use".

A table with a Name and an IsActive column has no search endpoint (its API is `NameActiveCrudApi`): its screen is a short class on `NameActiveCrudScreen` that reads the whole list, with a name box and an "Active only" check box that narrow it on the page (`visibleRows`), an IsActive check box in the form, and no paging or sorting. `Screens` must list such a table for it to get a screen (the default list is the tables with a search endpoint). A foreign key to the table itself (an employee's manager) is a drop-down read through the screen's own service.

Not written, so a screen that needs any of these stays hand-maintained: a detail grid (Department's teams, Project's
tasks, TimeSheet's lines), drop-downs that depend on each other (Employee's team follows its department), fields that
are computed on screen, and the route / sidebar entry / app.config line that show the screen (ComponentChecklist.txt
steps 4-6).

When the rows change under the grid (found by moving a real server to server paging): a delete reads the page again (load()) instead of removing the row locally, so the
total, the rows that move up and the last page stay right; a page past the end (the last row of the last page was deleted) falls back to the last page there
is; the page size picked in the paginator (10, 20, 50, 100) is used; and "Nothing found." is shown when the server answers with no rows, but not before the
first page has arrived (loaded). The generated spec checks the first request (pageNumber 1, the page size) and the fall back. TS_DetailMasterComponent,
TSX_Page, TSX_DetailMasterPage and BLZ_Page do the same delete and fall back (the pager of the React and Blazor pages has no page size choice or empty message).

Pagination: the grid always loads one page at a time
through TS_Service.tt's getPage instead of getAll(), with Angular Material's mat-paginator underneath -
@angular/material is a real, already-installed dependency of the Angular project this family is
written against (^18.2.14), and two of its own screens (Project, Request) already use mat-paginator,
just client-side only (getAll() loads everything, then slices locally). This generates the OTHER
standard, equally documented way to use the same control instead: [length] set to the server's
totalCount rather than a local array length, and the (page) event triggers a new getPage() call rather
than a local re-slice. Requires the target project to have @angular/material's MatPaginatorModule
available (add it once, the same "assumed available" treatment TS_Service.tt/TSX_Api.tt already give
request<T>/client.ts) - unlike the React/WinUI3 pagination bar, this one is NOT hand-rolled by
CodeGenNew at all, since the library already exists in the real target project. **Update (2026-09-28):**
pagination no longer requires a searchable column - a table with none (e.g. a monthly-summary table
keyed only by ids/numbers) still gets it, found live needing exactly this; pagination and searchability
are separate concerns.

Search: when the table has a searchable column,
TS_Service.tt's getPage takes one optional filter per TableModel.SearchableColumns column, so this
template replaces the old single "Search by Name" box with one text input per searchable column
(bespoke per table, like the add/edit form - there is no shared "search bar" component the way
PaginationBar/mat-paginator are, since the field list differs per table), a Search button that resets to
page 0 and re-fetches with the current filter values, and a Clear button that empties every field and
re-fetches unfiltered. A table with no searchable column gets no search UI at all, but still pages
through every row via the plain, unfiltered getPage() call.
```

## Now a short class on PagedCrudScreen

The component class is `export class HolidayComponent extends PagedCrudScreen<Holiday>`. It injects its service and names the `noun`, `idKey`, `gridKey` and `sortableColumns`, the `filters` (one box per searchable column), `newRow()`, `prepareEdit` (date boxes), `opened` (tabs), `start` (parent lists for the drop-downs) and the name-of methods. Loading, paging, sorting, add / edit / submit / delete / clone / cancel, the dialog, the way back and the error messages are in crud-screen.ts (Crud essentials group; see TS_EssentialCrud_v1.md). The html reads `rows`. A generated screen is 50 to 60 lines instead of about 300. The behaviour described above is unchanged.

## Drop-downs read the lookup list

A drop-down over a parent with a single-column key (a number, a uniqueidentifier or text) and an API is filled from `getLookup()` (`GET <route>/lookup`: `{ id, name, isActive }`) instead of `getAll()`, so a big parent table is not sent in full to fill a list; the "name of" method reads the same list. The option uses `p.id` and `p.name`, the list is typed `Lookup[]` (from `crud.service.ts`) and the parent's model is no longer imported for it. The list is typed `Lookup<string>[]` for a guid or text key, and a type-ahead over such a parent is a `TypeAhead<string>`. A foreign key to the table itself reads its own lookup. `TS_DetailMasterComponent` does the same for its drop-downs, and its child grids name an id from the parent's `/lookup` list.

## Required fields and the form

The form is checked by `CrudScreen.submitForm` (`crud-screen.ts`, written by TS_EssentialCrud), not by `ngNativeValidate`: Angular's `novalidate` switches the browser's own check off, and the base class asks the form for the first field that is invalid AND no longer shows the value the row opened with, then shows it with the browser's message (`reportValidity`). A blank required field still stops the submit; an old stored value that breaks a rule (an email without an @) in a field the person left alone does not. In a tabbed form `showBroken` first opens the tab of that field (`data-tab` of each panel). A new row checks every field.

## Credentials and long text

A text column named like a credential (`ColumnModel.IsCredentialColumn`: a name with `Password` in it, or ending in `Salt`, `Secret`, `ApiKey` or `Token`) is in neither the grid, the form nor the search bar. A row that is edited keeps the value it was read with; a new row starts with `''`. A table that must not be read at all belongs in `NoApiTables`. A grid that would show fewer than `GridColumns.MinColumns` (3) columns because its text is 100 characters or longer is filled up with the first of those columns (a column of 500 characters or more, or one named like a note, stays out).

## Stamps, money, short boxes, bold buttons

A column the system writes (`ColumnModel.IsSystemStampColumn`: CreateDate, CreateUser, ModifiedDate, ModifiedBy, LastDateChanged) is a label with the value once the row exists, never a box, and its timestamp is not cut to a date when the row is opened. A money column (a money type, or a decimal, float or real whose name has a Fee, Balance, Amount, Surcharge, Upcharge, Discount, Price, Total, Average, Tax, Cost or Rate word and no percent, quantity, hours or ratio word) is edited in a money box showing $1,234.50 (React `CurrencyInput`, Angular `appCurrency`; WinUI3 already had a currency NumberBox) and the grid shows at least two decimals. A whole number with short values (a month, a year, a tinyint) gets a short box (`ProjectSettings.NumberBoxCharacters`). Every button's caption is bold (the Styles / Layout / App essentials: refresh them with `--replace`). An editable child with more than six columns is one block of labelled fields per row (`EditableChild.UsesCards`).

## Required fields

A required field (NOT NULL, other than a yes/no box and the stamps the system writes; `FormModel.IsRequired`) gets the native `required` attribute and, unless `RequiredMarks=false`, a red `*` written after its caption: `<span aria-hidden="true" style=...>*</span>`. Screen readers skip the mark because the field itself says it is required. There is no legend line. Save is not disabled; the browser refuses an incomplete submit as before.

**Roles** (`Auth=true`, `ApiAuth=true`): a screen of a table with `ReadRoles` carries `roles`; `app.routes.ts` then gets a `canRead` guard that sends a person whose role may not read it to the first screen they may (and the shell menu, from the Shell essentials group, lists only those). The Add New, Edit, Clone and Delete buttons of a table with `WriteRoles` are hidden by `canWrite`, and the generated spec checks Add New for a listed role and an unlisted one. A project made before needs `--replace` of the Auth and Shell essentials.

## The Columns list

The grid starts with the columns `GridColumns` picks and offers every column of the form in a **Columns** button above it (a native `<details>` of check boxes and a **Reset**): the key and a credential are never offered, a long-text column is offered but not shown at first, and the last shown column cannot be cleared. Which columns are offered and shown first is `ChosenGridColumns` in Core, the one rule the React and Blazor grids also read. A name / active list has no such button.

- The screen holds a `GridColumns` (`grid-columns.ts`, `TS_GridColumns`): the columns the table offers, then the ones the grid starts with, and the person's choice kept in the browser (`gridColumns:<grid>` in local storage). Its `sortable` and `properties` arrays change in place, so the screen's `sortableColumns` and (OData) `selectColumns` are those arrays and the base classes need no change.
- Every header, filter cell and cell carries `*ngIf="columns.has('Name')"`, so a hidden column draws nothing. A long or multi-line text cell shows its first 60 characters (`cut`).
- A sort on a column that is then hidden is dropped (`clearSort()` on a REST screen; the saved sorts on an OData screen), and so is a filter typed for it.
- **OData:** the page asks (`$select`) for the shown columns and the key and reads again when the choice changes. **REST:** rows are whole, so a column that is shown costs no call; the server sorts by every column that is not long text or binary (`SearchSort`), so a column the grid did not start with can sort too.
- The parent lists of the form are read when the screen opens, so a foreign key shown late needs no extra call.


## Type-ahead for a big parent

A parent with more rows than `TypeAheadRows` (default 1000, read from the database when the schema is read) or listed in `TypeAheadParents` is not read whole. The form field and the search-key filter are a box (`TypeAhead` in `services/type-ahead.ts`) that asks the parent's service `getLookup({ search })` after a pause and lists the matches; the grid and the form read the name of a key through `LookupNames`, which asks for the keys it has not seen together, once. Typing clears the key, so text that was not picked leaves nothing chosen. The OData screen does the same, in the form and in the filter row's parent column (the key picked is sent as an `eq` condition).
The rule is `FormParent.TypeAhead` in Core, the same for every stack; `TypeAheadRows=0` turns the automatic choice off. The API answers `GET <route>/lookup?search=text&take=10` (the first rows whose lookup text contains it) and `GET <route>/lookup?ids=1,2` (the names of those keys): see API_EssentialCrudApi.

- **Stamped dates in the grid:** a created / modified / last-changed column that holds a time (not a plain `date`) shows the day and the time of day (`CellKind.DateTime`: `2026-10-17 16:52`); every other date shows the day only.

**Generated specs that look at the page.** Besides `should create` and the paging specs, the spec flushes the first page with one row, runs change detection and checks the row is in the page, and presses the row's Edit button and checks the dialog heading, then presses Edit again and checks the dialog holds the row's value and that Submit sends the row in a PUT (`GridTestRow` in Core builds the row, the same one the React tests use). A screen with write roles signs the spec in first (`WriteRoleSpec.SignIn` in the `beforeEach`, cleared in `afterEach`). The OData screen answers the read by key that Edit makes (`odata/<set>/<key>`). A table with no plain required text column marks a required whole-number column instead (the cell shows 42). A last `describe` opens the screen with `?edit=<key>` (a stubbed `ActivatedRoute`) and checks the dialog shows that row.
