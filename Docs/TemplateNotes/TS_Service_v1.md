# TS_Service_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates: services/<table>.service.ts   (an Angular injectable; the path is written by the template itself, see
GeneratedFiles - point the output folder at the Angular app's src/app folder)

The HttpClient wrapper for one table's API, with the
same method names on every service so a component never has to remember which one this table uses:

    getAll(): Observable<Holiday[]>              GET    api/holidays
    getById(id): Observable<Holiday>             GET    api/holidays/{id}
    create(holiday): Observable<Holiday>         POST   api/holidays
    update(id, holiday): Observable<Holiday>     PUT    api/holidays/{id}
    delete(id): Observable<void>                 DELETE api/holidays/{id}
    findByName(name): Observable<Holiday[]>      GET    api/holidays/{name}      (only a table with a text Name column)
    getPage(pageNumber, pageSize, ...filters): Observable<{Table}PagedResult>
                                                  GET    api/holidays/search?pageNumber=&pageSize=&<column>=...
                                                  (always.
                                                  One optional string parameter per searchable column, in
                                                  TableModel.SearchableColumns order, matching API_Search.tt's own
                                                  query parameters name-for-name - zero of them for a table with no
                                                  searchable column, since pagination and searchability are separate
                                                  concerns (SP_Search.tt's own header comment); an omitted or blank
                                                  value is left out of the request entirely, not sent as ''.
                                                  )

  - Names: class <Table>Service in services/<table>.service.ts, the table name without its "E_" / "SY_" prefix
    (E_TimeSheet -> TimeSheetService, services/timesheet.service.ts); the interface is imported from models/<table>.ts.
  - URL: "api/" + the lower-cased name, pluralized (CodeGenNew.Core.Pluralizer.Pluralize) - what BaseApi.BreakIntoStrings
    registers on the server (the Angular dev proxy turns "api/..." into the server's own route).
  - Key: an int key is a number, a uniqueidentifier key a string (the route takes {id:guid}; the api assigns the value on
    create - a new row is sent with the empty GUID, as an int key is sent as 0). Any other key (composite, or a natural
    text key the person would have to type) is refused.
  - findByName exists on the server for the name-based tables, so it is generated for every table with a text column
    called Name; the plain (API_Crud) tables have none.

Not written: calls for extra routes a hand-written API adds (DepartmentService.getTeams, TimeSheetDetailService's
"by timesheet" call, a filtered search); a service that needs those stays hand-maintained.
```
