# RS_UserDirectory_v1

`src/users.rs`: the people who sign in to the Rust API are rows of a table of your own database, the same table the ASP.NET and Python APIs can use. Whole-database. It runs in a plan only with `ApiAuth=true` and `UserTable=<table>`; `src/auth.rs` (the `Jwt` essentials group, `RS_EssentialAuth`) then looks the user up here instead of using the one bootstrap administrator.

The settings are the ones in `API_UserDirectory_v1.md` (`UserTable`, `UserNameColumn`, `PasswordHashColumn`, `UserOwnerColumn`, `UserRoleColumn`, `UserAdminColumn`, `UserRoleWhenTrue`, `UserRoleWhenFalse`; the name, hash and administrator columns are found by their usual names when left out), checked by `UserTable.Resolve` when the project is generated.

- **`find_user(pool, name)`** runs one fixed statement written for the database that was read (the quotes and placeholders of PostgreSQL, MySQL or SQLite) and answers the hash, the role and the owner id; a row with no name or no hash cannot sign in.
- **`set-password <name>`** (`cargo run -- set-password <name>`, or the built program with the same arguments) reads the new password from the environment variable `NEW_PASSWORD` (at least 8 characters, never on the command line), writes its hash and ends without starting the server. Exit code 0 done, 1 no such user, 2 usage.
- The hash is `pbkdf2-sha256$600000$salt$hash`, the text the other stacks write and read.

## The first person, a new password and the members of a tenant

`src/people.rs` (written with the user directory) has the same routes, answers and sentences as the ASP.NET API (`API_UserDirectory_v1.md`): `GET` and `POST /api/auth/first-person` (no sign-in), `PUT /api/auth/password` and, with a tenant table and a membership table, `GET /api/auth/members`, `GET /api/auth/members/candidates`, `POST`, `PUT` and `DELETE /api/auth/members`. The first person is written from the user table's required columns (`FirstPersonPlan`): the sign-in name, the password hash, the administrator flag, a true yes/no role column, and a value that fits for every other required column. A table that cannot be filled that way (a required foreign key) has no first person route. Rust's app wiring includes the routers; regenerate the main file with `essentials --replace` for an existing project.
