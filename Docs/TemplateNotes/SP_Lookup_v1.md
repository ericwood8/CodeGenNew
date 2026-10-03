# SP_Lookup_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates: <TableName>_Lookup.sql

A Lookup procedure returns, for every row of the table, just enough to RECOGNIZE it: the row's ID and its
critical display columns, and for every foreign key the referenced row's ID and ITS critical display
columns - so a list or drop-down can show "Account 0110-000 PROSPECTS INVENTORY, Balance Type: Asset"
without the caller joining anything itself.

Which columns are "display columns" comes from SpecialLogicColumns.config's DisplayColumn rule (patterns
in priority order), applied to this table and, via SP_Lookup_v1.tt.config's NeedsReferencedDisplayColumns,
to each referenced table. A table matching none contributes its first ordinary string column instead.

Written to avoid the defects of the original generator's output (its sample lookup procedure):
  - Every output column has a UNIQUE name. The original returned a dozen columns all called "ID", which a
    caller cannot tell apart. Here a foreign key's ID is the base table's own FK column, under its own name
    (AccountType1099EnumID); a foreign row's display value is named after its ROLE (AccountType1099).
  - A foreign key's ROLE comes from the FK column's name: a trailing EnumID or ID is dropped, and so is a
    "_<ReferencedTable>" tail. AccountType1099EnumID -> AccountType1099; OwnerID -> Owner;
    ContraAccount_AccountID -> ContraAccount. The role is used as the table alias, so the SAME table joined
    several times (SYEnum, once per enum column) gets a distinct alias each time, and a table that
    references ITSELF (ContraAccount_AccountID -> Account) is joined under its role (ContraAccount), never
    under its own name - the original wrote "Account AS Account", which compared the table to itself and
    emitted an empty "Account. AS Account" column. A role that still collides gets a numeric suffix.
    A referenced table with several display columns names them <Role><Column>, with the referenced table's
    name trimmed from the column's front (ContraAccount + AccountNumber -> ContraAccountNumber).
  - LEFT JOIN wherever the foreign key column is nullable, INNER JOIN only when it is NOT NULL. The original
    INNER JOINed everything, which silently drops every row whose optional foreign key is NULL (most of
    the Account rows in the seed data). Composite foreign keys join on all of their columns.
  - A referenced table with no display column adds no join (the foreign key's ID is still returned).
  - Foreign keys are listed alphabetically by role, the row's own columns first.
  - ORDER BY is the row's best-ranked display column (the earliest DisplayColumn pattern that matched -
    ShortDescr for Account), then the primary key so equal descriptions come back in a stable order.
  - If the table has an active/inactive flag (the "consider adding an active flag" note in the original),
    only active rows are returned unless the caller passes @blnIncludeInactive = 1; the flag itself is
    returned so a screen can grey out inactive rows. (Referenced rows are never filtered: an existing row
    must still show what it points at, even if that has since been made inactive.)

Kept from the original: it takes no filter parameters, and it raises error 55508 ("No <Table> found for
<procedure>.") when there is nothing to return.

Requires a primary key (the ID) and a table, not a view.
```
