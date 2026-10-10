# TSX_Page_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates the React counterpart of TS_Component.tt - same field selection, lookups and rules, see that
template for the reasoning - but as a single function component in one file (pages/<Table>Page.tsx),
the way a typical React project builds its pages: a
named export, useState/useEffect instead of a class with fields and ngOnInit, and controlled inputs
(value + onChange) instead of [(ngModel)] two-way binding. A grid of the table's rows with Edit and
Delete buttons, an "Add New" button, a search-by-name box (only when the table has a text Name column)
and an add/edit form that opens under the grid - functionally identical to TS_Component.tt's screen.

Two real, deliberate departures from TS_Component.tt, found by inspecting a real React
project (the one this template was written against)
rather than assumed up front:
  - Errors are a plain `error` state string rendered as {error && <p>{error}</p>}, not alert() -
    that project's own pages all use exactly this pattern and
    never call alert() anywhere in the app; alert()'s blocking dialog is TS_Component.tt's own concession
    to a simpler Angular example, not a React convention worth carrying over.
  - No className is invented beyond "rail" (the one outer-wrapper class the project's index.css
    defines and that a generated page can safely assume exists). TS_Component.tt assumes the Angular project
    already has "btn"/"form-group"/"form-container"/"btn-action" classes defined globally - the React project
    has no such generic CRUD-screen vocabulary at all (grep of its whole src/ tree found none of the four,
    only product-specific classes like movie-grid/movie-card and a single ".button", not ".btn"). Carrying
    the Angular family's class-name assumptions into React uncritically would have silently generated
    unstyled/wrongly-named elements; every button, input, table and form below is bare semantic HTML instead.

No companion CSS file is generated (unlike TS_Component.tt's always-empty <table>.component.css): React
has no styleUrl mechanism requiring the file to exist, and the project styles everything from one
global stylesheet rather than a per-component one, so an empty per-page .css here would be dead weight.

  - Names: the table name without its "E_"/"SY_" prefix, lower-cased for the api import and PascalCase
    for the component (E_TimeSheet -> pages/TimeSheetPage.tsx, function TimeSheetPage). It calls
    <table>Api (TSX_Api.tt) and imports the <Table> interface from TS_Model.tt's output, so generate
    those first.
  - Grid and form: identical column/control selection to TS_Component.tt - one column/field per column
    except the key, computed and identity columns, and foreign keys to the display-style table
    SY_Display; "Name" first, then table order; text box (textarea from 100 characters), number box,
    checkbox for bit, date box for date/datetime, and a <select> for a foreign key.
  - Foreign keys: a <select> of the parent's rows, loaded from the parent's own <parent>Api.getAll() (the
    generated sibling file, called by name - same cross-template contract TS_Component.tt already has
    with TS_Service.tt), showing the parent's display column; the grid shows the same name instead of
    the id. A parent listed in noLookupParents stays a plain number box.
  - Key: an int key is a number, a uniqueidentifier key a string, same rule as TSX_Api.tt (Core's
    KeyType). A text key is the first field and a grid column: typed on a new row, read-only on an
    existing one. Because a typed key is set on a new row too, the page keeps an `adding` flag (set by
    Add New, cleared by Edit) and uses it for the heading, for create versus update and for the read-only
    box; a duplicate key (the API answers 409) says "A country with this code already exists!". A
    foreign key to a text key parent is a <select> of codes (string ids, a blank first choice). The
    generated test opens the form for a new and for an existing row and checks the key box is open and
    then read-only (jsdom has no showModal, so the test gives the dialog one that only opens it).
  - Dates: the API sends and takes "2025-12-25T00:00:00" strings; the date box wants "2025-12-25", so
    edit() trims the time off. Shown in the grid as the same trimmed 10 characters (no date-formatting
    library assumed) rather than TS_Component.tt's locale-formatted date pipe - a deliberately smaller
    claim, since a generated file cannot assume a project has picked a date library. A new row starts
    today, a new number at 0, a new text at "", a new bit at its database default.

Not written, so a screen that needs any of these stays hand-maintained: a detail grid (see
TSX_DetailMasterPage.tt for that gap), drop-downs that depend on each other, fields computed on screen,
and the route / nav-link line that shows the screen (a project wires each page into App.tsx's
<Routes> by hand; a generated page is not self-registering).

Around the bar: a "Rows per page" box (10, 20, 50, 100; load() takes the size as its last argument because the state is not updated yet), "Nothing found." once
the first page has arrived with no rows, a delete that reads the page again, and a page past the end that falls back to the last page there is. These are
written in the page, not in PaginationBar.tsx: that file belongs to the essentials (written once, never overwritten), so a project made earlier keeps the old
bar and a new prop on it would not compile.

Around the bar: a "Rows per page" box (10, 20, 50, 100; load() takes the size as its last argument because the state is not updated yet), "Nothing found." once
the first page has arrived with no rows, a delete that reads the page again, and a page past the end that falls back to the last page there is. These are
written in the page, not in PaginationBar.tsx: that file belongs to the essentials (written once, never overwritten), so a project made earlier keeps the old
bar and a new prop on it would not compile.

Pagination: the grid always loads one page at a time
through TSX_Api.tt's getPage instead of getAll(), with a Previous/Next PaginationBar underneath,
matching the usual page/totalPages control. **Update (2026-09-28):**
pagination no longer requires a searchable column - a table with none (e.g. a monthly-summary table
keyed only by ids/numbers) still gets it, found live needing exactly this; pagination and searchability
are separate concerns.

Search: when the table has a searchable column,
TSX_Api.tt's getPage takes a filters object with one optional property per TableModel.SearchableColumns
column, so this template replaces the old single "Search by Name" box with one controlled text input per
searchable column (bespoke per table, like the add/edit form - there is no shared "search bar"
component the way PaginationBar is, since the field list differs per table), a Search button that
resets to page 1 and re-fetches with the current filter values, and a Clear button that empties every
field and re-fetches unfiltered. A table with no searchable column gets no search UI at all, but still
pages through every row via the plain, unfiltered getPage() call.

PaginationBar is NOT generated by this template - assumed to already exist in the target project's
components folder, the same way TS_Service.tt/TSX_Api.tt assume a shared request<T>/client.ts (CodeGenNew
does not generate that file either). Add components/PaginationBar.tsx once per project:

    export function PaginationBar({ page, totalPages, onPrevious, onNext }: {
      page: number; totalPages: number; onPrevious: () => void; onNext: () => void;
    }) {
      return (
        <div className="pagination">
          <button type="button" onClick={onPrevious} disabled={page <= 1}>Previous</button>
          <span>Page {page} of {totalPages}</span>
          <button type="button" onClick={onNext} disabled={page >= totalPages}>Next</button>
        </div>
      );
    }
```
