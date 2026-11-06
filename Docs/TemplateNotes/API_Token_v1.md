# API_Token_v1 and the TokenTables setting

`TokenTables=Session,PasswordReset:CodeHash:ExpiresOn:UsedOn+RevokedOn:FamilyId` names the tables that hold sessions, refresh tokens, password resets and verification codes: a table and, after colons, its hash column, its expiry column, its ending columns joined with `+` and its family column. A part that is left out is found by name (a text column with `Hash` in its name; a date-and-time column with `Expir`, `ValidUntil`, `ValidTo`, `Until` or `EndsOn` in its name; every yes/no column or date-and-time column that allows NULL whose name starts with `Revoked`, `Used`, `Spent` or `Consumed`; a column with `Family` in its name). The rule is `TokenPlan` in Core. It needs `ApiAuth` and `AccessMode=Ef`.

The table needs a single whole-number key, a text hash column of at least 64 characters, and an expiry column that cannot be NULL (a token without an expiry would never end). The ending columns are optional: a yes/no column that cannot be NULL, or a date-and-time column that allows NULL. A row is active when it has not expired and none of its ending columns is set. A spend sets the used column (the first that is not named `Revoked...`), a revoke sets the revoked column; with one ending column both set it, and without any, ending a token sets its expiry to now. The family column is a whole number (int or bigint), a guid or text of at least 32 characters, and not a foreign key. A table that lacks something fails the run with the list of what it lacks.

## What the table does not get

No list and edit screen, no ordinary CRUD API and no TypeScript model in any front end: the table is treated as a `NoApiTables` table without being listed there. A caller never reads the hash, and nobody adds or edits a row by hand.

## The hash

The row holds the SHA-256 of the token in lower-case hex (64 characters), never the token. `TokenHash.Of(token)` is the one place that makes it. The lookup also accepts the same hash in capitals, so a row written by another program matches on a case-sensitive database (PostgreSQL) too. The token itself is made and sent by the application's own sign-in or reset code, which keeps it.

## Routes

`API_Token` writes `Apis/Tokens.cs`, one static class per table (`SessionTokens`), registered by `API_Registration` as `TokenApi.Register`. Every route needs a signed-in caller, except the `spend` route of a table named in `TokenAnonymousSpend` (below).

| Route | What it does |
|---|---|
| `GET /api/sessions` | An administrator's list, newest first, the last 200 rows: every column but the hash, and `active` (not expired and not revoked). With a person column, `?personId=` limits it to one person. Another role is a 403. |
| `POST /api/sessions/{id}/revoke` | An administrator ends one row with one UPDATE that still tests that the row is active. Revoking again changes nothing and answers 200; an absent row is a 404. |
| `POST /api/sessions/revoke-person/{personId}` | Only when the table has a column that is a foreign key to the `UserTable`: every active row of that person ends ("sign out this person everywhere"). Answers `{ "revoked": n }`. |
| `POST /api/sessions/spend` (body `{ "token": "..." }`) | Spends a token once: the row with that hash ends with one UPDATE that tests that it is active, so two requests with the same token at the same moment cannot both succeed. 200 with `{ id, personId, family }` (the last two only when the table has them); a token that is not known, was used or revoked, or has expired is the same 400 (`/problems/token-not-valid`), so the answer does not say which. |

Also in the same file, for the application's own code: `SessionTokens.IssueAsync(context, token, expiresOn, set)` puts a row in the table as the hash (`set` fills the other columns), and `SessionTokens.SpendAsync(context, token)` is the spend without the route. A reset code that is spent before a person is signed in is the application's own anonymous endpoint calling `SpendAsync`; the generated route needs a signed-in caller.

## Spending without a sign-in

`TokenAnonymousSpend=PasswordReset,EmailCode` lets the `spend` route of those tables be called without a sign-in, since a reset or verification code is spent before the person has signed in. Each name must also be in `TokenTables`. ASP.NET maps the route with `AllowAnonymous`, Python includes it from `public_router` in `tokens.py` (main.py leaves that router without the sign-in check) and Rust merges `tokens::public_router()` into the sign-in's own router. The answer is the same 400 for an unknown, used, revoked or expired token, and the list, revoke and sign-out routes still need the administrator. Nothing limits the number of tries, so use it for long random tokens: a six-digit code can be guessed.

## Families

A family column groups the tokens of one sign-in (the refresh tokens that follow each other). Spending a token that was already used or revoked, and has not expired, ends every active token of that family with the revoke column, so a stolen token that is replayed signs the thief and the owner both out. `Spent` answers the `family`, so the application's own sign-in code issues the next token of the family: `IssueAsync(context, next, expiresOn, row => row.FamilyId = spent.Family)`. A token that merely expired is refused without touching its family, and a row whose family is NULL has no family.

## Cleanup

`ArchiveTables=Session:30:delete` writes the script that removes the rows that expired more than 30 days ago: for a token table the age is measured from the expiry column.

## Python and Rust

`PY_Token` (`app/routes/tokens.py`) and `RS_Token` (`src/routes/tokens.rs`) write the same four routes with the same sentences (`TokenWords`), problem type and status codes. A token table has no model or repository, so the Python routes use a SQLAlchemy `table()` of its columns and the Rust routes read each row by position; every change is one UPDATE of the row that is still active. `PY_Mod` and `RS_Mod` list the module. The hash is SHA-256 in lower-case hex in both (`hashlib`, `sha2`).

## Front ends

With `Auth=true` and `ApiAuth` every front end that signs in gets a page for the administrator, one for each token table, listed in the menu under the table's plural name (`Sessions`): the rows the list route answers (every column but the hash), whether each is active, a Revoke button that is off for a row that has ended, and a Sign out everywhere button on a row that has a person. The sign out says how many rows ended. `TSX_Tokens` (React), `TS_Tokens` (Angular), `BLZ_Tokens` (Blazor) and `UNO_Tokens` (Uno) ask the API; `WinUI3_Tokens` asks the database through the app's own context with the same test of an active row, since the WinUI3 app never meets the API. The React, Angular and Blazor files carry their tests.

## Generated tests

The Python and Rust APIs have the same cases as tests of their own: with `PythonTests=true` and `ApiAuth`, `PY_TestTokens` writes `tests/test_tokens_<table>.py` (pytest, over the scratch database of the shared `conftest.py`; not for SQL Server), and with `RustTests=true` and `ApiAuth`, `RS_TestTokens` writes `tests/api/token_<table>_routes.rs`. Each test inserts rows of its own with plain SQL and removes them. A table whose other columns the generator cannot fill (a foreign key, a number type it does not know) gets no such tests.

With `ApiTests` and `ApiAuth`, `API_TestTokens` writes `<Table>TokenApiTests.cs`: a call without a token is a 401, a role that is not the administrator's is a 403, a token is spent once, an expired or unknown or empty token is not spent, a revoked token is not spent and the list never shows the hash, revoking a person ends their tokens, a table in `TokenAnonymousSpend` is spent without a sign-in, and a table with a family ends the family when a used token comes again. Each test adds a row of its own through `<Table>TokenTestSupport` and removes it. The new row copies the first row of the table for the columns the routes do not manage (so a check constraint or a parent row is satisfied), and a table with no row gets a value for each column that cannot be NULL, or a sentence that says a row is needed. A person test is inconclusive when the user table has no row.

With `UserTable` and no `TenantTable`, the test fixture makes the administrator's token with the key of the run instead of signing in, since the people who sign in are rows of the database.

## Not covered

Nothing issues tokens: the sign-in code calls `IssueAsync`, and the next token of a family is issued by the same code. The hash scheme is fixed (SHA-256 in hex, found in lower or upper case); a table that holds another hash is not matched. A table with two ending columns tests both, a revoke sets only the revoked one and a spend only the used one (a used row is already ended, so a revoke of it changes nothing; the generated tests of all three stacks prove this). `token_check.py` plays the routes against a running API. No rate limit sits in front of an anonymous spend.

Checked with `Docs/Verification/Tools/Schemas/tokens.sql`: the generated tests over a scratch SQLite copy (`matrix_check.py --schema tokens`), with the "still active" test removed from the UPDATE the spend, revoke and revoke-person tests fail; `token_check.py <base url> <sqlite file>` against the ASP.NET, Python and Rust APIs over the same copy (two ending columns, a family, an anonymous spend, a hash in capitals); the React and Angular tests and the Blazor, WinUI3 and Uno builds; the generated Python and Rust tests over SQLite, PostgreSQL and MySQL (the `AuthToken` tables below), where tests fail once the active guard is taken out of the spend, revoke and revoke-person updates; the Sessions page clicked in a browser (React, Angular and Blazor) and in the WinUI3 and Uno desktop apps (UI Automation): the list, a disabled Revoke on an ended row, Revoke, Sign out everywhere, with the database read back; and the generated tests over SQL Server (`AuthToken` of `InvoiceSystem_test`), PostgreSQL and MySQL (`Schemas/tokens_postgres.sql` and `Schemas/tokens_mysql.sql` add `Employee` and `AuthToken` to the standing `InvoiceSystem` and `invoicesystem` databases; the tests add rows of their own and remove them).
