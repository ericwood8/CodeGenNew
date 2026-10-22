# Verification tools

Small Python scripts for the steps that are repeated whenever a template or a Core helper changes. They need Python 3, the .NET SDK and a built CLI (`dotnet build CodeGenNew.Cli`, or `--build-cli`). They hold no passwords or machine names.

| Script | Use |
|---|---|
| `new_scratch_project.py` | Builds a throwaway SQLite database from a schema file (`Schemas/plain.sql`, `Schemas/master_detail.sql`, or your own), writes a project file for it in the CLI's `Projects` folder and generates into a temp folder. `--set Key=Value` adds project settings, `--stack` picks the stacks. |
| `compare_with_head.py` | Generates with the working tree's templates, puts the committed copy of every changed template back, generates again and lists the files that differ (`--diff` shows the lines). Run it with the scratch project to prove that a change moved only what it should. |

A typical check of a template change:

```bash
python new_scratch_project.py Shop --schema master_detail --stack api,react --set Dtos=true
python compare_with_head.py --project Shop --db <database path printed above> --stack api,react --diff
```

Then run the tests of the generated front end with `run_frontend_tests.py` from the general tools folder, passing the generated app folder.

Angular needs `--set AngularVersion=22` for Vitest on jsdom. Tables need five or more columns to get a screen. A change in Core code is in both runs of `compare_with_head.py`: compare that with a `git worktree` of HEAD.
