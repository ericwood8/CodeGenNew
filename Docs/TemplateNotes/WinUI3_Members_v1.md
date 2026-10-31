# WinUI3_Members_v1

`Views/MembersPage.xaml` and `Views/MembersPage.xaml.cs`: the page where an administrator says who works in the current tenant and in which role. Whole-database, written for a project with the desktop sign-in (`WinUI3_Auth`), a `TenantTable` and a `TenantMembership`.

The membership table has a role column besides its two keys, so the two-list junction editor (`WinUI3_JunctionEditor`) does not fit it: a junction table is exactly two key columns. This page takes its place for the membership table.

- **Menu.** `WinUI3_Screens` adds a Members entry at the end of the menu, for the administrator role only (`DesktopSession.HasRole`). The entry is added when the person signs in or switches tenant, with the rest of the menu.
- **The list** shows the people of the tenant the person works in: the person's name (the user table's `Name`, `FullName` or `DisplayName` column, else the sign-in name alone) with the sign-in name in brackets, and the role of the membership. A person who works in no tenant (a platform administrator) sees "Switch to a tenant to manage its members." and a disabled Add member.
- **Add member** lists the people of the user table who are not yet in this tenant, with the query filters off (the tenant's own filter hides everyone who belongs elsewhere), and adds a membership row with the role typed. The role may stay empty: the person's own role then counts in this tenant. Without a role column in the membership table the box is not offered.
- **Edit role** and **Remove** change or delete the membership row of this tenant only. A person cannot remove themself ("You cannot remove yourself from the tenant."). The person stays a row of the user table when removed.
- The page reads and writes the membership and user tables with `IgnoreQueryFilters()` and names the tenant in every query by hand, since one `IgnoreQueryFilters()` drops every filter of the query, the tenant's too.

Limits: the page does not stop an administrator from removing the last administrator of a tenant other than themself; it is the WinUI3 app only, since the other front ends have no API route for memberships yet.
