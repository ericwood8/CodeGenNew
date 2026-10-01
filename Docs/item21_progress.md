# Item 21 (project settings) — status: BUILT, awaiting user review/commit

Spec: `C:\EricWork\CodeGenPossibilities\spec.md` item 21 (has a "Built (2026-09-30)" note). Delete this file once committed.

Done: ProjectSettings.cs (Core), TemplateRunner `project` arg, AppSettings.ProjectsDirectory/LastProject, 17 templates
(`Project` parameter + `x = Project.X ?? x;`), CLI flags, App Project dialog (Alt+P), README section,
ProjectSettingsTests (9), live CLI smoke test against InvoiceSystem OK, App + CLI build clean.

Test suite: 272 pass, 2 fail (WinUI3_DetailMasterScreen_writes_the_form_plus_one_grid_per_child_table and
..._child_grid_hides_internal_ids_...) on template text this work did not touch -- pre-existing / unrelated; investigate separately.

Not done / follow-ups: click through the Project dialog in the running app; hiddenParent/modelFileOf/baseEntity as project
keys; item 19 should use Project.MinYear/MaxYear (the earlier uncommitted NumberBox edits in the two WinUI3 detail
templates are no longer in the working tree).
