# RS_Gates_v1

`src/routes/gates.rs` for a project with `ApiAuth=true` and a `ChangeGates` or `GuardedColumns` rule: the same checks and answers as `PY_Gates` and `API_Gates`, for the Rust API (PostgreSQL, MySQL and SQLite).

`RS_Routes` calls `check_<table>(&pool, &user, &mut row, current)` on create and update and `after_clone_<table>(&pool, &user, &mut copy)` on clone (the copy is updated through the repository and removed again when a gate refuses it). The module's router answers `GET <route>/guards` for each table with a guarded column and `RS_Mod` merges it. A parent's value in a condition is read with its own statement. The sentence of a rule that does not fit the schema is `GatePlan`'s.

Limits: a condition compares whole numbers, yes / no values and text (a decimal or a date in a rule stops the run with a sentence naming the column), a guarded column is a yes / no, number or text column, and a `stampOn` column must be nullable (the stamp of a row that was never guarded is empty). `ApiAuth` is required, as for the ASP.NET API.

The decisions are not made here: `GatePlan.Tables` lists each gated table with its checks (guards, then gates), the holder tables and the sentences, so `API_Gates`, `PY_Gates` and `RS_Gates` write only syntax and a change of wording is one change in Core.
