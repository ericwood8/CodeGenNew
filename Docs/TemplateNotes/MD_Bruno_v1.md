# MD_Bruno_v1

`collections/bruno/`: a Bruno collection for the generated API. Whole-database; runs only in a plan that names it (`PlanAlso=MD_Bruno`). Open the folder in Bruno, or run it with the `bru` CLI.

- `bruno.json`: the collection file.
- `environments/Local.bru`: `baseUrl` (`http://localhost:<ApiPort>`) and `id` (1), and one more variable for each table with a text or uniqueidentifier key (`countryId` = the key's example; `ApiRequests.IdVariable`), so a collection that mixes kinds of key gives each its own.
- `<Table>/<Request>.bru`: a file per request, numbered in order in its `seq`. The requests are the ones `API_Http` and `MD_Postman` write (`ApiRequests` in Core is the one list): all, one by id, search, add and change (a JSON body with a sample value for every column that cannot be NULL), remove, copy.

Select the Local environment in Bruno before sending. It does not log in: add an Authorization header if the API has authentication.
