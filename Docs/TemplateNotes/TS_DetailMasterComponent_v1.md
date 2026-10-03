# TS_DetailMasterComponent_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
TS_Component.tt's own grid + add/edit form - identical field selection, lookups and rules, see that
template for the reasoning - PLUS one read-only grid per table in TableModel.ChildForeignKeys, shown
under the form while editing an existing row. The Angular counterpart of WinUI3_DetailMasterScreen.tt,
for the gap TS_Component.tt's own header comment names explicitly ("not written: a detail grid -
Department's teams, Project's tasks, TimeSheet's lines").

Per the same answer that shaped WinUI3_DetailMasterScreen.tt ("just have the generated code assume all
are grids and the developer can cut out what grids are not needed"): EVERY table in ChildForeignKeys
gets a grid, unconditionally.

Why a child grid calls the child table's API directly (this.http.get<any[]>('api/<child>s')) instead
of importing a generated <Child>Service/<Child> model, the way every other lookup in this template
does: TableModel is built for exactly one table per generation run, so this template never sees the
child table's own column list or knows what its generated (or hand-written) service looks like -
TS_Service.tt's own header comment calls out a real example (TimeSheetDetailService) whose actual
hand-written service has neither a getAll() method nor the standard method names. Calling the raw API
URL directly (same "api/" + lower-cased-plural-name convention TS_Service.tt itself uses) sidesteps
that entirely - same reasoning TS_JunctionComponent.tt already used for its own HttpClient calls, and
the same "read the shape at run time, not generation time" idea WinUI3_DetailMasterScreen.tt uses EF
Core metadata for. Rows are loosely typed (any[]); columns are whatever keys the first loaded row has
(Object.keys), so renaming or excluding one is a one-line edit in the generated file once you know
the child's real shape - this exists to get you unblocked, not to be the final word. Loads the WHOLE
child table and filters client-side by the foreign key column, same "no paging" simplification
TS_Component.tt already makes for its own grid.

Only loaded once the row being edited has a real key (a brand-new, unsaved row has no child rows
yet); the child sections show a "save first" message instead while adding.

Requires a primary key that is a single int or uniqueidentifier column (same as TS_Component.tt) and
at least one table in TableModel.ChildForeignKeys (TableModel.HasAtLeastOneChildForeignKey) - a table
with none is exactly what TS_Component.tt is for.
```
