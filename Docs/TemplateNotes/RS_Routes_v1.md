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
| `GET /api/<plural>/lookup` | `[{ id, name, isActive }]` of every row ordered by name, for the drop-downs and the "name of" columns of other screens (a table with a display column; `RustTable.LookupSql`) |
| `GET /api/<plural>/search` | `{ items, page, pageSize, totalCount, totalPages }`; 400 for `pageNumber` below 1 |

- The plural is `Pluralizer` of the lower-cased table name without an `E_` or `SY_` prefix (a bare trailing `s` stays: `/api/customerstatus`), the rule the TypeScript clients use too.
- A body that cannot be read answers 400 (`ApiJson` in `support.rs`; axum's own `Json` answers 422). A unique, check or not-null violation answers 400 with the database's message, and a database error answers 500 with nothing from the database in the body.
- With `ApiValidation=true` the create and update handlers call `validate` (`RS_Validate`) first and answer 400 in the ASP.NET validation-problem form.
- The page size is limited to 1000 rows; the default is 100.
- `RS_Mod` merges every table's `router()` into `routes::router()`.
- With a front end to serve (`FRONTEND_DIR`), an `/api` path no route answers is a 404; every other unknown path is the front end's `index.html`. Without it a browser keeps the page as the answer of a route that was missing when it first asked.

## Sign-in (ApiAuth)

With `ApiAuth=true` the Rust API signs people in like the ASP.NET one (`RS_EssentialAuth`, the `Jwt` group: `src/auth.rs`, which adds the crates `hmac`, `sha2`, `pbkdf2` and `base64`). `POST /api/auth/login` takes `{ "userName", "password" }` and answers `{ token, expiresUtc, user: { name, role, ownerId } }` (400 for a blank body, 401 for a wrong password, 429 after five wrong tries in a minute from one address). `lib.rs` puts the `require_user` middleware on every other route (the dashboard included), so a missing, bad or expired token is a 401. The token is an HS256 JWT; the key is the environment variable `Jwt__Key` (at least 32 characters; the server does not start without it). The password hash is the same `pbkdf2-sha256$600000$salt$hash` text the ASP.NET API writes, so one user table serves both. `Cargo.toml` builds the dependencies with optimisation even in a debug build (the hash runs 600000 rounds).

- **Who signs in:** without `UserTable` the one user is `Bootstrap__AdminUserName` with `Bootstrap__AdminPassword`; with it, the rows of that table (`RS_UserDirectory`, `src/users.rs`; settings as in `API_UserDirectory_v1.md`). `NEW_PASSWORD='a long throwaway' cargo run -- set-password <name>` (or the built program with the same arguments) sets a password (exit 0 done, 1 no such user, 2 usage).
- **Owned tables:** a table in `OwnedTables` lists, reads, changes, deletes and searches only the rows of the owner the caller may act for (`CurrentUser::scope`: the administrator role every owner, anyone else the owner id in the token). Another owner's row is a 403 problem (`/problems/not-your-row`); a create with a blank owner takes the caller's; a table with a NOT NULL foreign key to an owned table is scoped through its parent (`OwnedChildren`): the repository filters by the keys of the parents of that owner.
- **The user table:** the struct skips the password hash in JSON (the sign-in still reads it through its own statement), the repository never writes it in an insert, update or clone, only an administrator changes the administrator and role columns (403 `/problems/column-not-yours`; a create by anyone else starts them at false), a sign-in name is trimmed and unique (409), the last active administrator cannot lose the flag or be deleted (422), and only an administrator copies a person.
- **Checked:** generated from a SQLite file, built with cargo and driven over HTTP with real sign-ins: 55 checks (the sign-in, the 401s, the hash, the guards, the owner scope, a child through its parent, the throttle).

**Roles** (`ReadRoles`, `TableReadRoles`, `WriteRoles`, `TableWriteRoles`, with `ApiAuth`): a table with a rule gets `READ_ROLES` and `WRITE_ROLES` and its reading and writing methods become separate routes of the same path, each with `route_layer(middleware::from_fn(... check_roles ...))` (`check_roles` is in `src/auth.rs`); the lookup stays open. Another role gets 403 `role-not-allowed`; the administrator role is always listed. A project made before needs `--replace` of the Auth essentials.

## The lookup for a type-ahead

`lookup` takes the query string (`Query<repo::LookupQuery>`: `search`, `ids`, `take`) and passes it to `repo::lookup`; see RS_Repo for what each does. Without any of them the list is whole, as before.
