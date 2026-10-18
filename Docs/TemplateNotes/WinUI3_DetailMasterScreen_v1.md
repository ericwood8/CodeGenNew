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

## Stamps, money, short boxes, bold buttons

A column the system writes (`ColumnModel.IsSystemStampColumn`: CreateDate, CreateUser, ModifiedDate, ModifiedBy, LastDateChanged) is a label with the value once the row exists, never a box, and its timestamp is not cut to a date when the row is opened. A money column (a money type, or a decimal, float or real whose name has a Fee, Balance, Amount, Surcharge, Upcharge, Discount, Price, Total, Average, Tax, Cost or Rate word and no percent, quantity, hours or ratio word) is edited in a money box showing $1,234.50 (React `CurrencyInput`, Angular `appCurrency`; WinUI3 already had a currency NumberBox) and the grid shows at least two decimals. A whole number with short values (a month, a year, a tinyint) gets a short box (`ProjectSettings.NumberBoxCharacters`). Every button's caption is bold (the Styles / Layout / App essentials: refresh them with `--replace`). An editable child with more than six columns is one block of labelled fields per row (`EditableChild.UsesCards`).

## Required fields

A required field (`FormModel.IsRequired`: NOT NULL, not a yes/no box) is flagged for UI Automation with `AutomationProperties.IsRequiredForForm="True"`. Unless `RequiredMarks=false`, its header is drawn by the dialog's `RequiredHeader` template (`HeaderTemplate`), which prints the caption and a red `*`. The template is declared once in the dialog's resources, and only when a field is marked. Save is not disabled; `SaveAsync` still refuses an empty required value with its message.


## Type-ahead for a big parent

A parent with more rows than `TypeAheadRows` (default 1000, read from the database when the schema is read) or listed in `TypeAheadParents` is not read whole. The form field is an `AutoSuggestBox` that asks the context for the first ten rows whose text contains what was typed (`Suggest<Parent>Async`); typing takes the chosen key off (the save then says the field is required), picking a row chooses it. Only the row being edited is read, for the name the box starts with. A child grid that shows a name from such a parent reads only the names of the keys its rows hold (`Where(p => wanted.Contains(p.Key))` after the rows are read), instead of every row of the parent.
The rule is `FormParent.TypeAhead` in Core, the same for every stack; `TypeAheadRows=0` turns the automatic choice off. The API answers `GET <route>/lookup?search=text&take=10` (the first rows whose lookup text contains it) and `GET <route>/lookup?ids=1,2` (the names of those keys): see API_EssentialCrudApi.
- **Field checks:** `Validate` also refuses a text box whose name says email, phone, URL, postal code or social security number when the value has not that shape, and a date later than today for a column in `NotFutureDateColumns` (the message appears in the dialog's error bar and the dialog stays open). The tests are `ColumnRules.CSharpTest`; `PlainTextColumns` switches them off.
