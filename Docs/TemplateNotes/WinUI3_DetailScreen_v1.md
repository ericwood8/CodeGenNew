# WinUI3_DetailScreen_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates a WinUI3 ContentDialog for adding or editing ONE row of the table (the "detail" screen of a
master/detail pair - see WinUI3_MasterScreen.tt, which opens this dialog, and WinUI3_DetailMasterScreen.tt,
the same idea plus child grids for tables that hang off this one):
    Views/<TableName>DetailDialog.xaml         one field per column, Save/Cancel
    Views/<TableName>DetailDialog.xaml.cs       wires PrimaryButtonClick to the ViewModel's SaveAsync
    ViewModels/<TableName>DetailViewModel.cs    loads drop-down options, validates, calls <TableName>Repo

Column selection mirrors TS_Component.tt (its Angular add/edit form): every column except the primary
key, computed and identity columns. A foreign key becomes a ComboBox of the parent's rows (skipped for
a table listed in noLookupParents, which stays a plain number box); a bit column becomes a CheckBox.

Field binding: WinUI3's compiled x:Bind cannot convert a TextBox's string Text to a numeric, date or
Guid property without a converter class this template cannot assume your project has, so every
TEXT-BOX-BACKED field (numbers, dates, plain text, Guid) is a STRING property on the ViewModel; SaveAsync
parses each one into the entity's real column type and reports the first bad value instead of throwing.
Dates are typed as plain text (yyyy-MM-dd), not a DatePicker, for the same reason (DatePicker.Date is a
DateTimeOffset, not a DateTime). A ComboBox's SelectedValue and a CheckBox's IsChecked DO bind straight
to an int?/bool? property - WinUI3 does not need a converter for either of those.

Data access: the entity's own CRUD goes through <TableName>Repo (generate it with CS_Repo first - same
assumption WinUI3_JunctionEditor.tt makes about SP_Junction.tt); each drop-down's OPTIONS come straight
from the DbContext (context.Set<Parent>()), not that parent's own repository, so this template never has
to assume every foreign-keyed table also got a CS_Repo run against it.

Requires a primary key that is a single, non-composite int, uniqueidentifier or text column (Core's KeyType; the
repository is GenericRepo<T, TKey>, whose GetById/UpdateAsync/DeleteAsync take that key). A text key (a country or
currency code) is the first field: typed on a new row, read-only on an existing one (ViewModel.IsExisting); a key
that is already taken is a message in the dialog (ExistsAsync), not an exception. A foreign key to a text-key parent
is a drop-down too, whatever its key type: the view model holds the id as int?, long?, Guid? or string? (TableModel.ForeignKeyType) and the options come from an option class for that type (<Table>DetailLookupOption for int, <Table>DetailLookupOptionString, ...Guid and ...Long for the others). Refuses a table shaped
like "name/active" tables (a NOT NULL text Name plus a NOT NULL bit IsActive), whose
repository is a NameActiveRepo - it has no GetAll for WinUI3_MasterScreen.tt to call, so the pair is
out of scope here the same way API_Crud.tt refuses them.

What it does NOT do, so a screen that needs any of these stays hand-maintained: validation beyond "is
this well-formed" (uniqueness, cross-field rules), a file picker for a FilePathColumn, or opening from a
NavigationView/Frame - that's WinUI3_MasterScreen.tt's job, or your own code, to wire up.
```
