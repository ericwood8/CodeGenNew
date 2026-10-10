# CS_DtoInput_v1

The request body of a table's create and update endpoints when the project sets `Dtos=true`. With the setting a table's API takes this class and answers the transfer class `CS_Dto` writes (every column, no navigation), where it used to bind and answer the entity itself.

`Dtos=true` adds `CS_Dto`, `CS_DtoInput` and `CS_Mapper` to the plan, for each table that has a plain CRUD API of any key (`DtoShape.Uses` in Core). A name and active table keeps its entity. The classes go to `Dtos/` in the API project, in the `DtoNamespace`.

## What it writes

`<Table>Input` has one property per column a caller may set (`DtoShape.InputColumns`):

- **Left out:** an identity key, a computed column, a version stamp, and the audit columns (create, modify and last-changed dates and users).
- **Kept:** a key the person chooses (text, Guid), and the owner column of an owned table. With `ApiAuth` the API checks the owner against the owners the caller may act for (403 for anyone else's, a blank owner of a caller tied to one person becomes that person), which is what lets the administrator create a row for someone.
- `ToEntity()` builds a new row for a create.
- `ApplyTo(entity)` copies the input's columns onto the row that is already there, so the columns the input leaves out keep their values. It never changes the key.

## What the API does with it

`CrudApi<TEntity, TRepo, TInput, TOutput>` (an int key; `CrudApi<TEntity, TRepo, TKey, TInput, TOutput>` for another key) binds the body as `TInput` and answers `TOutput`. See `API_EssentialCrudApi_v1.md`. `API_Crud` writes the three conversions: `ToOutput` is `<Table>Mapping.ToDto(row)!`, `ToEntity` is `input.ToEntity()`, `Apply` is `input.ApplyTo(row)`.

## Rules to know

- **The generated code sets no audit column.** A NOT NULL audit column (who and when created) needs the `Stamp(row, isNew)` override on the table's API class, a trigger, or a column default that EF Core knows about. `Stamp` runs on the row an input made, before it is validated and saved.
- **The route is the key on an update.** An input that holds no key cannot disagree with the route, so the 400 for a different id in the body no longer exists for an identity key. A key the person chooses is read by a create and ignored by an update.
- **Validators still run on the entity** (`ApiValidation`), on the new row after `Stamp`, and on the row an update has changed.
- Search (`SearchResult`), the lookup, CSV and the Angular and React models are unchanged: the transfer class has the entity's columns, so the JSON the screens read is the same minus the navigations.

## Not done

- A time sheet "detail" answer with the parent's names (the real server's DTO); the output holds the table's own columns.
- Stamping the audit columns.
- Name and active tables (`NameActiveCrudApi`).
