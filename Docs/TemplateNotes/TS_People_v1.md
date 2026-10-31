# TS_People_v1

`services/people.service.ts` and `components/members/` (component, template, styles and spec). Whole-database, written for a project with `Auth=true`, `ApiAuth=true` and a `UserTable`.

- **`PeopleService.changePassword`** calls `PUT api/auth/password`. The user table's screen (`TS_Component`, `TS_DetailMasterComponent`) has a Set password box and calls it after the row is saved, through the `followUp` hook of `CrudScreen` (`TS_EssentialCrud`; regenerate the base with `essentials --replace`). An administrator sets anyone's password, anyone else only their own; empty keeps the password; a new person needs one; fewer than 8 characters is refused. The administrator flag is shown on the form to administrators only.
- **`MembersComponent`** (with a `TenantMembership`) lists the people of the signed-in tenant with their role, with Add member, Edit role and Remove, over `GET`, `POST`, `PUT` and `DELETE api/auth/members`. `TS_Screens` adds the Members route with the administrator role.

The component avoids `*ngIf` and `*ngFor` on void elements such as `input`, since the control flow conversion of `AdaptAngularHtml` needs a closing tag.
