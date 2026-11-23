# TSX_ODataApi_v1

`api/<table>ODataApi.ts` for each table the project serves as OData (`OData=true`, `ODataTables`; `ODataPlan` in Core is the one rule) that can have a React page: a short module on `createODataApi` (`TSX_EssentialOData`) with the entity set (`/odata/<set>`), the key, the search boxes (the table's `SearchPlan` filters), the header sorts (a foreign key sorts by its parent's display column when the parent is served too), the order without a sort, the display column of a drop-down and the REST route a clone goes through.

It is the OData twin of `TSX_Api`: the same model (`models/<table>`), the same row JSON and the same method names (`getAll`, `getLookup`, `getById`, `create`, `update`, `delete`, `clone`, `getPage`), another route. A name/active table gets one too (`getAll` follows the next links; the page narrows the list itself). The page over it is `TSX_ODataPage`.

`PartyTables`: the module of a party table also has `byIdentifier`, which asks the table's REST route (`/api/<set>/by-identifier`) for the page's Find by identifier box.

`RateTables`: the module of a rate table also has `rateOn`, which asks the table's REST route (`/api/<set>/rate`) for the page's rate lookup.

`PriceListTables`: the module of a price table also has `priceOn`, which asks the table's REST route (`/api/<set>/price`) for the page's price lookup.

`InstallmentTables`: the module has a `schedule` method over the table's REST route (a price table gets its `priceOn` there too).

A product table of `FacetTables` gets `catalogFacets` and `catalogNarrow` in its OData api module too, over the REST routes (OData has no such query).
