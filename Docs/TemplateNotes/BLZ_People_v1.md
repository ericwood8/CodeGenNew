# BLZ_People_v1

`Services/PeopleApi.cs` and `Pages/Members.razor`; `BLZ_PeopleTest` writes `MembersTests.cs` into the test project. Whole-database, written for a project with `Auth=true`, `ApiAuth=true` and a `UserTable`.

- **`PeopleApi.ChangePasswordAsync`** calls `PUT /api/auth/password`. The user table's page (`BLZ_Page`) has a Set password box and calls it after the row is saved. An administrator sets anyone's password, anyone else only their own; empty keeps the password; a new person needs one; fewer than 8 characters is refused. The administrator flag is shown on the form to administrators only (`Session.HasRole`).
- **`Members`** (with a `TenantMembership`) lists the people of the signed-in tenant with their role, with Add member, Edit role and Remove. `BlazorMenu` adds the Members entry with the administrator role, so the menu and the menu test list it.
- **The sign-in** (`BLZ_EssentialAuth`) turns into "Create the first administrator" while `GET /api/auth/first-person` says the user table is empty (`FirstPersonTests.cs`).
