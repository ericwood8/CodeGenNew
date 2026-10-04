# WinUI3_JunctionEditor_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates a WinUI3 two-list "available/selected" shuttle-control ContentDialog for editing one row's
side of a many-to-many association (TableModel.IsJunctionTable - see Docs/Reference.md section 5), the
companion UI to SP_Junction.tt (generate that first; this template calls its three procedures):
    Views/<TableName>JunctionEditor.xaml       the two ListViews + <</</>/>> buttons
    Views/<TableName>JunctionEditor.xaml.cs    button click handlers, calling the ViewModel
    ViewModels/<TableName>JunctionEditorViewModel.cs   loads via <Table>_List, moves items via
                                                         <Table>_Link/<Table>_Unlink

Like SP_Junction.tt, TableModel.JunctionForeignKeys[0] is treated as the "anchor" (the row already
selected elsewhere, e.g. a specific NameBase - passed into the dialog's constructor) and [1] as the
"target" (the side being multi-selected, e.g. that NameBase's Groups). Swap the roles by hand in the
generated output if your table's natural direction reads the other way.

Design (matches CodeGenNew.App's own WinUI3 conventions - see the winui3/ui-conventions skills -
but assumes NOTHING from CodeGenNew.App itself is available in the target project, the same way
CS_Repo.tt assumes only that a GenericRepo base class exists there):
  - A ContentDialog, not a full Page: this is meant to be opened from a parent detail screen (e.g. a
    generated WinUI3_DetailScreen for the anchor table, once that template exists) the same way
    CodeGenNew.App's own ConnectionDialog/LocationDialog are opened.
  - Immediate commit: each Add/Remove click calls Link/Unlink right away (an INSERT or DELETE of one
    association row) rather than batching changes for a single Save button - simpler to reason
    about and verify, and matches how CodeGenNew.App's own TemplateManagementDialog commits each
    rename/delete immediately rather than batching.
  - Data access goes through the project's existing EF Core DbContext (the same `contextType`
    CS_Repo.tt/API_Crud.tt already use), calling SP_Junction.tt's three procedures via
    `Database.SqlQueryRaw<T>`/`ExecuteSqlInterpolatedAsync` (EF Core 8+) rather than raw ADO.NET -
    one fewer convention for the target project to have to support.
  - Display columns are always bound as plain strings: SP_Junction.tt CASTs them to NVARCHAR in its
    SELECT list specifically so this ViewModel's row type never has to know each one's real SQL type.

Requires a primary key and TableModel.IsJunctionTable (same restrictions as SP_Junction.tt), and the
target table's display columns (NeedsReferencedDisplayColumns=true).
```
