# Verification tools

Small Python scripts for the steps that are repeated whenever a template or a Core helper changes. They need Python 3, the .NET SDK and a built CLI (`dotnet build CodeGenNew.Cli`, or `--build-cli`). They hold no passwords or machine names.

| Script | Use |
|---|---|
| `new_scratch_project.py` | Builds a throwaway SQLite database from a schema file (`Schemas/plain.sql`, `Schemas/master_detail.sql`, `Schemas/junction.sql` with a many-to-many table and a cascading key, `Schemas/field_limits.sql` with a month, a money amount and a birth date and one row already outside the limits, `Schemas/approvals.sql` with an employee table and a time sheet for RowApprovals (apply the script `SP_ApprovalTable` writes, then generate again), `Schemas/approvers.sql` (the same with the side table and an `Approver` table for `ApproverTable=Approver:EmployeeId,TableName`), `Schemas/gated_changes.sql` with employees, customers with a guarded flag and invoices for ChangeGates and GuardedColumns, `Schemas/desktop_rules.sql` with an email, a phone, a not-future date, a start and end date, a whole number and a text-keyed child, or your own), writes a project file for it in the CLI's `Projects` folder and generates into a temp folder. `--set Key=Value` adds project settings, `--stack` picks the stacks. |
| `container_run.py` | Generates the API and its tests for an existing SQL Server, PostgreSQL or MySQL database with `ApiTestContainers` and no `ApiTestSchema`, then runs them over a container filled from the create script written from the catalog (needs Docker). The password comes from `PGPASSWORD` or `MYSQL_PWD`. Use it to prove a change to the create script or the container fixture. |
| `compare_with_head.py` | Generates with the working tree's templates, puts the committed copy of every changed template back, generates again and lists the files that differ (`--diff` shows the lines). Run it with the scratch project to prove that a change moved only what it should. |
| `ef_read_all.py` | Reads every table of a generated app (`--app Output/X.App`) through its generated DbContext in a throwaway console project (Entities, Data, Repositories only) and prints `FAIL <DbSet>: ...` per table that cannot be read, ending `tables ok=N fail=M`. Finds what a build cannot: a relationship EF cannot build, a NULL in a column EF made required, a missing column. SQL Server; read only. |

A typical check of a template change:

```bash
python new_scratch_project.py Shop --schema master_detail --stack api,react --set Dtos=true
python compare_with_head.py --project Shop --db <database path printed above> --stack api,react --diff
```

Then run the tests of the generated front end with `run_frontend_tests.py` from the general tools folder, passing the generated app folder.

Angular needs `--set AngularVersion=22` for Vitest on jsdom. Tables need five or more columns to get a screen. A change in Core code is in both runs of `compare_with_head.py`: compare that with a `git worktree` of HEAD.
