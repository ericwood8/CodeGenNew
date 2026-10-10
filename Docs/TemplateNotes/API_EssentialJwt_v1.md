# API_EssentialJwt_v1

The `Jwt` group of the API essentials: the API half of the sign-in the Angular `Auth` group calls. It is not ticked by default; `ApiAuth=true` in the project file adds it to every `codegen generate --essentials`, or name it: `codegen essentials --stack api --groups jwt --project <name> -o <folder>`.

## What it writes

| File | What it does |
|---|---|
| `Apis/Auth.cs` | `POST /api/auth/login`, the JWT bearer wiring (`AddGeneratedAuth`), the fallback policy and the `Administrator` policy, `IUserDirectory` and its default, `PasswordService`, the login throttle and the settings class `JwtOptions`. |
| `Apis/OwnerScope.cs` | `IOwnerScope`: which owners' rows the caller may read and change, and its default `ClaimsOwnerScope`. |

`ApiAuth=true` also changes the other groups: **Program** adds the package `Microsoft.AspNetCore.Authentication.JwtBearer`, `builder.AddGeneratedAuth()`, `UseAuthentication` and `UseAuthorization` after `UseCors`, `MapAuthEndpoints()`, and `AllowAnonymous()` on the health and document routes; **CrudApi** gets the owner scope; the classes of an owned table name their owner column (`API_Crud`); the search endpoint of an owned table is limited to the owners (`API_Search`); the CSV routes of an owned table are for the administrator only (`API_Csv`); the generated tests sign in (`ApiTests`); `.http` files, `openapi.yaml` and the Angular front end carry the token. Essentials are written once and kept, so a project that has `Program` and `CrudApi` already needs `--replace` for those two groups after turning `ApiAuth` on.

## The sign-in

`POST /api/auth/login` with `{ userName, password }` answers `{ token, expiresUtc, user: { name, role, ownerId } }`. 400 when the user name or password is blank, 401 for a wrong name or password (an unknown name takes as long to answer as a wrong password), 429 after five wrong tries in a minute for the same name and address (`LoginThrottle`; it is kept in memory, so it does not span several servers). The token is a JWT signed with HMAC SHA-256, valid for `Jwt:ExpiresMinutes` (480 by default), with the claims `sub`, `name`, the role claim (`AuthRoleClaim`, default `role`) and the owner claim (`AuthOwnerClaim`, default `employeeId`).

The `Jwt` section of the configuration: `Key` (required, at least 32 characters), `Issuer`, `Audience`, `ExpiresMinutes`. **The key belongs in an environment variable** (`Jwt__Key`); the API refuses to start without it, and nothing writes it into `appsettings.json`.

Every endpoint needs a signed-in caller: the fallback policy asks for an authenticated user wherever no other policy is named, so a hand-written route is protected too. The sign-in itself, the health checks and the `/docs` and `/openapi.yaml` routes are marked `AllowAnonymous`.

## Not generated on purpose: the people

`IUserDirectory` finds a user by name and returns a `DirectoryUser` (name, password hash, role, owner id). The default, `BootstrapDirectory`, knows one administrator, from `Bootstrap:AdminUserName` and `Bootstrap:AdminPassword` (environment variables `Bootstrap__AdminUserName` and `Bootstrap__AdminPassword`; with neither set nobody can sign in). A project with a user table implements the interface and registers it **before** `builder.AddGeneratedAuth()` in `Program.cs`; the default is added only when none is there. `PasswordService.Hash(password)` makes the value to store (`pbkdf2-sha256$iterations$salt$hash`, 600000 rounds); `Verify` compares in constant time. The user table itself is the project's; `NoApiTables` keeps it out of the generated API.

## Owned tables

`OwnedTables=TimeSheet,Expense` names the tables whose rows belong to a person. The owner is the NOT NULL whole-number column `OwnerColumn` (default `EmployeeId`); a listed table without it is left unscoped and the generated class says so in a comment. `IOwnerScope.VisibleOwnersAsync(user)` returns the owner ids the caller may reach, or null for every owner. The default allows the role `AuthAdminRole` (default `Administrator`) everything and anyone else only the id in their token. A rule the token cannot carry, such as a manager seeing the people who report to them, is an `IOwnerScope` of the project's, registered before `AddGeneratedAuth`.

What an owned table's API does (`CrudApi`):

- the list, a page of it and the lookup hold only the rows of the allowed owners;
- one row, update, delete and clone of someone else's row answer 403, of a row that is not there 404;
- a create for an owner outside the scope is 403; a caller who may act for exactly one owner and leaves the owner blank creates the row for that owner;
- an update cannot hand a row to an owner outside the scope.

The search endpoint (`API_Search`) filters on the owners when `AccessMode=Ef`. The search routines take no owner, so with routines a caller limited to some owners gets 403 from the search of an owned table; use `AccessMode=Ef` or sign in as the administrator. The CSV export and import of an owned table need the `Administrator` policy. The dashboard and the junction editors are protected by the sign-in only and do not look at owners.

## Not done

- Python and Rust APIs do not sign people in.
- The Postman and Bruno collections have no login request.
- The roles the project's own tables name (a manager table, an HR role) are the project's `IOwnerScope`.
