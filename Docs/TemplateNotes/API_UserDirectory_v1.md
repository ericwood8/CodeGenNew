# API_UserDirectory_v1

`Apis/TableUserDirectory.cs`: the people who sign in are rows of a table of your own database (an `Employee` table with a login, a password hash and a manager flag), not the single bootstrap administrator. Whole-database. It runs in a plan only with `ApiAuth=true` and `UserTable=<table>`; the Program group then registers it and handles the `set-password` command.

```
ApiAuth=true
UserTable=Employee
UserNameColumn=Login
PasswordHashColumn=PasswordHash
UserOwnerColumn=EmployeeId
UserRoleColumn=IsManager
UserAdminColumn=IsAdministrator
UserRoleWhenTrue=Manager
UserRoleWhenFalse=Employee
```

- **What the sign-in does with the table:** `FindAsync` looks the typed name up in `UserNameColumn` (the database's own collation decides about case) and answers the row's hash, owner id and role. A row whose name or hash is empty cannot sign in. The Jwt group verifies the password against the hash (PBKDF2, `PasswordService`) and writes the role and owner id into the token.
- **Role:** a yes/no `UserRoleColumn` gives two roles (`UserRoleWhenTrue`, default `Manager`; `UserRoleWhenFalse`, default `Employee`); a text column is the role itself; blank: no role. The default `IOwnerScope` still lets only the `AuthAdminRole` see every owner; register your own `IOwnerScope` for "a manager sees the people who report to them".
- **Settings are checked when the project is generated** (`UserTable.Resolve`): the table must exist, the name and hash columns must be text, the hash column at least 90 characters wide (`varchar(200)` is plenty), the owner column an `int`, the role column yes/no or text, and `ApiAuth` must be on. A wrong setting stops the run with a sentence that names it.
- **`set-password`:** `Program.cs` calls `UserCommands.TryRunAsync(args, app.Services)` right after `Build()`. `dotnet run --project <Api> -- set-password jdoe` reads the new password from the environment variable `NEW_PASSWORD` (at least 8 characters), hashes it and updates that row, then ends without starting the server. The password is never on the command line, in a file or in the output. Exit code 0 done, 1 no such user, 2 usage.

```
NEW_PASSWORD='a long throwaway' dotnet run --project InvoiceSystem.Api -- set-password jdoe
```

- **Administrator:** `UserAdminColumn` (a yes/no column such as `IsAdministrator`) makes a person the `AuthAdminRole`, the role that sees every owner in `OwnedTables`, whatever `UserRoleColumn` says. Without it, a text `UserRoleColumn` can hold that role; with only a yes/no `UserRoleColumn` nobody is an administrator.
- **The bootstrap administrator stops working** once `UserTable` is set (checked: its sign-in answers 401), because the table is the only directory.
- **Not done:** a person cannot change their own password through the API; lock-out after failures is the Jwt group's throttle, not a column.
- **Other stacks:** the Python and Rust APIs have the same sign-in (`POST /api/auth/login`, the same token and password hash, `set-password`, the same guards on the user table, the owner scope): see `PY_UserDirectory_v1.md` and `RS_UserDirectory_v1.md`. One user table serves all three.

## What the user table's own API does

The generated CRUD API of the user table (`API_Crud`, the table named in `UserTable`) is guarded, so a signed-in caller cannot read or take credentials through it:

- **The password hash** is never in an answer or a request body: the entity (and the transfer class) carries `[JsonIgnore]` on it, the input type leaves it out, and an update puts the stored hash back before it saves. A create starts with no hash; `set-password` is the one way to set it.
- **The administrator and role columns** (`UserAdminColumn`, `UserRoleColumn`) can be changed only by an administrator. An update by anyone else that changes one is a 403 problem (`/problems/column-not-yours`), the same value is accepted, and a create by anyone else starts them at false (or empty).
- **The sign-in name** is trimmed and must be unique: a second person with it is a 409 problem (`/problems/duplicate-value`), the answer the unique index would give.
- **The last administrator** cannot lose the flag, be deactivated (an `IsActive` column) or be deleted: 422 (`/problems/last-administrator`). An administrator counts when the flag is true and, if the table has `IsActive`, the row is active.

The checks are overrides of two hooks in the `CrudApi` and `NameActiveCrudApi` bases, `BeforeSaveAsync(context, row, isNew)` and `BeforeDeleteAsync(context, id)`, which any table's API can override to refuse a write; both exist only with `ApiAuth`, as does `Caller` (the signed-in user). Regenerate the bases with `essentials --replace` (the `crudapi` group) to get them.
