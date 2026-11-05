# API_Features_v1 and the FeatureTables, FeaturePlanTable and FeatureScreens settings

Feature flags kept in tables, for an API with sign-in. A flag is on or off by default; the plan of a tenant can switch it on, a tenant can have its own value and so can a person. The API's own code asks whether a flag is on, a front end reads `GET /api/features` once after sign-in and hides the screens a flag controls, and an administrator switches a flag for the tenant on a page of the front end or with `PUT /api/features/{name}`.

```
ApiAuth=true
TenantTable=Tenant
UserTable=Employee
UserOwnerColumn=EmployeeId
FeatureTables=Feature,FeatureOverride
FeaturePlanTable=PlanFeature
FeatureScreens=Invoice:new-invoice-screen
FeatureFields=Invoice.Notes:beta-notes
FeatureCacheSeconds=30
```

The ASP.NET, Python and Rust APIs serve the flags (`API_Features`, `PY_Features`, `RS_Features`). The React, Angular, Blazor and Uno front ends read them from the API (`TSX_Features`, `TS_Features`, `BLZ_Features`, `UNO_Features`, with `Auth=true`). The WinUI3 app reads the database itself, so `WinUI3_Features` writes the rule of the API for its own context (it needs the desktop sign-in, and the flag tables are read with the same names as the API reads them). Overrides for a tenant need `TenantTable`; overrides for a person need `UserTable` with `UserOwnerColumn` naming the user table's key (the owner claim of the token is then the person). The ASP.NET API needs `AccessMode=Ef`.

## The tables

| Table | Needs |
|---|---|
| Feature | A single whole-number key, a text `Name` column (the flag's name) and a NOT NULL yes / no column for the default (`DefaultEnabled`, `IsEnabledByDefault`, `IsEnabled`, `Enabled`, `IsOn` or `Default`; the only yes / no column when none is named so). |
| FeatureOverride | Exactly one whole-number foreign key to Feature, a NOT NULL yes / no column for on or off (`IsEnabled`, `Enabled`, `IsOn`, `Value` or `State`), and the tenant column (`TenantColumn`, a foreign key to the tenant table) and / or one foreign key to the user table. When both columns exist both must allow NULL: a row names a tenant or a person, not both. No other NOT NULL column without a default. |
| `FeaturePlanTable` | With `SubscriptionTable` and `PlanTable`: a row per plan and flag, one whole-number foreign key to Feature and one to the plan table. A flag listed for a tenant's plan is on for the tenant. |

The flag names are the owner's: rows of the Feature table (and of the plan table) are added with SQL or a seed script. The tables get **no ordinary list and edit API** and no junction routes or screens (a screen would let a tenant's administrator rewrite the flags of every tenant), the same rule as the subscription tables.

## What a person sees

The order is: the default, then the flag being listed for the tenant's plan (on), then the tenant's override, then the person's override. A name that is not a flag is off.

## What is generated

- **API** (`Apis/Features.cs`, `app/routes/features.py`, `src/features.rs`): `FeatureFlags.AllAsync` / `EnabledAsync` and `http.FeatureEnabledAsync("name")` in ASP.NET, `all_flags` and `is_enabled` in Python, `all_flags` and `is_enabled` in Rust; `GET /api/features` (the caller's flags as `{ "name": true }`); `PUT /api/features/{name}` with `{ "enabled": true, "tenantId": 2, "personId": 7 }`: administrators only. A tenant's administrator changes their own tenant (the body names neither); a platform administrator names a tenant or a person (403 for anyone else who names one, 400 when both or neither is named, 404 for an unknown flag, tenant or person). `enabled: null` removes the override. The answer is the flags as the target sees them.
- **Cache** (`FeatureCacheSeconds`, default 0): the API keeps the flags it read for a tenant and a person for that many seconds (`FeatureFlags.CacheFor`, `CACHE_SECONDS`), so a request does not read the three tables again. A change through `PUT /api/features/{name}` forgets every copy at once (`FeatureFlags.Clear()`, `clear_cache()`). Another instance of the API, or a change made straight in the tables, is seen when the copy has expired, so keep the number small where several instances run.
- **Tests**: `FeatureApiTests.cs` (ASP.NET, with `ApiTests`), `tests/test_features.py` (with `PythonTests`) and `tests/api/feature_routes.rs` (with `RustTests`), for a project whose override table has a tenant column and which is not on SQL Server. With `FeaturePlanTable` each also plays a flag the plan lists: on for a tenant on that plan, the tenant's override wins, and without the override the plan decides again.
- **React, Angular, Blazor, Uno** (`Auth=true`): `getFeatures` / `setFeature`, the shell that reads the flags after sign-in and again when the person switches tenant, and the Features page in the administrator's menu (a check box per flag and a button that removes the tenant's override), each with its tests except Uno's. A change on the page shows in the menu at once. `FeatureScreens=Table:flag` hides the table's menu entry while the flag is off.
- **WinUI3** (desktop sign-in): `Repositories/FeatureFlags.cs` (`FeatureFlags.AllAsync` and `SetAsync` over the app's context, and `DesktopFeatures`, the flags of the signed-in person), the window that reads them at sign-in and at a switch of tenant, the same menu rule and the same Features page. It is a check in the client: anyone who writes to the database directly bypasses it.
- **Fields**: `FeatureFields=Table.Column:flag` puts the field in the edit form of the table's own page only while the flag is on (React, Angular, Blazor, Uno, WinUI3; the form of a master-detail page too). A field that is not shown keeps the value the row has, and a new row leaves it at its default, so hide a column that allows NULL or has a default. The columns of the grid, the filters and the child grids are not hidden, the user table's administrator flag (which administrators only see) is not hidden by a flag, and the OData pages ignore the setting.

## What is left

Nothing is cached in the front ends beyond the flags read at sign-in: a flag changed by someone else is seen at the next sign-in or switch of tenant.

Checked with `Docs/Verification/Tools/features_check.py` against a running API over `Schemas/features.sql`, with `Matrix/features.settings` (the generated tests of the ASP.NET, Python and Rust APIs with the plan link and the cache on), and the generated tests of the React, Angular and Blazor front ends.

Clicked through against a running API over a scratch SQLite copy: the React, Angular and Blazor pages in the browser pane (Project leaves and joins the menu, the Features page writes the tenant override, Started On leaves and returns in the Project form, a save while the field is hidden keeps its value), and the Uno and WinUI3 apps with `Docs/Verification/Tools/winui3_features_check.ps1` (`Schemas/features_noplan.sql` for WinUI3, which does not build with the plan link while `WinUI3_Subscription` has the bug noted in the spec).
