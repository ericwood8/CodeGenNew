# SP_Junction_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates: <TableName>_Junction.sql - three procedures for a many-to-many "junction"/"bridge"
table (TableModel.IsJunctionTable), backing a two-list "available/selected" shuttle-control screen
(see WinUI3_JunctionEditor.tt, and Docs/Reference.md section 5 for the detection rule):

  <Table>_List   (@<Anchor>Id)              - every row of the TARGET parent table, with an
                                                IsSelected bit saying whether it's linked to the
                                                given anchor row.
  <Table>_Link   (@<Anchor>Id, @<Target>Id) - adds the association if it doesn't already exist.
  <Table>_Unlink (@<Anchor>Id, @<Target>Id) - removes it.

Why not just reuse SP_Insert/SP_Delete: those operate on the table's PRIMARY KEY, but a junction
table's association columns are only sometimes its primary key (TableModel.IsJunctionTable
deliberately also recognizes the surrogate-identity-key shape - confirmed against a real database,
a second production database's dbo.NameBaseGroupXref table, 2026-09-25 - where the two FK columns
are NOT the primary key at all). Link/Unlink work by the association columns directly, whichever
shape they're in.

Which of the two association columns is the "anchor" (the side already picked elsewhere, e.g. "this
NameBase's Groups") and which is the "target" (the side being multi-selected) is not something the
schema can say - this template takes TableModel.JunctionForeignKeys[0] as the anchor and [1] as the
target (i.e. column order: NameBaseID before GroupID means "edit a NameBase's Groups", not the
other way around). If your table's natural direction reads the other way, swap the two @@@FILE
parameter names/roles by hand in the generated output - CodeGenNew does not guess intent here.

Audit columns are handled the same way SP_Insert.tt already does: a CreateDateColumn match is
hardcoded to GETDATE() on Link; a CreateUserColumn match is an optional caller-supplied parameter.
Any OTHER extra column (there normally isn't one - see TableModel.IsJunctionTable) is not written by
Link at all and is left at its database default.

Requires a primary key (RequiresPrimaryKey=true in .tt.config - NOT the junction's own PK
specifically, just that TableOnly/RequiresJunctionTable can be checked against a real TableModel) and
TableModel.IsJunctionTable (RequiresJunctionTable=true), and the target table's display columns
(NeedsReferencedDisplayColumns=true), the same way SP_Lookup.tt does.
```
