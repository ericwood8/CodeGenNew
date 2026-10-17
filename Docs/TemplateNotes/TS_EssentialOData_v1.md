# TS_EssentialOData_v1

The `OData` essentials group of the Angular stack (`src/app/services/odata-query.ts`, `odata-crud.service.ts` and their specs). Written once and kept; a replacing run refreshes them. It is turned on by `OData=true` (or named with `--groups OData`) and needs the `Crud` group.

- **`odata-query.ts`:** `odataString` and `odataLiteral` (a text becomes one literal with every single quote doubled, so a person's text can never end it; a number, date or guid that is not one is refused, never sent), `containsText` / `containsAny` (`contains(tolower(name),'text')`, the form that finds the same rows on SQL Server, PostgreSQL and MySQL), `and`, `orderBy`, `queryString` (every value URL-encoded with `encodeURIComponent`, so a plus sign or an ampersand cannot change the query; Angular's `HttpParams` leaves a plus sign alone) and `relativeLink` (the server's next link without its host, so the call goes through the same origin and proxy as the first).
- **`ODataCrudService<T, K>`:** a `CrudService` over `odata/<set>`. It overrides `getAll` (follows `@odata.nextLink`), `getLookup` (`$select` of the key, the display column and the active flag; `ids` become `key eq a or key eq b` (at most 200), `search` or `take` become `contains(tolower(name),'text')` and `$top`, 25 by default and 1 to 100, as the REST lookup answers), `searchPage` (`$count`, `$top`, `$skip`, `$filter` from the search boxes, `$orderby` from the sort and the default order) and `clone` (the REST route). `getById`, `create`, `update` and `delete` are the base's: the entity set takes the same key route and JSON.

The specs test the helper and the service once, with the request each call makes.

- **`ODataCondition` and `conditionFilter` (odata-query.ts):** the filter row's conditions (contains, a range, an exact value or null) as pieces of `$filter`; a date range ends before the day after its last day.
- **`queryPage` (ODataCrudService):** `searchPage` with several sorts, conditions and a `$select` (the key is always read).
- **`ODataPagedScreen` (components/odata-screen.ts, with its spec):** the base of the OData screens: `PagedCrudScreen` with the several-column sort (`sortBy(column, event)`, `sortOrder`, `loadSorts`, `saveSorts`), the filter row (`filterFields`, `columnFilters`, `conditions()`) and the slim grid (`selectColumns`, `open(row)`). Needs `grid-sort.ts` and `crud-screen.ts`.
