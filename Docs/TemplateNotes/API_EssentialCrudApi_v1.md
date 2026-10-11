# API_EssentialCrudApi_v1

With `EditableChildren` set (`TS_DetailMasterComponent_v1.md`) both bases also have `EditableChildren`, the child lists a form saves with the row (`ChildRows<TKey, TChild>`: the child's identity key and the property that links it to the parent; `API_Crud` writes it from the project's list). A create or update then reads its body as JSON: the row is read as before and each list the body holds is reconciled with the child table (a row with its key changed, a row without one added, a row left out removed) in one transaction with the row; 400 when a row is not one of the parent's or the database refuses one (a row left out that is still in use). A body without the list leaves the child rows alone. The group is written once, so refresh it with `--replace` after listing a child.

With `ApiAuth=true` the base also has `Owner`, the column that holds the owner of a row (a table in `OwnedTables` overrides it): every route reads the caller's `IOwnerScope` (`API_EssentialJwt_v1.md`), lists hold only the allowed owners' rows and a row of another owner answers 403. A table without an owner column can say `OwnedThroughParent`, `ScopeThroughParent` and `ParentOwnerAsync` instead (its rows belong to the owner of a parent row; the default scope keeps no rows, so forgetting it never opens them). Without `ApiAuth` none of that is written.

The `CrudApi` group of the API essentials: one file for the whole project, `Apis/CrudApi.cs`. No table and no database are needed (`codegen essentials --stack api --groups crudapi --project <name> -o <project root>`; `-o` is the project root, the project's `OutputApi` folder is added).

`CrudApi<TEntity, TRepo, TKey>` is the base of the class `API_Crud` writes for each table, over the key's type (int, long, Guid or string; the route is `{id:int}`, `{id:long}`, `{id:guid}` or `{id}`). `CrudApi<TEntity, TRepo>` is its int shortcut (`CustomerApi : CrudApi<Customer, CustomerRepo>`), so a table with an int key reads as it always did. A text key is chosen by the person: a create with an empty key answers 400 and with a key that exists 409. `LookupItem<TKey>` carries the key of the lookup route; `LookupItem` is the int form. It holds what every plain table shares, written once (the routes below show `{id:int}`):

```text
GET    <route>             every row as an array; with pageNumber and/or pageSize, one page as { items, page, pageSize, totalCount, totalPages } (CrudPage<T>)
GET    <route>/{id:int}    one row                          404 when there is none
POST   <route>             create                           201 with a Location header
PUT    <route>/{id:int}    update                           400 when the id in the route and in the body disagree, 404 when there is no row
DELETE <route>/{id:int}    delete                           404 no such row, 400 the row is in use
POST   <route>/{id:int}/clone    copy a row, answer with the copy         only when CanClone
GET    <route>/{name}      rows whose name starts with the text            only when CanFindByName (an int or "search" never reaches it)
```

A table's class says only what is its own:

| Member | Meaning |
|---|---|
| `NewRepo(context)` (abstract) | the table's `<Entity>Repo` over the request's context |
| `Key` (abstract) | the primary key property, `c => c.CustomerId` |
| `NewestFirst` | the date column the list comes newest first by; null lists in key order |
| `CanClone` / `CloneAsync` | true when the table has a clone routine (`CloneShape`; `CS_Repo` writes `CloneAsync` for the same tables) |
| `CanFindByName` / `FindByNameAsync` | true for a table with a NOT NULL text `Name` (`CS_Repo` writes `GetByName` for the same tables) |

A rule before a save, a route of its own or another answer is an override, or a hand-written class that does not use the base.

- **Needs** `BaseApi` (`API_EssentialApiBase`), the generic repository and `BaseEntity` (`CS_EssentialBase`), the context, and, when the project setting `ApiValidation` is on, `ValidationFilter` (`API_EssentialProgram`). Namespaces come from `ApiNamespace`, `EntityNamespace`, `RepoNamespace`, `ContextNamespace`, `ContextName`.
- **Written once and kept.** Like every essentials file it is not overwritten unless `--replace` is passed, so a sample's `Regenerate.sh` runs the group before `generate` (a first run creates it, later runs keep a hand-edited one). Without it the generated `XApi` classes do not compile.
- **Page size** is kept between 1 and 1000; a page number below 1 or too large for a 32 bit offset is a 400 problem.
- **Checked:** the Angular, React, PostgreSQL and MySQL samples regenerate, build and pass their tests; the Angular sample's API was called with curl (list, page, 400, 404, clone).

## NameActiveCrudApi

The same file also holds `NameActiveCrudApi<TEntity, TRepo>`, the base of the class `API_Crud` writes for a table with a Name and an IsActive column (`where TEntity : BaseNameActiveEntity`, `where TRepo : NameActiveRepo<TEntity>`):

```text
GET    <route>             every row, by name
GET    <route>/active      the active rows, by name
GET    <route>/{id:int}    one row                                  404 when there is none
GET    <route>/{name}      the active rows whose name starts with the text
POST   <route>             create                                   201 with a Location header; 400 an empty name; 409 another active row has the name
PUT    <route>/{id:int}    update                                   400 the ids disagree or the name is empty, 404 no row, 409 another active row has the name
DELETE <route>/{id:int}    delete                                   404 no such row, 400 the row is in use
```

Every refusal is a problem-details answer (RFC 9457), so the client shows the `detail` as it is. The class name of the entity base is the project's `BaseNameActiveEntity`. With `ApiValidation=true` the create and the update run the validators.

## The lookup route

`GET <route>/lookup` answers `[{ "id": 3, "name": "Admin", "isActive": true }]` for the drop-downs and the "name of" columns of other screens: only the key, the display column and the IsActive flag are read (`LookupItem(int Id, string Name, bool? IsActive)`; `isActive` is null for a table with no such column). `CrudApi` answers it when the class says `CanLookup` and gives `LookupQuery(rows)`; `NameActiveCrudApi` always does, ordered by name. Every row is listed, inactive ones too (with the flag), so the name of an inactive parent still shows in a grid. `lookup` is a literal, so it wins over find-by-name.

## Input and output types (`Dtos=true`)

`CrudApi<TEntity, TRepo, TKey, TInput, TOutput>` binds the create and update bodies as `TInput` and answers `TOutput` (the list, a page, one row, the find by name, a create's 201, an update and a clone); `CrudApi<TEntity, TRepo, TInput, TOutput>` is the int-key form. `CrudApi<TEntity, TRepo>` and `CrudApi<TEntity, TRepo, TKey>` still take and answer the entity (they derive from the five-argument class with the entity as both types), so a hand-written API keeps working. A generated class names the three conversions: `ToOutput(row)`, `ToEntity(input)` and `Apply(input, row)` (`API_Crud_v1.md`, `CS_DtoInput_v1.md`).

- **Update:** the row is loaded, `Apply` copies the input onto it, `Stamp(row, false)` runs, and the usual checks (owner, 404, validator) and the save follow. The columns the input leaves out keep their values.
- **Create:** `ToEntity`, `Stamp(row, true)`, the owner check, the validator, then the save.
- **Validation:** the endpoint filter only sees a body of the entity type, so with an input type the entity's validator is a service of the handler (`IValidator<TEntity>? validator`).
- **Editable children:** the body is read into the input type.
- Without `Dtos` the file is what it was.

## The Search list protocol

With `ListProtocol=Search` the list route (`GET <route>`) takes the project's parameter names (`PageParameter`, `PageSizeParameter`, `SortParameter` as `column:asc` or `column:desc`, `SearchParameter`) and answers a page as `{ <ItemsMember>, <TotalMember> }`; the first page is `PageBase`. A search text or a sort without a page answers the matching rows as a plain array, and a call with none of them still answers every row, so the drop-downs are unchanged. The search text is trimmed and lower-cased and handed to `SearchRows`; the sort name is handed to `SortRows` and followed by the key, so pages do not overlap; a name that is not a sortable column gives the default order. Both are hooks a table's class overrides (API_Crud writes them). A project that already has `Apis/CrudApi.cs` needs `essentials --groups crudapi --replace`. API_Search still writes its `/search` route; nothing calls it in this protocol.
