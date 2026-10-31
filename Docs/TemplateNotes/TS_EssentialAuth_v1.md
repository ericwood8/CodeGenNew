# TS_EssentialAuth_v1

The `Auth` group of the Angular essentials: the sign-in of the generated app. It is not ticked by default; `Auth=true` in the project file adds it to every `codegen generate --essentials`, or name it: `codegen essentials --stack angular --groups auth --project <name> -o <project root>`. No table and no database are needed, and no package: only Angular and RxJS.

## What it writes

| File | What it does |
|---|---|
| `<ServicesFolder>/auth.service.ts` | `AuthService`: `login(userName, password)` posts to `api/auth/login`, keeps the answer in a signal and in `sessionStorage` (the sign-in ends when the tab closes), `logout()` forgets it and goes to `/login`; `user`, `isLoggedIn`, `token`. A saved session that has expired (`expiresUtc`) or cannot be read is dropped. |
| `<ServicesFolder>/auth.interceptor.ts` | `authInterceptor`: adds `Authorization: Bearer <token>` to every call whose URL starts with `api/`, and signs the person out when such a call answers 401 (the sign-in call itself is left to the login page). |
| `<ServicesFolder>/auth.guard.ts` | `authGuard`: a signed-in person passes, anyone else is sent to `/login`. |
| `<ModelsFolder>/auth.ts` | `AuthUser` (`name`, optional `role`, anything else the API sends) and `LoginResponse` (`token`, `expiresUtc`, `user`). |
| `<ComponentsFolder>/login/` | The sign-in page: user name, password, a message for 401 (wrong name or password), 429 (too many tries) and anything else; goes to `/` after a sign-in. |
| the specs | `auth.service.spec.ts`, `auth.interceptor.spec.ts`, `auth.guard.spec.ts`, `login.component.spec.ts`. They use a fake `Router` and no spy library, so they run under Jasmine and Vitest. |

## What `Auth=true` changes in the other groups

- **Config** (`app.config.ts`): `provideHttpClient(withInterceptors([authInterceptor]))`.
- **Screens** (`app.routes.ts`, written by `TS_Screens`): a `login` route, and `canActivate: [authGuard]` on every generated route.
- **Shell** (`app.ts`, `app.html`, `app.css`): the menu and the hamburger show only to a signed-in person; a top bar shows the user's name and a **Sign out** button. With `TenantTable` and `TenantMembership` it also has `app-tenant-switcher`: the tenant's name, or a drop-down when the person belongs to several, which asks `POST api/auth/switch-tenant` for a token and loads the app again. It shows nothing when the API has no tenants route.

Essentials are written once and kept, so a project whose Shell and Config exist already needs `--replace` for those two groups after turning `Auth` on. `app.routes.ts` is regenerated every time.

## What the API has to do

`POST api/auth/login` with `{ userName, password }` answers `{ token, expiresUtc, user: { name, ... } }`, 401 for a wrong name or password, 429 for too many tries; every other `api/` route answers 401 without a valid bearer token. The generated ASP.NET API does this with `ApiAuth=true` (the `Jwt` group, `API_EssentialJwt_v1.md`: JWT bearer wiring, the login route and a fallback policy that asks for a token everywhere); the user table stays hand-written, and the project registers its `IUserDirectory`.

`hasRole(roles)` says whether the signed-in role is one of the list (used by the screens for `WriteRoles` and `ReadRoles`).

**Tenant drop-down.** With `TenantTable` and `TenantMembership` the sign-in page has a Tenant drop-down (the person picks a tenant by name; `AuthService.signInTenants()` reads the list). The names come from `GET /api/auth/sign-in-tenants` (no sign-in needed: the tenants that are active and not marked deleted, by name); the first entry is blank and means the person's first tenant; when the list cannot be read the drop-down holds only the blank entry. `AuthService.login(userName, password, tenantId?)` posts the `tenantId` only when a tenant is picked, and a 403 says the person is not a member of that tenant or it is switched off (`TenantWords`).
