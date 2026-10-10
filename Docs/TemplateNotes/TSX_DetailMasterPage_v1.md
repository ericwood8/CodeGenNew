# TSX_DetailMasterPage_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
TSX_Page_v1.tt's own grid + add/edit form - identical field selection, lookups, error-as-state and
rail/no-invented-classes rules, see that template for the reasoning - PLUS one read-only grid per table
in TableModel.ChildForeignKeys, shown under the form while editing an existing row. The React counterpart
of TS_DetailMasterComponent.tt, closing the same gap TSX_Page.tt's own header comment names explicitly
("not written: a detail grid").

Per the same answer that shaped WinUI3_DetailMasterScreen.tt/TS_DetailMasterComponent.tt ("just have the
generated code assume all are grids and the developer can cut out what grids are not needed"): EVERY
table in ChildForeignKeys gets a grid, unconditionally.

A child grid calls the child table's API directly (request<any[]>('/api/<child>s'), filtering client-side
by the foreign key column) instead of importing a generated <Child>Api module the way every other lookup
in this template does - same reason TS_DetailMasterComponent.tt's own header comment gives: TableModel is
built for exactly one table per generation run, so this template never sees the child table's own column
list or knows what its generated (or hand-written) api module looks like. Rows are loosely typed (any[]);
columns are whatever keys the first loaded row has (Object.keys), discovered at run time rather than
generation time - the same "read the shape at run time, not generation time" idea the Angular and WinUI3
families already use for this exact gap.

Only loaded once the row being edited has a real key (a brand-new, unsaved row has no child rows yet);
the child sections show a "save first" message instead while adding. A text key is typed on a new row,
so the key cannot say whether the row is new: the page has an `adding` flag (set by Add New, cleared by
Edit) that picks the heading, the create or update call, the read-only key box and the child grids. The
links to a child's own page and back (?edit=<key>&back=...) encode a text key, the parent's and the
child's, and so does the child's delete route.

Requires a primary key that is a single int, uniqueidentifier or text column (same as TSX_Page.tt) and at
least one table in TableModel.ChildForeignKeys (TableModel.HasAtLeastOneChildForeignKey) - a table with
none is exactly what TSX_Page.tt is for.
```
