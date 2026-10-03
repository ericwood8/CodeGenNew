# CS_Entity_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates: <TableName>.cs   (see OutputName in CS_Entity_v1.tt.config)

An Entity Framework entity class:

    public class E_DonateLeave : BaseEntity
    {
        #region Omitted                       <- the columns a grid does not show: the key and the foreign keys
            [Key] ... DonateLeaveId
            [ForeignKey(nameof(DonateFrom_Employee))] ... DonateFrom_EmployeeId
        #endregion Omitted
        // entities                           <- one navigation property per foreign key
        public Employee? DonateFrom_Employee { get; set; }
        ... then one property per remaining column, with its Display/DataType/StringLength attributes
    }

How each part is decided:
  - Name: the entity class is named exactly like the table, each property like its column.
  - Base class: a table with a NOT NULL text column called Name AND a NOT NULL bit column called IsActive derives from
    BaseNameActiveEntity (which already declares those two properties, so they are not repeated); any other table
    derives from BaseEntity.
  - Key: the single primary key column gets [Key]. A composite key cannot be expressed with attributes: its columns are written as
    ordinary "Omitted" properties and CS_DbContext names the key in OnModelCreating (HasKey). A table with no key stops with an error.
  - Foreign keys: each single-column foreign key is listed in "Omitted", and gets a navigation property named for its
    ROLE - the column name without its trailing "Id" (DonateFrom_EmployeeId -> DonateFrom_Employee, ManagerId ->
    Manager) - of the referenced table's type, always nullable ("Employee?"; the FK column itself says whether the
    row is required) with [ForeignKey(nameof(<role>))] on the FK column. A foreign key to a table listed in
    noNavigationTables below (an enum, or a table with no entity class) stays a plain int/string column.
  - Other columns: C# type from the SQL type; "required" when NOT NULL (it becomes "T?" / "string?" when nullable);
    Display(Name/Description) from the column name split into words ("WhenDonated" -> "When Donated", a bit column's
    "Is" prefix dropped); dates get DisplayFormat + DataType.Date; decimal/numeric get DisplayFormat and Precision(p, s);
    money gets DataType.Currency and Column(TypeName = "money"); text gets StringLength(chars), and DataType.MultilineText
    (with Display Order = -1, so a grid hides it) from 100 characters up, DataType.PhoneNumber if the name says Phone;
    a bit column with a database default starts at that default ("= false").
  - A table with a text column called Name that is NOT name/active-based gets "public override string? ToString() => Name;".

What it does NOT do, so an entity that needs any of these stays hand-maintained:
  - collection navigation properties (List<Child>? Teams) - their names are the developers' choice;
  - the reverse navigation of a one-to-many that the parent already lists (adding both sides makes serializing the
    parent loop forever);
  - enum-typed properties, [Range]/[RegularExpression]/[Required] validation, hand-picked display names, ToString on
    other columns, or a column the class deliberately makes optional although the table says NOT NULL.
  Nor does it write the entity's DbSet in the context or the Enums.
```
