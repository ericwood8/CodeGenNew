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
- **Other stacks:** the Python and Rust APIs do not have a sign-in yet.
