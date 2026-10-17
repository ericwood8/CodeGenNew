# BLZ_ODataPage_v1

The OData form of the Blazor page: `Pages/<Table>ODataPage.razor` over the table's `<Table>ODataClient`, with its own route (`<table>-odata`) and menu entry (`<Table> (OData)`, after the REST ones). The REST page is not touched.

`BLZ_ODataPage_v1.tt` is a stub. Its config says `Base=BLZ_Page_v1.tt` and `Variant=OData`: the engine runs the text of `BLZ_Page_v1.tt` with the template parameter `Variant` set to `OData`; with no variant the output is byte for byte what it was. The fields, the form, the grid columns, the captions, the drop-downs (read from the parents' REST lookup routes), the validation and the paging are the REST page's.

What differs:
- the list is `GET /odata/<set>?$count=true&$top=&$skip=&$filter=&$orderby=`;
- **several-column sort**: a click sorts by the column (again: flips it), a Shift-click adds it as the next sort (again: flips it, once more: takes it out); a column in a several-column sort shows its place in a badge and `aria-sort`;
- **filter row** under the headers, a control per column by its kind (`ODataFilterFields` in Core): text, number and date ranges, yes/no, a drop-down of the parent's rows (a parent with too many rows is a box that finds the row by typing, as in the form; the key picked is sent as an `eq` condition), a drop-down of the values the database lists and an "empty" check box for a column that can be empty. Enter in a box searches; Search and Clear are the last cell. The search boxes of the REST page are not there;
- a master's child grids read the child's rows with a filter on the parent's key when the child is served as OData too (`ODataPlan.ServesByName`), else through the child's REST client;
- a 409 shows the API's own words (a duplicate), a delete of a row in use says it is in use; Clone goes through the REST route.

`ODataScreens=Replace` gives the OData page the REST page's route (`@page "/customer"`) and the REST page loses its `@page` line, so two pages never share an address. Written only for the tables the project serves as OData and that have a Blazor page. Needs the `OData` essentials group and `BLZ_ODataClient`.

**Sorts and slim rows:** the sorts are kept in the browser (localStorage, `<project>.sorts.<page>`) and the next visit opens with them; a saved column the grid no longer sorts by, or a value that is not the saved list, is ignored. The grid asks for only the columns it shows (the person's choice in the Columns list, see `BLZ_Page_v1.md`) and the key (`$select`); a row is read whole by its key (`OpenAsync`) before the form opens it, so the form always has every column.

**Checked:** generated for a scratch copy of the SQL Server InvoiceSystem and run against the generated API in the browser: the first page and the page count, a click sort and a Shift-click second sort (`$orderby=customerName desc,customerStatus/description,customerId`, badges 1 and 2), the filter row (a text, a parent drop-down, a date range, a number range searched with Enter, yes/no, "empty" and Clear), Edit (the row saved by PUT and read back from the database), Clone (the REST route), Add, Delete, a delete of a row in use ("It cannot be deleted because it is in use!") and a customer's invoices read by the parent's key. With `ODataScreens=Replace` the OData page answered at `/customer`.
