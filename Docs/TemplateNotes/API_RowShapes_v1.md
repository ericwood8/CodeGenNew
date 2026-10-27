# API_RowShapes_v1 and the TreeTables, SoftDeleteTables and EffectiveDatedTables settings

Three table shapes that an ordinary list-and-edit API gets wrong: a table that refers to itself (a tree), a table whose rows are flagged instead of removed (soft delete), and a table whose rows are valid for a period (effective dating). The rules are `TreePlan`, `SoftDeletePlan` and `EffectiveDatePlan` in Core (`RowShapePlans.cs`); `RowShapeWriter` writes the C#. All three need the ASP.NET API and `AccessMode=Ef`, a single whole-number key, and they are off until the project lists the table. A table can have more than one shape (a category tree with soft delete); the user table cannot have any.

`API_RowShapes` writes `Apis/RowShapes.cs`: per table a static `<Table>Shape` class and `RowShapeApi.Register`, called by `API_Registration`. The table's own API (`API_Crud`) overrides `BeforeSaveAsync` and `BeforeDeleteAsync` to call `CheckSaveAsync` and `CheckDeleteAsync`. Those hooks used to exist only with `ApiAuth`; the `CrudApi` essentials now write them also for a project that lists a shape, so **refresh the CrudApi essentials** (the essentials replace option) in an existing project. With `ApiAuth` the extra routes need a signed-in caller, and `deleted` and `restore` need the administrator role.

## TreeTables=Category:ParentCategoryId:3

A table, optionally its parent column (default: the table's one foreign key to its own key) and optionally the deepest level allowed (a root is level 1; blank: no limit). The parent column must allow NULL.

| What | How |
|---|---|
| A parent that is the row itself, or one of its own descendants | 400, `/problems/tree-cycle`. The check walks up from the new parent, so it also stops a cycle that is already stored. |
| A level past the limit | 400, `/problems/tree-too-deep`. The row's level plus the height of the subtree under it, so moving a branch cannot push its leaves past the limit. |
| Delete of a row with children | 409, `/problems/tree-has-children`. |
| `POST {id}/promote-children` | The children become children of the row's own parent (roots when it is a root); the row stays, so it can be deleted next. 404 for a missing row, otherwise `{ moved }`. |
| `GET tree` | Every row depth first, children after their parent and by name (the `Name` or `Title` column, else the key): `{ id, parentId, depth, hasChildren, text }`. A row whose parent is not in the table (a soft-deleted parent) is a root. |

## SoftDeleteTables=Customer,Order:IsRemoved

A table and optionally the yes/no column (default `IsDeleted`, NOT NULL). `DeletedOn` (date and time) and `DeletedBy` (text), both allowing NULL, are filled when the table has them; `DeletedBy` is the signed-in name with `ApiAuth`.

- `DELETE` sets the flag instead of removing the row and answers 200; the delete of a row that is already flagged is 404.
- `CS_DbContext` adds `HasQueryFilter(e => !e.IsDeleted)`, so a flagged row is in no list, search, look-up or read of the generated API, and a PUT to it is 404. Regenerate the context. Other stacks that read the database directly (the Python and Rust APIs, a WinUI3 or Uno app over the context of its own, reports) do not know the filter.
- `GET deleted` lists up to 500 deleted rows, the last deleted first (an administrator).
- `POST {id}/restore` clears the flag and the stamps and answers the row; 404 when there is no such row, 409 `/problems/not-deleted` when it is not deleted (an administrator).

A row that other rows refer to is not removed by a soft delete, so the foreign keys keep pointing at it; a unique index still counts it.

## EffectiveDatedTables=Price:ValidFrom:ValidTo:ItemId+CurrencyId

A table, the first and the last day columns (the same date type; the first cannot be NULL, an empty last day is open) and optionally the columns that say what the row is a price of (default: every single-column foreign key). Both ends are inclusive.

- A save whose last day is before its first day: 400, `/problems/effective-range`.
- A save whose period overlaps another row with the same values in the group columns: 409, `/problems/effective-overlap`. A row is never compared with itself, so a change that keeps its own period is fine; a table with no group column is one group.
- `GET current?on=2026-10-27&itemId=5` answers the rows in effect on the day (today when `on` is left out), narrowed by the group columns that are given.
- The table gets no Clone: a copy would have the period of the row it copies.

## Generated tests, the http file and the clients

`API_Test` adds, to the table's own test class, the tests of each shape it has: cycles, delete with children and promote, the tree order and the depth limit; delete, hidden, listed as deleted, restore, restore twice and restore of a missing row; a backwards period, an overlap and a neighbouring period, the rows in effect on the first and last day and the days around them, and an open period. Every test makes its own rows with the sample body and removes them. The ordinary tests of an effective-dated table send periods of one day in the 19th century (before real data, after SQL Server's 1753) with a last day of their own, so they never overlap each other or a period that is open. The test that needs a group with no open period skips when the group has one.

`API_Http` writes the requests of the extra routes. The React, Angular, Blazor and Uno clients get `getTree`, `promoteChildren`, `getDeleted`, `restore` and `getCurrent` (`GetTreeAsync` and so on) for the tables that have the shape.

## Not covered

No screen yet: a tree as an indented list with expand and collapse and "add a child", a "Show deleted" switch with Restore, and a "valid on" date box are not written in any front end; the clients only have the calls. The Python and Rust APIs do not write these checks or routes. The check that a parent is not a descendant and the overlap check read and then write, so two requests at the same moment can both pass; a unique index or a serializable transaction closes that. Soft delete does not cascade: the children of a soft-deleted tree row stay and show as roots. A tree cannot have a composite key, and the depth limit is per table.

Checked with `Docs/Verification/Tools/Schemas/row_shapes.sql` (a category tree with soft delete, a soft-delete table and an effective-dated price table): the generated API tests over a SQLite copy, with and without `ApiAuth`, and over the same tables on SQL Server, PostgreSQL and MySQL.
