# API_Search_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates: <TableName>SearchApi.cs   (see OutputName in API_Search_v1.tt.config)

The HTTP companion to SP_Search.tt:
    GET /<plural>/search?<column>=&...&pageNumber=1&pageSize=100
one optional query parameter per SP_Search.tt's own searchable column (every IsStringColumn that
isn't IsAuditColumn - the identical rule, computed the same way, so the two templates can't drift
apart on what's searchable), plus pageNumber (1-based) and pageSize. Returns a <Table>SearchResult:
Items/Page/PageSize/TotalCount/TotalPages (a PagedResult<T> shape).

Like API_Junction.tt, there is no <Table>Repo to go through: the handler calls SP_Search and
SP_SearchCount directly through the injected DbContext (Database.SqlQueryRaw<T>), the same call the
WinUI3 side makes through CS_Repo.tt's own new Search method - one source of truth for "how do I
search+page this table" across every consumer, not three independently-maintained filter
implementations. SqlQueryRaw<T> works for any shape whose property names match the returned columns,
entity or not, without the target project's DbContext needing to register anything new - the same
property API_Junction.tt's own <Table>JunctionItem DTO already relies on.

The page limits (found on a real server whose first version failed on a page size of 0 and accepted any size):
- pageSize is kept between 1 and 1000 (MaxPageSize) with Math.Clamp; 0 or less is an error inside the database (FETCH NEXT 0 ROWS) and a huge size reads the
  whole table. The answer carries the size that was used, so a caller that asked for more sees the cap. CS_Repo.SearchAsync clamps the same way.
- pageNumber < 1 is rejected with a 400 problem (title "Invalid page"), and so is a page number whose first row does not fit in the 32 bit OFFSET
  ((pageNumber - 1) * pageSize > int.MaxValue), which was an overflow error (500) in the database.
- The rows are read AsNoTracking: they go straight to JSON, so there is nothing to track (CS_Repo.SearchAsync keeps tracking: its caller may edit the rows).
- The key is not part of the route (`/search` is a literal and beats `/{id}`), so a table with a uniqueidentifier or text key gets the same endpoint (`DatabaseModel.AspNetSearchApiTables`); the key is only the tie-break of the sort. A text key literally spelled `search` could not be fetched by id.
- An unknown sortBy still gives the default order on purpose (a grid may hold a saved sort for a column that no longer exists); the name is never pasted into SQL.
Requires a primary key and a table, not a view (SP_Search.tt's own restrictions - this calls that
procedure directly, so the same shape is required). A table with no searchable column still gets this
endpoint - pagination and searchability are separate concerns (SP_Search.tt's own header comment) -
it just has no query-string filter parameters, matching SP_Search.tt's own parameterless filter list.
```

- **Access mode (EF Core instead of routines):** with `AccessMode=Ef` (always for SQLite) this template writes LINQ over the context instead of calls to the routines. See CS_SearchQuery_v1.md and Docs/Reference.md.
