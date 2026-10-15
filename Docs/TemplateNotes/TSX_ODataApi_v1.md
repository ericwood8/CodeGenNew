# TSX_ODataApi_v1

`api/<table>ODataApi.ts` for each table the project serves as OData (`OData=true`, `ODataTables`; `ODataPlan` in Core is the one rule) that can have a React page: a short module on `createODataApi` (`TSX_EssentialOData`) with the entity set (`/odata/<set>`), the key, the search boxes (the table's `SearchPlan` filters), the header sorts (a foreign key sorts by its parent's display column when the parent is served too), the order without a sort, the display column of a drop-down and the REST route a clone goes through.

It is the OData twin of `TSX_Api`: the same model (`models/<table>`), the same row JSON and the same method names (`getAll`, `getLookup`, `getById`, `create`, `update`, `delete`, `clone`, `getPage`), another route. A name/active table gets one too (`getAll` follows the next links; the page narrows the list itself). The page over it is `TSX_ODataPage`.
