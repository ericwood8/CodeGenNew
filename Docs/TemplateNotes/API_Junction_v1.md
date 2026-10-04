# API_Junction_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates: <TableName>JunctionApi.cs   (see OutputName in API_Junction_v1.tt.config)

The HTTP companion to SP_Junction.tt for a many-to-many junction table (TableModel.IsJunctionTable,
RequiresJunctionTable=true) - what WinUI3_JunctionEditor.tt's ViewModel reaches directly via EF Core
(it's in the same process as the database), a browser-based screen (TS_JunctionComponent.tt) cannot,
so this exposes the same three operations over HTTP instead:
    GET  /<plural>/junction/{anchorId}          every target row + IsSelected  (calls <Table>_List)
    POST /<plural>/junction/link                { anchorId, targetId }          (calls <Table>_Link)
    POST /<plural>/junction/unlink               { anchorId, targetId }          (calls <Table>_Unlink)

Like API_Crud.tt, handlers are static methods over BaseApi<T>'s route-naming helper
(BreakIntoStrings), registered from Register(). Unlike API_Crud.tt, there is no <Table>Repo to go
through - a junction table's only real operations ARE List/Link/Unlink, so the handlers call the
three SP_Junction.tt procedures directly through the injected DbContext
(Database.SqlQueryRaw<T>/ExecuteSqlInterpolatedAsync), the same way WinUI3_JunctionEditor.tt's
ViewModel does - one fewer generated class, and one less thing for the two templates' conventions to
drift apart on.

Display columns are read as plain strings: SP_Junction.tt CASTs them to NVARCHAR in its SELECT list
specifically so this DTO never needs to know a display column's real SQL type.

Requires a primary key and TableModel.IsJunctionTable (RequiresJunctionTable=true), and the target
table's display columns (NeedsReferencedDisplayColumns=true), the same restrictions SP_Junction.tt
itself has - generate that first.
```

- **Access mode (EF Core instead of routines):** with `AccessMode=Ef` (always for SQLite) this template writes LINQ over the context instead of calls to the routines. See CS_SearchQuery_v1.md and Docs/Reference.md.
