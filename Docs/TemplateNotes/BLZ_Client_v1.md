# BLZ_Client_v1

`Services/<Table>Client.cs`: the Blazor counterpart of `TSX_Api`. A class over the app's `HttpClient` (its `BaseAddress` is the API) with `GetAllAsync`, `GetAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`, `CloneAsync` (a table `CloneShape` allows) and `GetPageAsync(page, pageSize, filters, sortBy, descending)`, which calls `/api/<table>s/search` with `pageNumber`, `pageSize`, the non-blank filters, `sortBy` and `sortDir`.

A call that does not succeed throws `ApiException` with the HTTP status (`Services/ApiSupport.cs`, the `Support` essentials group); `Explain(action)` gives the sentence a page shows. `BLZ_Screens` registers every client with `AddApiClients()`. Same table rules as `BLZ_Model`. See `BLZ_Screens_v1.md` for the whole stack.
