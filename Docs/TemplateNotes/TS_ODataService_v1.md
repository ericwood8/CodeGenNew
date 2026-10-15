# TS_ODataService_v1

`services/<table>-odata.service.ts` for each table the project serves as OData (`OData=true`, `ODataTables`; the rule is `ODataPlan` in Core, the same one `API_OData` uses). Whole-database, so it knows which parents are served. Opt in with `OData=true` (the plan writes it with the Angular stack) or `PlanAlso=TS_ODataService`.

A short class on `ODataCrudService` (`services/odata-crud.service.ts`, the `OData` essentials group): the entity set (`odata/<set>`, the REST route's name), and what the entity set does not say:
- `key`: the key property.
- `filters`: the search boxes, one per searchable text column (the table's `SearchPlan`, the same boxes the REST screen has), and the property each filters. The one `search` box of `ListProtocol=Search` matches all of them.
- `sorts`: the header names the screen sorts by and the OData path of each: the column itself, or for a foreign key `parent/displayColumn` (`customer/accountNumber`) when the parent is served as OData too and the entity can reach it. A parent that is not served would give a 400 in `$orderby`, so such a header sorts by the foreign key value.
- `defaultOrder`: the order without a sort (the best display column, then the key).
- `lookup`: the display column (and the active flag) a drop-down reads.
- the REST route a clone goes through (`api/<set>`; OData has no clone verb).

Every name is the JSON name the REST routes use, so the model (`models/<table>`) and the screens are the same. The class is the OData twin of `TS_Service`; `TS_ODataComponent` is the screen over it.
