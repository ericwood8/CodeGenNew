# UNO_People_v1

`Services/PeopleApi.cs`, `Views/MembersPage.xaml` and `Views/MembersPage.xaml.cs`. Whole-database, written for a project with `Auth=true`, `ApiAuth=true` and a `UserTable`.

- **`PeopleApi.ChangePasswordAsync`** calls `PUT /api/auth/password` over `ApiClients.Http`. The user table's dialog (`UNO_DetailScreen`, `UNO_DetailMasterScreen`) has a Set password box and calls it after the row is saved. An administrator sets anyone's password, anyone else only their own; empty keeps the password; a new person needs one; fewer than 8 characters is refused. The administrator flag is shown on the form and as a list column to administrators only (`ApiClients.Session`).
- **`MembersPage`** (with a `TenantMembership`) is the page of `WinUI3_Members` over the API: the people of the signed-in tenant with their role, Add member, Edit role and Remove. `UNO_Screens` adds the Members entry for the administrator role.
- **The sign-in** (`UNO_EssentialAuth`) turns into "Create the first administrator" while `GET /api/auth/first-person` says the user table is empty.

The Uno app is compiled, not run.
