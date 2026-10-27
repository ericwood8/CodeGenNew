# API_Tenants_v1 and the TenantTable, TenantColumn, NoTenantTables and TenantMembership settings

Shared-schema multi-tenancy for the ASP.NET API: one database, one schema, a tenant column on every tenant-owned table, and the rule that a caller reads and writes only the rows of the tenant in their token. It builds on `ApiAuth` and `AccessMode=Ef`; the rule is `TenantPlan` in Core. A schema per tenant or a database per tenant are outside what the generator reads (it reads one schema).

`TenantTable=Tenant` turns it on. **Tenant-owned** is derived, not listed: a table with a NOT NULL whole-number column `TenantId` (`TenantColumn`) that is a foreign key to the tenant table. `NoTenantTables=Country,Currency` leaves out tables that have the column but are shared. The tenant table needs a single whole-number key. The person table (`UserTable`) is normally not tenant-owned: a person can work in several tenants.

## What is generated

| Where | What |
|---|---|
| `CS_DbContext` | `ITenantScope` and `ClaimsTenantScope` (the `tenant` claim of the token), a second constructor that takes the scope, `CurrentTenantId`, and one query filter per tenant-owned entity: `e.TenantId == CurrentTenantId` (together with the soft-delete flag when `SoftDeleteTables` names the table; EF allows one filter per entity). The tenant table is filtered by its own key, so a caller sees only their own tenant. A caller in no tenant (`CurrentTenantId` null) sees no tenant-owned row; a context made without the scope sees none either. |
| `CrudApi` / `NameActiveCrudApi` | A tenant-owned table's API says `Tenant => c => c.TenantId`; the base writes the caller's tenant into the row on create **and** on update, whatever the body held, so a row never moves to another tenant. A caller in no tenant is refused (403, `/problems/tenant-required`). Reads, updates and deletes of another tenant's row are 404, since the context never finds it. |
| the tenant table's API | No create and no delete (403, `/problems/tenant-onboarding`): a tenant is switched off with its active flag and created by the onboarding route. |
| `Apis/Tenants.cs` (`API_Tenants`) | `TenantLoginClaims`, `TenantTestSupport` and the routes below. |
| `Auth.cs` (the Jwt essentials) | `LoginRequest` takes an optional `tenantId`; any number of `ILoginClaims` contributors add claims, a role or a tenant to the token (or refuse the sign-in); `AuthUser` answers the tenant. |
| `TableUserDirectory` | Reads the user table without its query filters, since nobody works in a tenant when they sign in (a soft-deleted person still cannot sign in). |

## Who is in which tenant

- **`TenantMembership=EmployeeTenant:EmployeeId:TenantId:Role`** (a table, its person column, its tenant column and optionally a text role column; needs `UserTable`): the person works in the tenant the login asks for (`tenantId` in the login body) or in their first active one. The role of the membership replaces the person's own role in the token, so one person can be an administrator of one tenant and an employee in another. A person who belongs to no tenant signs in to none (a platform administrator); naming a tenant they do not belong to is 403 (`/problems/tenant-not-member`). Inactive tenants (`IsActive` false) are not offered and cannot be signed in to.
- Without a membership table, a user table that is itself tenant-owned (it has the `TenantId` column) puts that tenant into the token.
- `GET /api/auth/tenants` lists the caller's tenants (`{ id, text, role, current }`) and `POST /api/auth/switch-tenant` (`{ "tenantId": 2 }`) answers a new login response with a new token for the same person in another of them (403 when they do not belong to it). Only with a membership table.
- A token keeps working until it expires after its tenant is switched off.

## Onboarding

`POST <tenants>/onboard` with `{ "tenant": { ... the tenant row ... }, "adminId": 7 }` creates the tenant and, with a membership table, a membership of the person `adminId` with `AuthNames.AdminRole` as their role in it, in one transaction. Only a **platform administrator** may: the administrator role in a token that has no tenant (the bootstrap administrator, or a person who belongs to no tenant); an administrator inside a tenant gets 403 (`/problems/tenant-onboarding`). A value the database refuses (a missing column, a taken slug, an unknown person) is 400 and nothing is saved. The tenant row is taken as sent, so a created-on stamp the table needs must be in the body. The person who becomes the first administrator is an existing row of the user table (create people with your own routes or script).

## Generated tests

With `ApiTests=true` the fixture finds the first two active tenants of the database (`TenantTestSupport.FirstTenantsAsync`) and the tests work as an administrator of the first, with tokens made from the key of the run (`ClientInTenant`, `PlatformClient`; no sign-in is needed, which also works when the people are rows of the database). Each tenant-owned table gets, in its own test class: another tenant cannot read, change, delete or list a row; a new row belongs to the caller's tenant whatever the body says and an update keeps it; a caller in no tenant sees no row and cannot create one; with `ApiCsv` the export of such a caller holds only the header; with OData the count is 0. `API_Test` writes no class for a Name and IsActive table, so `API_TestTenants` writes the classes of the tenant table (own tenant only, no create, no delete, only a platform administrator onboards) and of the tenant-owned Name and IsActive tables. The tests that need two tenants end inconclusive when the database has fewer than two active ones.

## Not covered

No screen: a tenant switcher in each front end, the tenant name in the header, an onboarding form and the sign-in's tenant box are not written (the calls exist in the API). The Python and Rust APIs, the stacks that read the database directly (a WinUI3 or Uno app over its own context) and anything that bypasses the context (raw SQL, the dashboard queries, a report, a stored routine) know nothing of the filter; `IgnoreQueryFilters()` in your own code removes it too (the soft-delete routes put the tenant back). A unique index across tenants (a name unique in the whole table) still refuses the second tenant's equal name: make such an index `(TenantId, Name)` in the database; the duplicate-name rule of a Name and IsActive table already compares only within the caller's tenant. Editable child rows of a tenant-owned table are written by the parent's save without the stamp; keep them out of `EditableChildren`. Hosting a tenant per host name needs your own `ITenantScope`. The audit trail, the approvals and the dashboard read through the same context, so they are filtered; their own side tables need the tenant column to be filtered too.

Checked with `Docs/Verification/Tools/Schemas/tenants.sql` (tenants, people, memberships, an organization table and a project table): the generated API tests over a SQLite copy, a sign-in, switch and onboarding battery written for the scratch project, and the whole generated test suite of the InvoiceSystem databases with `TenantTable=Tenant` on SQL Server, PostgreSQL and MySQL.
