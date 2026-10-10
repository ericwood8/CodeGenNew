# WinUI3_DetailMasterScreen_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
WinUI3_DetailScreen.tt's own form (identical field-by-field: same column selection, same drop-down and
validation rules - see that template for the reasoning) PLUS one grid per table in
TableModel.ChildForeignKeys, so editing a row also shows the child rows that hang off it (clicking one opens its own dialog) (e.g. a
Department's Teams, a TimeSheet's lines):
    Views/<TableName>DetailMasterDialog.xaml
    Views/<TableName>DetailMasterDialog.xaml.cs
    ViewModels/<TableName>DetailMasterViewModel.cs
    Views/PaginationBar.xaml and .xaml.cs   the Previous/Next bar the list pages use (same file for every table)

Per the project's own answer on this ("just have the generated code assume all are grids and the
developer can cut out what grids are not needed"): EVERY child table gets a grid, unconditionally -
this template does not try to guess which ones matter. Delete the <ChildTable>Rows section (XAML) and
its Load<ChildTable>Async call (ViewModel) for any you don't want.

Why the child grids use EF Core's own metadata (_context.Model.FindEntityType) instead of column info
like every other field in this template family: TableModel.ChildForeignKeys only carries the child's
constraint and column NAMES (see CodeGenNew.Core.ChildForeignKeyModel) - TableModel itself is built
for exactly ONE table per generation run, so this template never sees the child table's own column
list the way it sees this table's. EF's IEntityType.GetProperties() gives the same information back at
RUN time instead (scalar columns only, navigation properties excluded for free), so the grid can show
something useful without a second schema-introspection pass this architecture doesn't support. Column
order follows EF's own property order, and headers are the raw property name - rename them by hand
once you know what a given child table looks like; this exists to get you unblocked, not to be final.
A child table whose foreign key back to this one spans more than one column is skipped (no grid).

Only loaded when editing an existing row (a brand-new, unsaved row has no child rows yet); the child
sections show a "save first" message instead while adding.

WinUI3_MasterScreen.tt always opens the OTHER dialog, <TableName>DetailDialog (WinUI3_DetailScreen.tt) -
point its OnAddClick/OnEditClick at <TableName>DetailMasterDialog by hand if you want the master screen
to open this one instead.

Requires a primary key that is a single, non-composite int, uniqueidentifier or text column (a text key is typed on a new
row and read-only afterwards, and a foreign key of any key type is a drop-down, as in WinUI3_DetailScreen.tt; each child grid filters on EF.Property of the type of the
key it points at and names a child row's own foreign key through a dictionary keyed by the type of the id it holds), refuses a "name/active" table (same
reasons as WinUI3_DetailScreen.tt), and requires at least one table in TableModel.ChildForeignKeys
(TableModel.HasAtLeastOneChildForeignKey) - a table with none is exactly WinUI3_DetailScreen.tt's job.

The child rows are read with AsNoTracking: they are only shown, and a tracked child would be deleted along with its
parent (EF cascades a required foreign key to the rows it tracks) when the list page deletes the parent from the same
context.
```
