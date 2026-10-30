# Verification tools

Small Python scripts for the steps that are repeated whenever a template or a Core helper changes. They need Python 3, the .NET SDK and a built CLI (`dotnet build CodeGenNew.Cli`, or `--build-cli`). They hold no passwords or machine names.

| Script | Use |
|---|---|
| `new_scratch_project.py` | Builds a throwaway SQLite database from a schema file (`Schemas/plain.sql`, `Schemas/master_detail.sql`, `Schemas/junction.sql` with a many-to-many table and a cascading key, `Schemas/field_limits.sql` with a month, a money amount and a birth date and one row already outside the limits, `Schemas/approvals.sql` with an employee table and a time sheet for RowApprovals (apply the script `SP_ApprovalTable` writes, then generate again), `Schemas/approvers.sql` (the same with the side table and an `Approver` table for `ApproverTable=Approver:EmployeeId,TableName`), `Schemas/log_table.sql` with an `ErrorLog` (a timestamp, a level, a user and a message) beside an ordinary table, for `LogTables=ErrorLog`, `Schemas/queue_archive.sql` with an `ErrorLog` and its `ErrorLogArchive` and a `JobQueue` holding pending, done and failed rows, for `QueueTables=JobQueue` and `ArchiveTables=ErrorLog:90,JobQueue:30:delete`, `Schemas/gated_changes.sql` with employees, customers with a guarded flag and invoices for ChangeGates and GuardedColumns, `Schemas/desktop_rules.sql` with an email, a phone, a not-future date, a start and end date, a whole number and a text-keyed child, or your own), writes a project file for it in the CLI's `Projects` folder and generates into a temp folder. `--set Key=Value` adds project settings, `--stack` picks the stacks. |
| `container_run.py` | Generates the API and its tests for an existing SQL Server, PostgreSQL or MySQL database with `ApiTestContainers` and no `ApiTestSchema`, then runs them over a container filled from the create script written from the catalog (needs Docker). The password comes from `PGPASSWORD` or `MYSQL_PWD`. Use it to prove a change to the create script or the container fixture. |
| `compare_with_head.py` | Generates with the working tree's templates, puts the committed copy of every changed template back, generates again and lists the files that differ (`--diff` shows the lines). Run it with the scratch project to prove that a change moved only what it should. |
| `ef_read_all.py` | Reads every table of a generated app (`--app Output/X.App`) through its generated DbContext in a throwaway console project (Entities, Data, Repositories only) and prints `FAIL <DbSet>: ...` per table that cannot be read, ending `tables ok=N fail=M`. Finds what a build cannot: a relationship EF cannot build, a NULL in a column EF made required, a missing column. SQL Server; read only. |
| `tenant_bypass_check.py` | Plays the dashboard, a junction table, the row approvals and the host name of a tenant against a running multi-tenant API over a scratch copy of `Schemas/tenants_bypass.sql`: `python tenant_bypass_check.py http://localhost:5080 <sqlite file> [dashboard,junction,approvals,host]`. It writes test password hashes into the copy. Works against the ASP.NET, Python and Rust APIs. The generated test suites hold the same cases in a tenant that holds nothing; this script also plays the sign-in and the switch on a tenant's host name. |
| `tenant_onboard_check.py` | Plays `POST <tenants>/onboard` (a member is refused, an unknown first administrator is a 400 and saves nothing, a platform administrator onboards a tenant with and without a first administrator) against a running multi-tenant API over the same scratch copy: `python tenant_onboard_check.py http://localhost:5080 <sqlite file>`. Run `tenant_bypass_check.py` first, it writes the test password hashes. |

| `pytest_standin/play.py` | Plays a generated `tests/test_queue.py` (or the module you name in the script) without installing pytest: `python pytest_standin/play.py <generated api folder>`. `pytest_standin/pytest.py` fakes only `fixture`, `skip` and `mark.parametrize`, so it runs the generated tests but proves nothing about pytest itself. Use real pytest when it is installed. |

A typical check of a template change:

```bash
python new_scratch_project.py Shop --schema master_detail --stack api,react --set Dtos=true
python compare_with_head.py --project Shop --db <database path printed above> --stack api,react --diff
```

Then run the tests of the generated front end with `run_frontend_tests.py` from the general tools folder, passing the generated app folder.

Angular needs `--set AngularVersion=22` for Vitest on jsdom. Tables need five or more columns to get a screen. A change in Core code is in both runs of `compare_with_head.py`: compare that with a `git worktree` of HEAD.
