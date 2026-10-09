# TS_EssentialCrud_v1

The `Crud` group of the Angular essentials: five files for the whole project, `src/app/<ServicesFolder>/crud.service.ts`, `api-error.ts`, `api-error.spec.ts`, `notifier.ts` and `src/app/<ComponentsFolder>/crud-screen.ts` (`codegen essentials --stack angular --groups crud --project <name> -o <project root>`). No table and no database are needed. The folders are the project's `ServicesFolder` and `ComponentsFolder`.

They are the base classes that the generated services (`TS_Service`) and screens (`TS_Component`, `TS_DetailMasterComponent`) are short classes on.

## crud.service.ts

- `CrudService<T, K = number>` (K is `string` for a uniqueidentifier key): `getAll`, `getById`, `create`, `update`, `delete`, `clone`, and `searchPage(query)`, which calls `GET <route>/search` with `pageNumber`, `pageSize`, one parameter per search box that has text, and `sortBy` / `sortDir` only when there is a sort.
- `NamedCrudService<T, K>` adds `findByName(name)`, `GET <route>/{name}` with the name URL-encoded (`API_Crud` answers it for a table with a NOT NULL `Name`).
- `NameActiveCrudService<T, K>` adds `getAllActive()`, `GET <route>/active`, for a table with a Name and an IsActive column.
- `PagedResult<T>` and `PageQuery`, the shapes the search endpoint answers and takes.

## api-error.ts and notifier.ts

- `apiMessage(error, fallback)` reads the API's RFC 9457 answer: `detail` (a duplicate name, a business rule), or the field `errors` joined with a space (validation from `ApiValidation=true`); anything else gets the fallback. `api-error.spec.ts` tests it.
- `Notifier` (`error`, `info`, `confirm`) is the one seam through which a screen speaks. It uses the browser's `alert` and `confirm`, so no package is needed. `Toasts=ngx-toastr` writes the toast version instead: `TS_EssentialBuild` adds the package (`^19.1.0`, with `@angular/animations`, before Angular 21; `^20.0.5` from 21, with an `overrides` entry that relaxes its peer range on 22) and the stylesheet, and `TS_EssentialConfig` adds `provideToastr()` (and `provideAnimations()` before 21). The toast service is looked up when the first toast is shown, so a screen under test needs no toastr provider. The delete question stays a `confirm` dialog.

## crud-screen.ts

- `CrudScreen<T, K>`: the rows, the open row (always a copy), the edit dialog, `add`, `edit`, `submit`, `delete`, `clone`, `cancel`, the way back to the page that opened a row (`?edit=<id>&back=<page>`), and one set of messages for what the API refuses, shown through the `Notifier` in the API's own words when it gives them (`apiMessage`) and otherwise as 400 bad value, 404 gone, 400 on delete = in use. A screen supplies `service`, `noun`, `idKey`, `newRow()`, and may override `emptyKey`, `badInputMessage`, `prepare`, `prepareEdit`, `start`, `opened`.
- `PagedCrudScreen<T, K>` adds what a server-paged grid needs: the page and its size, one box per searchable column (`filters`), the sort from `grid-sort.ts` (kept between visits under `gridKey`; `sortableColumns` say which headers sort), the right-click Clear sort menu, a fall back to the last page when the current one has no rows, and `pageLoaded()`.

- `NameActiveCrudScreen<T, K>` (T has `name` and `isActive`) is the screen of a Name and IsActive table: it reads the whole list and narrows it on the page with `searchText` and `activeOnly` (`visibleRows`, `clearSearch()`).

Needs `grid-sort.ts` (`TS_GridSort`) and Angular Material's paginator. Written once and kept, like every essentials file: a project that changed the base keeps its copy, `--replace` refreshes it. Without the group the generated services and screens do not compile.

Checked on the Angular sample (build, 21 vitest tests, the live API, the customer-item and customer master-detail screens in the browser: sort, menu, paging, dialog, add).

## The specs of the bases

The group also writes `crud.service.spec.ts` and `crud-screen.spec.ts` (and `TS_GridSort` writes `grid-sort.spec.ts`), so the bases are tested once instead of through every screen:

- `crud.service.spec.ts`: the request each `CrudService` call makes (`HttpTestingController`), `searchPage` leaving empty filters out and writing `sortBy` / `sortDir`, `findByName` encoding the name, `getAllActive`.
- `crud-screen.spec.ts`: `CrudScreen` showing the API's own words (a 409 problem's `detail`, joined validation messages) through a fake `Notifier` and keeping the form open, the fixed words as the fallback, a save closing the form, a vanished row, edit working on a copy, the delete question and its refusals, clone; `PagedCrudScreen` asking for the first page, falling back to the last page, sorting from a header, remembering and clearing the sort, ignoring a saved sort for a column the grid cannot sort by; `NameActiveCrudScreen` narrowing the list.
- `grid-sort.spec.ts`: `nextSort`, `saveSort` / `loadSort` (one sort per grid, unreadable storage, a column the grid cannot sort by), `sortRows`.

Written once and kept, like the bases: a project that edits a base edits its spec with it. Checked by deliberately removing the step back to the last page: the base spec and every screen spec failed.

## The list protocol

`searchPage` is the one place that knows how a list is asked and answered. `ListProtocol=Columns` (the default) is what `API_Search` and `CrudApi` write: `GET <route>/search?pageNumber&pageSize&<one parameter per searchable column>&sortBy&sortDir`, answered as `{ items, page, pageSize, totalCount, totalPages }`. `ListProtocol=Search` is for an API that already exists: `GET <route>?pageIndex=0&pageSize=25&sort=name:desc&search=text` with one search box for the whole table, answered as `{ data, count }`; `searchPage` turns that answer into a `PagedResult`, so the screens do not change. The names are settings (`PageParameter`, `PageSizeParameter`, `SortParameter`, `SearchParameter`, `PageBase` 0 or 1, `ItemsMember`, `TotalMember`) and only the Search protocol reads them. `TS_Service` then writes `getPage(pageNumber, pageSize, search?, sortBy?, sortDescending?)`, the screens one `Search` box (`filters.search`), and the specs ask for the pages the way the project's protocol writes them. **The generated ASP.NET API still answers the Columns protocol only**, so use Search when the screens run against an API that exists.

`DefaultSorts=TimeSheet=WhenEntered:desc,Holiday=Name` gives a screen the sort it starts with until the person sorts (`defaultSort` on `PagedCrudScreen`); a saved sort wins, and Clear sort returns to the API's own order.
