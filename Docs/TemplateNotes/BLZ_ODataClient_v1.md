# BLZ_ODataClient_v1

`Services/<Table>ODataClient.cs` for each table the project serves as OData (`OData=true`, `ODataTables`; `ODataPlan` in Core is the one rule) that has a Blazor client: a short class on `ODataClient<T, TKey>` (`BLZ_EssentialOData`) with the entity set (`/odata/<set>`), the key, the search boxes (the table's `SearchPlan` filters), the header sorts (a foreign key sorts by its parent's display column when the parent is served too), the order without a sort and the REST route a clone goes through.

It is the OData twin of `BLZ_Client`: the same model (`Models/<table>`), the same row JSON, another route. `BLZ_Screens` registers every OData client with `AddApiClients()`. The page over it is `BLZ_ODataPage`. A name/active table gets none (Blazor has no page for it).

For a table with a `GuardedColumns` rule (`ApiAuth=true`) the class also has `GetGuardsAsync()`, which asks the table's REST route (`/guards`); the OData page shows a guarded column by its answer (see `BLZ_Page_v1.md`).
