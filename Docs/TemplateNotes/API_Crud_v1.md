# API_Crud_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates: <TableName>Api.cs   (see OutputName in API_Crud_v1.tt.config)

A minimal-API class for one table:
five endpoints registered from Register(), each a static handler over the table's repository (<Entity>Repo, from CS_Repo):
    GET    /<plural>          all rows
    GET    /<plural>/{id}     one row                 (404 when there is no such row)
    POST   /<plural>          create                  (201 with a Location header)
    PUT    /<plural>/{id}     update                  (400 when the id in the URL and the id in the body disagree,
                                                         404 when there is no such row)
    DELETE /<plural>/{id}     delete                  (404 = no such row, 400 = row is in use by another table)
The route names come from BaseApi.BreakIntoStrings (an "E_" / "SY_" prefix is dropped, the rest is lower-cased and
pluralised with an "s"), so this template never spells a route out.

What comes from the table:
  - the entity class is assumed to be named exactly like the table, and its key property like the key column
    (E_DonateLeave.cs / DonateLeaveId);
  - GetAll's ordering: the first NOT NULL date column (date, datetime, datetime2, smalldatetime) that is not an
    audit column, newest first - GenericRepo.GetAllOrderByDescending only takes a non-nullable DateTime.
    A table with no such column gets a plain GetAll() instead. Change the line in the generated file if another
    column suits the screen better.

What it does NOT do: the entity, its DbSet in the context and the line in ApiRegisterExtension.cs that calls
Register() are written by hand. Nor does it write business rules (validation, defaults, name/duplicate checks); a
table that needs them keeps a hand-maintained API class - generate this one only for the plain shape.

The handlers use <Table>Repo (generate it with CS_Repo), which must sit on GenericRepo: GetAll, GetAllOrderByDescending and
the delete check live there. A name/active table (a NOT NULL text Name plus a NOT NULL bit IsActive - NameActiveRepo)
has no GetAll and does its own duplicate-name and trimming work in its API, so it stops with an error and stays
hand-maintained. So does a table with no repository (enum / *Type lookup tables, tables with no entity).
Requires a table with a single int primary key (routes are {id:int}). Anything else stops with an error.
```
