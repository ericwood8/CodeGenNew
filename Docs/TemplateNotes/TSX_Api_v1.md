# TSX_Api_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates: api/<table>Api.ts   (a plain object of fetch-based functions; the path is written by the
template itself, see GeneratedFiles - point the output folder at the React app's src folder)

The React counterpart of TS_Service.tt - same routes, same method names, same restrictions - but a
plain object of async functions built on fetch instead of an @Injectable class built on HttpClient,
since a React app has no dependency-injection container to register a service with. Assumes the target
project already has a shared request<T>(path, init) helper and an ApiError class (imported here from
"./client"), the same way TS_Service.tt assumes Angular's own HttpClient is already available -
CodeGenNew does not generate that shared file itself. Modeled on a
src/api/client.ts, which is exactly that shape:

    export class ApiError extends Error { constructor(public status: number, message: string) { super(message); } }
    export async function request<T>(path: string, init?: RequestInit): Promise<T> { ... }

    getAll(): Promise<Holiday[]>                         GET    /api/holidays
    getById(id): Promise<Holiday>                        GET    /api/holidays/{id}
    create(holiday): Promise<Holiday>                    POST   /api/holidays
    update(id, holiday): Promise<Holiday>                PUT    /api/holidays/{id}
    delete(id): Promise<void>                            DELETE /api/holidays/{id}
    findByName(name): Promise<Holiday[]>                 GET    /api/holidays/{name}   (only a table with a text Name column)
    getPage(pageNumber, pageSize, filters?): Promise<{Table}PagedResult>
                                                          GET    /api/holidays/search?pageNumber=&pageSize=&<column>=...
                                                          (always. A filters object with one optional string property
                                                          per searchable column, matching API_Search.tt's own query
                                                          parameters name-for-name - an empty object for a table with
                                                          no searchable column, since pagination and searchability are
                                                          separate concerns (SP_Search.tt's own header comment); an
                                                          omitted or blank value is left out of the URL entirely.
                                                          )

  - Names: exported const <table>Api in api/<table>.ts (lower-cased table stem), the table name without
    its "E_"/"SY_" prefix - same convention TS_Service.tt uses; the row type is imported from
    ../models/<table> (TS_Model.tt's own output - that template writes a plain TS interface with no
    Angular dependency, so it is reused as-is for a React project; no separate React model template exists).
  - URL: apiPrefix + "/" + the lower-cased name, pluralized (CodeGenNew.Core.Pluralizer.Pluralize) - identical route
    convention to TS_Service.tt (same backend, same BaseApi.BreakIntoStrings-registered route).
  - Key: read from Core's KeyType. An int key is a number, a uniqueidentifier key a string (the route takes
    {id:guid}; the api assigns the value on create - a new row is sent with the empty GUID, an int key is
    sent as 0). A text key (a country or currency code) is a string the person types; it is put in the
    path with encodeURIComponent, so a space or an & in a code reaches the route intact. A composite key
    is refused.
  - findByName exists on the server for the name-based tables, so it is generated for every table with a
    text column called Name; the plain (API_Crud) tables have none, and a text key table has none either
    (GET <route>/{id} and GET <route>/{name} would be the same route).

Not written: calls for extra routes a hand-written client adds (a parent-scoped list, a filtered search);
an api module that needs those stays hand-maintained.
```
