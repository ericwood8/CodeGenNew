# API_Crud_v1

With `ApiAuth=true`, a table named in `OwnedTables` that has the NOT NULL whole-number `OwnerColumn` (default `EmployeeId`) gets `Owner => c => c.EmployeeId`, and `CrudApi` then scopes its rows to the signed-in person (`API_EssentialJwt_v1.md`). A listed table without the column gets a comment saying so. `OwnedTable` in Core is the one rule.

An `OwnedTables` entry may name its own owner column, `DonateLeave:DonateFrom_EmployeeId`; an entry without one uses `OwnerColumn`.

A table with no owner column of its own that has a NOT NULL single-column foreign key to a listed table (the first one, in column order) belongs to the owner of that parent row (`OwnedTable.Through`; `OwnedChildren=false` turns it off). Its class says `OwnedThroughParent => true` and writes `ScopeThroughParent` (the rows whose parent belongs to an allowed owner) and `ParentOwnerAsync` (the owner of the parent a row names). `CrudApi` then answers 403 for a row under another owner's parent, for a create or update that names such a parent (or one that does not exist), and lists only the allowed owners' rows. The rows of an editable child list are saved after the parent's own check and are tied to that parent. The parent is taken to have its owner column; only one level is followed.

With `EditableChildren` the parent's class also says which child lists its create and update save with the row (`protected override IReadOnlyList<ChildRows<int>> EditableChildren`, one `new ChildRows<int, DepartmentTeam>("departmentTeams", c => c.DepartmentTeamId, c => c.DepartmentId)` per child, from the `EditableChild` rule the Angular form uses too). The config asks for the referenced display columns because the rule reads the child's columns.

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
  - GetAll pages when asked: with no query string it answers every row as a plain array (drop-downs and small tables rely on that);
    with pageNumber and/or pageSize (1-based pageNumber, default size 100, kept between 1 and 1000) it answers one page as
    { items, page, pageSize, totalCount, totalPages } (the shape of the search endpoints, as a <Table>Page record), ordered like the array and by the key
    so pages never overlap, read without tracking. A page number below 1 or one whose first row does not fit a 32 bit offset is a 400 problem.
    The tables that grow still use /search (filters, sort by any column, routines); this is the plain paged read.
  - GetAll pages when asked: with no query string it answers every row as a plain array (drop-downs and small tables rely on that);
    with pageNumber and/or pageSize (1-based pageNumber, default size 100, kept between 1 and 1000) it answers one page as
    { items, page, pageSize, totalCount, totalPages } (the shape of the search endpoints, as a <Table>Page record), ordered like the array and by the key
    so pages never overlap, read without tracking. A page number below 1 or one whose first row does not fit a 32 bit offset is a 400 problem.
    The tables that grow still use /search (filters, sort by any column, routines); this is the plain paged read.
  - GetAll's ordering: the first NOT NULL date column (date, datetime, datetime2, smalldatetime) that is not an
    audit column, newest first - GenericRepo.GetAllOrderByDescending only takes a non-nullable DateTime.
    A table with no such column gets a plain GetAll() instead. Change the line in the generated file if another
    column suits the screen better.

What it does NOT do: the entity, its DbSet in the context and the line in ApiRegisterExtension.cs that calls
Register() are written by hand. Nor does it write business rules (validation, defaults, name/duplicate checks); a
table that needs them keeps a hand-maintained API class - generate this one only for the plain shape.

The handlers use <Table>Repo (generate it with CS_Repo), which sits on GenericRepo: GetAll, GetAllOrderByDescending and
the delete check live there. A name/active table (a NOT NULL text Name plus a NOT NULL bit IsActive) has a NameActiveRepo
instead and gets `public class DepartmentApi : NameActiveCrudApi<Department, DepartmentRepo>` (see "Name and IsActive tables" below).
A table with no repository (enum / *Type lookup tables, tables with no entity) stops with an error.
Requires a table with a single int, uniqueidentifier or text primary key; the route, the `Key` expression and the base class follow the key (`KeyType` in Core): `{id:int}` and `CrudApi<Customer, CustomerRepo>` for an int, `{id:guid}` and `CrudApi<Row, RowRepo, Guid>`, `{id}` and `CrudApi<Country, CountryRepo, string>` for text (a bigint is `{id:long}`). A text key table has no find-by-name route (`/{id}` and `/{name}` would be the same route); a create with an empty key is a 400 and with a key that exists a 409, and a key with a `/` or `.` is not supported in the route. Only the ASP.NET API serves a guid or text key table so far (`TableModel.HasKeyedCrudApi`; it has no `/search` or clone). Anything else stops with an error (a composite key, a key of another type, a name/active table whose key is not an int).
```

## Now a short class on CrudApi

The template no longer writes the five endpoints and their handlers into every table's file. It writes `public class CustomerApi : CrudApi<Customer, CustomerRepo>` with `NewRepo`, `Key`, `NewestFirst` when the table has a NOT NULL date column to order by, `CanClone` / `CloneAsync` when the table has a clone routine, and `CanFindByName` / `FindByNameAsync` when it has a NOT NULL text Name. Everything described above (routes, answers, paging, ordering) is now `Apis/CrudApi.cs`, written once by the API essentials group `crudapi` (see API_EssentialCrudApi_v1.md); run that group first or the generated classes do not compile. The refusals are unchanged (a name/active table, a table with no repository, a key that is not a single int).

`GET <route>/{name}` is new: the Angular and React services' `findByName` called it, and no generated API answered it. It lists the rows whose name starts with the text (the repository's `GetByName`); a number in that place is still an id.

## Name and IsActive tables

A table with a NOT NULL text `Name` and a NOT NULL bit `IsActive` (a department, a team, an employee, a project) is written as a short class on `NameActiveCrudApi<TEntity, TRepo>` (Apis/CrudApi.cs, the CrudApi essentials group) over its `NameActiveRepo` (the NameActive essentials group):

```csharp
public class DepartmentTeamApi : NameActiveCrudApi<DepartmentTeam, DepartmentTeamRepo>
{
    protected override DepartmentTeamRepo NewRepo(AppContext context) => new(context);
    protected override Expression<Func<DepartmentTeam, int>> Key => c => c.DepartmentTeamId;
}
```

The base answers `GET <route>` (every row by name), `GET <route>/active`, `GET <route>/{id}`, `GET <route>/{name}` (the active rows that start with the text), `POST`, `PUT` and `DELETE`. The name is trimmed and may not be empty (400); a second active row with the same name is a 409 problem whose `detail` the Angular screens show as it is; a delete that a foreign key blocks is a 400 problem. There is no search endpoint, clone or newest-first order for these tables. `PlanTables=AspNetApi` runs the template for them (and `API_Registration` lists them); the Rust, Python, Blazor and React stacks still leave them out. A table listed in `NoApiTables` (a user table with password hashes) gets no class.

## Lookup

A table with a display column (`DisplayColumnSelector`: the column its drop-downs show) gets `CanLookup => true` and a `LookupQuery` that orders by that column and reads `new LookupItem(key, display, IsActive or null)`. A text display column is read as it is (`?? ""` when nullable); any other type is `ToString()`. A table with no display column has no `/lookup` route and its screens' drop-downs would not be asked either.

With `Dtos=true` a table that has a plain CRUD API (not a name and active table) derives from `CrudApi<Entity, EntityRepo, EntityInput, EntityDto>` (`CrudApi<Entity, EntityRepo, Key, EntityInput, EntityDto>` for a Guid or text key), adds the `DtoNamespace` to its usings and names the three conversions: `ToOutput` (`EntityMapping.ToDto(row)!`), `ToEntity` (`input.ToEntity()`) and `Apply` (`input.ApplyTo(row)`). `DtoShape` in Core is the rule the three DTO templates ask too (`CS_DtoInput_v1.md`).

## The Search list protocol

With `ListProtocol=Search` the class overrides `SearchRows` and `SortRows` (CrudApi calls them). `SearchRows` keeps a row when the lower-cased text is contained in any column of `SearchBox`: the table's searchable text columns (`TableModel.SearchableColumns`) and the display column of each parent the entity can reach (at most `SearchBox.MaxParents`), or the columns the project lists in `SearchColumns` (`Customer=Name,Customer=Status.Description`: a column of the table, or a navigation name and a column of its parent; an entry that is not there is skipped). `SortRows` has one case per sortable column (`SearchPlan`, the names the Columns protocol sorts by); a foreign key sorts by its parent's display column. A table with no text column and no parent gets no `SearchRows`, so its search box matches nothing. A Name and IsActive table has no paged list and gets neither.

## The user table

With `ApiAuth` and `UserTable` naming this table, the class also overrides `BeforeSaveAsync` and, when the table has an administrator flag, `BeforeDeleteAsync` (see `API_UserDirectory_v1.md`): the stored password hash is put back, an administrator or role column changes only for an administrator, a sign-in name is unique and the last administrator stays. `CS_Entity` and `CS_Dto` mark the hash `[JsonIgnore]` and `DtoShape.InputColumns(table, project)` leaves it out of the input.
