# UNO_ODataPage_v1

With `OData=true` the API also serves the tables as OData v4 (`/odata/<set>`), and the Uno app gets, for each served table that has an Uno screen (`ODataPlan.UnoScreenTables`):

- `Services/<Table>ODataClient.cs` (`UNO_ODataClient`): a short class on `ODataClient<T, TKey>` with the entity set, the key, the search boxes, the header sorts and the order without a sort (the table's `SearchPlan`, so the REST page and this page offer the same ones);
- `Views/<Table>ODataListPage.xaml` and `.xaml.cs` and `ViewModels/<Table>ODataListViewModel.cs` (`UNO_ODataPage`, which is `UNO_MasterScreen` run with `Variant=OData`): the same list page, named for the OData form, whose rows come from `QueryPageAsync`: `$count`, `$top` and `$skip` for the page, `$filter` (a contains match, ignoring case) for the search boxes and `$orderby` for the header sort. Add, Edit, Clone and Delete go through the table's REST client and dialog; the page has no parent drop-down filters;
- `Services/ODataSupport.cs` (the `OData` essentials group, ticked by `OData=true`): the query helper, the filter kinds and the client base, the Blazor ones (`BLZ_EssentialOData`) without the browser storage. Needs the `Support` group.

The menu gets a second entry "<Table> (OData)" (tag `<Table>OData`) after the REST screens; `ODataScreens=Replace` puts the OData page in the REST page's place (same menu entry). `ODataTables` limits the tables served.

Limits: no filter row (the per-column conditions) and no several-column sort; the sort is one column, remembered between runs (`GridSortStore`).

**Checked** (2026-10-18): the generated API over `InvoiceSystem_test` served `/odata/customers` (`$count`, `$select`); the desktop app's "Sales Invoice (OData)" page listed 51 rows in 3 pages and a search for "7" in Customer PO gave 8 rows.
