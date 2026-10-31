# TSX_People_v1

`src/api/people.ts`, `src/pages/MembersPage.tsx` and its test. Whole-database, written for a project with `Auth=true`, `ApiAuth=true` and a `UserTable` (the plan adds it).

- **`changePassword(userName, password)`** calls `PUT /api/auth/password`. The Set password box of the user table's form uses it after the row is saved.
- **The user table's form** (`TSX_Page`, `TSX_DetailMasterPage`) has a Set password box. An administrator sets anyone's password and anyone else only their own (the box is shown for the row of the signed-in person). Empty keeps the password; a new person needs one; fewer than 8 characters is refused before any call. The password hash is never in the form. The administrator flag of the user table is shown on the form to administrators only (`signedIn.role`).
- **`MembersPage`** (with a `TenantMembership`) lists the people of the signed-in tenant with their role, with Add member (people not yet in the tenant, from `GET /api/auth/members/candidates`), Edit role and Remove. A 403 on Remove says "You cannot remove yourself from the tenant." `TSX_Screens` adds the Members entry with the administrator role, so the menu shows it to administrators only.

Limits: the administrator flag stays a column of the grid for everyone, and the API still sends the flag to people who are not administrators.
