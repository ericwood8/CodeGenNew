# RS_Routes_v1

`src/routes/<table>.rs`: the Axum handlers of a table, with the contract the other stacks keep.

| Route | Answer |
|:-|:-|
| `GET /api/<plural>` | every row (200) |
| `GET /api/<plural>/{id}` | the row, or 404 |
| `POST /api/<plural>` | 201 with a `Location` header and the new row |
| `PUT /api/<plural>/{id}` | the changed row; 400 when the id in the body differs, 404 for no such row |
| `DELETE /api/<plural>/{id}` | 200, 404, or 400 when the row is in use (a foreign key) |
| `POST /api/<plural>/{id}/clone` | 201 and the copy (a cloneable table) |
| `GET /api/<plural>/search` | `{ items, page, pageSize, totalCount, totalPages }`; 400 for `pageNumber` below 1 |

- The plural is `Pluralizer` of the lower-cased table name without an `E_` or `SY_` prefix (a bare trailing `s` stays: `/api/customerstatus`), the rule the TypeScript clients use too.
- A body that cannot be read answers 400 (`ApiJson` in `support.rs`; axum's own `Json` answers 422). A unique, check or not-null violation answers 400 with the database's message, and a database error answers 500 with nothing from the database in the body.
- With `ApiValidation=true` the create and update handlers call `validate` (`RS_Validate`) first and answer 400 in the ASP.NET validation-problem form.
- The page size is limited to 1000 rows; the default is 100.
- `RS_Mod` merges every table's `router()` into `routes::router()`.
